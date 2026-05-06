using MedWNetworkSim.App.Insights;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.App.VisualAnalytics;

namespace MedWNetworkSim.App.Agents;

public sealed class SimulationActorCoordinator
{
    private readonly NetworkSimulationEngine simulationEngine = new();
    private readonly TrafficEconomicSettlementService economicSettlementService = new();
    private readonly INetworkInsightService insightService;
    private readonly SimulationActorActionApplier actionApplier;
    private readonly IAgentActionLogger actionLogger;

    public SimulationActorCoordinator(
        INetworkInsightService? insightService = null,
        SimulationActorActionApplier? actionApplier = null,
        IAgentActionLogger? actionLogger = null)
    {
        this.insightService = insightService ?? new NetworkInsightService();
        this.actionApplier = actionApplier ?? new SimulationActorActionApplier();
        this.actionLogger = actionLogger ?? new AgentActionLogger();
    }

    public SimulationActorRunResult RunActorsForTicks(NetworkModel network, IReadOnlyList<SimulationActorState> actors, int ticks)
    {
        var working = Clone(network);
        var decisions = new List<SimulationActorDecision>();
        var metrics = new List<SimulationActorMetrics>();

        for (var tick = 0; tick < Math.Max(0, ticks); tick++)
        {
            var step = StepActorsOnce(working, actors, tick, decisions);
            working = step.NetworkAfterStep;
            decisions.AddRange(step.Decisions);
            metrics.Add(step.Metrics);
        }

        return new SimulationActorRunResult
        {
            InitialNetwork = Clone(network),
            FinalNetwork = working,
            DecisionsByTick = decisions,
            MetricsByTick = metrics,
            FinalSummary = $"Executed {ticks} ticks with {actors.Count(a => a.IsEnabled)} enabled actors."
        };
    }

    public SimulationActorStepResult StepActorsOnce(
        NetworkModel network,
        IReadOnlyList<SimulationActorState> actorStates,
        int tick = 0,
        IReadOnlyList<SimulationActorDecision>? previousDecisions = null,
        SimulationActorPolicySettings? policySettings = null)
    {
        previousDecisions ??= [];
        policySettings ??= new SimulationActorPolicySettings();

        var orderedActors = BuildActors(actorStates)
            .Where(actor => actor.State.IsEnabled)
            .OrderBy(actor => GetOrder(actor.State.Kind))
            .ThenBy(actor => actor.State.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var baseSnapshot = BuildSnapshot(network, tick);
        var insights = insightService.Generate(baseSnapshot);
        var context = new SimulationActorContext
        {
            CurrentNetwork = network,
            CurrentSnapshot = baseSnapshot,
            CurrentInsights = insights,
            Tick = tick,
            PreviousDecisions = previousDecisions,
            PolicySettings = policySettings
        };

        var actorMap = BuildActorMap(actorStates);
        var decisions = orderedActors.Select(actor => actor.Decide(context)).ToList();
        LogDecisions(decisions, outcomes: baseSnapshot.TrafficOutcomes, tick, actorMap);
        var actorKindsById = actorMap.ToDictionary(pair => pair.Key, pair => pair.Value.Kind, StringComparer.OrdinalIgnoreCase);
        var resolvedActions = ResolveConflicts(decisions.SelectMany(d => d.Actions).ToList(), actorKindsById);
        var flowByEdge = BuildFlowByEdge(baseSnapshot.TrafficOutcomes);
        var (appliedNetwork, outcomes) = actionApplier.Apply(network, resolvedActions, actorMap, flowByEdge);

        foreach (var outcome in outcomes.Where(o => o.Applied && o.Action.Cost > 0d))
        {
            var actor = actorMap[outcome.Action.ActorId];
            if (actor.Budget <= 0d)
            {
                actor.Cash = Math.Max(0d, actor.Cash - outcome.Action.Cost);
            }
        }

        var affordableNetwork = ApplyBuyerAffordabilityPolicy(appliedNetwork, actorMap);
        var appliedSnapshot = BuildSnapshot(affordableNetwork, tick + 1);
        var settlement = economicSettlementService.Settle(appliedNetwork, appliedSnapshot.TrafficOutcomes, actorMap);
        foreach (var ledger in settlement.Ledgers.Values)
        {
            if (actorMap.TryGetValue(ledger.ActorId, out var actor))
            {
                ApplySettlementCashDelta(actor, ledger);
            }
        }

        appliedSnapshot = new VisualAnalyticsSnapshot
        {
            Network = appliedSnapshot.Network,
            TrafficOutcomes = settlement.Outcomes,
            ConsumerCosts = simulationEngine.SummarizeConsumerCosts(settlement.Outcomes.SelectMany(outcome => outcome.Allocations)),
            Period = appliedSnapshot.Period
        };
        var metrics = BuildMetrics(tick, appliedSnapshot, actorStates, decisions, settlement.Ledgers);

        return new SimulationActorStepResult
        {
            NetworkAfterStep = affordableNetwork,
            Decisions = decisions,
            ActionOutcomes = outcomes,
            Metrics = metrics
        };
    }

    public IReadOnlyList<SimulationActorDecision> PreviewActorActions(
        NetworkModel network,
        IReadOnlyList<SimulationActorState> actors,
        int tick = 0,
        IReadOnlyList<SimulationActorDecision>? previousDecisions = null,
        SimulationActorPolicySettings? policySettings = null)
    {
        previousDecisions ??= [];
        policySettings ??= new SimulationActorPolicySettings();
        var snapshot = BuildSnapshot(network, tick);
        var insights = insightService.Generate(snapshot);
        var context = new SimulationActorContext
        {
            CurrentNetwork = network,
            CurrentSnapshot = snapshot,
            CurrentInsights = insights,
            Tick = tick,
            PreviousDecisions = previousDecisions,
            PolicySettings = policySettings
        };

        return BuildActors(actors)
            .Where(actor => actor.State.IsEnabled)
            .OrderBy(actor => GetOrder(actor.State.Kind))
            .ThenBy(actor => actor.State.Id, StringComparer.OrdinalIgnoreCase)
            .Select(actor => actor.Decide(context))
            .ToList();
    }

    private IReadOnlyList<SimulationActorAction> ResolveConflicts(
        IReadOnlyList<SimulationActorAction> actions,
        IReadOnlyDictionary<string, SimulationActorKind> actorKindsById)
    {
        var ordered = actions
            .OrderBy(a => GetOrder(GetKind(a, actorKindsById)))
            .ThenBy(a => a.IsPolicyAction ? 0 : 1)
            .ThenBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var bannedByEdgeTraffic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<SimulationActorAction>();

        foreach (var action in ordered)
        {
            if (action.Kind == SimulationActorActionKind.BanTrafficOnEdge && !string.IsNullOrWhiteSpace(action.TargetEdgeId) && !string.IsNullOrWhiteSpace(action.TrafficType))
            {
                bannedByEdgeTraffic.Add($"{action.TargetEdgeId}:{action.TrafficType}");
                resolved.Add(action);
                continue;
            }

            if (action.Kind == SimulationActorActionKind.PreferRoute && !string.IsNullOrWhiteSpace(action.TargetEdgeId) && !string.IsNullOrWhiteSpace(action.TrafficType))
            {
                var key = $"{action.TargetEdgeId}:{action.TrafficType}";
                if (bannedByEdgeTraffic.Contains(key))
                {
                    continue;
                }
            }

            resolved.Add(action);
        }

        return resolved;
    }

    private static SimulationActorMetrics BuildMetrics(
        int tick,
        VisualAnalyticsSnapshot snapshot,
        IReadOnlyList<SimulationActorState> actors,
        IReadOnlyList<SimulationActorDecision> decisions,
        IReadOnlyDictionary<string, SimulationActorEconomicLedger>? ledgers = null)
    {
        ledgers ??= new Dictionary<string, SimulationActorEconomicLedger>(StringComparer.OrdinalIgnoreCase);
        var flows = BuildFlowByEdge(snapshot.TrafficOutcomes);
        var utilisation = snapshot.Network.Edges
            .Where(edge => edge.Capacity.HasValue && edge.Capacity.Value > 0d)
            .Select(edge => flows.GetValueOrDefault(edge.Id) / edge.Capacity!.Value)
            .ToList();

        return new SimulationActorMetrics
        {
            Tick = tick,
            TotalDelivered = snapshot.TrafficOutcomes.Sum(o => o.TotalDelivered),
            TotalUnmetDemand = snapshot.TrafficOutcomes.Sum(o => o.UnmetDemand),
            TotalMovementCost = snapshot.TrafficOutcomes.Sum(o => o.Allocations.Sum(a => a.TotalMovementCost)),
            AverageEdgeUtilisation = utilisation.Count == 0 ? 0d : utilisation.Average(),
            BottleneckEdgeCount = utilisation.Count(u => u >= 0.9d),
            ActorCashById = actors.ToDictionary(a => a.Id, a => a.Cash, StringComparer.OrdinalIgnoreCase),
            ActorUtilityById = decisions.ToDictionary(d => d.ActorId, d => d.ExpectedUtilityAfter, StringComparer.OrdinalIgnoreCase),
            ActorSalesRevenueById = actors.ToDictionary(a => a.Id, a => ledgers.GetValueOrDefault(a.Id)?.SalesRevenue ?? 0d, StringComparer.OrdinalIgnoreCase),
            ActorProductionCostById = actors.ToDictionary(a => a.Id, a => ledgers.GetValueOrDefault(a.Id)?.ProductionCost ?? 0d, StringComparer.OrdinalIgnoreCase),
            ActorTransportCostById = actors.ToDictionary(a => a.Id, a => ledgers.GetValueOrDefault(a.Id)?.TransportCost ?? 0d, StringComparer.OrdinalIgnoreCase),
            ActorTaxesPaidById = actors.ToDictionary(a => a.Id, a => ledgers.GetValueOrDefault(a.Id)?.TaxesPaid ?? 0d, StringComparer.OrdinalIgnoreCase),
            ActorTaxesReceivedById = actors.ToDictionary(a => a.Id, a => ledgers.GetValueOrDefault(a.Id)?.TaxesReceived ?? 0d, StringComparer.OrdinalIgnoreCase),
            ActorProfitById = actors.ToDictionary(a => a.Id, a => ledgers.GetValueOrDefault(a.Id)?.Profit ?? 0d, StringComparer.OrdinalIgnoreCase),
            PolicyRestrictionCount = snapshot.Network.Edges.Sum(edge => edge.TrafficPermissions.Count(p => p.IsActive && p.Mode == EdgeTrafficPermissionMode.Blocked)),
            CooperationIndex = actors.Count == 0 ? 0d : actors.Average(a => Math.Clamp(a.CooperationWeight, 0d, 1d))
        };
    }

    private static Dictionary<string, double> BuildFlowByEdge(IReadOnlyList<TrafficSimulationOutcome> outcomes)
    {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var allocation in outcomes.SelectMany(outcome => outcome.Allocations))
        {
            if (allocation.PathEdgeIds is null)
            {
                continue;
            }

            foreach (var edgeId in allocation.PathEdgeIds)
            {
                if (string.IsNullOrWhiteSpace(edgeId))
                {
                    continue;
                }

                map.TryGetValue(edgeId, out var current);
                map[edgeId] = current + allocation.Quantity;
            }
        }

        return map;
    }

    private VisualAnalyticsSnapshot BuildSnapshot(NetworkModel network, int tick)
    {
        var outcomes = simulationEngine.Simulate(network);
        var costs = simulationEngine.SummarizeConsumerCosts(outcomes.SelectMany(outcome => outcome.Allocations));
        return new VisualAnalyticsSnapshot { Network = network, TrafficOutcomes = outcomes, ConsumerCosts = costs, Period = tick };
    }

    private static NetworkModel ApplyBuyerAffordabilityPolicy(
        NetworkModel network,
        IReadOnlyDictionary<string, SimulationActorState> actorsById)
    {
        if (actorsById.Count == 0)
        {
            return network;
        }

        var adjusted = Clone(network);
        var definitionsByTraffic = adjusted.TrafficTypes
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Name))
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var remainingFundsByActorId = actorsById.Values
            .Where(actor => actor.IsEnabled && !string.IsNullOrWhiteSpace(actor.Id))
            .ToDictionary(actor => actor.Id, ResolveSettlementFunds, StringComparer.OrdinalIgnoreCase);

        foreach (var node in adjusted.Nodes.OrderBy(node => node.Id, StringComparer.OrdinalIgnoreCase))
        {
            var buyerActorId = ResolveActorForNode(node.Id, node, actorsById);
            if (string.IsNullOrWhiteSpace(buyerActorId) ||
                !remainingFundsByActorId.TryGetValue(buyerActorId, out var remainingFunds))
            {
                continue;
            }

            foreach (var profile in node.TrafficProfiles
                .Where(profile => profile.Consumption > 0d)
                .OrderBy(profile => profile.TrafficType, StringComparer.OrdinalIgnoreCase))
            {
                var unitPrice = ResolveExpectedUnitPurchasePrice(adjusted, definitionsByTraffic, profile.TrafficType);
                if (unitPrice <= 0d)
                {
                    continue;
                }

                var affordableDemand = Math.Max(0d, remainingFunds) / unitPrice;
                var cappedConsumption = Math.Min(profile.Consumption, affordableDemand);
                profile.Consumption = cappedConsumption;
                remainingFunds = Math.Max(0d, remainingFunds - cappedConsumption * unitPrice);
            }

            remainingFundsByActorId[buyerActorId] = remainingFunds;
        }

        return adjusted;
    }

    private static double ResolveSettlementFunds(SimulationActorState actor)
    {
        var spendLimit = actor.Budget > 0d ? actor.Budget : actor.Cash;
        return Math.Max(0d, spendLimit);
    }

    private static double ResolveExpectedUnitPurchasePrice(
        NetworkModel network,
        IReadOnlyDictionary<string, TrafficTypeDefinition> definitionsByTraffic,
        string trafficType)
    {
        definitionsByTraffic.TryGetValue(trafficType, out var definition);
        var prices = network.Nodes
            .SelectMany(node => node.TrafficProfiles)
            .Where(profile => profile.Production > 0d && StringComparer.OrdinalIgnoreCase.Equals(profile.TrafficType, trafficType))
            .Select(profile => profile.UnitPrice > 0d ? profile.UnitPrice : Math.Max(0d, definition?.DefaultUnitSalePrice ?? 0d))
            .Where(price => price > 0d)
            .ToList();

        // Use the highest available seller price so a pre-routing cap cannot admit purchases
        // that later settle above the buyer's available funds.
        return prices.Count == 0 ? Math.Max(0d, definition?.DefaultUnitSalePrice ?? 0d) : prices.Max();
    }

    private static string? ResolveActorForNode(
        string nodeId,
        NodeModel node,
        IReadOnlyDictionary<string, SimulationActorState> actorsById)
    {
        var controlledActor = actorsById.Values
            .Where(actor => actor.IsEnabled && actor.ControlledNodeIds.Contains(nodeId, StringComparer.OrdinalIgnoreCase))
            .OrderBy(actor => actor.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (controlledActor is not null)
        {
            return controlledActor.Id;
        }

        return string.IsNullOrWhiteSpace(node.ControllingActor) ? null : node.ControllingActor;
    }

    private static void ApplySettlementCashDelta(SimulationActorState actor, SimulationActorEconomicLedger ledger)
    {
        var updatedCash = actor.Cash + ledger.CashDelta;
        actor.Cash = actor.Budget > 0d && ledger.PurchaseCost > 0d
            ? Math.Max(0d, updatedCash)
            : updatedCash;
    }

    private static List<ISimulationActor> BuildActors(IReadOnlyList<SimulationActorState> states)
    {
        var actors = new List<ISimulationActor>(states.Count);
        foreach (var state in states)
        {
            state.Capability ??= SimulationActorCapabilityCatalog.ForKind(state.Id, state.Kind);
            state.Capability.Permissions ??= [];
            if (!state.Capability.AllowedActionKinds.Any() ||
                (string.IsNullOrWhiteSpace(state.Capability.ActorId) && state.Capability.Permissions.Count == 0))
            {
                state.Capability = SimulationActorCapabilityCatalog.ForKind(state.Id, state.Kind);
            }
            else if (string.IsNullOrWhiteSpace(state.Capability.ActorId))
            {
                state.Capability.ActorId = state.Id;
            }

            if (!state.GenerateAutomaticDecisions)
            {
                continue;
            }

            actors.Add(state.Kind switch
            {
                SimulationActorKind.Firm => new FirmSimulationActor(state),
                SimulationActorKind.Government => new GovernmentSimulationActor(state),
                SimulationActorKind.LogisticsPlanner => new LogisticsPlannerSimulationActor(state),
                _ => throw new InvalidOperationException($"Unsupported actor kind '{state.Kind}'.")
            });
        }

        return actors;
    }

    private static int GetOrder(SimulationActorKind kind) => kind switch
    {
        SimulationActorKind.Government => 0,
        SimulationActorKind.LogisticsPlanner => 1,
        SimulationActorKind.Firm => 2,
        _ => 99
    };

    private static SimulationActorKind GetKind(
        SimulationActorAction action,
        IReadOnlyDictionary<string, SimulationActorKind> actorKindsById)
    {
        if (actorKindsById.TryGetValue(action.ActorId, out var kind))
        {
            return kind;
        }

        return action.IsPolicyAction ? SimulationActorKind.Government : SimulationActorKind.Firm;
    }

    private static IReadOnlyDictionary<string, SimulationActorState> BuildActorMap(IReadOnlyList<SimulationActorState> actorStates)
    {
        var actorMap = new Dictionary<string, SimulationActorState>(StringComparer.OrdinalIgnoreCase);
        var duplicateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var actorState in actorStates)
        {
            if (string.IsNullOrWhiteSpace(actorState.Id))
            {
                continue;
            }

            if (!actorMap.TryAdd(actorState.Id, actorState))
            {
                duplicateIds.Add(actorState.Id);
            }
        }

        if (duplicateIds.Count > 0)
        {
            var duplicateList = string.Join(", ", duplicateIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
            throw new InvalidOperationException($"Duplicate actor ids are not supported: {duplicateList}.");
        }

        return actorMap;
    }

    private static NetworkModel Clone(NetworkModel network)
    {
        return NetworkModelCloneUtility.Clone(network);
    }

    private void LogDecisions(
        IReadOnlyList<SimulationActorDecision> decisions,
        IReadOnlyList<TrafficSimulationOutcome> outcomes,
        int tick,
        IReadOnlyDictionary<string, SimulationActorState> actorsById)
    {
        var delivered = outcomes.Sum(o => o.TotalDelivered);
        var unmet = outcomes.Sum(o => o.UnmetDemand);
        var movementCost = outcomes.Sum(o => o.Allocations.Sum(a => a.TotalMovementCost));
        var demand = outcomes.Sum(o => o.TotalConsumption);

        foreach (var decision in decisions)
        {
            var agentId = Guid.TryParse(decision.ActorId, out var parsedAgentId) ? parsedAgentId : CreateStableGuid(decision.ActorId);
            actorsById.TryGetValue(decision.ActorId, out var actor);
            foreach (var action in decision.Actions)
            {
                actionLogger.Log(new AgentActionLogEntry
                {
                    Id = Guid.NewGuid(),
                    AgentId = agentId,
                    ActorId = decision.ActorId,
                    AgentName = actor?.Name ?? string.Empty,
                    Timestamp = DateTime.UtcNow,
                    SimulationTick = tick,
                    ActionType = action.Kind.ToString(),
                    TargetId = action.TargetEdgeId ?? action.TargetNodeId ?? "(none)",
                    StateMetrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["total_delivered"] = delivered,
                        ["total_unmet_demand"] = unmet,
                        ["total_movement_cost"] = movementCost,
                        ["total_demand"] = demand
                    },
                    DecisionSummary = decision.ReasonSummary,
                    DecisionFactors = decision.Factors.Count == 0 ? [action.Reason] : [.. decision.Factors],
                    AlternativesConsidered = decision.Alternatives is { Count: > 0 } ? [.. decision.Alternatives] : null,
                    Outcome = action.ExpectedEffect,
                    UtilityScore = decision.Utility
                });
            }
        }
    }

    private static Guid CreateStableGuid(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "unknown-agent" : value.Trim();
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, guidBytes.Length);
        return new Guid(guidBytes);
    }
}

using MedWNetworkSim.App.Insights;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.App.VisualAnalytics;

namespace MedWNetworkSim.App.Agents;

public sealed class SimulationActorCoordinator
{
    private readonly NetworkSimulationEngine simulationEngine = new();
    private readonly INetworkInsightService insightService;
    private readonly SimulationActorActionApplier actionApplier;

    public SimulationActorCoordinator(INetworkInsightService? insightService = null, SimulationActorActionApplier? actionApplier = null)
    {
        this.insightService = insightService ?? new NetworkInsightService();
        this.actionApplier = actionApplier ?? new SimulationActorActionApplier();
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

        var decisions = orderedActors.Select(actor => actor.Decide(context)).ToList();
        var resolvedActions = ResolveConflicts(decisions.SelectMany(d => d.Actions).ToList());

        var actorMap = actorStates.ToDictionary(a => a.Id, a => a, StringComparer.OrdinalIgnoreCase);
        var flowByEdge = BuildFlowByEdge(baseSnapshot.TrafficOutcomes);
        var (appliedNetwork, outcomes) = actionApplier.Apply(network, resolvedActions, actorMap, flowByEdge);

        foreach (var outcome in outcomes.Where(o => o.Applied && o.Action.Cost > 0d))
        {
            actorMap[outcome.Action.ActorId].Cash = Math.Max(0d, actorMap[outcome.Action.ActorId].Cash - outcome.Action.Cost);
        }

        var appliedSnapshot = BuildSnapshot(appliedNetwork, tick + 1);
        var metrics = BuildMetrics(tick, appliedSnapshot, actorStates, decisions);

        return new SimulationActorStepResult
        {
            NetworkAfterStep = appliedNetwork,
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

    private IReadOnlyList<SimulationActorAction> ResolveConflicts(IReadOnlyList<SimulationActorAction> actions)
    {
        var ordered = actions
            .OrderBy(a => GetOrder(GetKind(a)))
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
        IReadOnlyList<SimulationActorDecision> decisions)
    {
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

    private static List<ISimulationActor> BuildActors(IReadOnlyList<SimulationActorState> states)
    {
        var actors = new List<ISimulationActor>(states.Count);
        foreach (var state in states)
        {
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

    private static SimulationActorKind GetKind(SimulationActorAction action)
    {
        return action.IsPolicyAction ? SimulationActorKind.Government : SimulationActorKind.Firm;
    }

    private static NetworkModel Clone(NetworkModel network)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(network);
        return System.Text.Json.JsonSerializer.Deserialize<NetworkModel>(json) ?? new NetworkModel();
    }
}

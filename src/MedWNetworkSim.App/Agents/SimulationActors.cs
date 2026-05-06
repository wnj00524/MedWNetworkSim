using MedWNetworkSim.App.Insights;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.Agents;

public abstract class SimulationActorBase : ISimulationActor
{
    protected static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    protected SimulationActorBase(SimulationActorState state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public SimulationActorState State { get; }

    public abstract SimulationActorDecision Decide(SimulationActorContext context);

    protected static double EstimateUtility(SimulationActorObjective objective, IReadOnlyList<TrafficSimulationOutcome> outcomes)
    {
        return objective switch
        {
            SimulationActorObjective.MaximiseProfit => outcomes.Sum(o => o.TotalProfit),
            SimulationActorObjective.MinimiseUnmetDemand => -outcomes.Sum(o => o.UnmetDemand),
            SimulationActorObjective.MinimiseMovementCost => -outcomes.Sum(o => o.Allocations.Sum(a => a.TotalMovementCost)),
            SimulationActorObjective.MaximiseThroughput => outcomes.Sum(o => o.TotalDelivered),
            SimulationActorObjective.EnforcePolicy => -outcomes.Sum(o => o.NoPermittedPathDemand),
            SimulationActorObjective.StabiliseNetwork => -outcomes.Sum(o => o.UnmetDemand) - outcomes.Sum(o => o.Allocations.Sum(a => a.TotalMovementCost)),
            SimulationActorObjective.ReduceCongestion => -outcomes.Sum(o => o.Allocations.Count(a => a.PathEdgeIds?.Count > 2)),
            _ => 0d
        };
    }

    protected static IEnumerable<NetworkInsight> InsightsForEdge(IReadOnlyList<NetworkInsight> insights, string edgeId) =>
        insights.Where(i => string.Equals(i.TargetEdgeId, edgeId, StringComparison.OrdinalIgnoreCase));

    protected bool IsPermittedByPermissions(
        SimulationActorActionKind actionKind,
        string? trafficType = null,
        string? nodeId = null,
        string? edgeId = null)
    {
        var permissions = State.Capability?.Permissions ?? [];
        var hasExplicitAllows = permissions.Any(permission => permission.IsAllowed);
        var actionRules = permissions
            .Where(permission => permission.ActionKind == actionKind)
            .ToList();
        if (actionRules.Count == 0)
        {
            return !hasExplicitAllows;
        }

        var matching = actionRules
            .Where(permission =>
                (permission.TrafficType == null || Comparer.Equals(permission.TrafficType, trafficType)) &&
                (permission.NodeId == null || Comparer.Equals(permission.NodeId, nodeId)) &&
                (permission.EdgeId == null || Comparer.Equals(permission.EdgeId, edgeId)))
            .ToList();

        return matching.Count == 0
            ? !hasExplicitAllows
            : matching.Any(permission => permission.IsAllowed) && !matching.Any(permission => !permission.IsAllowed);
    }

    protected bool HasSpendingCapacity => State.Budget > 0d || State.Cash > 0d;
}

public sealed class FirmSimulationActor : SimulationActorBase
{
    public FirmSimulationActor(SimulationActorState state) : base(state) { }

    public override SimulationActorDecision Decide(SimulationActorContext context)
    {
        var actions = new List<SimulationActorAction>();
        var outcomes = context.CurrentSnapshot.TrafficOutcomes;
        var utilityBefore = EstimateUtility(State.Objective, outcomes);

        foreach (var node in context.CurrentNetwork.Nodes.OrderBy(node => node.Id, Comparer))
        {
            foreach (var profile in node.TrafficProfiles)
            {
                if (profile.Production <= 0d &&
                    profile.Consumption <= 0d &&
                    !string.IsNullOrWhiteSpace(profile.TrafficType) &&
                    HasSpendingCapacity &&
                    IsPermittedByPermissions(SimulationActorActionKind.AdjustProduction, profile.TrafficType, node.Id))
                {
                    actions.Add(new SimulationActorAction
                    {
                        Id = $"{State.Id}:seed-prod:{context.Tick}:{node.Id}:{profile.TrafficType}",
                        ActorId = State.Id,
                        Kind = SimulationActorActionKind.AdjustProduction,
                        TargetNodeId = node.Id,
                        TrafficType = profile.TrafficType,
                        DeltaValue = 10d,
                        Cost = 2.5d,
                        Reason = "Permitted node has no active production or demand; seed a visible production plan.",
                        ExpectedEffect = "Start producing traffic at the controlled node.",
                        IsPolicyAction = false
                    });
                    continue;
                }

                var outcome = outcomes.FirstOrDefault(o => Comparer.Equals(o.TrafficType, profile.TrafficType));
                if (outcome is null)
                {
                    continue;
                }

                var unmetRatio = outcome.TotalConsumption <= 0d ? 0d : outcome.UnmetDemand / outcome.TotalConsumption;
                var demandFillRatio = outcome.TotalConsumption <= 0d ? 0d : outcome.TotalDelivered / outcome.TotalConsumption;
                var productionDeliveryRatio = profile.Production <= 0d
                    ? 0d
                    : Math.Min(1d, outcome.TotalDelivered / profile.Production);
                var hasSignificantUnmetDemand = outcome.UnmetDemand > Math.Max(0.01d, outcome.TotalConsumption * 0.01d);
                var isMostlyDeliveredProduction = productionDeliveryRatio >= 0.8d;
                var shouldHoldOrGrowDeliveredProduction = hasSignificantUnmetDemand && isMostlyDeliveredProduction;
                var hasMaterialOverproduction =
                    profile.Production > Math.Max(1d, outcome.TotalDelivered) * 1.5d &&
                    !hasSignificantUnmetDemand;
                var hasOverproductionEvidence =
                    profile.Production > outcome.TotalDelivered + 0.01d &&
                    !hasSignificantUnmetDemand;
                var expectedMargin = EstimateMargin(context.CurrentNetwork, profile, outcome);

                if (profile.Production > 0d &&
                    (shouldHoldOrGrowDeliveredProduction || (demandFillRatio >= 0.85d && !hasMaterialOverproduction)) &&
                    expectedMargin >= 0d &&
                    HasSpendingCapacity)
                {
                    var delta = Math.Max(1d, profile.Production * 0.1d);
                    var cost = delta * 0.25d;
                    var actionKind = ResolveMarketAction(
                        SimulationActorActionKind.SellTraffic,
                        SimulationActorActionKind.AdjustProduction,
                        profile.TrafficType,
                        node.Id);

                    if (actionKind is not null)
                    {
                        actions.Add(new SimulationActorAction
                        {
                            Id = $"{State.Id}:prod:{context.Tick}:{node.Id}:{profile.TrafficType}",
                            ActorId = State.Id,
                            Kind = actionKind.Value,
                            TargetNodeId = node.Id,
                            TrafficType = profile.TrafficType,
                            DeltaValue = delta,
                            Cost = cost,
                            Reason = actionKind.Value == SimulationActorActionKind.SellTraffic
                                ? "Offer additional traffic for sale because delivered demand and unit margin are positive."
                                : $"Delivered demand is strong with estimated unit margin {expectedMargin:0.##}.",
                            ExpectedEffect = "Increase delivered quantity and profit.",
                            IsPolicyAction = false
                        });
                    }
                }

                if (profile.Consumption > 0d &&
                    unmetRatio > 0d &&
                    HasSpendingCapacity)
                {
                    var actionKind = ResolveMarketAction(
                        SimulationActorActionKind.BuyTraffic,
                        SimulationActorActionKind.AdjustConsumption,
                        profile.TrafficType,
                        node.Id);
                    if (actionKind is not null)
                    {
                        actions.Add(new SimulationActorAction
                        {
                            Id = $"{State.Id}:buy:{context.Tick}:{node.Id}:{profile.TrafficType}",
                            ActorId = State.Id,
                            Kind = actionKind.Value,
                            TargetNodeId = node.Id,
                            TrafficType = profile.TrafficType,
                            DeltaValue = Math.Max(1d, profile.Consumption * 0.1d),
                            Cost = 0d,
                            Reason = actionKind.Value == SimulationActorActionKind.BuyTraffic
                                ? "Buy/input traffic because it is required for profitable downstream production or unmet demand exists."
                                : "Unmet demand exists for this controlled consumer profile.",
                            ExpectedEffect = "Increase demand-side market intent for this traffic.",
                            IsPolicyAction = false
                        });
                    }
                }

                if (unmetRatio > 0.2d &&
                    IsPermittedByPermissions(SimulationActorActionKind.AdjustTrafficPrice, profile.TrafficType, node.Id))
                {
                    actions.Add(new SimulationActorAction
                    {
                        Id = $"{State.Id}:price:{context.Tick}:{node.Id}:{profile.TrafficType}",
                        ActorId = State.Id,
                        Kind = SimulationActorActionKind.AdjustTrafficPrice,
                        TargetNodeId = node.Id,
                        TrafficType = profile.TrafficType,
                        DeltaValue = 0.5d,
                        Cost = 0d,
                        Reason = "Unmet demand indicates scarcity for this traffic.",
                        ExpectedEffect = "Raise unit price moderately to capture margin and shape demand.",
                        IsPolicyAction = false
                    });
                }
                else if (!shouldHoldOrGrowDeliveredProduction &&
                    hasOverproductionEvidence &&
                    productionDeliveryRatio < 0.4d &&
                    profile.Production > 0d &&
                    IsPermittedByPermissions(SimulationActorActionKind.AdjustProduction, profile.TrafficType, node.Id))
                {
                    actions.Add(new SimulationActorAction
                    {
                        Id = $"{State.Id}:cut:{context.Tick}:{node.Id}:{profile.TrafficType}",
                        ActorId = State.Id,
                        Kind = SimulationActorActionKind.AdjustProduction,
                        TargetNodeId = node.Id,
                        TrafficType = profile.TrafficType,
                        DeltaValue = -Math.Max(1d, profile.Production * 0.1d),
                        Cost = 0d,
                        Reason = "Delivered production is poor and no unmet demand remains for this traffic.",
                        ExpectedEffect = "Reduce overproduction and protect margin.",
                        IsPolicyAction = false
                    });
                }
                else if (!shouldHoldOrGrowDeliveredProduction &&
                    expectedMargin < 0d &&
                    profile.Production > 0d &&
                    IsPermittedByPermissions(SimulationActorActionKind.AdjustProduction, profile.TrafficType, node.Id))
                {
                    actions.Add(new SimulationActorAction
                    {
                        Id = $"{State.Id}:margin-cut:{context.Tick}:{node.Id}:{profile.TrafficType}",
                        ActorId = State.Id,
                        Kind = SimulationActorActionKind.AdjustProduction,
                        TargetNodeId = node.Id,
                        TrafficType = profile.TrafficType,
                        DeltaValue = -Math.Max(1d, profile.Production * 0.1d),
                        Cost = 0d,
                        Reason = $"Actual margin is negative ({expectedMargin:0.##} per unit).",
                        ExpectedEffect = "Reduce loss-making production.",
                        IsPolicyAction = false
                    });
                }
            }
        }

        foreach (var edge in context.CurrentNetwork.Edges.OrderBy(edge => edge.Id, Comparer))
        {
            if (!IsPermittedByPermissions(SimulationActorActionKind.AdjustEdgeCapacity, edgeId: edge.Id))
            {
                continue;
            }

            var isBottleneck = InsightsForEdge(context.CurrentInsights, edge.Id).Any(i => i.Category == InsightCategory.Capacity);
            if (!isBottleneck || !HasSpendingCapacity)
            {
                if (HasSpendingCapacity)
                {
                    var preventiveDelta = Math.Max(1d, (edge.Capacity ?? 10d) * 0.05d);
                    actions.Add(new SimulationActorAction
                    {
                        Id = $"{State.Id}:edge-plan:{context.Tick}:{edge.Id}",
                        ActorId = State.Id,
                        Kind = SimulationActorActionKind.AdjustEdgeCapacity,
                        TargetEdgeId = edge.Id,
                        DeltaValue = preventiveDelta,
                        Cost = preventiveDelta,
                        Reason = "Permitted route has room for a small capacity investment.",
                        ExpectedEffect = "Increase visible route capacity for future deliveries.",
                        IsPolicyAction = false
                    });
                }

                continue;
            }

            var delta = Math.Max(1d, (edge.Capacity ?? 10d) * 0.1d);
            var cost = delta;
            actions.Add(new SimulationActorAction
            {
                Id = $"{State.Id}:cap:{context.Tick}:{edge.Id}",
                ActorId = State.Id,
                Kind = SimulationActorActionKind.AdjustEdgeCapacity,
                TargetEdgeId = edge.Id,
                DeltaValue = delta,
                Cost = cost,
                Reason = "Profitable edge is capacity constrained.",
                ExpectedEffect = "Increase edge throughput and revenue capture.",
                IsPolicyAction = false
            });
        }

        if (actions.Count == 0)
        {
            actions.Add(BuildNoOp("No profitable action available this tick."));
        }

        return new SimulationActorDecision
        {
            ActorId = State.Id,
            Tick = context.Tick,
            Actions = actions,
            ActionType = actions.FirstOrDefault()?.Kind.ToString() ?? SimulationActorActionKind.NoOp.ToString(),
            TargetId = actions.FirstOrDefault()?.TargetEdgeId ?? actions.FirstOrDefault()?.TargetNodeId ?? "(none)",
            ReasonSummary = "Firm actor evaluated demand and profitability levers for permitted assets.",
            Factors = actions.Select(action => action.Reason).Distinct(Comparer).ToList(),
            Alternatives = ["No operation", "Hold price and capacity steady"],
            ExpectedOutcome = "Increase delivered volume and maintain positive margin.",
            Utility = utilityBefore + actions.Count(a => a.Kind != SimulationActorActionKind.NoOp),
            UtilityBefore = utilityBefore,
            ExpectedUtilityAfter = utilityBefore + actions.Count(a => a.Kind != SimulationActorActionKind.NoOp),
            Explanation = "Firm actor evaluated demand, delivery, route constraints, and profit levers.",
            Evidence = context.CurrentInsights.Take(3).Select(i => i.Summary).ToList()
        };
    }

    private SimulationActorActionKind? ResolveMarketAction(
        SimulationActorActionKind semanticAction,
        SimulationActorActionKind fallbackAction,
        string trafficType,
        string nodeId)
    {
        if (IsPermittedByPermissions(semanticAction, trafficType, nodeId))
        {
            return semanticAction;
        }

        return IsPermittedByPermissions(fallbackAction, trafficType, nodeId)
            ? fallbackAction
            : null;
    }

    private static double EstimateMargin(NetworkModel network, NodeTrafficProfile profile, TrafficSimulationOutcome outcome)
    {
        if (outcome.TotalDelivered > 0d)
        {
            return outcome.TotalProfit / outcome.TotalDelivered;
        }

        var definition = network.TrafficTypes.FirstOrDefault(definition => Comparer.Equals(definition.Name, profile.TrafficType));
        var unitPrice = profile.UnitPrice > 0d ? profile.UnitPrice : Math.Max(0d, definition?.DefaultUnitSalePrice ?? 0d);
        var productionCost = profile.ProductionCostPerUnit ?? Math.Max(0d, definition?.DefaultUnitProductionCost ?? 0d);
        return unitPrice - productionCost;
    }

    private SimulationActorAction BuildNoOp(string reason) => new()
    {
        Id = $"{State.Id}:noop",
        ActorId = State.Id,
        Kind = SimulationActorActionKind.NoOp,
        Reason = reason,
        ExpectedEffect = "No changes applied."
    };
}

public sealed class GovernmentSimulationActor : SimulationActorBase
{
    public GovernmentSimulationActor(SimulationActorState state) : base(state) { }

    public override SimulationActorDecision Decide(SimulationActorContext context)
    {
        var actions = new List<SimulationActorAction>();
        var outcomes = context.CurrentSnapshot.TrafficOutcomes;
        var utilityBefore = EstimateUtility(State.Objective, outcomes);

        foreach (var outcome in outcomes)
        {
            if (outcome.UnmetDemand > 0d)
            {
                var bottleneck = context.CurrentInsights
                    .FirstOrDefault(i => i.Category == InsightCategory.Capacity && !string.IsNullOrWhiteSpace(i.TargetEdgeId));
                if (bottleneck?.TargetEdgeId is { Length: > 0 } edgeId)
                {
                    actions.Add(new SimulationActorAction
                    {
                        Id = $"{State.Id}:sub:{context.Tick}:{edgeId}",
                        ActorId = State.Id,
                        Kind = SimulationActorActionKind.SubsidiseCapacity,
                        TargetEdgeId = edgeId,
                        DeltaValue = 2d,
                        Cost = 2d,
                        Reason = "Unmet demand is high; subsidise bottleneck capacity.",
                        ExpectedEffect = "Improve public service delivery.",
                        IsPolicyAction = true
                    });
                }
            }

            if (context.PolicySettings.ConstrainedTrafficTypes.Contains(outcome.TrafficType))
            {
                foreach (var edge in context.CurrentNetwork.Edges
                             .Where(edge => IsPermittedByPermissions(SimulationActorActionKind.BanTrafficOnEdge, outcome.TrafficType, edgeId: edge.Id)))
                {
                    actions.Add(new SimulationActorAction
                    {
                        Id = $"{State.Id}:ban:{context.Tick}:{edge.Id}:{outcome.TrafficType}",
                        ActorId = State.Id,
                        Kind = SimulationActorActionKind.BanTrafficOnEdge,
                        TargetEdgeId = edge.Id,
                        TrafficType = outcome.TrafficType,
                        Reason = "Policy requires constrained traffic type enforcement.",
                        ExpectedEffect = "Restrict disallowed traffic movement.",
                        IsPolicyAction = true,
                        IsReversible = true
                    });
                }
            }
        }

        if (context.CurrentInsights.Any(i => i.Category == InsightCategory.Connectivity))
        {
            var cheapest = context.CurrentNetwork.Edges.OrderBy(e => e.Cost).FirstOrDefault();
            if (cheapest is not null)
            {
                actions.Add(new SimulationActorAction
                {
                    Id = $"{State.Id}:relax:{context.Tick}:{cheapest.Id}",
                    ActorId = State.Id,
                    Kind = SimulationActorActionKind.AdjustRoutePermission,
                    TargetEdgeId = cheapest.Id,
                    DeltaValue = 1d,
                    Reason = "Disconnected network; restore connectivity on affordable corridor.",
                    ExpectedEffect = "Increase reachable node set.",
                    IsPolicyAction = true
                });
            }
        }

        if (actions.Count == 0 && HasSpendingCapacity)
        {
            var targetEdge = context.CurrentNetwork.Edges
                .Where(edge => IsPermittedByPermissions(SimulationActorActionKind.SubsidiseCapacity, edgeId: edge.Id))
                .OrderBy(edge => edge.Capacity ?? 0d)
                .ThenBy(edge => edge.Id, Comparer)
                .FirstOrDefault();
            if (targetEdge is not null)
            {
                actions.Add(new SimulationActorAction
                {
                    Id = $"{State.Id}:resilience:{context.Tick}:{targetEdge.Id}",
                    ActorId = State.Id,
                    Kind = SimulationActorActionKind.SubsidiseCapacity,
                    TargetEdgeId = targetEdge.Id,
                    DeltaValue = 2d,
                    Cost = 2d,
                    Reason = "Permitted public route has no urgent incident; fund a small resilience improvement.",
                    ExpectedEffect = "Increase route capacity under government oversight.",
                    IsPolicyAction = true
                });
            }
        }

        if (actions.Count == 0)
        {
            actions.Add(new SimulationActorAction
            {
                Id = $"{State.Id}:noop",
                ActorId = State.Id,
                Kind = SimulationActorActionKind.NoOp,
                Reason = "No policy intervention needed this tick.",
                ExpectedEffect = "Monitor network stability.",
                IsPolicyAction = true
            });
        }

        return new SimulationActorDecision
        {
            ActorId = State.Id,
            Tick = context.Tick,
            Actions = actions,
            ActionType = actions.FirstOrDefault()?.Kind.ToString() ?? SimulationActorActionKind.NoOp.ToString(),
            TargetId = actions.FirstOrDefault()?.TargetEdgeId ?? actions.FirstOrDefault()?.TargetNodeId ?? "(none)",
            ReasonSummary = "Government actor balanced service stability against policy restrictions.",
            Factors = actions.Select(action => action.Reason).Distinct(Comparer).ToList(),
            Alternatives = ["No intervention", "Delay enforcement to next tick"],
            ExpectedOutcome = "Reduce unmet demand while keeping constrained traffic compliant.",
            Utility = utilityBefore + actions.Count(a => a.Kind != SimulationActorActionKind.NoOp) * 0.75d,
            UtilityBefore = utilityBefore,
            ExpectedUtilityAfter = utilityBefore + actions.Count(a => a.Kind != SimulationActorActionKind.NoOp) * 0.75d,
            Explanation = "Government actor enforced policy constraints and service-level interventions.",
            Evidence = context.CurrentInsights.Take(4).Select(i => i.Title).ToList()
        };
    }
}

public sealed class LogisticsPlannerSimulationActor : SimulationActorBase
{
    private const double Epsilon = 0.000001d;

    public LogisticsPlannerSimulationActor(SimulationActorState state) : base(state) { }

    public override SimulationActorDecision Decide(SimulationActorContext context)
    {
        var actions = new List<SimulationActorAction>();
        var outcomes = context.CurrentSnapshot.TrafficOutcomes;
        var utilityBefore = EstimateUtility(State.Objective, outcomes);
        var totalUnmetDemand = outcomes.Sum(o => Math.Max(0d, o.UnmetDemand));
        var totalDemand = outcomes.Sum(o => Math.Max(0d, o.TotalConsumption));
        var severeUnmetDemand = totalUnmetDemand > 0d &&
            (totalUnmetDemand >= 10d || totalDemand <= Epsilon || totalUnmetDemand / totalDemand >= 0.1d);
        var flowByEdgeId = outcomes
            .SelectMany(o => o.Allocations)
            .Where(a => a.PathEdgeIds is { Count: > 0 })
            .SelectMany(a => a.PathEdgeIds!.Select(edgeId => new { EdgeId = edgeId, a.Quantity }))
            .GroupBy(item => item.EdgeId, Comparer)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity), Comparer);

        if (severeUnmetDemand)
        {
            foreach (var action in BuildUnmetDemandActions(context, outcomes, flowByEdgeId))
            {
                actions.Add(action);
                if (actions.Count >= 2)
                {
                    break;
                }
            }
        }

        foreach (var edge in context.CurrentNetwork.Edges.OrderBy(e => e.Cost).ThenBy(e => e.Id, Comparer))
        {
            var edgeOutcomes = outcomes
                .SelectMany(o => o.Allocations)
                .Where(a => a.PathEdgeIds?.Contains(edge.Id, Comparer) == true)
                .ToList();
            var routed = edgeOutcomes.Sum(a => a.Quantity);
            var capacity = edge.Capacity;
            var utilisation = capacity.HasValue && capacity.Value > 0d ? routed / capacity.Value : 0d;

            if (utilisation >= context.PolicySettings.OverloadThreshold &&
                IsPermittedByPermissions(SimulationActorActionKind.AdjustEdgeCapacity, edgeId: edge.Id))
            {
                actions.Add(new SimulationActorAction
                {
                    Id = $"{State.Id}:cap:{context.Tick}:{edge.Id}",
                    ActorId = State.Id,
                    Kind = SimulationActorActionKind.AdjustEdgeCapacity,
                    TargetEdgeId = edge.Id,
                    DeltaValue = Math.Max(1d, (capacity ?? routed) * 0.15d),
                    Cost = 1d,
                    Reason = "Edge is near capacity; expand before congestion worsens.",
                    ExpectedEffect = "Reduce unmet demand on constrained paths.",
                    IsPolicyAction = false
                });
            }
            else if (edge.Cost > context.CurrentNetwork.Edges.Average(e => e.Cost) * 1.5d &&
                IsPermittedByPermissions(SimulationActorActionKind.PreferRoute, edgeId: edge.Id))
            {
                actions.Add(new SimulationActorAction
                {
                    Id = $"{State.Id}:prefer:{context.Tick}:{edge.Id}",
                    ActorId = State.Id,
                    Kind = SimulationActorActionKind.PreferRoute,
                    TargetEdgeId = edge.Id,
                    DeltaValue = -0.5d,
                    Reason = "Current path set includes expensive route; prefer cheaper alternatives.",
                    ExpectedEffect = "Lower blended movement cost.",
                    IsPolicyAction = false
                });
            }
        }

        if (actions.Count == 0 && severeUnmetDemand)
        {
            var targetEdge = context.CurrentNetwork.Edges
                .Where(edge => IsPermittedByPermissions(SimulationActorActionKind.AdjustEdgeCapacity, edgeId: edge.Id))
                .OrderBy(edge => edge.Capacity ?? double.PositiveInfinity)
                .ThenByDescending(edge => edge.Cost)
                .ThenBy(edge => edge.Id, Comparer)
                .FirstOrDefault();
            if (targetEdge is not null)
            {
                var baseline = Math.Max(1d, targetEdge.Capacity ?? 1d);
                actions.Add(new SimulationActorAction
                {
                    Id = $"{State.Id}:proactive-cap:{context.Tick}:{targetEdge.Id}",
                    ActorId = State.Id,
                    Kind = SimulationActorActionKind.AdjustEdgeCapacity,
                    TargetEdgeId = targetEdge.Id,
                    DeltaValue = Math.Max(5d, baseline * 0.5d),
                    Cost = 1d,
                    Reason = $"Severe unmet demand ({totalUnmetDemand:0.##}) persists while routed utilisation is low; proactively expand the lowest-capacity corridor.",
                    ExpectedEffect = "Create route headroom for unmet demand that is not visible in current edge utilisation.",
                    IsPolicyAction = false
                });
            }
        }

        if (actions.Count == 0 && totalUnmetDemand <= 0d)
        {
            var targetEdge = context.CurrentNetwork.Edges
                .Where(edge => edge.Cost > 0d && IsPermittedByPermissions(SimulationActorActionKind.PreferRoute, edgeId: edge.Id))
                .OrderByDescending(edge => edge.Cost)
                .ThenBy(edge => edge.Id, Comparer)
                .FirstOrDefault();
            if (targetEdge is not null)
            {
                actions.Add(new SimulationActorAction
                {
                    Id = $"{State.Id}:route-tune:{context.Tick}:{targetEdge.Id}",
                    ActorId = State.Id,
                    Kind = SimulationActorActionKind.PreferRoute,
                    TargetEdgeId = targetEdge.Id,
                    DeltaValue = -Math.Min(0.5d, Math.Max(0.1d, targetEdge.Cost * 0.1d)),
                    Reason = "Permitted route has no active congestion; tune route preference cost for future flow.",
                    ExpectedEffect = "Make the permitted route visibly cheaper for allocation.",
                    IsPolicyAction = false
                });
            }
        }

        if (actions.Count == 0)
        {
            actions.Add(new SimulationActorAction
            {
                Id = $"{State.Id}:noop:{context.Tick}",
                ActorId = State.Id,
                Kind = SimulationActorActionKind.NoOp,
                Reason = totalUnmetDemand <= 0d
                    ? "Unmet demand is already minimal."
                    : $"No permitted planner action found for {totalUnmetDemand:0.##} unmet demand; check actor edge permissions, route permissions, supply, and transhipment roles.",
                ExpectedEffect = totalUnmetDemand <= 0d
                    ? "No logistics intervention required."
                    : "No change until a permitted corridor, capacity, or route-cost action becomes available."
            });
        }

        return new SimulationActorDecision
        {
            ActorId = State.Id,
            Tick = context.Tick,
            Actions = actions,
            ActionType = actions.FirstOrDefault()?.Kind.ToString() ?? SimulationActorActionKind.NoOp.ToString(),
            TargetId = actions.FirstOrDefault()?.TargetEdgeId ?? actions.FirstOrDefault()?.TargetNodeId ?? "(none)",
            ReasonSummary = actions.Any(action => action.Kind == SimulationActorActionKind.NoOp)
                ? actions[0].Reason
                : "Logistics planner targeted unmet demand, congestion, and route-cost inefficiencies.",
            Factors = actions.Select(action => action.Reason).Distinct(Comparer).ToList(),
            Alternatives = ["No rerouting", "Maintain current capacities", "Wait for utilisation to rise"],
            ExpectedOutcome = totalUnmetDemand > 0d
                ? "Reduce unmet demand by adding headroom or lowering cost on plausible unmet-demand corridors."
                : "Lower movement costs and reduce congested-path demand.",
            Utility = utilityBefore + actions.Count(a => a.Kind != SimulationActorActionKind.NoOp),
            UtilityBefore = utilityBefore,
            ExpectedUtilityAfter = utilityBefore + actions.Count(a => a.Kind != SimulationActorActionKind.NoOp),
            Explanation = actions.Any(action => action.Kind == SimulationActorActionKind.NoOp)
                ? actions[0].Reason
                : "Logistics planner optimized for lower unmet demand and lower movement cost.",
            Evidence = context.CurrentInsights.Select(i => i.Summary).Take(3).ToList()
        };
    }

    private IEnumerable<SimulationActorAction> BuildUnmetDemandActions(
        SimulationActorContext context,
        IReadOnlyList<TrafficSimulationOutcome> outcomes,
        IReadOnlyDictionary<string, double> flowByEdgeId)
    {
        var network = context.CurrentNetwork;
        var edgesById = network.Edges.ToDictionary(edge => edge.Id, Comparer);
        var averageCost = network.Edges.Count == 0 ? 0d : network.Edges.Average(edge => edge.Cost);

        foreach (var outcome in outcomes.Where(o => o.UnmetDemand > Epsilon).OrderByDescending(o => o.UnmetDemand))
        {
            foreach (var path in FindUnmetDemandPaths(network, outcome.TrafficType).Take(3))
            {
                var pathEdges = path
                    .Select(edgeId => edgesById.TryGetValue(edgeId, out var edge) ? edge : null)
                    .Where(edge => edge is not null)
                    .Cast<EdgeModel>()
                    .ToList();
                if (pathEdges.Count == 0)
                {
                    continue;
                }

                var constrainedEdge = pathEdges
                    .Where(edge => IsPermittedByPermissions(SimulationActorActionKind.AdjustEdgeCapacity, outcome.TrafficType, edgeId: edge.Id))
                    .OrderBy(edge => edge.Capacity ?? double.PositiveInfinity)
                    .ThenBy(edge => flowByEdgeId.GetValueOrDefault(edge.Id))
                    .ThenBy(edge => edge.Id, Comparer)
                    .FirstOrDefault();
                if (constrainedEdge is not null)
                {
                    var routed = flowByEdgeId.GetValueOrDefault(constrainedEdge.Id);
                    var currentCapacity = constrainedEdge.Capacity ?? Math.Max(1d, routed);
                    var targetHeadroom = Math.Max(outcome.UnmetDemand * 0.25d, 5d);
                    yield return new SimulationActorAction
                    {
                        Id = $"{State.Id}:unmet-cap:{context.Tick}:{outcome.TrafficType}:{constrainedEdge.Id}",
                        ActorId = State.Id,
                        Kind = SimulationActorActionKind.AdjustEdgeCapacity,
                        TargetEdgeId = constrainedEdge.Id,
                        TrafficType = outcome.TrafficType,
                        DeltaValue = Math.Max(targetHeadroom, Math.Max(1d, currentCapacity * 0.5d)),
                        Cost = 1d,
                        Reason = $"Severe unmet {outcome.TrafficType} demand ({outcome.UnmetDemand:0.##}) has a plausible route through a low-capacity corridor.",
                        ExpectedEffect = "Increase corridor headroom even though current utilisation is low or zero.",
                        IsPolicyAction = false
                    };
                    continue;
                }

                var expensiveEdge = pathEdges
                    .Where(edge => edge.Cost > 0d &&
                        edge.Cost >= Math.Max(averageCost * 1.25d, averageCost + 0.1d) &&
                        IsPermittedByPermissions(SimulationActorActionKind.AdjustEdgeCost, outcome.TrafficType, edgeId: edge.Id))
                    .OrderByDescending(edge => edge.Cost)
                    .ThenBy(edge => edge.Id, Comparer)
                    .FirstOrDefault();
                if (expensiveEdge is not null)
                {
                    yield return new SimulationActorAction
                    {
                        Id = $"{State.Id}:unmet-cost:{context.Tick}:{outcome.TrafficType}:{expensiveEdge.Id}",
                        ActorId = State.Id,
                        Kind = SimulationActorActionKind.AdjustEdgeCost,
                        TargetEdgeId = expensiveEdge.Id,
                        TrafficType = outcome.TrafficType,
                        DeltaValue = -Math.Min(expensiveEdge.Cost * 0.2d, Math.Max(0.1d, expensiveEdge.Cost - averageCost)),
                        Cost = 0.5d,
                        Reason = $"Severe unmet {outcome.TrafficType} demand persists on a costly feasible corridor.",
                        ExpectedEffect = "Make the corridor more attractive to routing when demand remains unmet.",
                        IsPolicyAction = false
                    };
                }
            }
        }
    }

    private static IEnumerable<IReadOnlyList<string>> FindUnmetDemandPaths(NetworkModel network, string trafficType)
    {
        var profilesByNodeId = network.Nodes.ToDictionary(
            node => node.Id,
            node => node.TrafficProfiles.FirstOrDefault(profile => Comparer.Equals(profile.TrafficType, trafficType)),
            Comparer);
        var producers = profilesByNodeId
            .Where(pair => pair.Value?.Production > Epsilon)
            .Select(pair => pair.Key)
            .ToList();
        var consumers = profilesByNodeId
            .Where(pair => pair.Value?.Consumption > Epsilon)
            .OrderByDescending(pair => pair.Value!.Consumption)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var consumerId in consumers)
        {
            foreach (var producerId in producers)
            {
                var path = FindPathIgnoringCapacity(network, profilesByNodeId, trafficType, producerId, consumerId);
                if (path.Count > 0)
                {
                    yield return path;
                }
            }
        }
    }

    private static IReadOnlyList<string> FindPathIgnoringCapacity(
        NetworkModel network,
        IReadOnlyDictionary<string, NodeTrafficProfile?> profilesByNodeId,
        string trafficType,
        string producerId,
        string consumerId)
    {
        if (Comparer.Equals(producerId, consumerId))
        {
            return [];
        }

        var permissionResolver = new EdgeTrafficPermissionResolver();
        var adjacency = BuildAdjacency(network);
        var queue = new Queue<(string NodeId, List<string> EdgeIds, List<string> NodeIds)>();
        var visited = new HashSet<string>(Comparer) { producerId };
        queue.Enqueue((producerId, [], [producerId]));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current.NodeId, out var arcs))
            {
                continue;
            }

            foreach (var (edge, toNodeId) in arcs)
            {
                if (!visited.Add(toNodeId))
                {
                    continue;
                }

                if (permissionResolver.Resolve(network, edge, trafficType).Mode == EdgeTrafficPermissionMode.Blocked ||
                    !CanTraverseNode(toNodeId, producerId, consumerId, profilesByNodeId))
                {
                    continue;
                }

                var edgeIds = current.EdgeIds.Concat([edge.Id]).ToList();
                if (Comparer.Equals(toNodeId, consumerId))
                {
                    return edgeIds;
                }

                queue.Enqueue((toNodeId, edgeIds, current.NodeIds.Concat([toNodeId]).ToList()));
            }
        }

        return [];
    }

    private static Dictionary<string, List<(EdgeModel Edge, string ToNodeId)>> BuildAdjacency(NetworkModel network)
    {
        var adjacency = new Dictionary<string, List<(EdgeModel Edge, string ToNodeId)>>(Comparer);

        void Add(string fromNodeId, string toNodeId, EdgeModel edge)
        {
            if (!adjacency.TryGetValue(fromNodeId, out var arcs))
            {
                arcs = [];
                adjacency[fromNodeId] = arcs;
            }

            arcs.Add((edge, toNodeId));
        }

        foreach (var edge in network.Edges)
        {
            Add(edge.FromNodeId, edge.ToNodeId, edge);
            if (edge.IsBidirectional)
            {
                Add(edge.ToNodeId, edge.FromNodeId, edge);
            }
        }

        return adjacency;
    }

    private static bool CanTraverseNode(
        string nodeId,
        string producerId,
        string consumerId,
        IReadOnlyDictionary<string, NodeTrafficProfile?> profilesByNodeId)
    {
        return Comparer.Equals(nodeId, producerId) ||
            Comparer.Equals(nodeId, consumerId) ||
            (profilesByNodeId.TryGetValue(nodeId, out var profile) && profile?.CanTransship == true);
    }
}

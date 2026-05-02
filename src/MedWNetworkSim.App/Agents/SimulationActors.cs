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
            SimulationActorObjective.MaximiseProfit => outcomes.Sum(o => o.TotalDelivered * 2d) - outcomes.Sum(o => o.Allocations.Sum(a => a.TotalMovementCost)),
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
        var actionRules = permissions
            .Where(permission => permission.ActionKind == actionKind)
            .ToList();
        if (actionRules.Count == 0)
        {
            return true;
        }

        var matching = actionRules
            .Where(permission =>
                (permission.TrafficType == null || Comparer.Equals(permission.TrafficType, trafficType)) &&
                (permission.NodeId == null || Comparer.Equals(permission.NodeId, nodeId)) &&
                (permission.EdgeId == null || Comparer.Equals(permission.EdgeId, edgeId)))
            .ToList();

        return matching.Count == 0
            ? !actionRules.Any(permission => permission.IsAllowed)
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
                var deliveredRatio = outcome.TotalConsumption <= 0d ? 0d : outcome.TotalDelivered / outcome.TotalConsumption;

                if (profile.Production > 0d &&
                    deliveredRatio >= 0.85d &&
                    HasSpendingCapacity &&
                    IsPermittedByPermissions(SimulationActorActionKind.AdjustProduction, profile.TrafficType, node.Id))
                {
                    var delta = Math.Max(1d, profile.Production * 0.1d);
                    var cost = delta * 0.25d;
                    actions.Add(new SimulationActorAction
                    {
                        Id = $"{State.Id}:prod:{context.Tick}:{node.Id}:{profile.TrafficType}",
                        ActorId = State.Id,
                        Kind = SimulationActorActionKind.AdjustProduction,
                        TargetNodeId = node.Id,
                        TrafficType = profile.TrafficType,
                        DeltaValue = delta,
                        Cost = cost,
                        Reason = "Delivered demand is strong and production is profitable.",
                        ExpectedEffect = "Increase delivered quantity and revenue.",
                        IsPolicyAction = false
                    });
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
                else if (deliveredRatio < 0.4d &&
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
                        Reason = "Delivered demand collapsed relative to output.",
                        ExpectedEffect = "Reduce overproduction and waste.",
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
    public LogisticsPlannerSimulationActor(SimulationActorState state) : base(state) { }

    public override SimulationActorDecision Decide(SimulationActorContext context)
    {
        var actions = new List<SimulationActorAction>();
        var outcomes = context.CurrentSnapshot.TrafficOutcomes;
        var utilityBefore = EstimateUtility(State.Objective, outcomes);
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

        if (actions.Count == 0)
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

        if (actions.Count == 0 && outcomes.Sum(o => o.UnmetDemand) <= 0d)
        {
            actions.Add(new SimulationActorAction
            {
                Id = $"{State.Id}:noop",
                ActorId = State.Id,
                Kind = SimulationActorActionKind.NoOp,
                Reason = "Unmet demand is already minimal.",
                ExpectedEffect = "No logistics intervention required."
            });
        }

        return new SimulationActorDecision
        {
            ActorId = State.Id,
            Tick = context.Tick,
            Actions = actions,
            ActionType = actions.FirstOrDefault()?.Kind.ToString() ?? SimulationActorActionKind.NoOp.ToString(),
            TargetId = actions.FirstOrDefault()?.TargetEdgeId ?? actions.FirstOrDefault()?.TargetNodeId ?? "(none)",
            ReasonSummary = "Logistics planner targeted congestion and route-cost inefficiencies.",
            Factors = actions.Select(action => action.Reason).Distinct(Comparer).ToList(),
            Alternatives = ["No rerouting", "Maintain current capacities"],
            ExpectedOutcome = "Lower movement costs and reduce congested-path demand.",
            Utility = utilityBefore + actions.Count(a => a.Kind != SimulationActorActionKind.NoOp),
            UtilityBefore = utilityBefore,
            ExpectedUtilityAfter = utilityBefore + actions.Count(a => a.Kind != SimulationActorActionKind.NoOp),
            Explanation = "Logistics planner optimized for lower unmet demand and lower movement cost.",
            Evidence = context.CurrentInsights.Select(i => i.Summary).Take(3).ToList()
        };
    }
}

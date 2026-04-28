using MedWNetworkSim.App.Insights;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.VisualAnalytics;

namespace MedWNetworkSim.App.Agents;

public enum SimulationActorKind
{
    Firm,
    Government,
    LogisticsPlanner
}

public enum SimulationActorObjective
{
    MaximiseProfit,
    MinimiseUnmetDemand,
    MinimiseMovementCost,
    MaximiseThroughput,
    EnforcePolicy,
    StabiliseNetwork,
    ReduceCongestion
}

public sealed class SimulationActorState
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SimulationActorKind Kind { get; set; }
    public SimulationActorObjective Objective { get; set; }
    public List<string> ControlledNodeIds { get; set; } = [];
    public List<string> ControlledEdgeIds { get; set; } = [];
    public double Budget { get; set; }
    public double Cash { get; set; }
    public double RiskTolerance { get; set; } = 0.5d;
    public double CooperationWeight { get; set; } = 0.5d;
    public bool IsEnabled { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
    public bool GenerateAutomaticDecisions { get; set; } = true;
    public SimulationActorCapability Capability { get; set; } = SimulationActorCapabilityCatalog.ForKind(string.Empty, SimulationActorKind.Firm);
}

public enum SimulationActorActionKind
{
    BuyTraffic,
    SellTraffic,
    AdjustProduction,
    AdjustConsumption,
    AdjustEdgeCapacity,
    AdjustEdgeCost,
    AdjustRoutePermission,
    AdjustTrafficPrice,
    SubsidiseCapacity,
    TaxRoute,
    BanTrafficOnEdge,
    SetNodePolicy,
    SetEdgePolicy,
    PreferRoute,
    NoOp
}

public sealed class SimulationActorCapability
{
    public string ActorId { get; set; } = string.Empty;
    public IReadOnlyCollection<SimulationActorActionKind> AllowedActionKinds { get; set; } = [];
    public IReadOnlyCollection<string> AllowedTrafficTypes { get; set; } = [];
    public bool AllowAllTrafficTypes { get; set; } = true;
    public bool IsCustomActorType { get; set; }
    public string? CustomActorTypeName { get; set; }
}

public static class SimulationActorCapabilityCatalog
{
    public static SimulationActorCapability ForKind(string actorId, SimulationActorKind kind)
    {
        IReadOnlyCollection<SimulationActorActionKind> allowed = kind switch
        {
            SimulationActorKind.Firm =>
            [
                SimulationActorActionKind.BuyTraffic,
                SimulationActorActionKind.SellTraffic,
                SimulationActorActionKind.AdjustProduction,
                SimulationActorActionKind.AdjustConsumption,
                SimulationActorActionKind.AdjustTrafficPrice,
                SimulationActorActionKind.PreferRoute,
                SimulationActorActionKind.AdjustEdgeCapacity
            ],
            SimulationActorKind.Government =>
            [
                SimulationActorActionKind.AdjustRoutePermission,
                SimulationActorActionKind.BanTrafficOnEdge,
                SimulationActorActionKind.TaxRoute,
                SimulationActorActionKind.SubsidiseCapacity,
                SimulationActorActionKind.SetNodePolicy,
                SimulationActorActionKind.SetEdgePolicy,
                SimulationActorActionKind.AdjustEdgeCapacity
            ],
            SimulationActorKind.LogisticsPlanner =>
            [
                SimulationActorActionKind.PreferRoute,
                SimulationActorActionKind.AdjustEdgeCapacity,
                SimulationActorActionKind.AdjustEdgeCost
            ],
            _ => [SimulationActorActionKind.NoOp]
        };

        return new SimulationActorCapability
        {
            ActorId = actorId,
            AllowedActionKinds = allowed,
            AllowedTrafficTypes = [],
            AllowAllTrafficTypes = true
        };
    }
}

public sealed class SimulationActorAction
{
    public string Id { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public SimulationActorActionKind Kind { get; set; }
    public string? TargetNodeId { get; set; }
    public string? TargetEdgeId { get; set; }
    public string? TrafficType { get; set; }
    public double DeltaValue { get; set; }
    public double? AbsoluteValue { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ExpectedEffect { get; set; } = string.Empty;
    public double Cost { get; set; }
    public bool IsPolicyAction { get; set; }
    public bool IsReversible { get; set; } = true;
    public bool IsForced { get; set; }
}

public sealed class SimulationActorDecision
{
    public string ActorId { get; set; } = string.Empty;
    public int Tick { get; set; }
    public List<SimulationActorAction> Actions { get; set; } = [];
    public string ActionType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string ReasonSummary { get; set; } = string.Empty;
    public List<string> Factors { get; set; } = [];
    public List<string>? Alternatives { get; set; }
    public string ExpectedOutcome { get; set; } = string.Empty;
    public double Utility { get; set; }
    public double UtilityBefore { get; set; }
    public double ExpectedUtilityAfter { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public List<string> Evidence { get; set; } = [];
}

public sealed class SimulationActorMetrics
{
    public int Tick { get; set; }
    public double TotalDelivered { get; set; }
    public double TotalUnmetDemand { get; set; }
    public double TotalMovementCost { get; set; }
    public double AverageEdgeUtilisation { get; set; }
    public int BottleneckEdgeCount { get; set; }
    public Dictionary<string, double> ActorCashById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> ActorUtilityById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int PolicyRestrictionCount { get; set; }
    public double CooperationIndex { get; set; }
}

public sealed class SimulationActorRunResult
{
    public required NetworkModel InitialNetwork { get; init; }
    public required NetworkModel FinalNetwork { get; init; }
    public required IReadOnlyList<SimulationActorDecision> DecisionsByTick { get; init; }
    public required IReadOnlyList<SimulationActorMetrics> MetricsByTick { get; init; }
    public required string FinalSummary { get; init; }
}

public sealed class SimulationActorPolicySettings
{
    public IReadOnlySet<string> ConstrainedTrafficTypes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public double OverloadThreshold { get; init; } = 0.9d;
    public bool AllowForcedCapacityReduction { get; init; }
}

public sealed class SimulationActorContext
{
    public required NetworkModel CurrentNetwork { get; init; }
    public required VisualAnalyticsSnapshot CurrentSnapshot { get; init; }
    public required IReadOnlyList<NetworkInsight> CurrentInsights { get; init; }
    public required int Tick { get; init; }
    public required IReadOnlyList<SimulationActorDecision> PreviousDecisions { get; init; }
    public required SimulationActorPolicySettings PolicySettings { get; init; }
}

public interface ISimulationActor
{
    SimulationActorState State { get; }
    SimulationActorDecision Decide(SimulationActorContext context);
}

public sealed class SimulationActorActionOutcome
{
    public required SimulationActorAction Action { get; init; }
    public bool Applied { get; init; }
    public required string Reason { get; init; }
}

public sealed class SimulationActorStepResult
{
    public required NetworkModel NetworkAfterStep { get; init; }
    public required IReadOnlyList<SimulationActorDecision> Decisions { get; init; }
    public required IReadOnlyList<SimulationActorActionOutcome> ActionOutcomes { get; init; }
    public required SimulationActorMetrics Metrics { get; init; }
}

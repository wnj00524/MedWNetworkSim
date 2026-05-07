using MedWNetworkSim.App.Insights;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.VisualAnalytics;

namespace MedWNetworkSim.App.Agents;
/// <summary>
/// Specifies the simulation actor kind.
/// </summary>

public enum SimulationActorKind
{
    Firm,
    Government,
    LogisticsPlanner
}
/// <summary>
/// Specifies the simulation actor objective.
/// </summary>

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
/// <summary>
/// Represents the simulation actor state component.
/// </summary>

public sealed class SimulationActorState
{
    /// <summary>
    /// Gets or sets the unique identifier for this instance.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the kind.
    /// </summary>
    public SimulationActorKind Kind { get; set; }
    /// <summary>
    /// Gets or sets the objective.
    /// </summary>
    public SimulationActorObjective Objective { get; set; }
    /// <summary>
    /// Gets the collection of controlled node ids associated with this entity.
    /// </summary>
    public List<string> ControlledNodeIds { get; set; } = [];
    /// <summary>
    /// Gets the collection of controlled edge ids associated with this entity.
    /// </summary>
    public List<string> ControlledEdgeIds { get; set; } = [];
    /// <summary>
    /// Gets or sets the budget.
    /// </summary>
    public double Budget { get; set; }
    /// <summary>
    /// Gets or sets the cash.
    /// </summary>
    public double Cash { get; set; }
    /// <summary>
    /// Gets or sets the risk tolerance.
    /// </summary>
    public double RiskTolerance { get; set; } = 0.5d;
    /// <summary>
    /// Gets or sets the cooperation weight.
    /// </summary>
    public double CooperationWeight { get; set; } = 0.5d;
    /// <summary>
    /// Gets a value indicating whether is enabled is enabled or active.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>
    /// Gets or sets the notes.
    /// </summary>
    public string Notes { get; set; } = string.Empty;
    /// <summary>
    /// Gets a value indicating whether generate automatic decisions is enabled or active.
    /// </summary>
    public bool GenerateAutomaticDecisions { get; set; } = true;
    /// <summary>
    /// Gets or sets the capability.
    /// </summary>
    public SimulationActorCapability Capability { get; set; } = SimulationActorCapabilityCatalog.ForKind(string.Empty, SimulationActorKind.Firm);
}
/// <summary>
/// Specifies the simulation actor action kind.
/// </summary>

public enum SimulationActorActionKind
{
    BuyTraffic,
    SellTraffic,
    SellLocal,
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
/// <summary>
/// Represents the simulation actor capability component.
/// </summary>

public sealed class SimulationActorCapability
{
    /// <summary>
    /// Gets or sets the actor id.
    /// </summary>
    public string ActorId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the allowed action kinds.
    /// </summary>
    public IReadOnlyCollection<SimulationActorActionKind> AllowedActionKinds { get; set; } = [];
    /// <summary>
    /// Gets or sets the allowed traffic types.
    /// </summary>
    public IReadOnlyCollection<string> AllowedTrafficTypes { get; set; } = [];
    /// <summary>
    /// Gets the collection of permissions associated with this entity.
    /// </summary>
    public List<SimulationActorPermission> Permissions { get; set; } = [];
    /// <summary>
    /// Gets a value indicating whether allow all traffic types is enabled or active.
    /// </summary>
    public bool AllowAllTrafficTypes { get; set; } = true;
    /// <summary>
    /// Gets a value indicating whether is custom actor type is enabled or active.
    /// </summary>
    public bool IsCustomActorType { get; set; }
    /// <summary>
    /// Gets or sets the custom actor type name.
    /// </summary>
    public string? CustomActorTypeName { get; set; }
}
/// <summary>
/// Represents the simulation actor capability catalog component.
/// </summary>

public static class SimulationActorCapabilityCatalog
{
    /// <summary>
    /// Executes the for kind operation.
    /// </summary>
    public static SimulationActorCapability ForKind(string actorId, SimulationActorKind kind)
    {
        IReadOnlyCollection<SimulationActorActionKind> allowed = kind switch
        {
            SimulationActorKind.Firm =>
            [
                SimulationActorActionKind.BuyTraffic,
                SimulationActorActionKind.SellTraffic,
                SimulationActorActionKind.SellLocal,
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
/// <summary>
/// Represents the simulation actor action component.
/// </summary>

public sealed class SimulationActorAction
{
    /// <summary>
    /// Gets or sets the unique identifier for this instance.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the actor id.
    /// </summary>
    public string ActorId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the kind.
    /// </summary>
    public SimulationActorActionKind Kind { get; set; }
    /// <summary>
    /// Gets or sets the target node id.
    /// </summary>
    public string? TargetNodeId { get; set; }
    /// <summary>
    /// Gets or sets the target edge id.
    /// </summary>
    public string? TargetEdgeId { get; set; }
    /// <summary>
    /// Gets or sets the traffic type.
    /// </summary>
    public string? TrafficType { get; set; }
    /// <summary>
    /// Gets or sets the delta value.
    /// </summary>
    public double DeltaValue { get; set; }
    /// <summary>
    /// Gets or sets the absolute value.
    /// </summary>
    public double? AbsoluteValue { get; set; }
    /// <summary>
    /// Gets or sets the reason.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the expected effect.
    /// </summary>
    public string ExpectedEffect { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the cost.
    /// </summary>
    public double Cost { get; set; }
    /// <summary>
    /// Gets a value indicating whether is policy action is enabled or active.
    /// </summary>
    public bool IsPolicyAction { get; set; }
    /// <summary>
    /// Gets a value indicating whether is reversible is enabled or active.
    /// </summary>
    public bool IsReversible { get; set; } = true;
    /// <summary>
    /// Gets a value indicating whether is forced is enabled or active.
    /// </summary>
    public bool IsForced { get; set; }
}
/// <summary>
/// Represents the traffic purchase intent component.
/// </summary>

public sealed class TrafficPurchaseIntent
{
    /// <summary>
    /// Gets or sets the buyer actor id.
    /// </summary>
    public string BuyerActorId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the target node id.
    /// </summary>
    public string TargetNodeId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the traffic type.
    /// </summary>
    public string TrafficType { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the requested quantity.
    /// </summary>
    public double RequestedQuantity { get; set; }
    /// <summary>
    /// Gets or sets the max unit price.
    /// </summary>
    public double? MaxUnitPrice { get; set; }
    /// <summary>
    /// Gets or sets the offered premium.
    /// </summary>
    public double OfferedPremium { get; set; }
    /// <summary>
    /// Gets or sets the tick.
    /// </summary>
    public int Tick { get; set; }
}
/// <summary>
/// Represents the simulation actor decision component.
/// </summary>

public sealed class SimulationActorDecision
{
    /// <summary>
    /// Gets or sets the actor id.
    /// </summary>
    public string ActorId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the tick.
    /// </summary>
    public int Tick { get; set; }
    /// <summary>
    /// Gets the collection of actions associated with this entity.
    /// </summary>
    public List<SimulationActorAction> Actions { get; set; } = [];
    /// <summary>
    /// Gets or sets the action type.
    /// </summary>
    public string ActionType { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the target id.
    /// </summary>
    public string TargetId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the reason summary.
    /// </summary>
    public string ReasonSummary { get; set; } = string.Empty;
    /// <summary>
    /// Gets the collection of factors associated with this entity.
    /// </summary>
    public List<string> Factors { get; set; } = [];
    /// <summary>
    /// Gets the collection of alternatives associated with this entity.
    /// </summary>
    public List<string>? Alternatives { get; set; }
    /// <summary>
    /// Gets or sets the expected outcome.
    /// </summary>
    public string ExpectedOutcome { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the utility.
    /// </summary>
    public double Utility { get; set; }
    /// <summary>
    /// Gets or sets the utility before.
    /// </summary>
    public double UtilityBefore { get; set; }
    /// <summary>
    /// Gets or sets the expected utility after.
    /// </summary>
    public double ExpectedUtilityAfter { get; set; }
    /// <summary>
    /// Gets or sets the explanation.
    /// </summary>
    public string Explanation { get; set; } = string.Empty;
    /// <summary>
    /// Gets the collection of evidence associated with this entity.
    /// </summary>
    public List<string> Evidence { get; set; } = [];
}
/// <summary>
/// Represents the simulation actor metrics component.
/// </summary>

public sealed class SimulationActorMetrics
{
    /// <summary>
    /// Gets or sets the tick.
    /// </summary>
    public int Tick { get; set; }
    /// <summary>
    /// Gets or sets the total delivered.
    /// </summary>
    public double TotalDelivered { get; set; }
    /// <summary>
    /// Gets or sets the total unmet demand.
    /// </summary>
    public double TotalUnmetDemand { get; set; }
    /// <summary>
    /// Gets or sets the total movement cost.
    /// </summary>
    public double TotalMovementCost { get; set; }
    /// <summary>
    /// Gets or sets the average edge utilisation.
    /// </summary>
    public double AverageEdgeUtilisation { get; set; }
    /// <summary>
    /// Gets or sets the bottleneck edge count.
    /// </summary>
    public int BottleneckEdgeCount { get; set; }
    /// <summary>
    /// Gets or sets the actor cash by id.
    /// </summary>
    public Dictionary<string, double> ActorCashById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the actor utility by id.
    /// </summary>
    public Dictionary<string, double> ActorUtilityById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the actor sales revenue by id.
    /// </summary>
    public Dictionary<string, double> ActorSalesRevenueById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the actor purchase cost by id.
    /// </summary>
    public Dictionary<string, double> ActorPurchaseCostById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the actor production cost by id.
    /// </summary>
    public Dictionary<string, double> ActorProductionCostById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the actor transport cost by id.
    /// </summary>
    public Dictionary<string, double> ActorTransportCostById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the actor taxes paid by id.
    /// </summary>
    public Dictionary<string, double> ActorTaxesPaidById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the actor taxes received by id.
    /// </summary>
    public Dictionary<string, double> ActorTaxesReceivedById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the actor profit by id.
    /// </summary>
    public Dictionary<string, double> ActorProfitById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the actor cash delta by id.
    /// </summary>
    public Dictionary<string, double> ActorCashDeltaById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the policy restriction count.
    /// </summary>
    public int PolicyRestrictionCount { get; set; }
    /// <summary>
    /// Gets or sets the cooperation index.
    /// </summary>
    public double CooperationIndex { get; set; }
}
/// <summary>
/// Represents the simulation actor run result component.
/// </summary>

public sealed class SimulationActorRunResult
{
    /// <summary>
    /// Gets or sets the initial network.
    /// </summary>
    public required NetworkModel InitialNetwork { get; init; }
    /// <summary>
    /// Gets or sets the final network.
    /// </summary>
    public required NetworkModel FinalNetwork { get; init; }
    /// <summary>
    /// Gets the collection of decisions by tick associated with this entity.
    /// </summary>
    public required IReadOnlyList<SimulationActorDecision> DecisionsByTick { get; init; }
    /// <summary>
    /// Gets the collection of metrics by tick associated with this entity.
    /// </summary>
    public required IReadOnlyList<SimulationActorMetrics> MetricsByTick { get; init; }
    /// <summary>
    /// Gets or sets the final summary.
    /// </summary>
    public required string FinalSummary { get; init; }
}
/// <summary>
/// Represents the simulation actor policy settings component.
/// </summary>

public sealed class SimulationActorPolicySettings
{
    /// <summary>
    /// Gets or sets the constrained traffic types.
    /// </summary>
    public IReadOnlySet<string> ConstrainedTrafficTypes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the overload threshold.
    /// </summary>
    public double OverloadThreshold { get; init; } = 0.9d;
    /// <summary>
    /// Gets a value indicating whether allow forced capacity reduction is enabled or active.
    /// </summary>
    public bool AllowForcedCapacityReduction { get; init; }
}
/// <summary>
/// Represents the simulation actor context component.
/// </summary>

public sealed class SimulationActorContext
{
    /// <summary>
    /// Gets or sets the current network.
    /// </summary>
    public required NetworkModel CurrentNetwork { get; init; }
    /// <summary>
    /// Gets or sets the current snapshot.
    /// </summary>
    public required VisualAnalyticsSnapshot CurrentSnapshot { get; init; }
    /// <summary>
    /// Gets the collection of current insights associated with this entity.
    /// </summary>
    public required IReadOnlyList<NetworkInsight> CurrentInsights { get; init; }
    /// <summary>
    /// Gets or sets the tick.
    /// </summary>
    public required int Tick { get; init; }
    /// <summary>
    /// Gets the collection of previous decisions associated with this entity.
    /// </summary>
    public required IReadOnlyList<SimulationActorDecision> PreviousDecisions { get; init; }
    /// <summary>
    /// Gets or sets the policy settings.
    /// </summary>
    public required SimulationActorPolicySettings PolicySettings { get; init; }
}
/// <summary>
/// Defines the contract and required members for isimulation actor implementations.
/// </summary>

public interface ISimulationActor
{
    SimulationActorState State { get; }
    SimulationActorDecision Decide(SimulationActorContext context);
}
/// <summary>
/// Represents the simulation actor action outcome component.
/// </summary>

public sealed class SimulationActorActionOutcome
{
    /// <summary>
    /// Gets or sets the action.
    /// </summary>
    public required SimulationActorAction Action { get; init; }
    /// <summary>
    /// Gets a value indicating whether applied is enabled or active.
    /// </summary>
    public bool Applied { get; init; }
    /// <summary>
    /// Gets or sets the reason.
    /// </summary>
    public required string Reason { get; init; }
}
/// <summary>
/// Represents the simulation actor step result component.
/// </summary>

public sealed class SimulationActorStepResult
{
    /// <summary>
    /// Gets or sets the network after step.
    /// </summary>
    public required NetworkModel NetworkAfterStep { get; init; }
    /// <summary>
    /// Gets the collection of decisions associated with this entity.
    /// </summary>
    public required IReadOnlyList<SimulationActorDecision> Decisions { get; init; }
    /// <summary>
    /// Gets the collection of action outcomes associated with this entity.
    /// </summary>
    public required IReadOnlyList<SimulationActorActionOutcome> ActionOutcomes { get; init; }
    /// <summary>
    /// Gets or sets the metrics.
    /// </summary>
    public required SimulationActorMetrics Metrics { get; init; }
}

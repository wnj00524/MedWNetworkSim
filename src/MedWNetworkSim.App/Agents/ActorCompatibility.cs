using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

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

public enum SimulationActorActionKind
{
    NoOp,
    AdjustProduction,
    AdjustConsumption,
    AdjustTrafficPrice,
    AdjustEdgeCapacity,
    SubsidiseCapacity,
    AdjustEdgeCost,
    TaxRoute,
    PreferRoute,
    BanTrafficOnEdge,
    AdjustRoutePermission,
    BuyTraffic,
    SellTraffic,
    SellLocal,
    SetNodePolicy,
    SetEdgePolicy
}

public sealed class SimulationActorPermission
{
    public SimulationActorActionKind ActionKind { get; set; }
    public string? TrafficType { get; set; }
    public string? NodeId { get; set; }
    public string? EdgeId { get; set; }
    public bool IsAllowed { get; set; } = true;
}

public sealed class SimulationActorCapability
{
    public string ActorId { get; set; } = string.Empty;
    public bool AllowAllTrafficTypes { get; set; }
    public bool IsCustomActorType { get; set; }
    public IReadOnlyList<SimulationActorActionKind> AllowedActionKinds { get; set; } = [];
    public IReadOnlyList<string> AllowedTrafficTypes { get; set; } = [];
    public List<SimulationActorPermission> Permissions { get; set; } = [];
    public string? CustomActorTypeName { get; set; }
}

public static class SimulationActorCapabilityCatalog
{
    public static SimulationActorCapability ForKind(string actorId, SimulationActorKind kind) => new()
    {
        ActorId = actorId
    };
}

public sealed class SimulationActorState
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public SimulationActorKind Kind { get; set; }
    public SimulationActorObjective Objective { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool GenerateAutomaticDecisions { get; set; } = true;
    public double Budget { get; set; }
    public double Cash { get; set; }
    public double RiskTolerance { get; set; }
    public double CooperationWeight { get; set; }
    public SimulationActorCapability Capability { get; set; } = new();
    public IReadOnlyList<string> ControlledNodeIds { get; set; } = [];
    public IReadOnlyList<string> ControlledEdgeIds { get; set; } = [];
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
    public double Cost { get; set; }
    public bool IsForced { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ExpectedEffect { get; set; } = string.Empty;
}

public sealed class SimulationActorDecision
{
    public int Tick { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ReasonSummary { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public double? UtilityScore { get; set; }
    public List<SimulationActorAction> Actions { get; set; } = [];
}

public sealed class SimulationActorActionOutcome
{
    public SimulationActorAction Action { get; set; } = new();
    public bool Applied { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class SimulationActorMetrics
{
    public int Tick { get; set; }
    public Dictionary<string, double> ActorCashById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> ActorSalesRevenueById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> ActorPurchaseCostById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> ActorProductionCostById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> ActorTransportCostById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> ActorTaxesPaidById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> ActorTaxesReceivedById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> ActorProfitById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> ActorCashDeltaById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double TotalDelivered { get; set; }
    public double TotalUnmetDemand { get; set; }
    public double TotalMovementCost { get; set; }
    public double AverageEdgeUtilisation { get; set; }
    public int BottleneckEdgeCount { get; set; }
    public int PolicyRestrictionCount { get; set; }
    public double CooperationIndex { get; set; }
}

public sealed class SimulationActorRunResult
{
    public required IReadOnlyList<SimulationActorDecision> DecisionsByTick { get; init; }
    public required IReadOnlyList<SimulationActorMetrics> MetricsByTick { get; init; }
}

public sealed class SimulationActorStepResult
{
    public required SimulationActorDecision Decision { get; init; }
    public required SimulationActorMetrics Metrics { get; init; }
    public NetworkModel NetworkAfterStep { get; init; } = new();
    public IReadOnlyList<SimulationActorDecision> Decisions { get; init; } = [];
    public IReadOnlyList<SimulationActorActionOutcome> ActionOutcomes { get; init; } = [];
}

public sealed record AgentActionLogEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AgentId { get; init; }
    public string? ActorId { get; init; }
    public string AgentName { get; init; } = string.Empty;
    public int SimulationTick { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string ActionType { get; init; } = string.Empty;
    public string? TargetId { get; init; }
    public string DecisionSummary { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public double? UtilityScore { get; init; }
    public List<string> DecisionFactors { get; init; } = [];
    public List<string> AlternativesConsidered { get; init; } = [];
    public Dictionary<string, double> StateMetrics { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public interface IAgentActionLogger
{
    void Log(AgentActionLogEntry entry);
    IReadOnlyList<AgentActionLogEntry> GetAll();
    IReadOnlyList<AgentActionLogEntry> GetByAgent(Guid agentId);
    void Clear();
}

public sealed class AgentActionLogger : IAgentActionLogger
{
    private readonly List<AgentActionLogEntry> entries = [];

    public void Log(AgentActionLogEntry entry) => entries.Add(entry);
    public IReadOnlyList<AgentActionLogEntry> GetAll() => entries.ToList();
    public IReadOnlyList<AgentActionLogEntry> GetByAgent(Guid agentId) => entries.Where(entry => entry.AgentId == agentId).ToList();
    public void Clear() => entries.Clear();
}

public sealed class SimulationActorCoordinator
{
    public SimulationActorCoordinator(IAgentActionLogger? actionLogger = null)
    {
    }

    public SimulationActorRunResult RunActorsForTicks(NetworkModel network, IReadOnlyList<SimulationActorState> actors, int ticks) => new()
    {
        DecisionsByTick = [],
        MetricsByTick = []
    };

    public SimulationActorStepResult StepActorsOnce(
        NetworkModel network,
        IReadOnlyList<SimulationActorState> actors,
        int tick,
        IReadOnlyList<SimulationActorDecision>? previousDecisions = null) => new()
        {
            Decision = new SimulationActorDecision(),
            Metrics = new SimulationActorMetrics(),
            NetworkAfterStep = network,
            Decisions = [],
            ActionOutcomes = []
        };

    public IReadOnlyList<SimulationActorDecision> PreviewActorActions(
        NetworkModel network,
        IReadOnlyList<SimulationActorState> actors,
        int tick,
        IReadOnlyList<SimulationActorDecision>? previousDecisions = null) => [];
}

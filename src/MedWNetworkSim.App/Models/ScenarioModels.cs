namespace MedWNetworkSim.App.Models;
/// <summary>
/// Specifies the scenario target kind.
/// </summary>

public enum ScenarioTargetKind
{
    Network,
    Node,
    Edge,
    TrafficType
}
/// <summary>
/// Defines the contract and required members for iscenario event implementations.
/// </summary>

public interface IScenarioEvent
{
    Guid Id { get; }

    string Name { get; }

    double Time { get; }

    ScenarioTargetKind TargetKind { get; }

    string? TargetId { get; }

    void Apply(SimulationContext context);

    void Revert(SimulationContext context);
}
/// <summary>
/// Represents the scenario definition component.
/// </summary>

public sealed class ScenarioDefinition
{
    /// <summary>
    /// Gets or sets the unique identifier for this instance.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the name.
    /// </summary>

    public string Name { get; set; } = "Scenario";
    /// <summary>
    /// Gets the collection of events associated with this entity.
    /// </summary>

    public List<IScenarioEvent> Events { get; set; } = [];
}
/// <summary>
/// Represents the scenario comparison result component.
/// </summary>

public sealed class ScenarioComparisonResult
{
    /// <summary>
    /// Gets or sets the baseline.
    /// </summary>
    public SimulationResult Baseline { get; set; } = new();
    /// <summary>
    /// Gets or sets the variant.
    /// </summary>

    public SimulationResult Variant { get; set; } = new();
    /// <summary>
    /// Gets or sets the throughput delta.
    /// </summary>

    public double ThroughputDelta { get; set; }
    /// <summary>
    /// Gets or sets the cost delta.
    /// </summary>

    public double CostDelta { get; set; }
    /// <summary>
    /// Gets or sets the unmet demand delta.
    /// </summary>

    public double UnmetDemandDelta { get; set; }
    /// <summary>
    /// Gets or sets the economic delta.
    /// </summary>

    public EconomicSummary? EconomicDelta { get; set; }
}
/// <summary>
/// Specifies the network issue severity.
/// </summary>

public enum NetworkIssueSeverity
{
    Info,
    Warning,
    Critical
}
/// <summary>
/// Specifies the network issue type.
/// </summary>

public enum NetworkIssueType
{
    CongestedEdge,
    StarvedNode,
    Deadlock,
    IsolatedNode,
    PolicyBlockedFlow,
    HighCostRoute
}
/// <summary>
/// Represents the network issue component.
/// </summary>

public sealed class NetworkIssue
{
    /// <summary>
    /// Gets or sets the type.
    /// </summary>
    public NetworkIssueType Type { get; set; }
    /// <summary>
    /// Gets or sets the severity.
    /// </summary>

    public NetworkIssueSeverity Severity { get; set; }
    /// <summary>
    /// Gets or sets the target id.
    /// </summary>

    public string? TargetId { get; set; }
    /// <summary>
    /// Gets or sets the target name.
    /// </summary>

    public string TargetName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the title.
    /// </summary>

    public string Title { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the explanation.
    /// </summary>

    public string Explanation { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the suggested action.
    /// </summary>

    public string SuggestedAction { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the score.
    /// </summary>

    public double Score { get; set; }
}
/// <summary>
/// Represents the node explanation component.
/// </summary>

public sealed class NodeExplanation
{
    /// <summary>
    /// Gets or sets the node id.
    /// </summary>
    public string NodeId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the node name.
    /// </summary>

    public string NodeName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the summary.
    /// </summary>

    public string Summary { get; set; } = string.Empty;
    /// <summary>
    /// Gets the collection of causes associated with this entity.
    /// </summary>

    public List<string> Causes { get; set; } = [];
    /// <summary>
    /// Gets the collection of suggested actions associated with this entity.
    /// </summary>

    public List<string> SuggestedActions { get; set; } = [];
    /// <summary>
    /// Gets or sets the unmet demand by traffic type.
    /// </summary>

    public Dictionary<string, double> UnmetDemandByTrafficType { get; set; } = [];
}
/// <summary>
/// Represents the edge explanation component.
/// </summary>

public sealed class EdgeExplanation
{
    /// <summary>
    /// Gets or sets the edge id.
    /// </summary>
    public string EdgeId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the edge name.
    /// </summary>

    public string EdgeName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the summary.
    /// </summary>

    public string Summary { get; set; } = string.Empty;
    /// <summary>
    /// Gets the collection of causes associated with this entity.
    /// </summary>

    public List<string> Causes { get; set; } = [];
    /// <summary>
    /// Gets the collection of suggested actions associated with this entity.
    /// </summary>

    public List<string> SuggestedActions { get; set; } = [];
}
/// <summary>
/// Represents the adaptive edge state component.
/// </summary>

public sealed class AdaptiveEdgeState
{
    /// <summary>
    /// Gets or sets the edge id.
    /// </summary>
    public Guid EdgeId { get; set; }
    /// <summary>
    /// Gets or sets the historical delay.
    /// </summary>

    public double HistoricalDelay { get; set; }
    /// <summary>
    /// Gets or sets the reinforcement score.
    /// </summary>

    public double ReinforcementScore { get; set; }
    /// <summary>
    /// Gets or sets the last observed utilisation.
    /// </summary>

    public double LastObservedUtilisation { get; set; }
}

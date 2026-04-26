namespace MedWNetworkSim.App.Models;

public enum ScenarioTargetKind
{
    Network,
    Node,
    Edge,
    TrafficType
}

public interface IScenarioEvent
{
    Guid Id { get; }

    string Name { get; }

    double Time { get; }

    ScenarioTargetKind TargetKind { get; }

    Guid? TargetId { get; }

    void Apply(SimulationContext context);

    void Revert(SimulationContext context);
}

public sealed class ScenarioDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Scenario";

    public List<IScenarioEvent> Events { get; set; } = [];
}

public sealed class ScenarioComparisonResult
{
    public SimulationResult Baseline { get; set; } = new();

    public SimulationResult Variant { get; set; } = new();

    public double ThroughputDelta { get; set; }

    public double CostDelta { get; set; }

    public double UnmetDemandDelta { get; set; }

    public EconomicSummary? EconomicDelta { get; set; }
}

public enum NetworkIssueSeverity
{
    Info,
    Warning,
    Critical
}

public enum NetworkIssueType
{
    CongestedEdge,
    StarvedNode,
    Deadlock,
    IsolatedNode,
    PolicyBlockedFlow,
    HighCostRoute
}

public sealed class NetworkIssue
{
    public NetworkIssueType Type { get; set; }

    public NetworkIssueSeverity Severity { get; set; }

    public Guid? TargetId { get; set; }

    public string TargetName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Explanation { get; set; } = string.Empty;

    public string SuggestedAction { get; set; } = string.Empty;

    public double Score { get; set; }
}

public sealed class NodeExplanation
{
    public Guid NodeId { get; set; }

    public string NodeName { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public List<string> Causes { get; set; } = [];

    public List<string> SuggestedActions { get; set; } = [];

    public Dictionary<string, double> UnmetDemandByTrafficType { get; set; } = [];
}

public sealed class EdgeExplanation
{
    public Guid EdgeId { get; set; }

    public string EdgeName { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public List<string> Causes { get; set; } = [];

    public List<string> SuggestedActions { get; set; } = [];
}

public sealed class AdaptiveEdgeState
{
    public Guid EdgeId { get; set; }

    public double HistoricalDelay { get; set; }

    public double ReinforcementScore { get; set; }

    public double LastObservedUtilisation { get; set; }
}

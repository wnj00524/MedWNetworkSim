namespace MedWNetworkSim.App.Models;

public enum ScenarioEventKind
{
    NodeFailure,
    EdgeClosure,
    DemandSpike,
    EdgeCostChange,
    ProductionMultiplier,
    ConsumptionMultiplier,
    RouteCostMultiplier
}

public sealed class ScenarioDefinitionModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Scenario";
    public string Description { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; } = 10d;
    public double DeltaTime { get; set; } = 1d;
    public bool EnableAdaptiveRouting { get; set; }
    public List<ScenarioEventModel> Events { get; set; } = [];
}

public sealed class ScenarioEventModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ScenarioEventKind Kind { get; set; }
    public ScenarioTargetKind TargetKind { get; set; }
    public string? TargetId { get; set; }
    public string? TrafficTypeIdOrName { get; set; }
    public double Time { get; set; }
    public double? EndTime { get; set; }
    public double Value { get; set; } = 1.0;
    public string Notes { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public sealed class ScenarioRunOptions
{
    public double StartTime { get; set; } = 0;
    public double EndTime { get; set; } = 10;
    public double DeltaTime { get; set; } = 1;
    public bool EnableAdaptiveRouting { get; set; }
}

public sealed class ScenarioRunResult
{
    public string ScenarioName { get; set; } = string.Empty;
    public SimulationResult? SimulationResult { get; set; }
    public IReadOnlyList<NetworkIssue> Issues { get; set; } = Array.Empty<NetworkIssue>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

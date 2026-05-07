namespace MedWNetworkSim.App.Models;
/// <summary>
/// Specifies the scenario event kind.
/// </summary>

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
/// <summary>
/// Represents a data model for scenario definition entities within the simulation.
/// </summary>

public sealed class ScenarioDefinitionModel
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
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the start time.
    /// </summary>
    public double StartTime { get; set; }
    /// <summary>
    /// Gets or sets the end time.
    /// </summary>
    public double EndTime { get; set; } = 10d;
    /// <summary>
    /// Gets or sets the delta time.
    /// </summary>
    public double DeltaTime { get; set; } = 1d;
    /// <summary>
    /// Gets a value indicating whether enable adaptive routing is enabled or active.
    /// </summary>
    public bool EnableAdaptiveRouting { get; set; }
    /// <summary>
    /// Gets the collection of events associated with this entity.
    /// </summary>
    public List<ScenarioEventModel> Events { get; set; } = [];
}
/// <summary>
/// Represents a data model for scenario event entities within the simulation.
/// </summary>

public sealed class ScenarioEventModel
{
    /// <summary>
    /// Gets or sets the unique identifier for this instance.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the kind.
    /// </summary>
    public ScenarioEventKind Kind { get; set; }
    /// <summary>
    /// Gets or sets the target kind.
    /// </summary>
    public ScenarioTargetKind TargetKind { get; set; }
    /// <summary>
    /// Gets or sets the target id.
    /// </summary>
    public string? TargetId { get; set; }
    /// <summary>
    /// Gets or sets the traffic type id or name.
    /// </summary>
    public string? TrafficTypeIdOrName { get; set; }
    /// <summary>
    /// Gets or sets the time.
    /// </summary>
    public double Time { get; set; }
    /// <summary>
    /// Gets or sets the end time.
    /// </summary>
    public double? EndTime { get; set; }
    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    public double Value { get; set; } = 1.0;
    /// <summary>
    /// Gets or sets the notes.
    /// </summary>
    public string Notes { get; set; } = string.Empty;
    /// <summary>
    /// Gets a value indicating whether is enabled is enabled or active.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
/// <summary>
/// Represents the scenario run options component.
/// </summary>

public sealed class ScenarioRunOptions
{
    /// <summary>
    /// Gets or sets the start time.
    /// </summary>
    public double StartTime { get; set; } = 0;
    /// <summary>
    /// Gets or sets the end time.
    /// </summary>
    public double EndTime { get; set; } = 10;
    /// <summary>
    /// Gets or sets the delta time.
    /// </summary>
    public double DeltaTime { get; set; } = 1;
    /// <summary>
    /// Gets a value indicating whether enable adaptive routing is enabled or active.
    /// </summary>
    public bool EnableAdaptiveRouting { get; set; }
}
/// <summary>
/// Represents the scenario run result component.
/// </summary>

public sealed class ScenarioRunResult
{
    /// <summary>
    /// Gets or sets the scenario name.
    /// </summary>
    public string ScenarioName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the simulation result.
    /// </summary>
    public SimulationResult? SimulationResult { get; set; }
    /// <summary>
    /// Gets the collection of issues associated with this entity.
    /// </summary>
    public IReadOnlyList<NetworkIssue> Issues { get; set; } = Array.Empty<NetworkIssue>();
    /// <summary>
    /// Gets the collection of warnings associated with this entity.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

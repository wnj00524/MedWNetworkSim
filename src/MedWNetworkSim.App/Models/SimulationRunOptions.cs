namespace MedWNetworkSim.App.Models;
/// <summary>
/// Represents the simulation run options component.
/// </summary>

public sealed class SimulationRunOptions
{
    /// <summary>
    /// Gets or sets the steps.
    /// </summary>
    public int Steps { get; set; } = 1;
    /// <summary>
    /// Gets or sets the delta time.
    /// </summary>

    public double DeltaTime { get; set; } = 1.0;
    /// <summary>
    /// Gets a value indicating whether adaptive routing enabled is enabled or active.
    /// </summary>

    public bool AdaptiveRoutingEnabled { get; set; }
    /// <summary>
    /// Gets or sets a value indicating whether expensive invariant validation should run during each temporal tick.
    /// Disabled by default for interactive performance; tests and verification tools can enable it explicitly.
    /// </summary>
    public bool EnableInvariantValidation { get; set; }
    /// <summary>
    /// Gets or sets a value indicating whether temporal advance should clone the simulation state before mutating it.
    /// Disabled by default so one-tick advancement can mutate the provided state in place.
    /// </summary>
    public bool CopyStateBeforeAdvance { get; set; }
}

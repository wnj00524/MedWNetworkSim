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
}

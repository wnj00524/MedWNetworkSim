namespace MedWNetworkSim.App.Models;

public sealed class SimulationRunOptions
{
    public int Steps { get; set; } = 1;

    public double DeltaTime { get; set; } = 1.0;

    public bool AdaptiveRoutingEnabled { get; set; }
}

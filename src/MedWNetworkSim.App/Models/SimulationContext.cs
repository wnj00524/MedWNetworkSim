using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.Models;

public sealed class SimulationContext
{
    public NetworkModel Network { get; init; } = new();

    public TemporalNetworkSimulationEngine.TemporalSimulationState TemporalState { get; init; } = new();

    public SimulationRunOptions Options { get; init; } = new();
}

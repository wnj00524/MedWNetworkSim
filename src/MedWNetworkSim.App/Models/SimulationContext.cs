using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.Models;

/// <summary>
/// Encapsulates the entire state, configuration, and structural definition of a network simulation.
/// It acts as the central repository holding the complete graph of nodes and edges, layer definitions,
/// traffic profiles, agents, policies, scenarios, and the execution configuration (like route choice settings).
/// </summary>
public sealed class SimulationContext
{
    /// <summary>
    /// Gets or sets the network.
    /// </summary>
    public NetworkModel Network { get; init; } = new();

    public TemporalNetworkSimulationEngine.TemporalSimulationState TemporalState { get; init; } = new();
    /// <summary>
    /// Gets or sets the options.
    /// </summary>

    public SimulationRunOptions Options { get; init; } = new();
}

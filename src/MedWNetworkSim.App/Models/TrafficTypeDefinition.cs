namespace MedWNetworkSim.App.Models;

/// <summary>
/// Defines how one traffic type behaves when the simulator routes it through the network.
/// </summary>
public sealed class TrafficTypeDefinition
{
    /// <summary>
    /// Gets or sets the name of the traffic type.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional description shown in the editor.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the route-scoring preference for this traffic type.
    /// </summary>
    public RoutingPreference RoutingPreference { get; set; } = RoutingPreference.TotalCost;

    /// <summary>
    /// Gets or sets how supply is allocated across feasible routes for this traffic type.
    /// </summary>
    public AllocationMode AllocationMode { get; set; } = AllocationMode.GreedyBestRoute;

    /// <summary>
    /// Gets or sets the optional per-unit bid used when competing for constrained edge or node transhipment capacity.
    /// </summary>
    public double? CapacityBidPerUnit { get; set; }
}

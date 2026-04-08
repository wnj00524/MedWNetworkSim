using System.Collections.Generic;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

/// <summary>
/// Describes one simulated movement allocation from a producer node to a consumer node.
/// </summary>
public sealed class RouteAllocation
{
    /// <summary>
    /// Gets the traffic type moved by this allocation.
    /// </summary>
    public string TrafficType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the route-scoring preference that selected this path.
    /// </summary>
    public RoutingPreference RoutingPreference { get; init; }

    /// <summary>
    /// Gets the producing node identifier.
    /// </summary>
    public string ProducerNodeId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the producing node name.
    /// </summary>
    public string ProducerName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the consuming node identifier.
    /// </summary>
    public string ConsumerNodeId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the consuming node name.
    /// </summary>
    public string ConsumerName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the quantity delivered by this allocation.
    /// </summary>
    public double Quantity { get; init; }

    /// <summary>
    /// Gets a value indicating whether the quantity was satisfied locally on the same node.
    /// </summary>
    public bool IsLocalSupply { get; init; }

    /// <summary>
    /// Gets the total edge time across the chosen route.
    /// </summary>
    public double TotalTime { get; init; }

    /// <summary>
    /// Gets the transit-only cost per unit across the chosen route.
    /// </summary>
    public double TotalCost { get; init; }

    /// <summary>
    /// Gets the additional per-unit cost caused by capacity bidding.
    /// </summary>
    public double BidCostPerUnit { get; init; }

    /// <summary>
    /// Gets the full per-unit delivered movement cost, including any bid premium.
    /// </summary>
    public double DeliveredCostPerUnit { get; init; }

    /// <summary>
    /// Gets the total movement cost for the full delivered quantity.
    /// </summary>
    public double TotalMovementCost { get; init; }

    /// <summary>
    /// Gets the route score used for path comparison under the active routing preference.
    /// </summary>
    public double TotalScore { get; init; }

    /// <summary>
    /// Gets the ordered node names visited by the movement path.
    /// </summary>
    public IReadOnlyList<string> PathNodeNames { get; init; } = [];

    /// <summary>
    /// Gets the ordered node identifiers visited by the movement path.
    /// </summary>
    public IReadOnlyList<string> PathNodeIds { get; init; } = [];

    /// <summary>
    /// Gets the ordered edge identifiers traversed by the movement path.
    /// </summary>
    public IReadOnlyList<string> PathEdgeIds { get; init; } = [];
}

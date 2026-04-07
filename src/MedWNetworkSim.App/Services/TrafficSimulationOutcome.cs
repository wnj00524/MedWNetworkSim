using System.Collections.Generic;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

/// <summary>
/// Captures the full simulation result for one traffic type.
/// </summary>
public sealed class TrafficSimulationOutcome
{
    /// <summary>
    /// Gets the traffic type summarized by this outcome.
    /// </summary>
    public string TrafficType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the route-scoring preference used for this traffic type.
    /// </summary>
    public RoutingPreference RoutingPreference { get; init; }

    /// <summary>
    /// Gets the total available production for this traffic type.
    /// </summary>
    public double TotalProduction { get; init; }

    /// <summary>
    /// Gets the total requested consumption for this traffic type.
    /// </summary>
    public double TotalConsumption { get; init; }

    /// <summary>
    /// Gets the total quantity successfully delivered.
    /// </summary>
    public double TotalDelivered { get; init; }

    /// <summary>
    /// Gets the production quantity left unused after routing.
    /// </summary>
    public double UnusedSupply { get; init; }

    /// <summary>
    /// Gets the demand quantity left unmet after routing.
    /// </summary>
    public double UnmetDemand { get; init; }

    /// <summary>
    /// Gets the detailed local and routed allocations that make up the outcome.
    /// </summary>
    public IReadOnlyList<RouteAllocation> Allocations { get; init; } = [];

    /// <summary>
    /// Gets informational notes describing notable routing conditions or constraints.
    /// </summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

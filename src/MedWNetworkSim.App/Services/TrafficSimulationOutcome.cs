using System.Collections.Generic;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public sealed class TrafficSimulationOutcome
{
    public string TrafficType { get; init; } = string.Empty;

    public RoutingPreference RoutingPreference { get; init; }

    public double TotalProduction { get; init; }

    public double TotalConsumption { get; init; }

    public double TotalDelivered { get; init; }

    public double UnusedSupply { get; init; }

    public double UnmetDemand { get; init; }

    public IReadOnlyList<RouteAllocation> Allocations { get; init; } = [];

    public IReadOnlyList<string> Notes { get; init; } = [];
}

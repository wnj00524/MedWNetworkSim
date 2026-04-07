using System.Collections.Generic;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public sealed class RouteAllocation
{
    public string TrafficType { get; init; } = string.Empty;

    public RoutingPreference RoutingPreference { get; init; }

    public string ProducerNodeId { get; init; } = string.Empty;

    public string ProducerName { get; init; } = string.Empty;

    public string ConsumerNodeId { get; init; } = string.Empty;

    public string ConsumerName { get; init; } = string.Empty;

    public double Quantity { get; init; }

    public bool IsLocalSupply { get; init; }

    public double TotalTime { get; init; }

    public double TotalCost { get; init; }

    public double BidCostPerUnit { get; init; }

    public double DeliveredCostPerUnit { get; init; }

    public double TotalMovementCost { get; init; }

    public double TotalScore { get; init; }

    public IReadOnlyList<string> PathNodeNames { get; init; } = [];
}

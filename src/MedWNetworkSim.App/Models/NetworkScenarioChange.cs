namespace MedWNetworkSim.App.Models;

/// <summary>
/// Describes a network-level change that can be applied to a cloned scenario variant.
/// </summary>
public sealed class NetworkScenarioChange
{
    public string Id { get; set; } = string.Empty;

    public NetworkChangeKind Kind { get; set; }

    public string? TargetNodeId { get; set; }

    public string? TargetEdgeId { get; set; }

    public string? TrafficType { get; set; }

    public double DeltaValue { get; set; }

    public double? AbsoluteValue { get; set; }

    public bool AllowReduceBelowCurrentFlow { get; set; }
}

public enum NetworkChangeKind
{
    NoOp,
    AdjustProduction,
    AdjustConsumption,
    AdjustTrafficPrice,
    AdjustEdgeCapacity,
    AdjustEdgeCost,
    SetTrafficPermission,
    ClearTrafficRestrictions
}

public sealed class NetworkScenarioChangeOutcome
{
    public required NetworkScenarioChange Change { get; init; }

    public bool Applied { get; init; }

    public string Reason { get; init; } = string.Empty;
}

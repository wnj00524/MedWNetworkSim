namespace MedWNetworkSim.App.Services;

public sealed class ConsumerCostSummary
{
    public string TrafficType { get; init; } = string.Empty;

    public string ConsumerNodeId { get; init; } = string.Empty;

    public string ConsumerName { get; init; } = string.Empty;

    public double LocalQuantity { get; init; }

    public double LocalUnitCost { get; init; }

    public double ImportedQuantity { get; init; }

    public double ImportedUnitCost { get; init; }

    public double BlendedUnitCost { get; init; }

    public double TotalMovementCost { get; init; }
}

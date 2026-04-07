using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.ViewModels;

public sealed class ConsumerCostSummaryRowViewModel(ConsumerCostSummary summary) : ObservableObject
{
    public string TrafficType { get; } = summary.TrafficType;

    public string ConsumerName { get; } = summary.ConsumerName;

    public double LocalQuantity { get; } = summary.LocalQuantity;

    public double LocalUnitCost { get; } = summary.LocalUnitCost;

    public double ImportedQuantity { get; } = summary.ImportedQuantity;

    public double ImportedUnitCost { get; } = summary.ImportedUnitCost;

    public double BlendedUnitCost { get; } = summary.BlendedUnitCost;

    public double TotalMovementCost { get; } = summary.TotalMovementCost;
}

using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.ViewModels;

public sealed class PerishingEventRowViewModel(
    int period,
    string trafficType,
    string locationType,
    string locationId,
    string locationLabel,
    string cause,
    double quantity,
    double weightedImpact,
    string detail) : ObservableObject
{
    public int Period { get; } = period;

    public string TrafficType { get; } = trafficType;

    public string LocationType { get; } = locationType;

    public string LocationId { get; } = locationId;

    public string LocationLabel { get; } = locationLabel;

    public string Cause { get; } = cause;

    public double Quantity { get; } = quantity;

    public double WeightedImpact { get; } = weightedImpact;

    public string Detail { get; } = detail;

    public static bool IsPerishingCause(TemporalNetworkSimulationEngine.PressureCauseKind cause)
    {
        return cause is TemporalNetworkSimulationEngine.PressureCauseKind.PerishedInNodeInventory
            or TemporalNetworkSimulationEngine.PressureCauseKind.PerishedInTransit;
    }
}

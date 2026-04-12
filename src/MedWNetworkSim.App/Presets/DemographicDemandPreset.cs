namespace MedWNetworkSim.App.Presets;

public sealed record DemographicDemandPreset(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<DemographicDemandPresetTrafficRow> TrafficRows)
{
    public string MappingSummary => string.Join(
        "; ",
        TrafficRows.Select(row => $"{row.TrafficType}: need {row.Consumption:0.##}"));
}

public sealed record DemographicDemandPresetTrafficRow(
    string TrafficType,
    double Consumption,
    double? StoreCapacity = null,
    double ConsumerPremiumPerUnit = 0d,
    bool CanTransship = false);

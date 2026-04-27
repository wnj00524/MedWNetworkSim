using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.Presentation;

namespace MedWNetworkSim.App.VisualAnalytics;

public enum VisualisationMode
{
    Graph,
    Sankey,
    Map,
    ScenarioDiff
}

public sealed class VisualAnalyticsSnapshot
{
    public required NetworkModel Network { get; init; }
    public required IReadOnlyList<TrafficSimulationOutcome> TrafficOutcomes { get; init; }
    public required IReadOnlyList<ConsumerCostSummary> ConsumerCosts { get; init; }
    public required int Period { get; init; }
}

public sealed class VisualisationState : ObservableObject
{
    private VisualisationMode activeMode = VisualisationMode.Graph;
    private string? activeTrafficTypeFilter;
    private bool showInsights = true;
    private bool showMapBackground = true;
    private bool showUnmetDemand = true;
    private bool showCapacityUtilisation = true;
    private bool collapseMinorFlows = true;

    public VisualisationMode ActiveMode
    {
        get => activeMode;
        set => SetProperty(ref activeMode, value);
    }

    public string? ActiveTrafficTypeFilter
    {
        get => activeTrafficTypeFilter;
        set => SetProperty(ref activeTrafficTypeFilter, value);
    }

    public bool ShowInsights
    {
        get => showInsights;
        set => SetProperty(ref showInsights, value);
    }

    public bool ShowMapBackground
    {
        get => showMapBackground;
        set => SetProperty(ref showMapBackground, value);
    }

    public bool ShowUnmetDemand
    {
        get => showUnmetDemand;
        set => SetProperty(ref showUnmetDemand, value);
    }

    public bool ShowCapacityUtilisation
    {
        get => showCapacityUtilisation;
        set => SetProperty(ref showCapacityUtilisation, value);
    }

    public bool CollapseMinorFlows
    {
        get => collapseMinorFlows;
        set => SetProperty(ref collapseMinorFlows, value);
    }
}

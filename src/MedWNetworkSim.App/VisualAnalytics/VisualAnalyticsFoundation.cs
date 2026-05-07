using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.Presentation;

namespace MedWNetworkSim.App.VisualAnalytics;
/// <summary>
/// Specifies the visualisation mode.
/// </summary>

public enum VisualisationMode
{
    Graph,
    Sankey,
    Analytics,
    Map,
    ScenarioDiff
}
/// <summary>
/// Represents the visual analytics snapshot component.
/// </summary>

public sealed class VisualAnalyticsSnapshot
{
    /// <summary>
    /// Gets or sets the network.
    /// </summary>
    public required NetworkModel Network { get; init; }
    /// <summary>
    /// Gets the collection of traffic outcomes associated with this entity.
    /// </summary>
    public required IReadOnlyList<TrafficSimulationOutcome> TrafficOutcomes { get; init; }
    /// <summary>
    /// Gets the collection of consumer costs associated with this entity.
    /// </summary>
    public required IReadOnlyList<ConsumerCostSummary> ConsumerCosts { get; init; }
    /// <summary>
    /// Gets or sets the period.
    /// </summary>
    public required int Period { get; init; }
}
/// <summary>
/// Represents the visualisation state component.
/// </summary>

public sealed class VisualisationState : ObservableObject
{
    private VisualisationMode activeMode = VisualisationMode.Graph;
    private string? activeTrafficTypeFilter;
    private bool showInsights = true;
    private bool showMapBackground = true;
    private bool showGraphLabels = true;
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
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
            SetProperty(ref activeTrafficTypeFilter, normalized);
        }
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

    public bool ShowGraphLabels
    {
        get => showGraphLabels;
        set => SetProperty(ref showGraphLabels, value);
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

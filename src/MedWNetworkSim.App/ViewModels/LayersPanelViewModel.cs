using System.Collections.ObjectModel;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class LayersPanelViewModel : ObservableObject
{
    private bool showCombinedTraffic = true;
    private CanvasDisplayMode selectedDisplayMode = CanvasDisplayMode.Combined;
    private bool showCongestionOverlay = true;
    private bool showLabels = true;
    private bool showEdgeCapacities = true;
    private bool showNodeCapacities = true;
    private bool showStoresInventoryMarkers = true;
    private bool showTranshipmentHubs = true;
    private bool showRouteHighlights = true;
    private bool showTemporalInFlightOverlays = true;

    public event EventHandler? LayersChanged;

    public ObservableCollection<LayerVisibilityState> TrafficLayers { get; } = [];

    public Array DisplayModes { get; } = Enum.GetValues(typeof(CanvasDisplayMode));

    public bool ShowCombinedTraffic
    {
        get => showCombinedTraffic;
        set => SetAndNotify(ref showCombinedTraffic, value);
    }

    public CanvasDisplayMode SelectedDisplayMode
    {
        get => selectedDisplayMode;
        set => SetAndNotify(ref selectedDisplayMode, value);
    }

    public bool ShowCongestionOverlay
    {
        get => showCongestionOverlay;
        set => SetAndNotify(ref showCongestionOverlay, value);
    }

    public bool ShowLabels
    {
        get => showLabels;
        set => SetAndNotify(ref showLabels, value);
    }

    public bool ShowEdgeCapacities
    {
        get => showEdgeCapacities;
        set => SetAndNotify(ref showEdgeCapacities, value);
    }

    public bool ShowNodeCapacities
    {
        get => showNodeCapacities;
        set => SetAndNotify(ref showNodeCapacities, value);
    }

    public bool ShowStoresInventoryMarkers
    {
        get => showStoresInventoryMarkers;
        set => SetAndNotify(ref showStoresInventoryMarkers, value);
    }

    public bool ShowTranshipmentHubs
    {
        get => showTranshipmentHubs;
        set => SetAndNotify(ref showTranshipmentHubs, value);
    }

    public bool ShowRouteHighlights
    {
        get => showRouteHighlights;
        set => SetAndNotify(ref showRouteHighlights, value);
    }

    public bool ShowTemporalInFlightOverlays
    {
        get => showTemporalInFlightOverlays;
        set => SetAndNotify(ref showTemporalInFlightOverlays, value);
    }

    public IReadOnlyCollection<string> VisibleTrafficTypes =>
        TrafficLayers.Where(layer => layer.IsVisible).Select(layer => layer.TrafficType).ToList();

    public IReadOnlyCollection<string> HighlightedTrafficTypes =>
        TrafficLayers.Where(layer => layer.IsHighlighted).Select(layer => layer.TrafficType).ToList();

    public void SyncTrafficTypes(IEnumerable<string> trafficTypes)
    {
        var orderedNames = trafficTypes
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var layer in TrafficLayers.ToList())
        {
            if (orderedNames.Contains(layer.TrafficType, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            layer.LayerStateChanged -= HandleLayerStateChanged;
            TrafficLayers.Remove(layer);
        }

        foreach (var name in orderedNames)
        {
            if (TrafficLayers.Any(layer => string.Equals(layer.TrafficType, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var layer = new LayerVisibilityState(name);
            layer.LayerStateChanged += HandleLayerStateChanged;
            TrafficLayers.Add(layer);
        }

        RaiseLayerCollectionsChanged();
    }

    public bool ShouldIncludeTraffic(string trafficType)
    {
        if (SelectedDisplayMode is CanvasDisplayMode.Combined or CanvasDisplayMode.CombinedWithHighlights)
        {
            return ShowCombinedTraffic;
        }

        var layer = TrafficLayers.FirstOrDefault(item => string.Equals(item.TrafficType, trafficType, StringComparison.OrdinalIgnoreCase));
        return layer is not null && (layer.IsVisible || layer.IsHighlighted);
    }

    public void HighlightRouteTraffic(string trafficType)
    {
        var layer = TrafficLayers.FirstOrDefault(item => string.Equals(item.TrafficType, trafficType, StringComparison.OrdinalIgnoreCase));
        if (layer is not null)
        {
            layer.IsVisible = true;
            layer.IsHighlighted = true;
        }
    }

    private void SetAndNotify<T>(ref T field, T value)
    {
        if (SetProperty(ref field, value))
        {
            RaiseLayerCollectionsChanged();
        }
    }

    private void HandleLayerStateChanged(object? sender, EventArgs e)
    {
        RaiseLayerCollectionsChanged();
    }

    private void RaiseLayerCollectionsChanged()
    {
        OnPropertyChanged(nameof(VisibleTrafficTypes));
        OnPropertyChanged(nameof(HighlightedTrafficTypes));
        LayersChanged?.Invoke(this, EventArgs.Empty);
    }
}

namespace MedWNetworkSim.App.ViewModels;

public sealed class CanvasViewModel : ObservableObject
{
    private string? highlightedRouteTrafficType;
    private string highlightedRoutePath = string.Empty;
    private IReadOnlyList<string> highlightedRouteEdgeIds = [];
    private int currentPeriod;

    public string? HighlightedRouteTrafficType
    {
        get => highlightedRouteTrafficType;
        private set => SetProperty(ref highlightedRouteTrafficType, value);
    }

    public string HighlightedRoutePath
    {
        get => highlightedRoutePath;
        private set => SetProperty(ref highlightedRoutePath, value);
    }

    public int CurrentPeriod
    {
        get => currentPeriod;
        set => SetProperty(ref currentPeriod, value);
    }

    public IReadOnlyList<string> HighlightedRouteEdgeIds
    {
        get => highlightedRouteEdgeIds;
        private set => SetProperty(ref highlightedRouteEdgeIds, value);
    }

    public void HighlightRoute(RouteAllocationRowViewModel route)
    {
        HighlightedRouteTrafficType = route.TrafficType;
        HighlightedRoutePath = route.PathDescription;
        HighlightedRouteEdgeIds = route.PathEdgeIds;
    }

    public void ClearRouteHighlight()
    {
        HighlightedRouteTrafficType = null;
        HighlightedRoutePath = string.Empty;
        HighlightedRouteEdgeIds = [];
    }
}

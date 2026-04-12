using System.Windows;

namespace MedWNetworkSim.App.ViewModels;

public sealed class InspectorPanelViewModel : ObservableObject
{
    private bool isOpen;
    private string title = "Inspector";
    private string summary = "Select a node, edge, traffic type, or route to inspect it.";
    private object? selectedObject;

    public bool IsOpen
    {
        get => isOpen;
        set
        {
            if (SetProperty(ref isOpen, value))
            {
                OnPropertyChanged(nameof(Visibility));
            }
        }
    }

    public Visibility Visibility => IsOpen ? Visibility.Visible : Visibility.Collapsed;

    public string Title
    {
        get => title;
        private set => SetProperty(ref title, value);
    }

    public string Summary
    {
        get => summary;
        private set => SetProperty(ref summary, value);
    }

    public object? SelectedObject
    {
        get => selectedObject;
        private set => SetProperty(ref selectedObject, value);
    }

    public void InspectNode(NodeViewModel node)
    {
        SelectedObject = node;
        Title = $"Node: {node.Name}";
        Summary = node.FullTrafficSummary;
        IsOpen = true;
    }

    public void InspectEdge(EdgeViewModel edge)
    {
        SelectedObject = edge;
        Title = $"Edge: {edge.Id}";
        Summary = $"{edge.FromNodeId} -> {edge.ToNodeId}\n{edge.SummaryLabel}\n{edge.CapacityDisplayLabel}";
        IsOpen = true;
    }

    public void InspectTraffic(TrafficSummaryViewModel traffic)
    {
        SelectedObject = traffic;
        Title = $"Traffic: {traffic.Name}";
        Summary = $"{traffic.SupplyDemandSummary}\n{traffic.OutcomeSummary}\n{traffic.NotesSummary}";
        IsOpen = true;
    }

    public void InspectRoute(RouteAllocationRowViewModel route)
    {
        SelectedObject = route;
        Title = $"Route: {route.TrafficType}";
        Summary = $"{route.Quantity:0.##} unit(s)\n{route.PathDescription}\nTime {route.TotalTime:0.##}, landed/unit {route.DeliveredCostPerUnit:0.##}";
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
    }
}

using System.Windows;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class InspectorPanelViewModel : ObservableObject
{
    private readonly Func<string?, IReadOnlyList<string>> getCompositeInterfaceSummaries;
    private readonly Func<string?, string?, string?> getCompositeInterfaceSummary;

    private bool isOpen;
    private string title = "Inspector";
    private string summaryText = "Select a node, edge, traffic type, or route to inspect it.";
    private string flowsText = "No selection yet.";
    private string capacityText = "No selection yet.";
    private string routingText = "No selection yet.";
    private string timelineText = "No selection yet.";
    private InspectorTab selectedTab = InspectorTab.Summary;
    private object? selectedObject;

    public InspectorPanelViewModel(
        Func<string?, IReadOnlyList<string>> getCompositeInterfaceSummaries,
        Func<string?, string?, string?> getCompositeInterfaceSummary)
    {
        this.getCompositeInterfaceSummaries = getCompositeInterfaceSummaries
            ?? throw new ArgumentNullException(nameof(getCompositeInterfaceSummaries));
        this.getCompositeInterfaceSummary = getCompositeInterfaceSummary
            ?? throw new ArgumentNullException(nameof(getCompositeInterfaceSummary));
    }

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

    public InspectorTab SelectedTab
    {
        get => selectedTab;
        set
        {
            if (SetProperty(ref selectedTab, value))
            {
                OnPropertyChanged(nameof(SelectedTabIndex));
            }
        }
    }

    public int SelectedTabIndex
    {
        get => (int)SelectedTab;
        set
        {
            if (Enum.IsDefined(typeof(InspectorTab), value))
            {
                SelectedTab = (InspectorTab)value;
            }
        }
    }

    public string SummaryText
    {
        get => summaryText;
        private set => SetProperty(ref summaryText, value);
    }

    public string FlowsText
    {
        get => flowsText;
        private set => SetProperty(ref flowsText, value);
    }

    public string CapacityText
    {
        get => capacityText;
        private set => SetProperty(ref capacityText, value);
    }

    public string RoutingText
    {
        get => routingText;
        private set => SetProperty(ref routingText, value);
    }

    public string TimelineText
    {
        get => timelineText;
        private set => SetProperty(ref timelineText, value);
    }

    public object? SelectedObject
    {
        get => selectedObject;
        private set => SetProperty(ref selectedObject, value);
    }

    public void InspectNode(NodeViewModel node, bool open = true)
    {
        SelectedObject = node;
        Title = $"Node: {node.Name}";

        var summaryLines = new List<string>
        {
            $"{node.Name} ({node.Id})",
            node.FullTrafficSummary
        };

        var interfaceSummaries = node.IsCompositeSubnetwork
            ? getCompositeInterfaceSummaries(node.Id)
            : [];

        if (interfaceSummaries.Count > 0)
        {
            summaryLines.Add(string.Empty);
            summaryLines.Add("Subnetwork interfaces");
            summaryLines.AddRange(interfaceSummaries.Select(line => $"- {line}"));
        }

        SummaryText = string.Join(Environment.NewLine, summaryLines);

        FlowsText = node.HasSimulationDetails
            ? node.FlowSummaryLabel
            : "No routed flow has been visualized for this node yet.";

        CapacityText = $"{node.TranshipmentCapacityLabel}{Environment.NewLine}{node.TranshipmentUsageLabel}";

        var routingLines = node.TrafficProfiles.Count == 0
            ? new List<string> { "No traffic roles are configured for this node." }
            : node.TrafficProfiles.Select(profile => $"{profile.TrafficType}: {profile.RoleSummary}").ToList();

        if (interfaceSummaries.Count > 0)
        {
            routingLines.Add(string.Empty);
            routingLines.Add("Interface contracts");
            routingLines.AddRange(interfaceSummaries.Select(line => $"- {line}"));
        }

        RoutingText = string.Join(Environment.NewLine, routingLines);

        TimelineText = node.HasTimelineDetails
            ? node.TimelineSummaryLabel
            : "No inventory or backlog is visible for the current period.";

        SelectedTab = InspectorTab.Summary;
        IsOpen = open;
    }

    public void InspectEdge(EdgeViewModel edge, bool open = true)
    {
        SelectedObject = edge;
        Title = $"Edge: {edge.Id}";

        SummaryText = $"{edge.Id}: {edge.FromNodeId} -> {edge.ToNodeId}{Environment.NewLine}{edge.DirectionLabel}{Environment.NewLine}{edge.SummaryLabel}";

        FlowsText = edge.HasSimulationDetails
            ? edge.FlowSummaryLabel
            : "No routed movement is currently visible on this edge.";

        CapacityText = $"{edge.CapacityDisplayLabel}{Environment.NewLine}Utilization: {edge.UtilizationPercentLabel}";

        var routingLines = new List<string>
        {
            $"Base route segment: {edge.FromNodeId} -> {edge.ToNodeId}",
            edge.DirectionLabel
        };

        var sourceInterfaceSummary = getCompositeInterfaceSummary(edge.FromNodeId, edge.FromInterfaceNodeId);
        if (!string.IsNullOrWhiteSpace(sourceInterfaceSummary))
        {
            routingLines.Add(string.Empty);
            routingLines.Add("Source interface");
            routingLines.Add($"- {sourceInterfaceSummary}");
        }

        var targetInterfaceSummary = getCompositeInterfaceSummary(edge.ToNodeId, edge.ToInterfaceNodeId);
        if (!string.IsNullOrWhiteSpace(targetInterfaceSummary))
        {
            routingLines.Add(string.Empty);
            routingLines.Add("Target interface");
            routingLines.Add($"- {targetInterfaceSummary}");
        }

        RoutingText = string.Join(Environment.NewLine, routingLines);

        TimelineText = edge.HasSimulationDetails
            ? "Current period uses the visible flow and capacity state shown on the canvas."
            : "No temporal flow is currently visible on this edge.";

        SelectedTab = InspectorTab.Summary;
        IsOpen = open;
    }

    public void InspectTraffic(TrafficSummaryViewModel traffic, bool open = true)
    {
        SelectedObject = traffic;
        Title = $"Traffic: {traffic.Name}";
        SummaryText = traffic.SupplyDemandSummary;
        FlowsText = traffic.OutcomeSummary;
        CapacityText = $"Routing preference: {traffic.RoutingPreferenceLabel}{Environment.NewLine}Allocation: {traffic.AllocationModeLabel}";
        RoutingText = $"{traffic.RoutingPreferenceLabel}{Environment.NewLine}{traffic.AllocationModeLabel}{Environment.NewLine}{traffic.NotesSummary}";
        TimelineText = "Timeline context follows the current period in the canvas and reports drawer.";
        SelectedTab = InspectorTab.Summary;
        IsOpen = open;
    }

    public void InspectRoute(RouteAllocationRowViewModel route, bool open = true)
    {
        SelectedObject = route;
        Title = $"Route: {route.TrafficType}";
        SummaryText = $"{route.Quantity:0.##} unit(s), period {route.Period}{Environment.NewLine}{route.PathDescription}";
        FlowsText = $"{route.SourceLabel} movement from {route.ProducerName} to {route.ConsumerName}{Environment.NewLine}Quantity: {route.Quantity:0.##}";
        CapacityText = route.PathEdgeIds.Count == 0
            ? "Local supply. No edge bottleneck applies."
            : $"Uses {route.PathEdgeIds.Count} edge(s): {string.Join(", ", route.PathEdgeIds)}";
        RoutingText = $"{route.RoutingPreferenceLabel} / {route.AllocationModeLabel}{Environment.NewLine}Time {route.TotalTime:0.##}, transit/unit {route.TransitCostPerUnit:0.##}, landed/unit {route.DeliveredCostPerUnit:0.##}";
        TimelineText = route.Period > 0
            ? $"Reported for period {route.Period}."
            : "Static simulation allocation.";
        SelectedTab = InspectorTab.Summary;
        IsOpen = open;
    }

    public void ClearSelection()
    {
        SelectedObject = null;
        Title = "Inspector";
        SummaryText = "Select a node, edge, traffic type, or route to inspect it.";
        FlowsText = "No selection yet.";
        CapacityText = "No selection yet.";
        RoutingText = "No selection yet.";
        TimelineText = "No selection yet.";
        SelectedTab = InspectorTab.Summary;
    }

    public void Close()
    {
        IsOpen = false;
    }
}
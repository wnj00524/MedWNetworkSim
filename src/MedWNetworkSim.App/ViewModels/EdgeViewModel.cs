using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.ViewModels;

public sealed class EdgeViewModel : ObservableObject
{
    private const double DefaultLabelWidth = 132d;
    private const double DefaultLabelHeight = 30d;
    private const double SimulatedLabelHeight = 62d;
    private const double UtilizationTrackWidth = 76d;
    private const double Epsilon = 0.000001d;

    private static readonly Brush IdleBrush = CreateFrozenBrush("#FFD7C7B1");
    private static readonly Brush LowUsageBrush = CreateFrozenBrush("#FF9BAA76");
    private static readonly Brush MediumUsageBrush = CreateFrozenBrush("#FFC48B4B");
    private static readonly Brush HighUsageBrush = CreateFrozenBrush("#FFC56245");

    private string id;
    private string fromNodeId;
    private string? fromInterfaceNodeId;
    private string toNodeId;
    private string? toInterfaceNodeId;
    private double time;
    private double cost;
    private double? capacity;
    private bool isBidirectional;
    private string? routeType;
    private string? accessNotes;
    private string? seasonalRisk;
    private string? tollNotes;
    private string? securityNotes;
    private NodeViewModel? sourceNode;
    private NodeViewModel? targetNode;
    private bool hasSimulationDetails;
    private double pressureScore;
    private string pressureTopCause = string.Empty;
    private double routedForwardQuantity;
    private double routedReverseQuantity;
    private double flowStrokeThickness;
    private double capacityUtilizationRatio;
    private Brush flowStrokeBrush = IdleBrush;
    private bool isRouteHighlighted;
    private string trafficDetailsLabel = "none visible";

    public EdgeViewModel(EdgeModel model, NodeViewModel? sourceNode, NodeViewModel? targetNode)
    {
        id = model.Id;
        fromNodeId = model.FromNodeId;
        fromInterfaceNodeId = model.FromInterfaceNodeId;
        toNodeId = model.ToNodeId;
        toInterfaceNodeId = model.ToInterfaceNodeId;
        time = model.Time;
        cost = model.Cost;
        capacity = model.Capacity;
        isBidirectional = model.IsBidirectional;
        routeType = model.RouteType;
        accessNotes = model.AccessNotes;
        seasonalRisk = model.SeasonalRisk;
        tollNotes = model.TollNotes;
        securityNotes = model.SecurityNotes;
        UpdateResolvedNodes(sourceNode, targetNode);
    }

    public event EventHandler? DefinitionChanged;

    public string Id
    {
        get => id;
        set
        {
            if (!SetProperty(ref id, value))
            {
                return;
            }

            OnPropertyChanged(nameof(EdgeToolTipText));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string FromNodeId
    {
        get => fromNodeId;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                // WPF ComboBox can transiently push null/empty when the ItemsSource is refreshed.
                // Ignore that intermediate value so an existing endpoint is not accidentally cleared.
                return;
            }

            if (!SetProperty(ref fromNodeId, value))
            {
                return;
            }

            OnPropertyChanged(nameof(EdgeToolTipText));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? FromInterfaceNodeId
    {
        get => fromInterfaceNodeId;
        set
        {
            if (!SetProperty(ref fromInterfaceNodeId, value))
            {
                return;
            }

            OnPropertyChanged(nameof(EndpointInterfaceLabel));
            OnPropertyChanged(nameof(EdgeToolTipText));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string ToNodeId
    {
        get => toNodeId;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                // WPF ComboBox can transiently push null/empty when the ItemsSource is refreshed.
                // Ignore that intermediate value so an existing endpoint is not accidentally cleared.
                return;
            }

            if (!SetProperty(ref toNodeId, value))
            {
                return;
            }

            OnPropertyChanged(nameof(EdgeToolTipText));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? ToInterfaceNodeId
    {
        get => toInterfaceNodeId;
        set
        {
            if (!SetProperty(ref toInterfaceNodeId, value))
            {
                return;
            }

            OnPropertyChanged(nameof(EndpointInterfaceLabel));
            OnPropertyChanged(nameof(EdgeToolTipText));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Time
    {
        get => time;
        set
        {
            if (!SetProperty(ref time, value))
            {
                return;
            }

            OnPropertyChanged(nameof(TotalCost));
            OnPropertyChanged(nameof(SummaryLabel));
            OnPropertyChanged(nameof(RouteDetailLabel));
            OnPropertyChanged(nameof(EdgeToolTipText));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Cost
    {
        get => cost;
        set
        {
            if (!SetProperty(ref cost, value))
            {
                return;
            }

            OnPropertyChanged(nameof(TotalCost));
            OnPropertyChanged(nameof(SummaryLabel));
            OnPropertyChanged(nameof(RouteDetailLabel));
            OnPropertyChanged(nameof(EdgeToolTipText));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double? Capacity
    {
        get => capacity;
        set
        {
            if (!SetProperty(ref capacity, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CapacityLabel));
            OnPropertyChanged(nameof(EdgeToolTipText));
            OnPropertyChanged(nameof(UtilizationPercentLabel));
            RefreshSimulationDerivedState();
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsBidirectional
    {
        get => isBidirectional;
        set
        {
            if (!SetProperty(ref isBidirectional, value))
            {
                return;
            }

            OnPropertyChanged(nameof(DirectionLabel));
            OnPropertyChanged(nameof(RouteSummaryLabel));
            OnPropertyChanged(nameof(RouteDetailLabel));
            OnPropertyChanged(nameof(ArrowVisibility));
            OnPropertyChanged(nameof(FlowArrowVisibility));
            OnPropertyChanged(nameof(ArrowPoints));
            OnPropertyChanged(nameof(FlowSummaryLabel));
            OnPropertyChanged(nameof(EdgeToolTipText));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? RouteType
    {
        get => routeType;
        set
        {
            if (!SetProperty(ref routeType, value))
            {
                return;
            }

            OnPropertyChanged(nameof(RouteSummaryLabel));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? AccessNotes
    {
        get => accessNotes;
        set
        {
            if (!SetProperty(ref accessNotes, value))
            {
                return;
            }

            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? SeasonalRisk
    {
        get => seasonalRisk;
        set
        {
            if (!SetProperty(ref seasonalRisk, value))
            {
                return;
            }

            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? TollNotes
    {
        get => tollNotes;
        set
        {
            if (!SetProperty(ref tollNotes, value))
            {
                return;
            }

            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? SecurityNotes
    {
        get => securityNotes;
        set
        {
            if (!SetProperty(ref securityNotes, value))
            {
                return;
            }

            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double TotalCost => Time + Cost;

    public string DirectionLabel => IsBidirectional ? "2-way" : "1-way";

    public string RouteSummaryLabel => string.IsNullOrWhiteSpace(RouteType)
        ? DirectionLabel
        : RouteType.Trim();

    public string RouteDetailLabel => $"{DirectionLabel} | time {Time:0.##} | cost {Cost:0.##}";

    public string EndpointInterfaceLabel
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(FromInterfaceNodeId))
            {
                parts.Add($"from {FromInterfaceNodeId}");
            }

            if (!string.IsNullOrWhiteSpace(ToInterfaceNodeId))
            {
                parts.Add($"to {ToInterfaceNodeId}");
            }

            return parts.Count == 0 ? string.Empty : $"interfaces {string.Join(" | ", parts)}";
        }
    }

    public string SummaryLabel => $"t {Time:0.##} | c {Cost:0.##} | tc {TotalCost:0.##}";

    public string CapacityLabel => Capacity.HasValue
        ? $"cap {Capacity.Value:0.##}"
        : "cap inf";

    public string CapacityDisplayLabel => !HasSimulationDetails
        ? CapacityLabel
        : Capacity.HasValue
            ? $"used {RoutedTotalQuantity:0.##} / {Capacity.Value:0.##} ({capacityUtilizationRatio:0%})"
            : $"used {RoutedTotalQuantity:0.##} | cap inf";

    public string UtilizationPercentLabel => Capacity.HasValue
        ? $"{capacityUtilizationRatio:0%}"
        : "Unlimited capacity";

    public string EdgeToolTipText =>
        $"{Id}{Environment.NewLine}" +
        $"{FromNodeId} -> {ToNodeId} ({DirectionLabel}){Environment.NewLine}" +
        $"{EndpointInterfaceLabel}{Environment.NewLine}" +
        $"Time {Time:0.##} | Cost {Cost:0.##} | Total {TotalCost:0.##}{Environment.NewLine}" +
        $"{CapacityDisplayLabel}{Environment.NewLine}" +
        $"Flow: {(HasSimulationDetails ? FlowSummaryLabel : "none visible")}{Environment.NewLine}" +
        $"Traffic: {TrafficDetailsLabel}{Environment.NewLine}" +
        $"Utilization: {UtilizationPercentLabel}{Environment.NewLine}" +
        $"Pressure: {PressureSummaryLabel}";

    public string TrafficDetailsLabel => trafficDetailsLabel;

    public string PressureSummaryLabel => pressureScore <= Epsilon
        ? "none"
        : $"{pressureScore:0.##}" +
          (string.IsNullOrWhiteSpace(pressureTopCause) ? string.Empty : $" ({pressureTopCause})");

    public string FlowSummaryLabel
    {
        get
        {
            if (!HasSimulationDetails)
            {
                return string.Empty;
            }

            if (!IsBidirectional)
            {
                return $"flow {RoutedTotalQuantity:0.##}";
            }

            if (routedForwardQuantity > Epsilon && routedReverseQuantity > Epsilon)
            {
                return $"flow -> {routedForwardQuantity:0.##} | <- {routedReverseQuantity:0.##}";
            }

            if (routedForwardQuantity > Epsilon)
            {
                return $"flow -> {routedForwardQuantity:0.##}";
            }

            if (routedReverseQuantity > Epsilon)
            {
                return $"flow <- {routedReverseQuantity:0.##}";
            }

            return "flow 0";
        }
    }

    public double RoutedTotalQuantity => routedForwardQuantity + routedReverseQuantity;

    public bool HasSimulationDetails => hasSimulationDetails;

    public double LabelWidth => DefaultLabelWidth;

    public double LabelHeight => HasSimulationDetails ? SimulatedLabelHeight : DefaultLabelHeight;

    public Visibility FlowSummaryVisibility => HasSimulationDetails ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TechnicalLabelVisibility => HasSimulationDetails || isRouteHighlighted
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility UtilizationBarVisibility => HasSimulationDetails && Capacity.HasValue ? Visibility.Visible : Visibility.Collapsed;

    public double UtilizationBarWidth => UtilizationBarVisibility == Visibility.Visible
        ? UtilizationTrackWidth * capacityUtilizationRatio
        : 0d;

    public Brush FlowStrokeBrush => flowStrokeBrush;

    public double FlowStrokeThickness => flowStrokeThickness;

    public Visibility FlowOverlayVisibility => HasValidEndpoints && RoutedTotalQuantity > Epsilon
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility RouteHighlightVisibility => HasValidEndpoints && isRouteHighlighted
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double RouteHighlightStrokeThickness => Math.Max(9d, FlowStrokeThickness + 5d);

    public Visibility FlowArrowVisibility => FlowOverlayVisibility == Visibility.Visible && ArrowVisibility == Visibility.Visible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ArrowVisibility => IsBidirectional || !HasValidEndpoints ? Visibility.Collapsed : Visibility.Visible;

    public Visibility EdgeVisibility => HasValidEndpoints ? Visibility.Visible : Visibility.Collapsed;

    public double X1 => GetSegmentEndpoints().start.X;

    public double Y1 => GetSegmentEndpoints().start.Y;

    public double X2 => GetSegmentEndpoints().end.X;

    public double Y2 => GetSegmentEndpoints().end.Y;

    public double LabelLeft => ((X1 + X2) / 2d) - (LabelWidth / 2d);

    public double LabelTop => ((Y1 + Y2) / 2d) - (LabelHeight / 2d);

    public string ArrowPoints
    {
        get
        {
            if (IsBidirectional || !HasValidEndpoints)
            {
                return string.Empty;
            }

            var start = new Point(X1, Y1);
            var end = new Point(X2, Y2);
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));

            if (length <= 0.001d)
            {
                return string.Empty;
            }

            var ux = dx / length;
            var uy = dy / length;
            const double arrowLength = 18d;
            const double arrowWidth = 8d;

            // Build the arrowhead from the edge direction vector so it tracks node dragging automatically.
            var baseX = end.X - (ux * arrowLength);
            var baseY = end.Y - (uy * arrowLength);

            var leftX = baseX - (uy * arrowWidth);
            var leftY = baseY + (ux * arrowWidth);
            var rightX = baseX + (uy * arrowWidth);
            var rightY = baseY - (ux * arrowWidth);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{end.X:0.##},{end.Y:0.##} {leftX:0.##},{leftY:0.##} {rightX:0.##},{rightY:0.##}");
        }
    }

    public void ResolveNodes(IReadOnlyDictionary<string, NodeViewModel> nodeMap)
    {
        nodeMap.TryGetValue(FromNodeId, out var resolvedSourceNode);
        nodeMap.TryGetValue(ToNodeId, out var resolvedTargetNode);
        UpdateResolvedNodes(resolvedSourceNode, resolvedTargetNode);
    }

    public void ApplySimulationVisuals(
        double forwardQuantity,
        double reverseQuantity,
        double maxVisibleFlowQuantity,
        bool hasSimulationSnapshot)
    {
        routedForwardQuantity = Math.Max(0d, forwardQuantity);
        routedReverseQuantity = Math.Max(0d, reverseQuantity);
        hasSimulationDetails = hasSimulationSnapshot && (Capacity.HasValue || RoutedTotalQuantity > Epsilon);
        RefreshSimulationDerivedState(maxVisibleFlowQuantity);
    }

    public void ClearSimulationVisuals()
    {
        routedForwardQuantity = 0d;
        routedReverseQuantity = 0d;
        hasSimulationDetails = false;
        RefreshSimulationDerivedState(0d);
    }

    public void ApplyTimelinePressure(TemporalNetworkSimulationEngine.EdgePressureSnapshot? pressure)
    {
        pressureScore = pressure?.Score > Epsilon ? pressure.Value.Score : 0d;
        pressureTopCause = pressure is { TopCause: { Length: > 0 } } ? pressure.Value.TopCause : string.Empty;
        OnPropertyChanged(nameof(PressureSummaryLabel));
        OnPropertyChanged(nameof(EdgeToolTipText));
    }

    public void ClearTimelinePressure()
    {
        pressureScore = 0d;
        pressureTopCause = string.Empty;
        OnPropertyChanged(nameof(PressureSummaryLabel));
        OnPropertyChanged(nameof(EdgeToolTipText));
    }

    public void ApplyRouteHighlight(bool isHighlighted)
    {
        if (this.isRouteHighlighted == isHighlighted)
        {
            return;
        }

        this.isRouteHighlighted = isHighlighted;
        OnPropertyChanged(nameof(RouteHighlightVisibility));
        OnPropertyChanged(nameof(RouteHighlightStrokeThickness));
        OnPropertyChanged(nameof(TechnicalLabelVisibility));
    }

    public EdgeModel ToModel()
    {
        return new EdgeModel
        {
            Id = Id,
            FromNodeId = FromNodeId,
            FromInterfaceNodeId = FromInterfaceNodeId,
            ToNodeId = ToNodeId,
            ToInterfaceNodeId = ToInterfaceNodeId,
            Time = Time,
            Cost = Cost,
            Capacity = Capacity,
            IsBidirectional = IsBidirectional,
            RouteType = RouteType,
            AccessNotes = AccessNotes,
            SeasonalRisk = SeasonalRisk,
            TollNotes = TollNotes,
            SecurityNotes = SecurityNotes
        };
    }

    private bool HasValidEndpoints => sourceNode is not null && targetNode is not null;

    public void ApplyTrafficDetails(
        IReadOnlyList<KeyValuePair<string, double>> forwardByTraffic,
        IReadOnlyList<KeyValuePair<string, double>> reverseByTraffic)
    {
        var builder = new StringBuilder();
        AppendTrafficDetails(builder, "->", forwardByTraffic);
        AppendTrafficDetails(builder, "<-", reverseByTraffic);
        trafficDetailsLabel = builder.Length == 0 ? "none visible" : builder.ToString();
        OnPropertyChanged(nameof(TrafficDetailsLabel));
        OnPropertyChanged(nameof(EdgeToolTipText));
    }

    private void UpdateResolvedNodes(NodeViewModel? newSourceNode, NodeViewModel? newTargetNode)
    {
        if (ReferenceEquals(sourceNode, newSourceNode) && ReferenceEquals(targetNode, newTargetNode))
        {
            return;
        }

        if (sourceNode is not null)
        {
            sourceNode.PropertyChanged -= HandleEndpointChanged;
        }

        if (targetNode is not null)
        {
            targetNode.PropertyChanged -= HandleEndpointChanged;
        }

        sourceNode = newSourceNode;
        targetNode = newTargetNode;

        if (sourceNode is not null)
        {
            sourceNode.PropertyChanged += HandleEndpointChanged;
        }

        if (targetNode is not null)
        {
            targetNode.PropertyChanged += HandleEndpointChanged;
        }

        OnGeometryChanged();
        OnPropertyChanged(nameof(EdgeVisibility));
        OnPropertyChanged(nameof(ArrowVisibility));
        OnPropertyChanged(nameof(FlowArrowVisibility));
    }

    private (Point start, Point end) GetSegmentEndpoints()
    {
        if (sourceNode is null || targetNode is null)
        {
            return (new Point(0d, 0d), new Point(0d, 0d));
        }

        var source = new Point(sourceNode.CenterX, sourceNode.CenterY);
        var target = new Point(targetNode.CenterX, targetNode.CenterY);

        // Edges connect to the border of each node card rather than the center to keep the canvas readable.
        var outbound = FindRectangleIntersection(source, target, sourceNode.Width / 2d, sourceNode.Height / 2d);
        var inbound = FindRectangleIntersection(target, source, targetNode.Width / 2d, targetNode.Height / 2d);
        return (outbound, inbound);
    }

    private static Point FindRectangleIntersection(Point center, Point other, double halfWidth, double halfHeight)
    {
        var dx = other.X - center.X;
        var dy = other.Y - center.Y;

        if (Math.Abs(dx) < 0.001d && Math.Abs(dy) < 0.001d)
        {
            return center;
        }

        var scaleX = Math.Abs(dx) < 0.001d ? double.PositiveInfinity : halfWidth / Math.Abs(dx);
        var scaleY = Math.Abs(dy) < 0.001d ? double.PositiveInfinity : halfHeight / Math.Abs(dy);
        var scale = Math.Min(scaleX, scaleY);

        return new Point(center.X + (dx * scale), center.Y + (dy * scale));
    }

    private void HandleEndpointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(NodeViewModel.X) and not nameof(NodeViewModel.Y))
        {
            return;
        }

        OnGeometryChanged();
    }

    private void OnGeometryChanged()
    {
        OnPropertyChanged(nameof(X1));
        OnPropertyChanged(nameof(Y1));
        OnPropertyChanged(nameof(X2));
        OnPropertyChanged(nameof(Y2));
        OnPropertyChanged(nameof(LabelLeft));
        OnPropertyChanged(nameof(LabelTop));
        OnPropertyChanged(nameof(ArrowPoints));
    }

    private void RefreshSimulationDerivedState(double maxVisibleFlowQuantity = 0d)
    {
        var normalizedFlow = RoutedTotalQuantity <= Epsilon || maxVisibleFlowQuantity <= Epsilon
            ? 0d
            : Math.Min(1d, RoutedTotalQuantity / maxVisibleFlowQuantity);
        capacityUtilizationRatio = Capacity.HasValue && Capacity.Value > Epsilon
            ? Math.Min(1d, RoutedTotalQuantity / Capacity.Value)
            : 0d;
        flowStrokeThickness = RoutedTotalQuantity <= Epsilon
            ? 0d
            : 3d + (normalizedFlow * 5d);
        flowStrokeBrush = PickUsageBrush(
            Capacity.HasValue ? capacityUtilizationRatio : normalizedFlow,
            RoutedTotalQuantity > Epsilon);

        OnPropertyChanged(nameof(RoutedTotalQuantity));
        OnPropertyChanged(nameof(CapacityDisplayLabel));
        OnPropertyChanged(nameof(UtilizationPercentLabel));
        OnPropertyChanged(nameof(EdgeToolTipText));
        OnPropertyChanged(nameof(FlowSummaryLabel));
        OnPropertyChanged(nameof(HasSimulationDetails));
        OnPropertyChanged(nameof(LabelHeight));
        OnPropertyChanged(nameof(LabelTop));
        OnPropertyChanged(nameof(FlowSummaryVisibility));
        OnPropertyChanged(nameof(TechnicalLabelVisibility));
        OnPropertyChanged(nameof(UtilizationBarVisibility));
        OnPropertyChanged(nameof(UtilizationBarWidth));
        OnPropertyChanged(nameof(FlowStrokeBrush));
        OnPropertyChanged(nameof(FlowStrokeThickness));
        OnPropertyChanged(nameof(FlowOverlayVisibility));
        OnPropertyChanged(nameof(FlowArrowVisibility));
        OnPropertyChanged(nameof(RouteHighlightStrokeThickness));
    }

    private static Brush PickUsageBrush(double ratio, bool hasFlow)
    {
        if (!hasFlow)
        {
            return IdleBrush;
        }

        if (ratio >= 0.8d)
        {
            return HighUsageBrush;
        }

        if (ratio >= 0.45d)
        {
            return MediumUsageBrush;
        }

        return LowUsageBrush;
    }

    private static Brush CreateFrozenBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static void AppendTrafficDetails(StringBuilder builder, string direction, IReadOnlyList<KeyValuePair<string, double>> traffic)
    {
        if (traffic.Count == 0)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(" | ");
        }

        builder.Append(direction);
        builder.Append(' ');
        builder.Append(string.Join(", ", traffic.Select(item => $"{item.Key} {item.Value:0.##}")));
    }
}

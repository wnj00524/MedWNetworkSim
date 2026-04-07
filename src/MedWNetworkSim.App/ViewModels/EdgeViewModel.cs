using System.ComponentModel;
using System.Globalization;
using System.Windows;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class EdgeViewModel : ObservableObject
{
    private const double LabelWidth = 176d;
    private const double LabelHeight = 58d;

    private string id;
    private string fromNodeId;
    private string toNodeId;
    private double time;
    private double cost;
    private double? capacity;
    private bool isBidirectional;
    private NodeViewModel? sourceNode;
    private NodeViewModel? targetNode;

    public EdgeViewModel(EdgeModel model, NodeViewModel? sourceNode, NodeViewModel? targetNode)
    {
        id = model.Id;
        fromNodeId = model.FromNodeId;
        toNodeId = model.ToNodeId;
        time = model.Time;
        cost = model.Cost;
        capacity = model.Capacity;
        isBidirectional = model.IsBidirectional;
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

            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string FromNodeId
    {
        get => fromNodeId;
        set
        {
            if (!SetProperty(ref fromNodeId, value))
            {
                return;
            }

            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string ToNodeId
    {
        get => toNodeId;
        set
        {
            if (!SetProperty(ref toNodeId, value))
            {
                return;
            }

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
            OnPropertyChanged(nameof(ArrowVisibility));
            OnPropertyChanged(nameof(ArrowPoints));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double TotalCost => Time + Cost;

    public string DirectionLabel => IsBidirectional ? "2-way" : "1-way";

    public string SummaryLabel => $"t {Time:0.##} | c {Cost:0.##} | tc {TotalCost:0.##}";

    public string CapacityLabel => Capacity.HasValue
        ? $"cap {Capacity.Value:0.##}"
        : "cap inf";

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

    public EdgeModel ToModel()
    {
        return new EdgeModel
        {
            Id = Id,
            FromNodeId = FromNodeId,
            ToNodeId = ToNodeId,
            Time = Time,
            Cost = Cost,
            Capacity = Capacity,
            IsBidirectional = IsBidirectional
        };
    }

    private bool HasValidEndpoints => sourceNode is not null && targetNode is not null;

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
}

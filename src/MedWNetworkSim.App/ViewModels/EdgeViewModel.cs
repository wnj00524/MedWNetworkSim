using System.ComponentModel;
using System.Globalization;
using System.Windows;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class EdgeViewModel : ObservableObject
{
    private const double LabelWidth = 176d;
    private const double LabelHeight = 58d;

    public EdgeViewModel(EdgeModel model, NodeViewModel sourceNode, NodeViewModel targetNode)
    {
        Model = model;
        SourceNode = sourceNode;
        TargetNode = targetNode;

        SourceNode.PropertyChanged += HandleEndpointChanged;
        TargetNode.PropertyChanged += HandleEndpointChanged;
    }

    public EdgeModel Model { get; }

    public NodeViewModel SourceNode { get; }

    public NodeViewModel TargetNode { get; }

    public double Time => Model.Time;

    public double Cost => Model.Cost;

    public double? Capacity => Model.Capacity;

    public bool IsBidirectional => Model.IsBidirectional;

    public string DirectionLabel => IsBidirectional ? "2-way" : "1-way";

    public double TotalCost => Time + Cost;

    public double X1 => GetSegmentEndpoints().start.X;

    public double Y1 => GetSegmentEndpoints().start.Y;

    public double X2 => GetSegmentEndpoints().end.X;

    public double Y2 => GetSegmentEndpoints().end.Y;

    public double LabelLeft => ((X1 + X2) / 2d) - (LabelWidth / 2d);

    public double LabelTop => ((Y1 + Y2) / 2d) - (LabelHeight / 2d);

    public string SummaryLabel => $"t {Time:0.##} | c {Cost:0.##} | tc {TotalCost:0.##}";

    public string CapacityLabel => Capacity.HasValue
        ? $"cap {Capacity.Value:0.##}"
        : "cap inf";

    public Visibility ArrowVisibility => IsBidirectional ? Visibility.Collapsed : Visibility.Visible;

    public string ArrowPoints
    {
        get
        {
            if (IsBidirectional)
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

    public EdgeModel ToModel()
    {
        return new EdgeModel
        {
            Id = Model.Id,
            FromNodeId = Model.FromNodeId,
            ToNodeId = Model.ToNodeId,
            Time = Model.Time,
            Cost = Model.Cost,
            Capacity = Model.Capacity,
            IsBidirectional = Model.IsBidirectional
        };
    }

    private (Point start, Point end) GetSegmentEndpoints()
    {
        var source = new Point(SourceNode.CenterX, SourceNode.CenterY);
        var target = new Point(TargetNode.CenterX, TargetNode.CenterY);

        var outbound = FindRectangleIntersection(source, target, SourceNode.Width / 2d, SourceNode.Height / 2d);
        var inbound = FindRectangleIntersection(target, source, TargetNode.Width / 2d, TargetNode.Height / 2d);
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

        OnPropertyChanged(nameof(X1));
        OnPropertyChanged(nameof(Y1));
        OnPropertyChanged(nameof(X2));
        OnPropertyChanged(nameof(Y2));
        OnPropertyChanged(nameof(LabelLeft));
        OnPropertyChanged(nameof(LabelTop));
        OnPropertyChanged(nameof(ArrowPoints));
    }
}

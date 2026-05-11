using SkiaSharp;
using System.Globalization;

namespace MedWNetworkSim.Rendering;
/// <summary>
/// Represents the graph point component.
/// </summary>

public readonly record struct GraphPoint(double X, double Y)
{
    public static GraphPoint operator +(GraphPoint left, GraphVector right) => new(left.X + right.X, left.Y + right.Y);
    public static GraphVector operator -(GraphPoint left, GraphPoint right) => new(left.X - right.X, left.Y - right.Y);
}
/// <summary>
/// Represents the graph vector component.
/// </summary>

public readonly record struct GraphVector(double X, double Y)
{
    /// <summary>
    /// Gets or sets the length.
    /// </summary>
    public double Length => Math.Sqrt((X * X) + (Y * Y));
}
/// <summary>
/// Represents the graph size component.
/// </summary>

public readonly record struct GraphSize(double Width, double Height);
/// <summary>
/// Represents the graph rect component.
/// </summary>

public readonly record struct GraphRect(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// Gets or sets the left.
    /// </summary>
    public double Left => X;
    /// <summary>
    /// Gets or sets the top.
    /// </summary>
    public double Top => Y;
    /// <summary>
    /// Gets or sets the right.
    /// </summary>
    public double Right => X + Width;
    /// <summary>
    /// Gets or sets the bottom.
    /// </summary>
    public double Bottom => Y + Height;
    /// <summary>
    /// Gets or sets the center x.
    /// </summary>
    public double CenterX => X + (Width / 2d);
    /// <summary>
    /// Gets or sets the center y.
    /// </summary>
    public double CenterY => Y + (Height / 2d);
    /// <summary>
    /// Executes the contains operation.
    /// </summary>

    public bool Contains(GraphPoint point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
    /// <summary>
    /// Executes the from points operation.
    /// </summary>

    public static GraphRect FromPoints(GraphPoint a, GraphPoint b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.X, b.X);
        var bottom = Math.Max(a.Y, b.Y);
        return new GraphRect(left, top, right - left, bottom - top);
    }
    /// <summary>
    /// Gets or sets the empty.
    /// </summary>

    public static GraphRect Empty => new(0d, 0d, 0d, 0d);
}
/// <summary>
/// Represents the graph viewport component.
/// </summary>

public sealed class GraphViewport
{
    public const double MinimumZoom = 0.18d;
    public const double MaximumZoom = 3.75d;
    /// <summary>
    /// Gets or sets the center.
    /// </summary>

    public GraphPoint Center { get; private set; } = new(0d, 0d);
    /// <summary>
    /// Gets or sets the zoom.
    /// </summary>
    public double Zoom { get; private set; } = 1d;
    /// <summary>
    /// Executes the screen to world operation.
    /// </summary>

    public GraphPoint ScreenToWorld(GraphPoint screenPoint, GraphSize viewportSize)
    {
        var worldLeft = Center.X - (viewportSize.Width / (2d * Zoom));
        var worldTop = Center.Y - (viewportSize.Height / (2d * Zoom));
        return new GraphPoint(worldLeft + (screenPoint.X / Zoom), worldTop + (screenPoint.Y / Zoom));
    }
    /// <summary>
    /// Executes the world to screen operation.
    /// </summary>

    public GraphPoint WorldToScreen(GraphPoint worldPoint, GraphSize viewportSize)
    {
        var worldLeft = Center.X - (viewportSize.Width / (2d * Zoom));
        var worldTop = Center.Y - (viewportSize.Height / (2d * Zoom));
        return new GraphPoint((worldPoint.X - worldLeft) * Zoom, (worldPoint.Y - worldTop) * Zoom);
    }
    /// <summary>
    /// Executes the pan operation.
    /// </summary>

    public void Pan(GraphVector worldDelta)
    {
        Center = new GraphPoint(Center.X - worldDelta.X, Center.Y - worldDelta.Y);
    }
    /// <summary>
    /// Executes the zoom at operation.
    /// </summary>

    public void ZoomAt(GraphPoint anchorScreen, GraphSize viewportSize, double zoomFactor)
    {
        var before = ScreenToWorld(anchorScreen, viewportSize);
        Zoom = Math.Clamp(Zoom * zoomFactor, MinimumZoom, MaximumZoom);
        var after = ScreenToWorld(anchorScreen, viewportSize);
        Center = new GraphPoint(Center.X + (before.X - after.X), Center.Y + (before.Y - after.Y));
    }
    /// <summary>
    /// Executes the reset operation.
    /// </summary>

    public void Reset(GraphRect contentBounds, GraphSize viewportSize, double padding = 140d)
    {
        var width = Math.Max(1d, contentBounds.Width + (padding * 2d));
        var height = Math.Max(1d, contentBounds.Height + (padding * 2d));
        var zoomX = viewportSize.Width / width;
        var zoomY = viewportSize.Height / height;
        Zoom = Math.Clamp(Math.Min(zoomX, zoomY), MinimumZoom, MaximumZoom);
        Center = new GraphPoint(contentBounds.CenterX, contentBounds.CenterY);
    }
}
/// <summary>
/// Specifies the zoom tier.
/// </summary>

public enum ZoomTier
{
    Far,
    Medium,
    Near
}
/// <summary>
/// Represents the graph node text line component.
/// </summary>

public readonly record struct GraphNodeTextLine(string Text, bool IsEmphasized, bool IsWarning);
/// <summary>
/// Represents the facility coverage info component.
/// </summary>

public sealed class FacilityCoverageInfo
{
    /// <summary>
    /// Gets or sets the facility node id.
    /// </summary>
    public required string FacilityNodeId { get; init; }
    /// <summary>
    /// Gets or sets the facility display name.
    /// </summary>
    public required string FacilityDisplayName { get; init; }
    /// <summary>
    /// Gets or sets the travel time.
    /// </summary>
    public required double TravelTime { get; init; }
    /// <summary>
    /// Gets a value indicating whether is primary facility is enabled or active.
    /// </summary>
    public required bool IsPrimaryFacility { get; init; }
}
/// <summary>
/// Represents the graph node scene item component.
/// </summary>

public sealed class GraphNodeSceneItem
{
    /// <summary>
    /// Gets or sets the unique identifier for this instance.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public required string Name { get; set; }
    /// <summary>
    /// Gets or sets the type label.
    /// </summary>
    public required string TypeLabel { get; set; }
    /// <summary>
    /// Gets or sets the metrics label.
    /// </summary>
    public required string MetricsLabel { get; set; }
    /// <summary>
    /// Gets the collection of detail lines associated with this entity.
    /// </summary>
    public required IReadOnlyList<GraphNodeTextLine> DetailLines { get; set; }
    /// <summary>
    /// Gets or sets the bounds.
    /// </summary>
    public required GraphRect Bounds { get; set; }
    /// <summary>
    /// Gets or sets the fill color.
    /// </summary>
    public required SKColor FillColor { get; set; }
    /// <summary>
    /// Gets or sets the stroke color.
    /// </summary>
    public required SKColor StrokeColor { get; set; }
    /// <summary>
    /// Gets the collection of badges associated with this entity.
    /// </summary>
    public required IReadOnlyList<string> Badges { get; set; }
    /// <summary>
    /// Gets or sets the tool tip text.
    /// </summary>
    public string ToolTipText { get; set; } = string.Empty;
    /// <summary>
    /// Gets a value indicating whether has warning is enabled or active.
    /// </summary>
    public required bool HasWarning { get; set; }
    /// <summary>
    /// Gets or sets the visual opacity.
    /// </summary>
    public double VisualOpacity { get; set; } = 1d;
    /// <summary>
    /// Gets the collection of covering facilities associated with this entity.
    /// </summary>
    public IReadOnlyList<FacilityCoverageInfo> CoveringFacilities { get; set; } = [];
    /// <summary>
    /// Gets a value indicating whether is facility covered is enabled or active.
    /// </summary>
    public bool IsFacilityCovered { get; set; }
    /// <summary>
    /// Gets a value indicating whether is multi facility covered is enabled or active.
    /// </summary>
    public bool IsMultiFacilityCovered { get; set; }
    /// <summary>
    /// Gets or sets the primary facility id.
    /// </summary>
    public string? PrimaryFacilityId { get; set; }
    /// <summary>
    /// Gets or sets the primary facility travel time.
    /// </summary>
    public double? PrimaryFacilityTravelTime { get; set; }
    /// <summary>
    /// Gets or sets the layout content key.
    /// </summary>
    public string? LayoutContentKey { get; set; }
    /// <summary>
    /// Gets or sets the layout zoom tier.
    /// </summary>
    public ZoomTier? LayoutZoomTier { get; set; }
    /// <summary>
    /// Gets or sets the cached layout.
    /// </summary>
    public GraphNodeTextLayoutResult? CachedLayout { get; set; }
    /// <summary>
    /// Gets or sets the cached layout width.
    /// </summary>
    public double CachedLayoutWidth { get; set; }
    /// <summary>
    /// Gets or sets the cached layout height.
    /// </summary>
    public double CachedLayoutHeight { get; set; }
    /// <summary>
    /// Gets a value indicating whether is actor controlled is enabled or active.
    /// </summary>
    public bool IsActorControlled { get; set; }
}
/// <summary>
/// Represents the graph edge scene item component.
/// </summary>

public sealed class GraphEdgeSceneItem
{
    /// <summary>
    /// Gets or sets the unique identifier for this instance.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or sets the from node id.
    /// </summary>
    public required string FromNodeId { get; init; }
    /// <summary>
    /// Gets or sets the to node id.
    /// </summary>
    public required string ToNodeId { get; init; }
    /// <summary>
    /// Gets or sets the label.
    /// </summary>
    public required string Label { get; set; }
    /// <summary>
    /// Gets a value indicating whether is bidirectional is enabled or active.
    /// </summary>
    public required bool IsBidirectional { get; set; }
    /// <summary>
    /// Gets or sets the capacity.
    /// </summary>
    public required double Capacity { get; set; }
    /// <summary>
    /// Gets or sets the cost.
    /// </summary>
    public required double Cost { get; set; }
    /// <summary>
    /// Gets or sets the time.
    /// </summary>
    public required double Time { get; set; }
    /// <summary>
    /// Gets or sets the load ratio.
    /// </summary>
    public required double LoadRatio { get; set; }
    /// <summary>
    /// Gets or sets the flow rate.
    /// </summary>
    public required double FlowRate { get; set; }
    /// <summary>
    /// Gets or sets the tool tip text.
    /// </summary>
    public string ToolTipText { get; set; } = string.Empty;
    /// <summary>
    /// Gets a value indicating whether has warning is enabled or active.
    /// </summary>
    public required bool HasWarning { get; set; }
    /// <summary>
    /// Gets or sets the visual opacity.
    /// </summary>
    public double VisualOpacity { get; set; } = 1d;
    /// <summary>
    /// Gets a value indicating whether is actor controlled is enabled or active.
    /// </summary>
    public bool IsActorControlled { get; set; }
}
/// <summary>
/// Represents the graph transient state component.
/// </summary>

public sealed class GraphTransientState
{
    /// <summary>
    /// Gets or sets the drag start world.
    /// </summary>
    public GraphPoint? DragStartWorld { get; set; }
    /// <summary>
    /// Gets or sets the drag current world.
    /// </summary>
    public GraphPoint? DragCurrentWorld { get; set; }
    /// <summary>
    /// Gets or sets the connection source node id.
    /// </summary>
    public string? ConnectionSourceNodeId { get; set; }
    /// <summary>
    /// Gets or sets the connection world.
    /// </summary>
    public GraphPoint? ConnectionWorld { get; set; }
}
/// <summary>
/// Represents the graph selection state component.
/// </summary>

public sealed class GraphSelectionState
{
    /// <summary>
    /// Gets or sets the selected node ids.
    /// </summary>
    public HashSet<string> SelectedNodeIds { get; } = [];
    /// <summary>
    /// Gets or sets the selected edge ids.
    /// </summary>
    public HashSet<string> SelectedEdgeIds { get; } = [];
    /// <summary>
    /// Gets or sets the highlighted node ids.
    /// </summary>
    public HashSet<string> HighlightedNodeIds { get; } = [];
    /// <summary>
    /// Gets or sets the highlighted edge ids.
    /// </summary>
    public HashSet<string> HighlightedEdgeIds { get; } = [];
    /// <summary>
    /// Gets or sets the hover node id.
    /// </summary>
    public string? HoverNodeId { get; set; }
    /// <summary>
    /// Gets or sets the hover edge id.
    /// </summary>
    public string? HoverEdgeId { get; set; }
    /// <summary>
    /// Gets or sets the keyboard node id.
    /// </summary>
    public string? KeyboardNodeId { get; set; }
    /// <summary>
    /// Gets or sets the keyboard edge id.
    /// </summary>
    public string? KeyboardEdgeId { get; set; }
    /// <summary>
    /// Gets or sets the pulse node id.
    /// </summary>
    public string? PulseNodeId { get; set; }
    /// <summary>
    /// Gets or sets the pulse edge id.
    /// </summary>
    public string? PulseEdgeId { get; set; }
    /// <summary>
    /// Gets or sets the pulse progress.
    /// </summary>
    public double PulseProgress { get; set; }
}
/// <summary>
/// Represents the graph simulation scene state component.
/// </summary>

public sealed class GraphSimulationSceneState
{
    /// <summary>
    /// Gets a value indicating whether show animated flows is enabled or active.
    /// </summary>
    public bool ShowAnimatedFlows { get; set; } = true;
    /// <summary>
    /// Gets a value indicating whether reduced motion is enabled or active.
    /// </summary>
    public bool ReducedMotion { get; set; }
    /// <summary>
    /// Gets a value indicating whether show depth layer is enabled or active.
    /// </summary>
    public bool ShowDepthLayer { get; set; } = true;
    /// <summary>
    /// Gets or sets the animation time.
    /// </summary>
    public double AnimationTime { get; set; }
    /// <summary>
    /// Gets a value indicating whether show agent overlays is enabled or active.
    /// </summary>
    public bool ShowAgentOverlays { get; set; }
}
/// <summary>
/// Represents the graph scene component.
/// </summary>

public sealed class GraphScene
{
    /// <summary>
    /// Gets the collection of nodes associated with this entity.
    /// </summary>
    public IList<GraphNodeSceneItem> Nodes { get; } = [];
    /// <summary>
    /// Gets the collection of edges associated with this entity.
    /// </summary>
    public IList<GraphEdgeSceneItem> Edges { get; } = [];
    /// <summary>
    /// Gets or sets the selection.
    /// </summary>
    public GraphSelectionState Selection { get; } = new();
    /// <summary>
    /// Gets or sets the transient.
    /// </summary>
    public GraphTransientState Transient { get; } = new();
    /// <summary>
    /// Gets or sets the simulation.
    /// </summary>
    public GraphSimulationSceneState Simulation { get; } = new();
    /// <summary>
    /// Retrieves the content bounds based on the provided parameters.
    /// </summary>

    public GraphRect GetContentBounds()
    {
        if (Nodes.Count == 0)
        {
            return new GraphRect(-480d, -320d, 960d, 640d);
        }

        var left = Nodes.Min(node => node.Bounds.Left);
        var top = Nodes.Min(node => node.Bounds.Top);
        var right = Nodes.Max(node => node.Bounds.Right);
        var bottom = Nodes.Max(node => node.Bounds.Bottom);
        return new GraphRect(left, top, right - left, bottom - top);
    }
    /// <summary>
    /// Executes the find node operation.
    /// </summary>

    public GraphNodeSceneItem? FindNode(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase));
    /// <summary>
    /// Executes the find edge operation.
    /// </summary>

    public GraphEdgeSceneItem? FindEdge(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Edges.FirstOrDefault(edge => string.Equals(edge.Id, id, StringComparison.OrdinalIgnoreCase));
}
/// <summary>
/// Represents the graph hit result component.
/// </summary>

public readonly record struct GraphHitResult(string? NodeId, string? EdgeId);
/// <summary>
/// Represents the graph hit tester component.
/// </summary>

public sealed class GraphHitTester
{
    public const double CompactNodeRadius = 7d;
    /// <summary>
    /// Gets or sets the node hit padding.
    /// </summary>

    public double NodeHitPadding { get; set; } = 6d;
    /// <summary>
    /// Gets or sets the edge hit radius.
    /// </summary>
    public double EdgeHitRadius { get; set; } = 10d;
    /// <summary>
    /// Gets or sets the edge handle radius.
    /// </summary>
    public double EdgeHandleRadius { get; set; } = 10d;
    /// <summary>
    /// Executes the hit test operation.
    /// </summary>

    public GraphHitResult HitTest(GraphScene scene, GraphPoint worldPoint, double zoom = 1d, bool showNodeLabels = true)
    {
        var safeZoom = Math.Max(0.05d, zoom);
        var nodePaddingWorld = NodeHitPadding / safeZoom;
        var edgeRadiusWorld = EdgeHitRadius / safeZoom;
        var edgeHandleRadiusWorld = EdgeHandleRadius / safeZoom;

        foreach (var node in scene.Nodes.OrderByDescending(node => node.Bounds.Top))
        {
            if (showNodeLabels)
            {
                var padded = new GraphRect(
                    node.Bounds.X - nodePaddingWorld,
                    node.Bounds.Y - nodePaddingWorld,
                    node.Bounds.Width + (nodePaddingWorld * 2d),
                    node.Bounds.Height + (nodePaddingWorld * 2d));
                if (padded.Contains(worldPoint))
                {
                    return new GraphHitResult(node.Id, null);
                }
            }
            else if ((worldPoint - GetNodeCenter(node)).Length <= CompactNodeRadius + nodePaddingWorld)
            {
                return new GraphHitResult(node.Id, null);
            }
        }

        var handleHit = scene.Edges
            .Select(candidate => new { Edge = candidate, Distance = DistanceToMidpoint(scene, candidate, worldPoint, showNodeLabels) })
            .Where(candidate => candidate.Distance <= edgeHandleRadiusWorld)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        if (handleHit is not null)
        {
            return new GraphHitResult(null, handleHit.Edge.Id);
        }

        var edge = scene.Edges
            .Select(candidate => new { Edge = candidate, Distance = DistanceToEdge(scene, candidate, worldPoint, showNodeLabels) })
            .Where(candidate => candidate.Distance <= edgeRadiusWorld)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();

        return edge is null ? default : new GraphHitResult(null, edge.Edge.Id);
    }
    /// <summary>
    /// Retrieves the edge midpoint based on the provided parameters.
    /// </summary>

    public static GraphPoint GetEdgeMidpoint(GraphScene scene, GraphEdgeSceneItem edge)
    {
        var start = GetEdgeAnchor(scene, edge.FromNodeId, edge.ToNodeId, showNodeLabels: true);
        var end = GetEdgeAnchor(scene, edge.ToNodeId, edge.FromNodeId, showNodeLabels: true);
        return new GraphPoint((start.X + end.X) / 2d, (start.Y + end.Y) / 2d);
    }
    /// <summary>
    /// Retrieves the edge midpoint based on the provided parameters.
    /// </summary>

    public static GraphPoint GetEdgeMidpoint(GraphScene scene, GraphEdgeSceneItem edge, bool showNodeLabels)
    {
        var start = GetEdgeAnchor(scene, edge.FromNodeId, edge.ToNodeId, showNodeLabels);
        var end = GetEdgeAnchor(scene, edge.ToNodeId, edge.FromNodeId, showNodeLabels);
        return new GraphPoint((start.X + end.X) / 2d, (start.Y + end.Y) / 2d);
    }

    private static double DistanceToMidpoint(GraphScene scene, GraphEdgeSceneItem edge, GraphPoint worldPoint, bool showNodeLabels) =>
        (worldPoint - GetEdgeMidpoint(scene, edge, showNodeLabels)).Length;

    private static double DistanceToEdge(GraphScene scene, GraphEdgeSceneItem edge, GraphPoint worldPoint, bool showNodeLabels)
    {
        var start = GetEdgeAnchor(scene, edge.FromNodeId, edge.ToNodeId, showNodeLabels);
        var end = GetEdgeAnchor(scene, edge.ToNodeId, edge.FromNodeId, showNodeLabels);
        var segment = end - start;
        var point = worldPoint - start;
        var lengthSquared = (segment.X * segment.X) + (segment.Y * segment.Y);
        if (lengthSquared <= 0.0001d)
        {
            return point.Length;
        }

        var projection = Math.Clamp(((point.X * segment.X) + (point.Y * segment.Y)) / lengthSquared, 0d, 1d);
        var nearest = new GraphPoint(start.X + (segment.X * projection), start.Y + (segment.Y * projection));
        return (worldPoint - nearest).Length;
    }
    /// <summary>
    /// Retrieves the node center based on the provided parameters.
    /// </summary>

    public static GraphPoint GetNodeCenter(GraphNodeSceneItem node) => new(node.Bounds.CenterX, node.Bounds.CenterY);
    /// <summary>
    /// Retrieves the edge anchor based on the provided parameters.
    /// </summary>

    public static GraphPoint GetEdgeAnchor(GraphScene scene, string sourceId, string targetId, bool showNodeLabels = true)
    {
        if (!showNodeLabels)
        {
            var compactSource = scene.FindNode(sourceId);
            var compactTarget = scene.FindNode(targetId);
            if (compactSource is null || compactTarget is null)
            {
                return new GraphPoint(0d, 0d);
            }

            var compactSourceCenter = GetNodeCenter(compactSource);
            var compactTargetCenter = GetNodeCenter(compactTarget);
            var delta = compactTargetCenter - compactSourceCenter;
            var length = delta.Length;
            if (length < 0.001d)
            {
                return compactSourceCenter;
            }

            return new GraphPoint(
                compactSourceCenter.X + ((delta.X / length) * CompactNodeRadius),
                compactSourceCenter.Y + ((delta.Y / length) * CompactNodeRadius));
        }

        var source = scene.FindNode(sourceId);
        var target = scene.FindNode(targetId);
        if (source is null || target is null)
        {
            return new GraphPoint(0d, 0d);
        }

        var sourceCenter = new GraphPoint(source.Bounds.CenterX, source.Bounds.CenterY);
        var targetCenter = new GraphPoint(target.Bounds.CenterX, target.Bounds.CenterY);
        var dx = targetCenter.X - sourceCenter.X;
        var dy = targetCenter.Y - sourceCenter.Y;
        if (Math.Abs(dx) < 0.001d && Math.Abs(dy) < 0.001d)
        {
            return sourceCenter;
        }

        var scaleX = Math.Abs(dx) < 0.001d ? double.PositiveInfinity : (source.Bounds.Width / 2d) / Math.Abs(dx);
        var scaleY = Math.Abs(dy) < 0.001d ? double.PositiveInfinity : (source.Bounds.Height / 2d) / Math.Abs(dy);
        var scale = Math.Min(scaleX, scaleY);
        return new GraphPoint(sourceCenter.X + (dx * scale), sourceCenter.Y + (dy * scale));
    }
}
/// <summary>
/// Represents the graph renderer component.
/// </summary>

public sealed class GraphRenderer
{
    private const float NodeCornerRadius = 20f;
    private const double TextClipInset = 6d;
    private static readonly SKColor BackgroundColor = SKColor.Parse("#091726");
    private static readonly SKColor GridMajorColor = SKColor.Parse("#244664");
    private static readonly SKColor GridMinorColor = SKColor.Parse("#162E43");
    private static readonly SKColor EdgeColor = SKColor.Parse("#5A7F9F");
    private static readonly SKColor OverlayColor = SKColor.Parse("#4DDCFF");
    private static readonly SKColor FocusColor = SKColor.Parse("#F2D38B");
    private static readonly SKColor TextColor = SKColor.Parse("#E4EEF8");
    private static readonly SKColor MutedTextColor = SKColor.Parse("#89A5BA");
    private static readonly SKColor WarningColor = SKColor.Parse("#F39B68");
    private static readonly SKColor PulseColor = SKColor.Parse("#FFF1B8");
    private static readonly SKColor MinimapBackground = new(6, 13, 22, 220);
    /// <summary>
    /// Executes the render operation.
    /// </summary>

    public void Render(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize, bool showNodeLabels = true)
    {
        canvas.Clear(BackgroundColor);
        DrawBackgroundGrid(canvas, viewport, viewportSize);
        if (showNodeLabels)
        {
            PrepareNodeLayouts(scene, viewport);
        }

        DrawDepthLayer(canvas, scene, viewport, viewportSize, showNodeLabels);
        DrawEdges(canvas, scene, viewport, viewportSize, showNodeLabels);
        DrawEdgeOverlays(canvas, scene, viewport, viewportSize, showNodeLabels);
        if (showNodeLabels)
        {
            DrawNodes(canvas, scene, viewport, viewportSize);
            DrawLabels(canvas, scene, viewport, viewportSize);
        }
        else
        {
            DrawCompactNodes(canvas, scene, viewport, viewportSize);
        }

        DrawSelection(canvas, scene, viewport, viewportSize);
        DrawFlowAnimation(canvas, scene, viewport, viewportSize, showNodeLabels);
        DrawTransientInteraction(canvas, scene, viewport, viewportSize);
        DrawMinimap(canvas, scene, viewport, viewportSize, showNodeLabels);
    }
    /// <summary>
    /// Retrieves the zoom tier based on the provided parameters.
    /// </summary>

    public ZoomTier GetZoomTier(double zoom) =>
        zoom < 0.45d ? ZoomTier.Far : zoom < 1.15d ? ZoomTier.Medium : ZoomTier.Near;

    public static (GraphPoint Tip, GraphPoint Left, GraphPoint Right)? GetDirectionalArrowHead(
        GraphPoint start,
        GraphPoint end,
        double strokeWidth)
    {
        var delta = end - start;
        var length = delta.Length;
        if (length < 18d)
        {
            return null;
        }

        var unitX = delta.X / length;
        var unitY = delta.Y / length;
        var arrowLength = Math.Clamp(10d + (strokeWidth * 1.4d), 10d, 16d);
        var arrowInset = Math.Max(18d, arrowLength * 1.45d);
        if (length <= arrowInset + arrowLength)
        {
            return null;
        }

        var tip = new GraphPoint(
            end.X - (unitX * arrowInset),
            end.Y - (unitY * arrowInset));
        var baseCenter = new GraphPoint(
            tip.X - (unitX * arrowLength),
            tip.Y - (unitY * arrowLength));
        var perpendicularX = -unitY;
        var perpendicularY = unitX;
        var halfWidth = arrowLength * 0.58d;

        return (
            tip,
            new GraphPoint(baseCenter.X + (perpendicularX * halfWidth), baseCenter.Y + (perpendicularY * halfWidth)),
            new GraphPoint(baseCenter.X - (perpendicularX * halfWidth), baseCenter.Y - (perpendicularY * halfWidth)));
    }
    /// <summary>
    /// Retrieves the or build node layout based on the provided parameters.
    /// </summary>

    public static GraphNodeTextLayoutResult GetOrBuildNodeLayout(GraphNodeSceneItem node, ZoomTier zoomTier)
    {
        var visibleDetailLines = zoomTier switch
        {
            ZoomTier.Near => 6,
            ZoomTier.Medium => 3,
            _ => 0
        };
        var typeLabel = zoomTier >= ZoomTier.Medium ? node.TypeLabel : string.Empty;
        var contentKey = BuildNodeLayoutContentKey(node, typeLabel, visibleDetailLines);
        if (node.CachedLayout is not null &&
            string.Equals(node.LayoutContentKey, contentKey, StringComparison.Ordinal) &&
            node.LayoutZoomTier == zoomTier)
        {
            return node.CachedLayout;
        }

        var layout = GraphNodeTextLayout.BuildLayout(node.Name, typeLabel, node.DetailLines, maxDetailLines: visibleDetailLines);
        node.LayoutContentKey = contentKey;
        node.LayoutZoomTier = zoomTier;
        node.CachedLayout = layout;
        node.CachedLayoutWidth = layout.Width;
        node.CachedLayoutHeight = layout.Height;
        return layout;
    }
    /// <summary>
    /// Executes the apply layout bounds keeping center operation.
    /// </summary>

    public static void ApplyLayoutBoundsKeepingCenter(GraphNodeSceneItem node, GraphNodeTextLayoutResult layout)
    {
        var centerX = node.Bounds.CenterX;
        var centerY = node.Bounds.CenterY;
        node.Bounds = new GraphRect(
            centerX - (layout.Width / 2d),
            centerY - (layout.Height / 2d),
            layout.Width,
            layout.Height);
    }
    /// <summary>
    /// Executes the build node layout content key operation.
    /// </summary>

    public static string BuildNodeLayoutContentKey(GraphNodeSceneItem node, string effectiveTypeLabel, int visibleDetailLines)
    {
        var detailLines = node.DetailLines
            .Take(Math.Max(0, visibleDetailLines))
            .Select(line => $"{line.Text}|{line.IsEmphasized}|{line.IsWarning}");
        var badges = node.Badges.Select(badge => badge ?? string.Empty);
        return string.Join("~", new[]
        {
            node.Name ?? string.Empty,
            effectiveTypeLabel ?? string.Empty,
            node.MetricsLabel ?? string.Empty,
            string.Join("¦", detailLines),
            string.Join("¦", badges),
            node.HasWarning ? "1" : "0"
        });
    }

    private void PrepareNodeLayouts(GraphScene scene, GraphViewport viewport)
    {
        var tier = GetZoomTier(viewport.Zoom);
        foreach (var node in scene.Nodes)
        {
            var layout = GetOrBuildNodeLayout(node, tier);
            if (Math.Abs(node.Bounds.Width - layout.Width) > 0.001d ||
                Math.Abs(node.Bounds.Height - layout.Height) > 0.001d)
            {
                ApplyLayoutBoundsKeepingCenter(node, layout);
            }
        }
    }

    private static void DrawBackgroundGrid(SKCanvas canvas, GraphViewport viewport, GraphSize viewportSize)
    {
        using var minor = new SKPaint { Color = GridMinorColor, StrokeWidth = 1f, IsAntialias = true };
        using var major = new SKPaint { Color = GridMajorColor, StrokeWidth = 1.2f, IsAntialias = true };
        var minorStep = 48d;
        var majorStep = 240d;
        var topLeft = viewport.ScreenToWorld(new GraphPoint(0d, 0d), viewportSize);
        var bottomRight = viewport.ScreenToWorld(new GraphPoint(viewportSize.Width, viewportSize.Height), viewportSize);

        void DrawLines(double step, SKPaint paint)
        {
            var startX = Math.Floor(topLeft.X / step) * step;
            var endX = Math.Ceiling(bottomRight.X / step) * step;
            for (var x = startX; x <= endX; x += step)
            {
                var p1 = viewport.WorldToScreen(new GraphPoint(x, topLeft.Y), viewportSize);
                var p2 = viewport.WorldToScreen(new GraphPoint(x, bottomRight.Y), viewportSize);
                canvas.DrawLine((float)p1.X, (float)p1.Y, (float)p2.X, (float)p2.Y, paint);
            }

            var startY = Math.Floor(topLeft.Y / step) * step;
            var endY = Math.Ceiling(bottomRight.Y / step) * step;
            for (var y = startY; y <= endY; y += step)
            {
                var p1 = viewport.WorldToScreen(new GraphPoint(topLeft.X, y), viewportSize);
                var p2 = viewport.WorldToScreen(new GraphPoint(bottomRight.X, y), viewportSize);
                canvas.DrawLine((float)p1.X, (float)p1.Y, (float)p2.X, (float)p2.Y, paint);
            }
        }

        DrawLines(minorStep, minor);
        DrawLines(majorStep, major);
    }

    private static void DrawDepthLayer(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize, bool showNodeLabels)
    {
        if (!scene.Simulation.ShowDepthLayer || !showNodeLabels)
        {
            return;
        }

        using var shadow = new SKPaint
        {
            Color = new SKColor(19, 45, 63, 80),
            IsAntialias = true
        };

        foreach (var node in scene.Nodes)
        {
            var depthShift = scene.Simulation.ReducedMotion ? 6d : 6d + (Math.Sin(scene.Simulation.AnimationTime + node.Bounds.CenterX * 0.01d) * 2d);
            var topLeft = viewport.WorldToScreen(new GraphPoint(node.Bounds.Left + depthShift, node.Bounds.Top + depthShift), viewportSize);
            var size = new SKSize((float)(node.Bounds.Width * viewport.Zoom), (float)(node.Bounds.Height * viewport.Zoom));
            canvas.DrawRoundRect(
                new SKRect((float)topLeft.X, (float)topLeft.Y, (float)(topLeft.X + size.Width), (float)(topLeft.Y + size.Height)),
                20f,
                20f,
                shadow);
        }
    }

    private void DrawEdges(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize, bool showNodeLabels)
    {
        using var edgePaint = new SKPaint { Color = EdgeColor, IsAntialias = true, StrokeWidth = 2.8f, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        using var arrowPaint = new SKPaint { Color = EdgeColor, IsAntialias = true, Style = SKPaintStyle.Fill };

        foreach (var edge in scene.Edges)
        {
            var start = viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(scene, edge.FromNodeId, edge.ToNodeId, showNodeLabels), viewportSize);
            var end = viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(scene, edge.ToNodeId, edge.FromNodeId, showNodeLabels), viewportSize);
            var edgeAlpha = (byte)Math.Clamp(Math.Round((edge.HasWarning ? 190d : 180d) * edge.VisualOpacity), 15d, 255d);
            edgePaint.Color = edge.HasWarning ? WarningColor.WithAlpha(edgeAlpha) : EdgeColor.WithAlpha(edgeAlpha);
            edgePaint.StrokeWidth = (float)(4.8d + (edge.LoadRatio * 2.1d));
            using var flowPaint = new SKPaint { Color = (edge.HasWarning ? WarningColor : OverlayColor).WithAlpha((byte)Math.Clamp(edgeAlpha + 25, 40, 255)), IsAntialias = true, StrokeWidth = Math.Max(1.4f, edgePaint.StrokeWidth * 0.34f), Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
            canvas.DrawLine((float)start.X, (float)start.Y, (float)end.X, (float)end.Y, edgePaint);
            canvas.DrawLine((float)start.X, (float)start.Y, (float)end.X, (float)end.Y, flowPaint);
            if (!edge.IsBidirectional)
            {
                arrowPaint.Color = edgePaint.Color;
                DrawDirectionalArrow(canvas, start, end, edgePaint.StrokeWidth, arrowPaint);
            }
        }
    }

    private void DrawEdgeOverlays(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize, bool showNodeLabels)
    {
        using var overlayPaint = new SKPaint { IsAntialias = true, StrokeWidth = 6f, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        using var arrowPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        var overlayEdges = scene.Edges.Where(edge =>
            scene.Selection.SelectedEdgeIds.Contains(edge.Id) ||
            scene.Selection.HighlightedEdgeIds.Contains(edge.Id) ||
            string.Equals(scene.Selection.HoverEdgeId, edge.Id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scene.Selection.KeyboardEdgeId, edge.Id, StringComparison.OrdinalIgnoreCase));
        foreach (var edge in overlayEdges)
        {
            var start = viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(scene, edge.FromNodeId, edge.ToNodeId, showNodeLabels), viewportSize);
            var end = viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(scene, edge.ToNodeId, edge.FromNodeId, showNodeLabels), viewportSize);
            var isSelected = scene.Selection.SelectedEdgeIds.Contains(edge.Id);
            var isHovered = string.Equals(scene.Selection.HoverEdgeId, edge.Id, StringComparison.OrdinalIgnoreCase);
            var isKeyboard = string.Equals(scene.Selection.KeyboardEdgeId, edge.Id, StringComparison.OrdinalIgnoreCase);
            var pulse = GetPulseState(
                scene,
                isSelected && string.Equals(scene.Selection.PulseEdgeId, edge.Id, StringComparison.OrdinalIgnoreCase),
                scene.Selection.PulseProgress);
            overlayPaint.Color = (pulse.IsActive ? PulseColor : isKeyboard ? SKColor.Parse("#F2D38B") : FocusColor).WithAlpha((byte)(isSelected ? 190 : isHovered ? 180 : 145));
            overlayPaint.StrokeWidth = (float)((isSelected ? 6f : isHovered ? 5.5f : 4.5f) + pulse.StrokeBoost);
            canvas.DrawLine((float)start.X, (float)start.Y, (float)end.X, (float)end.Y, overlayPaint);
            if (!edge.IsBidirectional)
            {
                arrowPaint.Color = overlayPaint.Color;
                DrawDirectionalArrow(canvas, start, end, overlayPaint.StrokeWidth, arrowPaint);
            }

            if (isSelected)
            {
                var midpointWorld = GetEdgeMidpoint(scene, edge, showNodeLabels);
                var midpoint = viewport.WorldToScreen(midpointWorld, viewportSize);
                using var handleFill = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#091726") };
                using var handleStroke = new SKPaint { IsAntialias = true, Color = FocusColor, Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f };
                canvas.DrawCircle((float)midpoint.X, (float)midpoint.Y, 6.5f, handleFill);
                canvas.DrawCircle((float)midpoint.X, (float)midpoint.Y, 6.5f, handleStroke);
            }
        }
    }

    private static void DrawDirectionalArrow(SKCanvas canvas, GraphPoint start, GraphPoint end, float strokeWidth, SKPaint paint)
    {
        var arrowHead = GetDirectionalArrowHead(start, end, strokeWidth);
        if (!arrowHead.HasValue)
        {
            return;
        }

        using var path = new SKPath();
        path.MoveTo((float)arrowHead.Value.Tip.X, (float)arrowHead.Value.Tip.Y);
        path.LineTo((float)arrowHead.Value.Left.X, (float)arrowHead.Value.Left.Y);
        path.LineTo((float)arrowHead.Value.Right.X, (float)arrowHead.Value.Right.Y);
        path.Close();
        canvas.DrawPath(path, paint);
    }

    private void DrawNodes(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize)
    {
        foreach (var node in scene.Nodes)
        {
            var screenRect = GetNodeScreenRect(node, viewport, viewportSize);

            var isSelected = scene.Selection.SelectedNodeIds.Contains(node.Id);
            var isHighlighted = scene.Selection.HighlightedNodeIds.Contains(node.Id);
            var isHovered = string.Equals(scene.Selection.HoverNodeId, node.Id, StringComparison.OrdinalIgnoreCase);
            var isKeyboard = string.Equals(scene.Selection.KeyboardNodeId, node.Id, StringComparison.OrdinalIgnoreCase);
            var pulse = GetPulseState(
                scene,
                isSelected && string.Equals(scene.Selection.PulseNodeId, node.Id, StringComparison.OrdinalIgnoreCase),
                scene.Selection.PulseProgress);
            var nodeAlpha = (byte)Math.Clamp(Math.Round(255d * node.VisualOpacity), 32d, 255d);
            using var fill = new SKPaint { Color = node.FillColor.WithAlpha(nodeAlpha), IsAntialias = true };
            using var stroke = new SKPaint
            {
                Color = (isSelected ? (pulse.IsActive ? PulseColor : FocusColor) : node.StrokeColor).WithAlpha(nodeAlpha),
                IsAntialias = true,
                StrokeWidth = isSelected ? (float)(3.2f + pulse.StrokeBoost) : isHovered ? 2.4f : 1.6f,
                Style = SKPaintStyle.Stroke
            };

            var cx = (screenRect.Left + screenRect.Right) / 2f;
            var cy = (screenRect.Top + screenRect.Bottom) / 2f;
            var radius = Math.Max(10f, Math.Min(screenRect.Width, screenRect.Height) * 0.36f);
            using var halo = new SKPaint { Color = (isSelected || isHovered ? OverlayColor : node.StrokeColor).WithAlpha((byte)(isSelected ? 120 : 64)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = isSelected ? 6.4f : 3.6f };
            canvas.DrawCircle(cx, cy, radius + 6f, halo);
            canvas.DrawCircle(cx, cy, radius, fill);
            canvas.DrawCircle(cx, cy, radius, stroke);
            if (!isSelected && isHighlighted)
            {
                using var highlight = new SKPaint { Color = FocusColor.WithAlpha(150), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, PathEffect = SKPathEffect.CreateDash([6f, 4f], 0f), IsAntialias = true };
                canvas.DrawRoundRect(new SKRect(screenRect.Left - 3f, screenRect.Top - 3f, screenRect.Right + 3f, screenRect.Bottom + 3f), NodeCornerRadius, NodeCornerRadius, highlight);
            }
            if (isKeyboard)
            {
                using var keyboardPaint = new SKPaint { Color = FocusColor.WithAlpha(220), Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, IsAntialias = true };
                canvas.DrawRoundRect(new SKRect(screenRect.Left - 6f, screenRect.Top - 6f, screenRect.Right + 6f, screenRect.Bottom + 6f), NodeCornerRadius, NodeCornerRadius, keyboardPaint);
            }
            if (scene.Simulation.ShowAgentOverlays && node.IsActorControlled)
            {
                using var actorBorder = new SKPaint
                {
                    Color = SKColor.Parse("#C084FC").WithAlpha(nodeAlpha),
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 3.4f
                };
                canvas.DrawRoundRect(new SKRect(screenRect.Left - 5f, screenRect.Top - 5f, screenRect.Right + 5f, screenRect.Bottom + 5f), NodeCornerRadius + 4f, NodeCornerRadius + 4f, actorBorder);

                using var badge = new SKPaint { Color = SKColor.Parse("#C084FC"), IsAntialias = true, Style = SKPaintStyle.Fill };
                canvas.DrawCircle(screenRect.Right - 8f, screenRect.Top + 8f, 5f, badge);
            }
            DrawFacilityCoverageOverlay(canvas, node, screenRect, nodeAlpha);
        }
    }

    private void DrawCompactNodes(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize)
    {
        using var nodePaint = new SKPaint { Color = new SKColor(93, 116, 160), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var labelFont = new SKFont { Size = 12f };
        using var labelPaint = new SKPaint { Color = TextColor, IsAntialias = true };

        foreach (var node in scene.Nodes)
        {
            var center = viewport.WorldToScreen(GraphHitTester.GetNodeCenter(node), viewportSize);
            var isSelected = scene.Selection.SelectedNodeIds.Contains(node.Id);
            var isHighlighted = scene.Selection.HighlightedNodeIds.Contains(node.Id);
            var isHovered = string.Equals(scene.Selection.HoverNodeId, node.Id, StringComparison.OrdinalIgnoreCase);
            var isKeyboard = string.Equals(scene.Selection.KeyboardNodeId, node.Id, StringComparison.OrdinalIgnoreCase);
            var pulse = GetPulseState(
                scene,
                isSelected && string.Equals(scene.Selection.PulseNodeId, node.Id, StringComparison.OrdinalIgnoreCase),
                scene.Selection.PulseProgress);
            var nodeAlpha = (byte)Math.Clamp(Math.Round(255d * node.VisualOpacity), 32d, 255d);
            var radius = isSelected ? 8f : isHovered || isHighlighted ? 7f : 6f;

            nodePaint.Color = node.FillColor.WithAlpha(nodeAlpha);
            canvas.DrawCircle((float)center.X, (float)center.Y, radius, nodePaint);

            using var stroke = new SKPaint
            {
                Color = (isSelected ? pulse.IsActive ? PulseColor : FocusColor : node.StrokeColor).WithAlpha(nodeAlpha),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isSelected ? (float)(2.6f + pulse.StrokeBoost) : 1.8f
            };
            canvas.DrawCircle((float)center.X, (float)center.Y, radius + 1.5f, stroke);

            if (isKeyboard)
            {
                using var keyboardPaint = new SKPaint { Color = FocusColor.WithAlpha(220), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, PathEffect = SKPathEffect.CreateDash([5f, 4f], 0f), IsAntialias = true };
                canvas.DrawCircle((float)center.X, (float)center.Y, radius + 5f, keyboardPaint);
            }

            if (node.HasWarning)
            {
                using var warningPaint = new SKPaint { Color = WarningColor.WithAlpha(nodeAlpha), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f };
                canvas.DrawCircle((float)center.X, (float)center.Y, radius + 4f, warningPaint);
            }

            canvas.DrawText(node.Name, (float)center.X + 10f, (float)center.Y - 6f, SKTextAlign.Left, labelFont, labelPaint);
        }
    }

    private static (bool IsActive, double StrokeBoost) GetPulseState(GraphScene scene, bool isPulseTarget, double pulseProgress)
    {
        if (!isPulseTarget)
        {
            return (false, 0d);
        }

        if (scene.Simulation.ReducedMotion)
        {
            return (true, 1.8d);
        }

        if (pulseProgress <= 0d)
        {
            return (false, 0d);
        }

        var wave = Math.Sin((1d - pulseProgress) * Math.PI * 4d);
        var envelope = pulseProgress;
        var strokeBoost = Math.Max(0d, wave) * 3.1d * envelope;
        return (true, strokeBoost);
    }

    private static void DrawFacilityCoverageOverlay(SKCanvas canvas, GraphNodeSceneItem node, SKRect screenRect, byte nodeAlpha)
    {
        if (!node.IsFacilityCovered || node.CoveringFacilities.Count == 0)
        {
            return;
        }

        var outerRect = new SKRect(screenRect.Left - 5f, screenRect.Top - 5f, screenRect.Right + 5f, screenRect.Bottom + 5f);
        if (node.CoveringFacilities.Count == 1)
        {
            using var singleStroke = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2.3f,
                Color = SKColor.Parse("#E9FFF0").WithAlpha(nodeAlpha)
            };
            canvas.DrawRoundRect(outerRect, NodeCornerRadius + 4f, NodeCornerRadius + 4f, singleStroke);

            using var marker = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#E9FFF0").WithAlpha(nodeAlpha) };
            var markerSize = 5f;
            var markerX = outerRect.Right - 10f;
            var markerY = outerRect.Top + 10f;
            using var path = new SKPath();
            path.MoveTo(markerX, markerY - markerSize);
            path.LineTo(markerX + markerSize, markerY);
            path.LineTo(markerX, markerY + markerSize);
            path.LineTo(markerX - markerSize, markerY);
            path.Close();
            canvas.DrawPath(path, marker);
            return;
        }

        using var segmentStroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = 2.5f,
            Color = SKColor.Parse("#FFD166").WithAlpha(nodeAlpha)
        };
        var cx = outerRect.MidX;
        var cy = outerRect.MidY;
        var radius = Math.Min(outerRect.Width, outerRect.Height) * 0.56f;
        var segmentCount = Math.Min(6, node.CoveringFacilities.Count);
        var sweep = 300f / segmentCount;
        var gap = 60f / segmentCount;
        var circle = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        for (var index = 0; index < segmentCount; index++)
        {
            var startAngle = -90f + (index * (sweep + gap));
            canvas.DrawArc(circle, startAngle, sweep, false, segmentStroke);
        }

        var badgeCenter = new SKPoint(outerRect.Right - 2f, outerRect.Top + 2f);
        using var badgeFill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#FFF4B5").WithAlpha(nodeAlpha) };
        using var badgeStroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = SKColor.Parse("#1F2933").WithAlpha(nodeAlpha) };
        canvas.DrawCircle(badgeCenter, 8f, badgeFill);
        canvas.DrawCircle(badgeCenter, 8f, badgeStroke);

        using var badgeTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        using var badgeFont = new SKFont(badgeTypeface, 9f);
        using var badgeText = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#1F2933").WithAlpha(nodeAlpha) };
        canvas.DrawText(node.CoveringFacilities.Count.ToString(CultureInfo.InvariantCulture), badgeCenter.X, badgeCenter.Y + 3.1f, SKTextAlign.Center, badgeFont, badgeText);
    }

    private void DrawLabels(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize)
    {
        var tier = GetZoomTier(viewport.Zoom);
        using var defaultTypeface = SKTypeface.FromFamilyName("Segoe UI");
        using var boldTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        using var titlePaint = new SKPaint { Color = TextColor, IsAntialias = true };
        using var bodyPaint = new SKPaint { Color = MutedTextColor, IsAntialias = true };
        using var detailPaint = new SKPaint { Color = MutedTextColor, IsAntialias = true };
        using var emphasizedDetailPaint = new SKPaint { Color = TextColor, IsAntialias = true };
        using var warningDetailPaint = new SKPaint { Color = WarningColor, IsAntialias = true };
        using var titleFont = new SKFont(boldTypeface, 14f);
        using var bodyFont = new SKFont(defaultTypeface, 11f);
        using var detailFont = new SKFont(defaultTypeface, 10f);
        using var emphasizedDetailFont = new SKFont(boldTypeface, 10f);
        using var warningDetailFont = new SKFont(boldTypeface, 10f);

        foreach (var node in scene.Nodes)
        {
            var layout = GetOrBuildNodeLayout(node, tier);
            var origin = viewport.WorldToScreen(
                new GraphPoint(node.Bounds.Left + GraphNodeTextLayout.HorizontalPadding, node.Bounds.Top + GraphNodeTextLayout.TopPadding),
                viewportSize);
            var clipRect = InsetRect(GetNodeScreenRect(node, viewport, viewportSize), (float)Math.Max(TextClipInset, viewport.Zoom * TextClipInset));
            if (clipRect.Width <= 1f || clipRect.Height <= 1f)
            {
                continue;
            }

            using var clipPath = new SKPath();
            var clipRadius = Math.Max(8f, NodeCornerRadius - ((float)Math.Max(TextClipInset, viewport.Zoom * TextClipInset) * 0.6f));
            clipPath.AddRoundRect(clipRect, clipRadius, clipRadius);

            canvas.Save();
            canvas.ClipPath(clipPath, antialias: true);
            var y = (float)origin.Y;

            foreach (var line in layout.Lines)
            {
                var (font, paint) = line.Kind switch
                {
                    GraphNodeTextKind.Title => (titleFont, titlePaint),
                    GraphNodeTextKind.TypeLabel => (bodyFont, bodyPaint),
                    _ when line.IsWarning => (warningDetailFont, warningDetailPaint),
                    _ when line.IsEmphasized => (emphasizedDetailFont, emphasizedDetailPaint),
                    _ => (detailFont, detailPaint)
                };

                y += line.Kind == GraphNodeTextKind.Title ? 18f : 14f;
                canvas.DrawText(line.Text, (float)origin.X, y, SKTextAlign.Left, font, paint);
            }

            canvas.Restore();
        }
    }

    private static SKRect GetNodeScreenRect(GraphNodeSceneItem node, GraphViewport viewport, GraphSize viewportSize)
    {
        var topLeft = viewport.WorldToScreen(new GraphPoint(node.Bounds.Left, node.Bounds.Top), viewportSize);
        return new SKRect(
            (float)topLeft.X,
            (float)topLeft.Y,
            (float)(topLeft.X + (node.Bounds.Width * viewport.Zoom)),
            (float)(topLeft.Y + (node.Bounds.Height * viewport.Zoom)));
    }

    private static SKRect InsetRect(SKRect rect, float inset)
    {
        return new SKRect(rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset);
    }

    private static void DrawSelection(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize)
    {
        if (scene.Transient.DragStartWorld is null || scene.Transient.DragCurrentWorld is null)
        {
            return;
        }

        var start = viewport.WorldToScreen(scene.Transient.DragStartWorld.Value, viewportSize);
        var end = viewport.WorldToScreen(scene.Transient.DragCurrentWorld.Value, viewportSize);
        var rect = SKRect.Create(
            (float)Math.Min(start.X, end.X),
            (float)Math.Min(start.Y, end.Y),
            (float)Math.Abs(end.X - start.X),
            (float)Math.Abs(end.Y - start.Y));

        using var fill = new SKPaint { Color = FocusColor.WithAlpha(28), IsAntialias = true };
        using var stroke = new SKPaint { Color = FocusColor.WithAlpha(170), IsAntialias = true, StrokeWidth = 1.2f, Style = SKPaintStyle.Stroke };
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, stroke);
    }

    private static void DrawFlowAnimation(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize, bool showNodeLabels)
    {
        if (!scene.Simulation.ShowAnimatedFlows)
        {
            return;
        }

        using var pulsePaint = new SKPaint { Color = OverlayColor.WithAlpha(210), IsAntialias = true, Style = SKPaintStyle.Fill };
        foreach (var edge in scene.Edges.Where(edge => edge.FlowRate > 0.01d))
        {
            var start = viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(scene, edge.FromNodeId, edge.ToNodeId, showNodeLabels), viewportSize);
            var end = viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(scene, edge.ToNodeId, edge.FromNodeId, showNodeLabels), viewportSize);
            var t = scene.Simulation.ReducedMotion ? 0.55d : (scene.Simulation.AnimationTime * (0.12d + edge.FlowRate)) % 1d;
            var x = start.X + ((end.X - start.X) * t);
            var y = start.Y + ((end.Y - start.Y) * t);
            var radius = 3.5f + (float)(edge.LoadRatio * 4d);
            pulsePaint.Color = OverlayColor.WithAlpha((byte)(150 + (edge.LoadRatio * 80d)));
            canvas.DrawCircle((float)x, (float)y, radius, pulsePaint);
        }
    }

    private static void DrawTransientInteraction(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize)
    {
        if (scene.Transient.ConnectionSourceNodeId is null || scene.Transient.ConnectionWorld is null)
        {
            return;
        }

        var source = GraphHitTester.GetEdgeAnchor(scene, scene.Transient.ConnectionSourceNodeId, scene.Transient.ConnectionSourceNodeId);
        var start = viewport.WorldToScreen(source, viewportSize);
        var end = viewport.WorldToScreen(scene.Transient.ConnectionWorld.Value, viewportSize);
        using var paint = new SKPaint
        {
            Color = FocusColor,
            IsAntialias = true,
            StrokeWidth = 2.2f,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            PathEffect = SKPathEffect.CreateDash([10f, 7f], 0f)
        };

        canvas.DrawLine((float)start.X, (float)start.Y, (float)end.X, (float)end.Y, paint);
    }

    private static void DrawMinimap(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize, bool showNodeLabels)
    {
        var bounds = scene.GetContentBounds();
        var minimapWidth = 196f;
        var minimapHeight = 128f;
        var rect = new SKRect((float)viewportSize.Width - minimapWidth - 18f, 18f, (float)viewportSize.Width - 18f, 18f + minimapHeight);
        using var background = new SKPaint { Color = MinimapBackground, IsAntialias = true };
        using var linePaint = new SKPaint { Color = GridMajorColor, IsAntialias = true, StrokeWidth = 1f };
        using var nodePaint = new SKPaint { Color = OverlayColor.WithAlpha(180), IsAntialias = true };
        using var viewPaint = new SKPaint { Color = FocusColor.WithAlpha(160), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };

        canvas.DrawRoundRect(rect, 14f, 14f, background);
        if (bounds.Width <= 0d || bounds.Height <= 0d)
        {
            return;
        }

        var scale = Math.Min((minimapWidth - 20f) / (float)bounds.Width, (minimapHeight - 20f) / (float)bounds.Height);
        var originX = rect.Left + 10f;
        var originY = rect.Top + 10f;

        foreach (var edge in scene.Edges)
        {
            var start = GraphHitTester.GetEdgeAnchor(scene, edge.FromNodeId, edge.ToNodeId, showNodeLabels);
            var end = GraphHitTester.GetEdgeAnchor(scene, edge.ToNodeId, edge.FromNodeId, showNodeLabels);
            canvas.DrawLine(
                originX + (float)((start.X - bounds.Left) * scale),
                originY + (float)((start.Y - bounds.Top) * scale),
                originX + (float)((end.X - bounds.Left) * scale),
                originY + (float)((end.Y - bounds.Top) * scale),
                linePaint);
        }

        foreach (var node in scene.Nodes)
        {
            var center = GraphHitTester.GetNodeCenter(node);
            var x = originX + (float)((center.X - bounds.Left) * scale);
            var y = originY + (float)((center.Y - bounds.Top) * scale);
            canvas.DrawCircle(x, y, 3.2f, nodePaint);
        }

        var worldTopLeft = viewport.ScreenToWorld(new GraphPoint(0d, 0d), viewportSize);
        var worldBottomRight = viewport.ScreenToWorld(new GraphPoint(viewportSize.Width, viewportSize.Height), viewportSize);
        var viewRect = new SKRect(
            originX + (float)((worldTopLeft.X - bounds.Left) * scale),
            originY + (float)((worldTopLeft.Y - bounds.Top) * scale),
            originX + (float)((worldBottomRight.X - bounds.Left) * scale),
            originY + (float)((worldBottomRight.Y - bounds.Top) * scale));
        canvas.DrawRect(viewRect, viewPaint);
    }

    private static GraphPoint GetEdgeMidpoint(GraphScene scene, GraphEdgeSceneItem edge, bool showNodeLabels)
    {
        return GraphHitTester.GetEdgeMidpoint(scene, edge, showNodeLabels);
    }
}

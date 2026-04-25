using SkiaSharp;
using System.Globalization;

namespace MedWNetworkSim.Rendering;

public readonly record struct GraphPoint(double X, double Y)
{
    public static GraphPoint operator +(GraphPoint left, GraphVector right) => new(left.X + right.X, left.Y + right.Y);
    public static GraphVector operator -(GraphPoint left, GraphPoint right) => new(left.X - right.X, left.Y - right.Y);
}

public readonly record struct GraphVector(double X, double Y)
{
    public double Length => Math.Sqrt((X * X) + (Y * Y));
}

public readonly record struct GraphSize(double Width, double Height);

public readonly record struct GraphRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + (Width / 2d);
    public double CenterY => Y + (Height / 2d);

    public bool Contains(GraphPoint point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;

    public static GraphRect FromPoints(GraphPoint a, GraphPoint b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.X, b.X);
        var bottom = Math.Max(a.Y, b.Y);
        return new GraphRect(left, top, right - left, bottom - top);
    }

    public static GraphRect Empty => new(0d, 0d, 0d, 0d);
}

public sealed class GraphViewport
{
    public const double MinimumZoom = 0.18d;
    public const double MaximumZoom = 3.75d;

    public GraphPoint Center { get; private set; } = new(0d, 0d);
    public double Zoom { get; private set; } = 1d;

    public GraphPoint ScreenToWorld(GraphPoint screenPoint, GraphSize viewportSize)
    {
        var worldLeft = Center.X - (viewportSize.Width / (2d * Zoom));
        var worldTop = Center.Y - (viewportSize.Height / (2d * Zoom));
        return new GraphPoint(worldLeft + (screenPoint.X / Zoom), worldTop + (screenPoint.Y / Zoom));
    }

    public GraphPoint WorldToScreen(GraphPoint worldPoint, GraphSize viewportSize)
    {
        var worldLeft = Center.X - (viewportSize.Width / (2d * Zoom));
        var worldTop = Center.Y - (viewportSize.Height / (2d * Zoom));
        return new GraphPoint((worldPoint.X - worldLeft) * Zoom, (worldPoint.Y - worldTop) * Zoom);
    }

    public void Pan(GraphVector worldDelta)
    {
        Center = new GraphPoint(Center.X - worldDelta.X, Center.Y - worldDelta.Y);
    }

    public void ZoomAt(GraphPoint anchorScreen, GraphSize viewportSize, double zoomFactor)
    {
        var before = ScreenToWorld(anchorScreen, viewportSize);
        Zoom = Math.Clamp(Zoom * zoomFactor, MinimumZoom, MaximumZoom);
        var after = ScreenToWorld(anchorScreen, viewportSize);
        Center = new GraphPoint(Center.X + (before.X - after.X), Center.Y + (before.Y - after.Y));
    }

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

public enum ZoomTier
{
    Far,
    Medium,
    Near
}

public readonly record struct GraphNodeTextLine(string Text, bool IsEmphasized, bool IsWarning);

public sealed class FacilityCoverageInfo
{
    public required string FacilityNodeId { get; init; }
    public required string FacilityDisplayName { get; init; }
    public required double TravelTime { get; init; }
    public required bool IsPrimaryFacility { get; init; }
}

public sealed class GraphNodeSceneItem
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string TypeLabel { get; set; }
    public required string MetricsLabel { get; set; }
    public required IReadOnlyList<GraphNodeTextLine> DetailLines { get; set; }
    public required GraphRect Bounds { get; set; }
    public required SKColor FillColor { get; set; }
    public required SKColor StrokeColor { get; set; }
    public required IReadOnlyList<string> Badges { get; set; }
    public string ToolTipText { get; set; } = string.Empty;
    public required bool HasWarning { get; set; }
    public double VisualOpacity { get; set; } = 1d;
    public IReadOnlyList<FacilityCoverageInfo> CoveringFacilities { get; set; } = [];
    public bool IsFacilityCovered { get; set; }
    public bool IsMultiFacilityCovered { get; set; }
    public string? PrimaryFacilityId { get; set; }
    public double? PrimaryFacilityTravelTime { get; set; }
    public string? LayoutContentKey { get; set; }
    public ZoomTier? LayoutZoomTier { get; set; }
    public GraphNodeTextLayoutResult? CachedLayout { get; set; }
    public double CachedLayoutWidth { get; set; }
    public double CachedLayoutHeight { get; set; }
}

public sealed class GraphEdgeSceneItem
{
    public required string Id { get; init; }
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public required string Label { get; set; }
    public required bool IsBidirectional { get; set; }
    public required double Capacity { get; set; }
    public required double Cost { get; set; }
    public required double Time { get; set; }
    public required double LoadRatio { get; set; }
    public required double FlowRate { get; set; }
    public string ToolTipText { get; set; } = string.Empty;
    public required bool HasWarning { get; set; }
    public double VisualOpacity { get; set; } = 1d;
}

public sealed class GraphTransientState
{
    public GraphPoint? DragStartWorld { get; set; }
    public GraphPoint? DragCurrentWorld { get; set; }
    public string? ConnectionSourceNodeId { get; set; }
    public GraphPoint? ConnectionWorld { get; set; }
}

public sealed class GraphSelectionState
{
    public HashSet<string> SelectedNodeIds { get; } = [];
    public HashSet<string> SelectedEdgeIds { get; } = [];
    public string? HoverNodeId { get; set; }
    public string? HoverEdgeId { get; set; }
    public string? KeyboardNodeId { get; set; }
    public string? KeyboardEdgeId { get; set; }
}

public sealed class GraphSimulationSceneState
{
    public bool ShowAnimatedFlows { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public bool ShowDepthLayer { get; set; } = true;
    public double AnimationTime { get; set; }
}

public sealed class GraphScene
{
    public IList<GraphNodeSceneItem> Nodes { get; } = [];
    public IList<GraphEdgeSceneItem> Edges { get; } = [];
    public GraphSelectionState Selection { get; } = new();
    public GraphTransientState Transient { get; } = new();
    public GraphSimulationSceneState Simulation { get; } = new();

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

    public GraphNodeSceneItem? FindNode(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase));

    public GraphEdgeSceneItem? FindEdge(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Edges.FirstOrDefault(edge => string.Equals(edge.Id, id, StringComparison.OrdinalIgnoreCase));
}

public readonly record struct GraphHitResult(string? NodeId, string? EdgeId);

public sealed class GraphHitTester
{
    public GraphHitResult HitTest(GraphScene scene, GraphPoint worldPoint)
    {
        foreach (var node in scene.Nodes.OrderByDescending(node => node.Bounds.Top))
        {
            if (node.Bounds.Contains(worldPoint))
            {
                return new GraphHitResult(node.Id, null);
            }
        }

        var edge = scene.Edges
            .Select(candidate => new { Edge = candidate, Distance = DistanceToEdge(scene, candidate, worldPoint) })
            .Where(candidate => candidate.Distance <= 12d)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();

        return edge is null ? default : new GraphHitResult(null, edge.Edge.Id);
    }

    private static double DistanceToEdge(GraphScene scene, GraphEdgeSceneItem edge, GraphPoint worldPoint)
    {
        var start = GetEdgeAnchor(scene, edge.FromNodeId, edge.ToNodeId);
        var end = GetEdgeAnchor(scene, edge.ToNodeId, edge.FromNodeId);
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

    public static GraphPoint GetEdgeAnchor(GraphScene scene, string sourceId, string targetId)
    {
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

public sealed class GraphRenderer
{
    private const float NodeCornerRadius = 18f;
    private const double TextClipInset = 6d;
    private static readonly SKColor BackgroundColor = SKColor.Parse("#08111D");
    private static readonly SKColor GridMajorColor = SKColor.Parse("#1A3148");
    private static readonly SKColor GridMinorColor = SKColor.Parse("#102437");
    private static readonly SKColor EdgeColor = SKColor.Parse("#4B708A");
    private static readonly SKColor OverlayColor = SKColor.Parse("#67C6F0");
    private static readonly SKColor FocusColor = SKColor.Parse("#F2D38B");
    private static readonly SKColor TextColor = SKColor.Parse("#E4EEF8");
    private static readonly SKColor MutedTextColor = SKColor.Parse("#89A5BA");
    private static readonly SKColor WarningColor = SKColor.Parse("#F39B68");
    private static readonly SKColor MinimapBackground = new(6, 13, 22, 220);

    public void Render(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize)
    {
        canvas.Clear(BackgroundColor);
        DrawBackgroundGrid(canvas, viewport, viewportSize);
        PrepareNodeLayouts(scene, viewport);
        DrawDepthLayer(canvas, scene, viewport, viewportSize);
        DrawEdges(canvas, scene, viewport, viewportSize);
        DrawEdgeOverlays(canvas, scene, viewport, viewportSize);
        DrawNodes(canvas, scene, viewport, viewportSize);
        DrawLabels(canvas, scene, viewport, viewportSize);
        DrawSelection(canvas, scene, viewport, viewportSize);
        DrawFlowAnimation(canvas, scene, viewport, viewportSize);
        DrawTransientInteraction(canvas, scene, viewport, viewportSize);
        DrawMinimap(canvas, scene, viewport, viewportSize);
    }

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

    private static void DrawDepthLayer(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize)
    {
        if (!scene.Simulation.ShowDepthLayer)
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

    private void DrawEdges(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize)
    {
        using var edgePaint = new SKPaint { Color = EdgeColor, IsAntialias = true, StrokeWidth = 2.8f, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        using var arrowPaint = new SKPaint { Color = EdgeColor, IsAntialias = true, Style = SKPaintStyle.Fill };

        foreach (var edge in scene.Edges)
        {
            var start = viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(scene, edge.FromNodeId, edge.ToNodeId), viewportSize);
            var end = viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(scene, edge.ToNodeId, edge.FromNodeId), viewportSize);
            var edgeAlpha = (byte)Math.Clamp(Math.Round((edge.HasWarning ? 190d : 180d) * edge.VisualOpacity), 15d, 255d);
            edgePaint.Color = edge.HasWarning ? WarningColor.WithAlpha(edgeAlpha) : EdgeColor.WithAlpha(edgeAlpha);
            edgePaint.StrokeWidth = (float)(2.4d + (edge.LoadRatio * 1.6d));
            canvas.DrawLine((float)start.X, (float)start.Y, (float)end.X, (float)end.Y, edgePaint);
            if (!edge.IsBidirectional)
            {
                arrowPaint.Color = edgePaint.Color;
                DrawDirectionalArrow(canvas, start, end, edgePaint.StrokeWidth, arrowPaint);
            }
        }
    }

    private void DrawEdgeOverlays(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize)
    {
        using var overlayPaint = new SKPaint { IsAntialias = true, StrokeWidth = 6f, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        using var arrowPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        foreach (var edge in scene.Edges.Where(edge => scene.Selection.SelectedEdgeIds.Contains(edge.Id)))
        {
            var start = viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(scene, edge.FromNodeId, edge.ToNodeId), viewportSize);
            var end = viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(scene, edge.ToNodeId, edge.FromNodeId), viewportSize);
            overlayPaint.Color = FocusColor.WithAlpha(120);
            canvas.DrawLine((float)start.X, (float)start.Y, (float)end.X, (float)end.Y, overlayPaint);
            if (!edge.IsBidirectional)
            {
                arrowPaint.Color = overlayPaint.Color;
                DrawDirectionalArrow(canvas, start, end, overlayPaint.StrokeWidth, arrowPaint);
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

            var nodeAlpha = (byte)Math.Clamp(Math.Round(255d * node.VisualOpacity), 32d, 255d);
            using var fill = new SKPaint { Color = node.FillColor.WithAlpha(nodeAlpha), IsAntialias = true };
            using var stroke = new SKPaint
            {
                Color = (scene.Selection.SelectedNodeIds.Contains(node.Id) ? FocusColor : node.StrokeColor).WithAlpha(nodeAlpha),
                IsAntialias = true,
                StrokeWidth = scene.Selection.SelectedNodeIds.Contains(node.Id) ? 3.2f : 1.6f,
                Style = SKPaintStyle.Stroke
            };

            canvas.DrawRoundRect(screenRect, NodeCornerRadius, NodeCornerRadius, fill);
            canvas.DrawRoundRect(screenRect, NodeCornerRadius, NodeCornerRadius, stroke);
            DrawFacilityCoverageOverlay(canvas, node, screenRect, nodeAlpha);
        }
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
        using var badgeText = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#1F2933").WithAlpha(nodeAlpha), TextAlign = SKTextAlign.Center };
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

    private static void DrawFlowAnimation(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize)
    {
        if (!scene.Simulation.ShowAnimatedFlows)
        {
            return;
        }

        using var pulsePaint = new SKPaint { Color = OverlayColor.WithAlpha(210), IsAntialias = true, Style = SKPaintStyle.Fill };
        foreach (var edge in scene.Edges.Where(edge => edge.FlowRate > 0.01d))
        {
            var start = viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(scene, edge.FromNodeId, edge.ToNodeId), viewportSize);
            var end = viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(scene, edge.ToNodeId, edge.FromNodeId), viewportSize);
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

    private static void DrawMinimap(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize)
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
            var start = GraphHitTester.GetEdgeAnchor(scene, edge.FromNodeId, edge.ToNodeId);
            var end = GraphHitTester.GetEdgeAnchor(scene, edge.ToNodeId, edge.FromNodeId);
            canvas.DrawLine(
                originX + (float)((start.X - bounds.Left) * scale),
                originY + (float)((start.Y - bounds.Top) * scale),
                originX + (float)((end.X - bounds.Left) * scale),
                originY + (float)((end.Y - bounds.Top) * scale),
                linePaint);
        }

        foreach (var node in scene.Nodes)
        {
            var x = originX + (float)((node.Bounds.CenterX - bounds.Left) * scale);
            var y = originY + (float)((node.Bounds.CenterY - bounds.Top) * scale);
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
}

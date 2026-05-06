using SkiaSharp;

namespace MedWNetworkSim.Rendering.VisualAnalytics.Sankey;

public sealed class SankeyRenderNode
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Kind { get; init; }
}

public sealed class SankeyRenderLink
{
    public required string Id { get; init; }
    public required string SourceNodeId { get; init; }
    public required string TargetNodeId { get; init; }
    public required string TrafficType { get; init; }
    public required double Value { get; init; }
    public string? RouteSignature { get; init; }
    public IReadOnlyList<string> RouteEdgeIds { get; init; } = [];
    public bool IsUnmetDemand { get; init; }
}

public sealed class SankeyRenderDiagram
{
    public IReadOnlyList<SankeyRenderNode> Nodes { get; init; } = [];
    public IReadOnlyList<SankeyRenderLink> Links { get; init; } = [];
    public string EmptyStateMessage { get; init; } = string.Empty;
}

public sealed class SankeyLayoutNode { public required SankeyRenderNode Node { get; init; } public required SKRect Bounds { get; init; } }
public sealed class SankeyLayoutLink { public required SankeyRenderLink Link { get; init; } public required SKPoint Start { get; init; } public required SKPoint End { get; init; } public required float Thickness { get; init; } public required SKPath Path { get; init; } }
public sealed class SankeyLayoutResult { public IReadOnlyList<SankeyLayoutNode> Nodes { get; init; } = []; public IReadOnlyList<SankeyLayoutLink> Links { get; init; } = []; public string? EmptyStateMessage { get; init; } }

public sealed class SankeyLayoutEngine
{
    public SankeyLayoutResult Layout(SankeyRenderDiagram model, GraphSize viewport)
    {
        if (model.Nodes.Count == 0 || model.Links.Count == 0)
        {
            return new SankeyLayoutResult { EmptyStateMessage = string.IsNullOrWhiteSpace(model.EmptyStateMessage) ? "Run a simulation to build the Sankey view." : model.EmptyStateMessage };
        }

        var left = model.Nodes.Where(n => !string.Equals(n.Kind, "Sink", StringComparison.OrdinalIgnoreCase) && !string.Equals(n.Kind, "UnmetDemandSink", StringComparison.OrdinalIgnoreCase)).ToArray();
        var right = model.Nodes.Where(n => string.Equals(n.Kind, "Sink", StringComparison.OrdinalIgnoreCase) || string.Equals(n.Kind, "UnmetDemandSink", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (left.Length == 0 || right.Length == 0) { left = model.Nodes.Take(model.Nodes.Count / 2).ToArray(); right = model.Nodes.Skip(left.Length).ToArray(); }

        var nodeWidth = 24f;
        var verticalPadding = 18f;
        var top = 54f;
        var availableHeight = Math.Max(60f, (float)viewport.Height - 112f);
        var leftH = Math.Max(20f, (availableHeight - (left.Length * verticalPadding)) / Math.Max(1, left.Length));
        var rightH = Math.Max(20f, (availableHeight - (right.Length * verticalPadding)) / Math.Max(1, right.Length));

        var nodes = new List<SankeyLayoutNode>();
        float leftY = top;
        foreach (var node in left)
        {
            nodes.Add(new SankeyLayoutNode { Node = node, Bounds = new SKRect(70f, leftY, 70f + nodeWidth, leftY + leftH) });
            leftY += leftH + verticalPadding;
        }

        float rightY = top;
        foreach (var node in right)
        {
            nodes.Add(new SankeyLayoutNode { Node = node, Bounds = new SKRect((float)viewport.Width - 94f, rightY, (float)viewport.Width - 70f, rightY + rightH) });
            rightY += rightH + verticalPadding;
        }

        var nodeById = nodes.ToDictionary(n => n.Node.Id, StringComparer.OrdinalIgnoreCase);
        var maxLink = Math.Max(1d, model.Links.Max(l => l.Value));
        var links = new List<SankeyLayoutLink>();
        foreach (var link in model.Links)
        {
            if (!nodeById.TryGetValue(link.SourceNodeId, out var source) || !nodeById.TryGetValue(link.TargetNodeId, out var target))
            {
                continue;
            }

            var start = new SKPoint(source.Bounds.Right, source.Bounds.MidY);
            var end = new SKPoint(target.Bounds.Left, target.Bounds.MidY);
            var thickness = Math.Max(2f, (float)(link.Value / maxLink) * 24f);
            var path = new SKPath();
            var c = Math.Max(24f, Math.Abs(end.X - start.X) * .45f);
            path.MoveTo(start);
            path.CubicTo(start.X + c, start.Y, end.X - c, end.Y, end.X, end.Y);
            links.Add(new SankeyLayoutLink { Link = link, Start = start, End = end, Thickness = thickness, Path = path });
        }

        return new SankeyLayoutResult { Nodes = nodes, Links = links };
    }
}

public readonly record struct SankeyHitRegion(string? NodeId, string? LinkId, SKRect Bounds);

public sealed class SankeyRenderer
{
    private static readonly SKColor[] TrafficPalette =
    [
        SKColor.Parse("#37A7FF"),
        SKColor.Parse("#2FD38F"),
        SKColor.Parse("#E8B24A"),
        SKColor.Parse("#C27DFF"),
        SKColor.Parse("#FF8A5B"),
        SKColor.Parse("#69D2E7"),
        SKColor.Parse("#F07C9B")
    ];

    private readonly SankeyLayoutEngine layoutEngine = new();
    private readonly List<SankeyHitRegion> lastHitRegions = [];
    public IReadOnlyList<SankeyHitRegion> LastHitRegions => lastHitRegions;

    public SankeyLayoutResult Render(SKCanvas canvas, SankeyRenderDiagram model, GraphSize viewport, string? focusedNodeId = null, string? focusedLinkId = null)
    {
        var layout = layoutEngine.Layout(model, viewport);
        lastHitRegions.Clear();
        DrawBackground(canvas, viewport);

        if (!string.IsNullOrWhiteSpace(layout.EmptyStateMessage))
        {
            using var font = new SKFont { Size = 20f };
            using var paint = new SKPaint { Color = new SKColor(216, 223, 235), IsAntialias = true };
            canvas.DrawText(layout.EmptyStateMessage, 32f, (float)viewport.Height / 2f, SKTextAlign.Left, font, paint);
            return layout;
        }

        var hasFocus = !string.IsNullOrWhiteSpace(focusedNodeId) || !string.IsNullOrWhiteSpace(focusedLinkId);
        using var linkPaint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        using var textFont = new SKFont { Size = 13f };
        using var smallFont = new SKFont { Size = 11f };
        using var textPaint = new SKPaint { Color = new SKColor(230, 235, 245), IsAntialias = true };
        using var mutedTextPaint = new SKPaint { Color = new SKColor(174, 192, 218), IsAntialias = true };

        DrawLegend(canvas, model, viewport, smallFont);
        foreach (var link in layout.Links.OrderBy(l => l.Thickness))
        {
            var isFocused = string.Equals(link.Link.Id, focusedLinkId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(link.Link.SourceNodeId, focusedNodeId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(link.Link.TargetNodeId, focusedNodeId, StringComparison.OrdinalIgnoreCase);
            var alpha = (byte)(hasFocus && !isFocused ? 56 : 190);
            linkPaint.Color = GetLinkColor(link.Link).WithAlpha(alpha);
            linkPaint.StrokeWidth = link.Thickness;
            canvas.DrawPath(link.Path, linkPaint);

            if (isFocused)
            {
                using var focusPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.White.WithAlpha(220),
                    StrokeWidth = link.Thickness + 3f,
                    IsAntialias = true,
                    StrokeCap = SKStrokeCap.Round
                };
                canvas.DrawPath(link.Path, focusPaint);
            }

            if (link.Thickness >= 5f || isFocused)
            {
                var mx = (link.Start.X + link.End.X) / 2f;
                var my = (link.Start.Y + link.End.Y) / 2f;
                DrawLabelChip(canvas, $"{link.Link.TrafficType} {link.Link.Value:0.#}", mx, my - 8f, textFont, textPaint);
            }

            lastHitRegions.Add(new SankeyHitRegion(
                null,
                link.Link.Id,
                new SKRect(
                    Math.Min(link.Start.X, link.End.X),
                    Math.Min(link.Start.Y, link.End.Y) - Math.Max(8f, link.Thickness),
                    Math.Max(link.Start.X, link.End.X),
                    Math.Max(link.Start.Y, link.End.Y) + Math.Max(8f, link.Thickness))));
        }

        foreach (var node in layout.Nodes)
        {
            var isFocused = string.Equals(node.Node.Id, focusedNodeId, StringComparison.OrdinalIgnoreCase);
            var alpha = (byte)(hasFocus && !isFocused ? 115 : 235);
            using var nodePaint = new SKPaint
            {
                Color = GetNodeColor(node.Node).WithAlpha(alpha),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRoundRect(node.Bounds, 5f, 5f, nodePaint);
            using var borderPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)(isFocused ? 230 : 150)),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isFocused ? 2.5f : 1.3f,
                IsAntialias = true
            };
            canvas.DrawRoundRect(node.Bounds, 5f, 5f, borderPaint);

            var labelLeft = node.Bounds.MidX > viewport.Width / 2d;
            var labelX = labelLeft ? node.Bounds.Left - 10f : node.Bounds.Right + 10f;
            var align = labelLeft ? SKTextAlign.Right : SKTextAlign.Left;
            textPaint.Color = SKColors.White.WithAlpha(alpha);
            canvas.DrawText(node.Node.Label, labelX, node.Bounds.MidY - 2f, align, textFont, textPaint);
            mutedTextPaint.Color = new SKColor(174, 192, 218).WithAlpha(alpha);
            canvas.DrawText(node.Node.Kind, labelX, node.Bounds.MidY + 13f, align, smallFont, mutedTextPaint);
            lastHitRegions.Add(new SankeyHitRegion(node.Node.Id, null, node.Bounds));
        }

        return layout;
    }

    public SankeyHitRegion? HitTest(GraphPoint point) => lastHitRegions.LastOrDefault(r => r.Bounds.Contains((float)point.X, (float)point.Y));

    private static void DrawBackground(SKCanvas canvas, GraphSize viewport)
    {
        using var paint = new SKPaint { Color = SKColor.Parse("#08111D") };
        canvas.DrawRect(new SKRect(0, 0, (float)viewport.Width, (float)viewport.Height), paint);
    }

    private static void DrawLegend(SKCanvas canvas, SankeyRenderDiagram model, GraphSize viewport, SKFont font)
    {
        var trafficTypes = model.Links
            .Where(link => !link.IsUnmetDemand)
            .Select(link => link.TrafficType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        using var paint = new SKPaint { IsAntialias = true };
        var x = 24f;
        const float y = 22f;
        foreach (var trafficType in trafficTypes)
        {
            paint.Color = GetTrafficColor(trafficType);
            canvas.DrawCircle(x, y - 4f, 5f, paint);
            paint.Color = new SKColor(174, 192, 218);
            canvas.DrawText(trafficType, x + 9f, y, SKTextAlign.Left, font, paint);
            x += Math.Max(76f, (trafficType.Length * 7.2f) + 28f);
            if (x > viewport.Width - 120f)
            {
                break;
            }
        }

        if (model.Links.Any(link => link.IsUnmetDemand))
        {
            paint.Color = new SKColor(239, 91, 91);
            canvas.DrawCircle(x, y - 4f, 5f, paint);
            paint.Color = new SKColor(174, 192, 218);
            canvas.DrawText("Unmet", x + 9f, y, SKTextAlign.Left, font, paint);
        }
    }

    private static void DrawLabelChip(SKCanvas canvas, string label, float x, float y, SKFont font, SKPaint textPaint)
    {
        var width = font.MeasureText(label) + 14f;
        var rect = new SKRect(x - (width / 2f), y - 16f, x + (width / 2f), y + 4f);
        using var fill = new SKPaint { Color = new SKColor(7, 17, 29, 205), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var stroke = new SKPaint { Color = new SKColor(78, 110, 152, 170), Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(rect, 7f, 7f, fill);
        canvas.DrawRoundRect(rect, 7f, 7f, stroke);
        canvas.DrawText(label, x, y - 2f, SKTextAlign.Center, font, textPaint);
    }

    private static SKColor GetLinkColor(SankeyRenderLink link) =>
        link.IsUnmetDemand ? new SKColor(239, 91, 91) : GetTrafficColor(link.TrafficType);

    private static SKColor GetTrafficColor(string trafficType)
    {
        if (string.IsNullOrWhiteSpace(trafficType))
        {
            return TrafficPalette[0];
        }

        var index = Math.Abs(StableHash(trafficType)) % TrafficPalette.Length;
        return TrafficPalette[index];
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var ch in value)
            {
                hash = (hash * 31) + char.ToUpperInvariant(ch);
            }

            return hash == int.MinValue ? 0 : hash;
        }
    }

    private static SKColor GetNodeColor(SankeyRenderNode node)
    {
        if (string.Equals(node.Kind, "UnmetDemandSink", StringComparison.OrdinalIgnoreCase))
        {
            return new SKColor(177, 69, 82);
        }

        if (string.Equals(node.Kind, "CollapsedOther", StringComparison.OrdinalIgnoreCase))
        {
            return new SKColor(116, 126, 145);
        }

        return new SKColor(54, 85, 126);
    }
}

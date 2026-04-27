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

        var nodeWidth = 22f;
        var verticalPadding = 18f;
        var top = 40f;
        var availableHeight = Math.Max(60f, (float)viewport.Height - 80f);
        var leftH = Math.Max(18f, (availableHeight - (left.Length * verticalPadding)) / Math.Max(1, left.Length));
        var rightH = Math.Max(18f, (availableHeight - (right.Length * verticalPadding)) / Math.Max(1, right.Length));

        var nodes = new List<SankeyLayoutNode>();
        float leftY = top;
        foreach (var node in left) { nodes.Add(new SankeyLayoutNode { Node = node, Bounds = new SKRect(60f, leftY, 60f + nodeWidth, leftY + leftH) }); leftY += leftH + verticalPadding; }
        float rightY = top;
        foreach (var node in right) { nodes.Add(new SankeyLayoutNode { Node = node, Bounds = new SKRect((float)viewport.Width - 82f, rightY, (float)viewport.Width - 60f, rightY + rightH) }); rightY += rightH + verticalPadding; }

        var nodeById = nodes.ToDictionary(n => n.Node.Id, StringComparer.OrdinalIgnoreCase);
        var maxLink = Math.Max(1d, model.Links.Max(l => l.Value));
        var links = new List<SankeyLayoutLink>();
        foreach (var link in model.Links)
        {
            if (!nodeById.TryGetValue(link.SourceNodeId, out var source) || !nodeById.TryGetValue(link.TargetNodeId, out var target)) continue;
            var start = new SKPoint(source.Bounds.Right, source.Bounds.MidY);
            var end = new SKPoint(target.Bounds.Left, target.Bounds.MidY);
            var thickness = Math.Max(2f, (float)(link.Value / maxLink) * 22f);
            var path = new SKPath(); var c = Math.Max(24f, (end.X - start.X) * .45f);
            path.MoveTo(start); path.CubicTo(start.X + c, start.Y, end.X - c, end.Y, end.X, end.Y);
            links.Add(new SankeyLayoutLink { Link = link, Start = start, End = end, Thickness = thickness, Path = path });
        }

        return new SankeyLayoutResult { Nodes = nodes, Links = links };
    }
}

public readonly record struct SankeyHitRegion(string? NodeId, string? LinkId, SKRect Bounds);

public sealed class SankeyRenderer
{
    private readonly SankeyLayoutEngine layoutEngine = new();
    private readonly List<SankeyHitRegion> lastHitRegions = [];
    public IReadOnlyList<SankeyHitRegion> LastHitRegions => lastHitRegions;

    public SankeyLayoutResult Render(SKCanvas canvas, SankeyRenderDiagram model, GraphSize viewport, string? focusedNodeId = null, string? focusedLinkId = null)
    {
        var layout = layoutEngine.Layout(model, viewport); lastHitRegions.Clear();
        if (!string.IsNullOrWhiteSpace(layout.EmptyStateMessage)) { using var p = new SKPaint { Color = new SKColor(216, 223, 235), TextSize = 20f, IsAntialias = true }; canvas.DrawText(layout.EmptyStateMessage, 32f, (float)viewport.Height / 2f, p); return layout; }
        using var lp = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        using var tp = new SKPaint { Color = new SKColor(230, 235, 245), TextSize = 14f, IsAntialias = true };
        foreach (var link in layout.Links.OrderBy(l => l.Thickness))
        {
            lp.Color = link.Link.IsUnmetDemand ? new SKColor(230, 120, 120, 210) : new SKColor(125, 188, 255, 180); lp.StrokeWidth = link.Thickness; canvas.DrawPath(link.Path, lp);
            if (string.Equals(link.Link.Id, focusedLinkId, StringComparison.OrdinalIgnoreCase)) { using var f = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.White, StrokeWidth = link.Thickness + 3f, IsAntialias = true, PathEffect = SKPathEffect.CreateDash([8f, 6f], 0f) }; canvas.DrawPath(link.Path, f); }
            var mx = (link.Start.X + link.End.X) / 2f; var my = (link.Start.Y + link.End.Y) / 2f; canvas.DrawText($"{link.Link.TrafficType} {link.Link.Value:0.#}", mx - 30f, my - 4f, tp);
            lastHitRegions.Add(new SankeyHitRegion(null, link.Link.Id, new SKRect(Math.Min(link.Start.X, link.End.X), Math.Min(link.Start.Y, link.End.Y) - link.Thickness, Math.Max(link.Start.X, link.End.X), Math.Max(link.Start.Y, link.End.Y) + link.Thickness)));
        }
        foreach (var node in layout.Nodes)
        {
            using var np = new SKPaint { Color = string.Equals(node.Node.Kind, "UnmetDemandSink", StringComparison.OrdinalIgnoreCase) ? new SKColor(200, 92, 92) : new SKColor(82, 103, 141), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(node.Bounds, np); using var bp = new SKPaint { Color = SKColors.White.WithAlpha(160), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f }; canvas.DrawRect(node.Bounds, bp);
            canvas.DrawText(string.Equals(node.Node.Kind, "UnmetDemandSink", StringComparison.OrdinalIgnoreCase) ? "!" : "■", node.Bounds.Left + 4f, node.Bounds.Top + 12f, tp);
            canvas.DrawText(node.Node.Label, node.Bounds.Right + 8f, node.Bounds.MidY + 4f, tp);
            if (string.Equals(node.Node.Id, focusedNodeId, StringComparison.OrdinalIgnoreCase)) { using var f = new SKPaint { Color = SKColors.Yellow, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, PathEffect = SKPathEffect.CreateDash([5f, 4f], 0f) }; canvas.DrawRect(node.Bounds, f); }
            lastHitRegions.Add(new SankeyHitRegion(node.Node.Id, null, node.Bounds));
        }
        return layout;
    }

    public SankeyHitRegion? HitTest(GraphPoint point) => lastHitRegions.LastOrDefault(r => r.Bounds.Contains((float)point.X, (float)point.Y));
}

using SkiaSharp;

namespace MedWNetworkSim.Rendering.Geo;

public readonly record struct MapGeoCoordinate(double Latitude, double Longitude);
public readonly record struct MapProjectionViewport(double Width, double Height, double CenterLatitude, double CenterLongitude, double Zoom);

public interface IMapProjectionService
{
    (double X, double Y) Project(MapGeoCoordinate coordinate, MapProjectionViewport viewport);
}

public sealed class MapWebMercatorProjectionService : IMapProjectionService
{
    private const double EarthRadius = 6378137d;
    public (double X, double Y) Project(MapGeoCoordinate c, MapProjectionViewport v)
    {
        var lat = Math.Clamp(c.Latitude, -85.05112878d, 85.05112878d) * Math.PI / 180d;
        var lon = c.Longitude * Math.PI / 180d;
        var cx = EarthRadius * (v.CenterLongitude * Math.PI / 180d);
        var cy = EarthRadius * Math.Log(Math.Tan((Math.PI / 4d) + ((Math.Clamp(v.CenterLatitude, -85.05112878d, 85.05112878d) * Math.PI / 180d) / 2d)));
        var x = EarthRadius * lon;
        var y = EarthRadius * Math.Log(Math.Tan((Math.PI / 4d) + (lat / 2d)));
        var scale = Math.Max(0.0001d, v.Zoom);
        return ((x - cx) * scale + (v.Width / 2d), (cy - y) * scale + (v.Height / 2d));
    }
}

public interface IMapTileProvider { bool HasTiles { get; } }
public sealed class NoTileProvider : IMapTileProvider { public bool HasTiles => false; }

public sealed class MapGraphRenderer
{
    private readonly IMapProjectionService projectionService;
    private readonly IMapTileProvider tileProvider;

    public MapGraphRenderer(IMapProjectionService? projectionService = null, IMapTileProvider? tileProvider = null)
    {
        this.projectionService = projectionService ?? new MapWebMercatorProjectionService();
        this.tileProvider = tileProvider ?? new NoTileProvider();
    }

    public void Render(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize, IReadOnlyDictionary<string, MapGeoCoordinate> geoNodes, bool showBackground, out string? fallbackMessage)
    {
        fallbackMessage = null;
        DrawBackground(canvas, viewportSize, showBackground);
        if (geoNodes.Count == 0)
        {
            fallbackMessage = "This network has no geographic coordinates yet.";
            new GraphRenderer().Render(canvas, scene, viewport, viewportSize);
            using var p = new SKPaint { Color = new SKColor(252, 219, 107), TextSize = 20f, IsAntialias = true };
            canvas.DrawText(fallbackMessage, 24f, 40f, p);
            return;
        }

        var minLatitude = geoNodes.Values.Min(item => item.Latitude);
        var maxLatitude = geoNodes.Values.Max(item => item.Latitude);
        var minLongitude = geoNodes.Values.Min(item => item.Longitude);
        var maxLongitude = geoNodes.Values.Max(item => item.Longitude);
        var centerLatitude = (minLatitude + maxLatitude) / 2d;
        var centerLongitude = (minLongitude + maxLongitude) / 2d;
        var mapViewport = new MapProjectionViewport(viewportSize.Width, viewportSize.Height, centerLatitude, centerLongitude, Math.Max(0.0004d, viewport.Zoom * 0.0025d));
        using var edgePaint = new SKPaint { Color = new SKColor(125, 188, 255, 180), StrokeWidth = 2f, IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var nodePaint = new SKPaint { Color = new SKColor(93, 116, 160), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var labelPaint = new SKPaint { Color = new SKColor(222, 232, 245), TextSize = 12f, IsAntialias = true };

        foreach (var edge in scene.Edges)
        {
            if (!geoNodes.TryGetValue(edge.FromNodeId, out var from) || !geoNodes.TryGetValue(edge.ToNodeId, out var to)) continue;
            var s = projectionService.Project(from, mapViewport); var e = projectionService.Project(to, mapViewport);
            edgePaint.PathEffect = edge.Id == scene.Selection.KeyboardEdgeId ? SKPathEffect.CreateDash([10f, 4f], 0f) : null;
            canvas.DrawLine((float)s.X, (float)s.Y, (float)e.X, (float)e.Y, edgePaint);
        }

        foreach (var node in scene.Nodes)
        {
            if (!geoNodes.TryGetValue(node.Id, out var geo)) continue;
            var p = projectionService.Project(geo, mapViewport);
            var r = scene.Selection.SelectedNodeIds.Contains(node.Id) ? 8f : 6f;
            canvas.DrawCircle((float)p.X, (float)p.Y, r, nodePaint);
            if (scene.Selection.KeyboardNodeId == node.Id)
            {
                using var f = new SKPaint { Color = SKColors.Yellow, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, PathEffect = SKPathEffect.CreateDash([5f, 4f], 0f) };
                canvas.DrawCircle((float)p.X, (float)p.Y, r + 3f, f);
            }
            canvas.DrawText(node.Name, (float)p.X + 10f, (float)p.Y - 6f, labelPaint);
        }

        using var scale = new SKPaint { Color = new SKColor(220, 225, 235), StrokeWidth = 3f };
        var y = (float)viewportSize.Height - 28f;
        canvas.DrawLine(24f, y, 164f, y, scale);
        canvas.DrawText("Scale", 24f, y - 6f, labelPaint);
        canvas.DrawText("N", (float)viewportSize.Width - 36f, 28f, labelPaint);
        canvas.DrawLine((float)viewportSize.Width - 28f, 34f, (float)viewportSize.Width - 28f, 62f, scale);
    }

    private static void DrawBackground(SKCanvas canvas, GraphSize viewportSize, bool showBackground)
    {
        if (!showBackground) return;
        using var bg = new SKPaint { Color = new SKColor(18, 25, 38) };
        canvas.DrawRect(new SKRect(0f, 0f, (float)viewportSize.Width, (float)viewportSize.Height), bg);
        using var grid = new SKPaint { Color = new SKColor(64, 76, 99, 96), StrokeWidth = 1f };
        for (var x = 0f; x < viewportSize.Width; x += 80f) canvas.DrawLine(x, 0f, x, (float)viewportSize.Height, grid);
        for (var y = 0f; y < viewportSize.Height; y += 80f) canvas.DrawLine(0f, y, (float)viewportSize.Width, y, grid);
    }
}

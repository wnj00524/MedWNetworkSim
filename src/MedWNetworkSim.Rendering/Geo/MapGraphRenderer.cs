using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using SkiaSharp;

namespace MedWNetworkSim.Rendering.Geo;

public readonly record struct MapGeoCoordinate(double Latitude, double Longitude);
public readonly record struct MapProjectionViewport(double Width, double Height, double CenterLatitude, double CenterLongitude, double Zoom);
public sealed record MapCameraState(double CenterLatitude, double CenterLongitude, double Zoom, bool IsLockedToNetworkBounds);
public sealed record MapSelectionOverlay(
    MapGeoCoordinate? Start,
    MapGeoCoordinate? End,
    IReadOnlyList<(MapGeoCoordinate SouthWest, MapGeoCoordinate NorthEast)> Tiles,
    string? Label);

public interface IMapProjectionService
{
    (double X, double Y) Project(MapGeoCoordinate coordinate, MapProjectionViewport viewport);
    MapGeoCoordinate Unproject(double x, double y, MapProjectionViewport viewport);
}

public sealed class MapWebMercatorProjectionService : IMapProjectionService
{
    private const double EarthRadius = 6378137d;
    public (double X, double Y) Project(MapGeoCoordinate c, MapProjectionViewport v)
    {
        var lat = Math.Clamp(c.Latitude, -85.05112878d, 85.05112878d) * Math.PI / 180d;
        var lon = NormalizeLongitude(c.Longitude) * Math.PI / 180d;
        var cx = EarthRadius * (NormalizeLongitude(v.CenterLongitude) * Math.PI / 180d);
        var cy = EarthRadius * Math.Log(Math.Tan((Math.PI / 4d) + ((Math.Clamp(v.CenterLatitude, -85.05112878d, 85.05112878d) * Math.PI / 180d) / 2d)));
        var x = EarthRadius * lon;
        var y = EarthRadius * Math.Log(Math.Tan((Math.PI / 4d) + (lat / 2d)));
        var scale = Math.Max(0.0001d, v.Zoom);
        return ((x - cx) * scale + (v.Width / 2d), (cy - y) * scale + (v.Height / 2d));
    }

    public MapGeoCoordinate Unproject(double x, double y, MapProjectionViewport v)
    {
        var scale = Math.Max(0.0001d, v.Zoom);
        var cx = EarthRadius * (NormalizeLongitude(v.CenterLongitude) * Math.PI / 180d);
        var cy = EarthRadius * Math.Log(Math.Tan((Math.PI / 4d) + ((Math.Clamp(v.CenterLatitude, -85.05112878d, 85.05112878d) * Math.PI / 180d) / 2d)));
        var xMeters = ((x - (v.Width / 2d)) / scale) + cx;
        var yMeters = cy - ((y - (v.Height / 2d)) / scale);
        var lon = NormalizeLongitude((xMeters / EarthRadius) * 180d / Math.PI);
        var lat = Math.Clamp(((2d * Math.Atan(Math.Exp(yMeters / EarthRadius))) - (Math.PI / 2d)) * 180d / Math.PI, -85.05112878d, 85.05112878d);
        return new MapGeoCoordinate(lat, lon);
    }

    private static double NormalizeLongitude(double longitude)
    {
        var normalized = ((longitude + 180d) % 360d + 360d) % 360d - 180d;
        return normalized == -180d && longitude > 0d ? 180d : normalized;
    }
}

public readonly record struct OsmTile(int Zoom, int X, int Y);

public interface IMapTileProvider
{
    bool HasTiles { get; }
    string? StatusMessage { get; }
    event EventHandler? TilesChanged;
    bool TryGetTile(OsmTile tile, out SKBitmap? bitmap);
    void RequestTile(OsmTile tile);
}

public sealed class NoTileProvider : IMapTileProvider
{
    public bool HasTiles => false;
    public string? StatusMessage => "OSM tiles are unavailable. You can still drag to select an import area.";
    public event EventHandler? TilesChanged { add { } remove { } }
    public bool TryGetTile(OsmTile tile, out SKBitmap? bitmap)
    {
        bitmap = null;
        return false;
    }

    public void RequestTile(OsmTile tile)
    {
    }
}

public sealed class OsmRasterTileProvider : IMapTileProvider, IDisposable
{
    private sealed record TileCacheEntry(SKBitmap? Bitmap, bool IsLoading, DateTimeOffset LastFailureAt, bool HasFailure);

    private const int MaxCachedTiles = 256;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private readonly HttpClient httpClient;
    private readonly bool ownsClient;
    private readonly ConcurrentDictionary<OsmTile, TileCacheEntry> cache = new();
    private readonly Dictionary<OsmTile, LinkedListNode<OsmTile>> lruNodes = new();
    private readonly LinkedList<OsmTile> lruList = new();
    private readonly object cacheSync = new();
    private readonly SemaphoreSlim downloadLimiter = new(2, 2);
    private readonly CancellationTokenSource disposeCts = new();
    private int failedTileCount;
    private int isDisposed;

    public OsmRasterTileProvider(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        ownsClient = httpClient is null;
        if (!this.httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MedWNetworkSim/2.0 (Avalonia OSM map renderer)");
        }
    }

    public bool HasTiles => true;
    public string? StatusMessage => Volatile.Read(ref failedTileCount) > 0
        ? "Could not load some map tiles. Check your connection and continue selecting an area."
        : null;
    public event EventHandler? TilesChanged;

    public bool TryGetTile(OsmTile tile, out SKBitmap? bitmap)
    {
        if (cache.TryGetValue(tile, out var cached) && cached.Bitmap is not null)
        {
            TouchTile(tile);
            bitmap = cached.Bitmap;
            return true;
        }

        bitmap = null;
        return false;
    }

    public void RequestTile(OsmTile tile)
    {
        if (Volatile.Read(ref isDisposed) != 0)
        {
            return;
        }

        if (tile.Zoom is < 0 or > 19)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var lastFailureAt = default(DateTimeOffset);
        if (cache.TryGetValue(tile, out var existing))
        {
            lastFailureAt = existing.LastFailureAt;
            if (existing.Bitmap is not null || existing.IsLoading)
            {
                TouchTile(tile);
                return;
            }

            if (existing.LastFailureAt != default && now - existing.LastFailureAt < RetryDelay)
            {
                return;
            }
        }

        cache[tile] = new TileCacheEntry(null, true, lastFailureAt, false);
        TouchTile(tile);
        _ = DownloadTileAsync(tile, disposeCts.Token);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        disposeCts.Cancel();

        foreach (var item in cache)
        {
            item.Value.Bitmap?.Dispose();
        }

        cache.Clear();
        lock (cacheSync)
        {
            lruNodes.Clear();
            lruList.Clear();
        }

        disposeCts.Dispose();
        downloadLimiter.Dispose();
        if (ownsClient)
        {
            httpClient.Dispose();
        }
    }

    private async Task DownloadTileAsync(OsmTile tile, CancellationToken cancellationToken)
    {
        var limiterEntered = false;
        try
        {
            await downloadLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
            limiterEntered = true;
            using var response = await httpClient.GetAsync($"https://tile.openstreetmap.org/{tile.Zoom}/{tile.X}/{tile.Y}.png", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null)
            {
                SetFailure(tile);
                TilesChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            var previous = cache.TryGetValue(tile, out var priorEntry) ? priorEntry : null;
            cache[tile] = new TileCacheEntry(bitmap, false, default, false);
            TouchTile(tile);
            if (previous?.HasFailure == true)
            {
                Interlocked.Decrement(ref failedTileCount);
            }

            previous?.Bitmap?.Dispose();
            EnforceCacheLimit();
            TilesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[OsmRasterTileProvider] Tile download failed for {tile.Zoom}/{tile.X}/{tile.Y}: {ex}");
            SetFailure(tile);
            TilesChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            if (limiterEntered)
            {
                downloadLimiter.Release();
            }
        }
    }

    private void SetFailure(OsmTile tile)
    {
        var previous = cache.TryGetValue(tile, out var priorEntry) ? priorEntry : null;
        cache[tile] = new TileCacheEntry(null, false, DateTimeOffset.UtcNow, true);
        TouchTile(tile);
        if (previous?.HasFailure != true)
        {
            Interlocked.Increment(ref failedTileCount);
        }
    }

    private void TouchTile(OsmTile tile)
    {
        lock (cacheSync)
        {
            if (lruNodes.TryGetValue(tile, out var node))
            {
                lruList.Remove(node);
            }
            else
            {
                node = new LinkedListNode<OsmTile>(tile);
                lruNodes[tile] = node;
            }

            lruList.AddFirst(node);
        }
    }

    private void EnforceCacheLimit()
    {
        while (cache.Count > MaxCachedTiles)
        {
            OsmTile? tileToEvict = null;
            lock (cacheSync)
            {
                var node = lruList.Last;
                while (node is not null)
                {
                    if (cache.TryGetValue(node.Value, out var cached) && !cached.IsLoading)
                    {
                        tileToEvict = node.Value;
                        lruList.Remove(node);
                        lruNodes.Remove(node.Value);
                        break;
                    }

                    node = node.Previous;
                }
            }

            if (tileToEvict is null)
            {
                break;
            }

            if (cache.TryRemove(tileToEvict.Value, out var removed))
            {
                if (removed.HasFailure)
                {
                    Interlocked.Decrement(ref failedTileCount);
                }

                removed.Bitmap?.Dispose();
            }
        }
    }
}

public sealed class MapGraphRenderer
{
    private const double EarthRadius = 6378137d;
    private readonly IMapProjectionService projectionService;
    private readonly IMapTileProvider tileProvider;

    public MapGraphRenderer(IMapProjectionService? projectionService = null, IMapTileProvider? tileProvider = null)
    {
        this.projectionService = projectionService ?? new MapWebMercatorProjectionService();
        this.tileProvider = tileProvider ?? new NoTileProvider();
    }

    public void Render(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize, IReadOnlyDictionary<string, MapGeoCoordinate> geoNodes, bool showBackground, out string? fallbackMessage)
    {
        var camera = geoNodes.Count == 0
            ? new MapCameraState(51.5074d, -0.1278d, 0.0015d, true)
            : FitCameraToBoundingBox(
                geoNodes.Values.Min(item => item.Latitude),
                geoNodes.Values.Min(item => item.Longitude),
                geoNodes.Values.Max(item => item.Latitude),
                geoNodes.Values.Max(item => item.Longitude),
                viewportSize);
        Render(canvas, scene, viewport, viewportSize, geoNodes, showBackground, camera, null, out fallbackMessage);
    }

    public void Render(SKCanvas canvas, GraphScene scene, GraphViewport viewport, GraphSize viewportSize, IReadOnlyDictionary<string, MapGeoCoordinate> geoNodes, bool showBackground, MapCameraState camera, MapSelectionOverlay? overlay, out string? fallbackMessage)
    {
        fallbackMessage = null;
        DrawBackground(canvas, viewportSize, showBackground);

        var mapViewport = new MapProjectionViewport(viewportSize.Width, viewportSize.Height, camera.CenterLatitude, camera.CenterLongitude, Math.Max(0.0001d, camera.Zoom));
        var drewTiles = DrawTileBackground(canvas, mapViewport, showBackground);
        if (!drewTiles)
        {
            DrawGridOverlay(canvas, viewportSize);
            fallbackMessage = tileProvider.StatusMessage ?? "Map tiles are not available right now. You can still drag to select an area and import.";
        }

        using var edgePaint = new SKPaint { Color = new SKColor(125, 188, 255, 180), StrokeWidth = 2f, IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var nodePaint = new SKPaint { Color = new SKColor(93, 116, 160), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var labelFont = new SKFont { Size = 12f };
        using var labelPaint = new SKPaint { Color = new SKColor(222, 232, 245), IsAntialias = true };

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

            canvas.DrawText(node.Name, (float)p.X + 10f, (float)p.Y - 6f, SKTextAlign.Left, labelFont, labelPaint);
        }

        if (geoNodes.Count == 0)
        {
            var instruction = "Drag on the map to select an area, then choose Import selected area.";
            using var panelPaint = new SKPaint { Color = new SKColor(10, 16, 28, 180), Style = SKPaintStyle.Fill };
            using var panelBorder = new SKPaint { Color = new SKColor(252, 219, 107, 220), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
            var panelRect = new SKRect(18, 16, (float)viewportSize.Width - 18, 54);
            canvas.DrawRoundRect(panelRect, 8f, 8f, panelPaint);
            canvas.DrawRoundRect(panelRect, 8f, 8f, panelBorder);
            canvas.DrawText(instruction, 28f, 39f, SKTextAlign.Left, labelFont, labelPaint);
            fallbackMessage ??= instruction;
        }

        if (!string.IsNullOrWhiteSpace(tileProvider.StatusMessage))
        {
            using var warningFont = new SKFont { Size = 12f };
            using var warningPaint = new SKPaint
            {
                Color = new SKColor(255, 206, 122),
                IsAntialias = true
            };

            canvas.DrawText(
                tileProvider.StatusMessage,
                24f,
                (float)viewportSize.Height - 44f,
                SKTextAlign.Left,
                warningFont,
                warningPaint);
        }

        using var scale = new SKPaint { Color = new SKColor(220, 225, 235), StrokeWidth = 3f };
        var y = (float)viewportSize.Height - 28f;
        canvas.DrawLine(24f, y, 164f, y, scale);
        canvas.DrawText("Scale", 24f, y - 6f, SKTextAlign.Left, labelFont, labelPaint);
        canvas.DrawText("N", (float)viewportSize.Width - 36f, 28f, SKTextAlign.Left, labelFont, labelPaint);
        canvas.DrawLine((float)viewportSize.Width - 28f, 34f, (float)viewportSize.Width - 28f, 62f, scale);
        DrawOverlay(canvas, mapViewport, overlay);
    }

    public static MapCameraState FitCameraToBoundingBox(double minLatitude, double minLongitude, double maxLatitude, double maxLongitude, GraphSize viewportSize)
    {
        var projection = new MapWebMercatorProjectionService();
        var centerLatitude = (minLatitude + maxLatitude) / 2d;
        var centerLongitude = (minLongitude + maxLongitude) / 2d;
        var measureViewport = new MapProjectionViewport(viewportSize.Width, viewportSize.Height, centerLatitude, centerLongitude, 1d);
        var sw = projection.Project(new MapGeoCoordinate(minLatitude, minLongitude), measureViewport);
        var ne = projection.Project(new MapGeoCoordinate(maxLatitude, maxLongitude), measureViewport);
        var width = Math.Max(1d, Math.Abs(ne.X - sw.X));
        var height = Math.Max(1d, Math.Abs(ne.Y - sw.Y));
        var zoom = Math.Max(0.0001d, Math.Min((viewportSize.Width - 80d) / width, (viewportSize.Height - 80d) / height));
        return new MapCameraState(centerLatitude, centerLongitude, zoom, true);
    }

    private bool DrawTileBackground(SKCanvas canvas, MapProjectionViewport viewport, bool showBackground)
    {
        if (!showBackground || !tileProvider.HasTiles)
        {
            return false;
        }

        var worldPixelsAtCameraZoom = Math.Max(256d, (Math.Max(0.0001d, viewport.Zoom) * (2d * Math.PI * EarthRadius)));
        var slippyZoom = Math.Clamp((int)Math.Round(Math.Log(worldPixelsAtCameraZoom / 256d, 2d)), 0, 19);
        var worldPixelsAtSlippyZoom = 256d * Math.Pow(2d, slippyZoom);
        var scaleFactor = worldPixelsAtCameraZoom / worldPixelsAtSlippyZoom;
        var centerPixelX = LongitudeToPixelX(viewport.CenterLongitude, slippyZoom);
        var centerPixelY = LatitudeToPixelY(viewport.CenterLatitude, slippyZoom);
        var halfWidth = viewport.Width / (2d * scaleFactor);
        var halfHeight = viewport.Height / (2d * scaleFactor);

        var minTileX = (int)Math.Floor((centerPixelX - halfWidth) / 256d);
        var maxTileX = (int)Math.Ceiling((centerPixelX + halfWidth) / 256d);
        var minTileY = (int)Math.Floor((centerPixelY - halfHeight) / 256d);
        var maxTileY = (int)Math.Ceiling((centerPixelY + halfHeight) / 256d);
        var tilesPerAxis = 1 << slippyZoom;
        var drewAnyTile = false;

        for (var tileY = minTileY; tileY <= maxTileY; tileY++)
        {
            if (tileY < 0 || tileY >= tilesPerAxis)
            {
                continue;
            }

            for (var tileX = minTileX; tileX <= maxTileX; tileX++)
            {
                var wrappedX = ((tileX % tilesPerAxis) + tilesPerAxis) % tilesPerAxis;
                var tile = new OsmTile(slippyZoom, wrappedX, tileY);
                var left = ((tileX * 256d) - centerPixelX) * scaleFactor + (viewport.Width / 2d);
                var top = ((tileY * 256d) - centerPixelY) * scaleFactor + (viewport.Height / 2d);
                var size = 256d * scaleFactor;
                var rect = SKRect.Create((float)left, (float)top, (float)size, (float)size);

                if (tileProvider.TryGetTile(tile, out var bitmap) && bitmap is not null)
                {
                    canvas.DrawBitmap(bitmap, rect);
                    drewAnyTile = true;
                }
                else
                {
                    using var placeholder = new SKPaint { Color = new SKColor(30, 38, 52), Style = SKPaintStyle.Fill };
                    using var outline = new SKPaint { Color = new SKColor(60, 76, 104), Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
                    canvas.DrawRect(rect, placeholder);
                    canvas.DrawRect(rect, outline);
                    tileProvider.RequestTile(tile);
                }
            }
        }

        return drewAnyTile;
    }

    private void DrawOverlay(SKCanvas canvas, MapProjectionViewport viewport, MapSelectionOverlay? overlay)
    {
        if (overlay?.Start is null || overlay.End is null)
        {
            return;
        }

        using var fill = new SKPaint { Color = new SKColor(255, 214, 102, 48), Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { Color = new SKColor(255, 222, 125), Style = SKPaintStyle.Stroke, StrokeWidth = 3f, PathEffect = SKPathEffect.CreateDash([12f, 6f], 0f), IsAntialias = true };
        using var tileStroke = new SKPaint { Color = new SKColor(83, 245, 237, 210), Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, IsAntialias = true };
        using var labelFont = new SKFont { Size = 12f };
        using var labelPaint = new SKPaint { Color = new SKColor(245, 247, 250), IsAntialias = true };

        foreach (var tile in overlay.Tiles)
        {
            DrawGeoRect(canvas, viewport, tile.SouthWest, tile.NorthEast, tileStroke, null);
        }

        DrawGeoRect(canvas, viewport, overlay.Start.Value, overlay.End.Value, stroke, fill);
        if (!string.IsNullOrWhiteSpace(overlay.Label))
        {
            var p = projectionService.Project(overlay.End.Value, viewport);
            canvas.DrawText(overlay.Label, (float)p.X + 10f, (float)p.Y - 10f, SKTextAlign.Left, labelFont, labelPaint);
        }
    }

    private void DrawGeoRect(SKCanvas canvas, MapProjectionViewport viewport, MapGeoCoordinate a, MapGeoCoordinate b, SKPaint stroke, SKPaint? fill)
    {
        var p1 = projectionService.Project(a, viewport);
        var p2 = projectionService.Project(b, viewport);
        var rect = SKRect.Create((float)Math.Min(p1.X, p2.X), (float)Math.Min(p1.Y, p2.Y), (float)Math.Abs(p2.X - p1.X), (float)Math.Abs(p2.Y - p1.Y));
        if (fill is not null)
        {
            canvas.DrawRect(rect, fill);
        }

        canvas.DrawRect(rect, stroke);
    }

    private static void DrawBackground(SKCanvas canvas, GraphSize viewportSize, bool showBackground)
    {
        if (!showBackground) return;
        using var bg = new SKPaint { Color = new SKColor(18, 25, 38) };
        canvas.DrawRect(new SKRect(0f, 0f, (float)viewportSize.Width, (float)viewportSize.Height), bg);
    }

    private static void DrawGridOverlay(SKCanvas canvas, GraphSize viewportSize)
    {
        using var grid = new SKPaint { Color = new SKColor(64, 76, 99, 96), StrokeWidth = 1f };
        for (var x = 0f; x < viewportSize.Width; x += 80f) canvas.DrawLine(x, 0f, x, (float)viewportSize.Height, grid);
        for (var y = 0f; y < viewportSize.Height; y += 80f) canvas.DrawLine(0f, y, (float)viewportSize.Width, y, grid);
    }

    private static double LongitudeToPixelX(double longitude, int zoom)
    {
        var normalizedLongitude = ((longitude + 180d) % 360d + 360d) % 360d;
        return normalizedLongitude / 360d * (256d * Math.Pow(2d, zoom));
    }

    private static double LatitudeToPixelY(double latitude, int zoom)
    {
        var clamped = Math.Clamp(latitude, -85.05112878d, 85.05112878d);
        var radians = clamped * Math.PI / 180d;
        var mercator = Math.Log(Math.Tan(Math.PI / 4d + radians / 2d));
        return (1d - mercator / Math.PI) / 2d * (256d * Math.Pow(2d, zoom));
    }
}

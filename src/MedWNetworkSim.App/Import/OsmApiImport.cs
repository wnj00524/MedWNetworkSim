using System.Globalization;
using System.Net;
using MedWNetworkSim.App.Models;
using OsmSharp;
using OsmSharp.Streams;

namespace MedWNetworkSim.App.Import;

public sealed record OsmBoundingBox(double MinLon, double MinLat, double MaxLon, double MaxLat)
{
    public const double MinLatitudeLimit = -85.05112878d;
    public const double MaxLatitudeLimit = 85.05112878d;

    public double AreaDegrees => Math.Max(0d, MaxLon - MinLon) * Math.Max(0d, MaxLat - MinLat);
    public double CenterLatitude => (MinLat + MaxLat) / 2d;
    public double CenterLongitude => (MinLon + MaxLon) / 2d;

    public OsmBoundingBox Normalize()
    {
        var west = NormalizeLongitude(MinLon);
        var east = NormalizeLongitude(MaxLon);
        var south = Math.Clamp(MinLat, MinLatitudeLimit, MaxLatitudeLimit);
        var north = Math.Clamp(MaxLat, MinLatitudeLimit, MaxLatitudeLimit);
        if (west > east)
        {
            (west, east) = (east, west);
        }

        if (south > north)
        {
            (south, north) = (north, south);
        }

        return new OsmBoundingBox(west, south, east, north);
    }

    public void Validate()
    {
        if (MinLon < -180d || MinLon > 180d || MaxLon < -180d || MaxLon > 180d)
        {
            throw new ArgumentOutOfRangeException(nameof(MinLon), "Longitude must be between -180 and 180.");
        }

        if (MinLat < MinLatitudeLimit || MinLat > MaxLatitudeLimit || MaxLat < MinLatitudeLimit || MaxLat > MaxLatitudeLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(MinLat), "Latitude must be between -85.05112878 and 85.05112878.");
        }

        if (MinLon >= MaxLon || MinLat >= MaxLat)
        {
            throw new ArgumentException("West must be less than east, and south must be less than north.");
        }
    }

    public static bool TryCreate(double west, double south, double east, double north, out OsmBoundingBox bbox, out string? error)
    {
        bbox = new OsmBoundingBox(west, south, east, north).Normalize();
        try
        {
            bbox.Validate();
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            error = ex.Message;
            return false;
        }
    }

    public static double NormalizeLongitude(double longitude)
    {
        if (double.IsNaN(longitude) || double.IsInfinity(longitude))
        {
            return 0d;
        }

        var normalized = ((longitude + 180d) % 360d + 360d) % 360d - 180d;
        return normalized == -180d && longitude > 0d ? 180d : normalized;
    }
}

public interface IOsmApiClient
{
    Task<Stream> DownloadBoundingBoxAsync(OsmBoundingBox bbox, CancellationToken ct);
}

public sealed class OsmApiClient : IOsmApiClient, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly bool ownsClient;

    public OsmApiClient(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        ownsClient = httpClient is null;
        if (!this.httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MedWNetworkSim/2.0 (OSM bounding-box importer)");
        }
    }

    public async Task<Stream> DownloadBoundingBoxAsync(OsmBoundingBox bbox, CancellationToken ct)
    {
        bbox = bbox.Normalize();
        bbox.Validate();
        var query = string.Join(",",
            Format(bbox.MinLon),
            Format(bbox.MinLat),
            Format(bbox.MaxLon),
            Format(bbox.MaxLat));
        using var response = await httpClient.GetAsync($"https://api.openstreetmap.org/api/0.6/map?bbox={query}", HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new OsmImportException("Selected area is too large. Zoom in or reduce selection.");
        }

        if ((int)response.StatusCode is 429 or 509)
        {
            throw new OsmImportException("OpenStreetMap is rate limiting downloads. Wait a minute, reduce the selection, then try again.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new OsmImportException($"OpenStreetMap download failed ({(int)response.StatusCode}). Reduce the selection or try again later.");
        }

        var memory = new MemoryStream();
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await source.CopyToAsync(memory, ct);
        memory.Position = 0;
        return memory;
    }

    public void Dispose()
    {
        if (ownsClient)
        {
            httpClient.Dispose();
        }
    }

    private static string Format(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}

public static class OsmBoundingBoxTiler
{
    public const double MaxTileAreaDegrees = 0.25d;
    public const double AutoTileAreaLimitDegrees = 2.0d;

    public static IReadOnlyList<OsmBoundingBox> CreateTiles(OsmBoundingBox bbox)
    {
        bbox = bbox.Normalize();
        bbox.Validate();
        if (bbox.AreaDegrees > AutoTileAreaLimitDegrees)
        {
            throw new OsmImportException("Selected area is too large. Zoom in or reduce selection.");
        }

        if (bbox.AreaDegrees <= MaxTileAreaDegrees)
        {
            return [bbox];
        }

        var lonSpan = bbox.MaxLon - bbox.MinLon;
        var latSpan = bbox.MaxLat - bbox.MinLat;
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(bbox.AreaDegrees / MaxTileAreaDegrees * lonSpan / Math.Max(latSpan, 0.000001d))));
        var rows = Math.Max(1, (int)Math.Ceiling(bbox.AreaDegrees / MaxTileAreaDegrees / columns));
        while ((lonSpan / columns) * (latSpan / rows) > MaxTileAreaDegrees)
        {
            if ((lonSpan / columns) >= (latSpan / rows))
            {
                columns++;
            }
            else
            {
                rows++;
            }
        }

        var tiles = new List<OsmBoundingBox>(rows * columns);
        for (var row = 0; row < rows; row++)
        {
            var south = bbox.MinLat + (latSpan * row / rows);
            var north = row == rows - 1 ? bbox.MaxLat : bbox.MinLat + (latSpan * (row + 1) / rows);
            for (var column = 0; column < columns; column++)
            {
                var west = bbox.MinLon + (lonSpan * column / columns);
                var east = column == columns - 1 ? bbox.MaxLon : bbox.MinLon + (lonSpan * (column + 1) / columns);
                tiles.Add(new OsmBoundingBox(west, south, east, north));
            }
        }

        return tiles;
    }
}

public sealed class OsmBoundingBoxImporter(IOsmApiClient apiClient)
{
    public async Task<NetworkModel> ImportAsync(OsmBoundingBox bbox, OsmImportOptions options, CancellationToken ct = default)
    {
        var tiles = OsmBoundingBoxTiler.CreateTiles(bbox);
        var nodes = new Dictionary<long, Node>();
        var ways = new Dictionary<long, Way>();
        var relations = new Dictionary<long, Relation>();

        foreach (var tile in tiles)
        {
            await using var stream = await apiClient.DownloadBoundingBoxAsync(tile, ct);
            foreach (var geo in new XmlOsmStreamSource(stream))
            {
                switch (geo)
                {
                    case Node node when node.Id.HasValue:
                        nodes[node.Id.Value] = node;
                        break;
                    case Way way when way.Id.HasValue:
                        ways[way.Id.Value] = way;
                        break;
                    case Relation relation when relation.Id.HasValue:
                        relations[relation.Id.Value] = relation;
                        break;
                }
            }
        }

        var merged = nodes.Values.Cast<OsmGeo>()
            .Concat(ways.Values)
            .Concat(relations.Values)
            .ToList();
        return new OsmImporter().ImportFromGeos(merged, options);
    }
}

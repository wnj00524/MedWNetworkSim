using System.Text;
using MedWNetworkSim.App.Import;
using MedWNetworkSim.Presentation;
using MedWNetworkSim.Rendering;
using MedWNetworkSim.Rendering.Geo;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class OsmBoundingBoxImportTests
{
    [Fact]
    public void BoundingBox_NormalizeAndValidate_OrdersAndClamps()
    {
        var bbox = new OsmBoundingBox(1, 52, 0, 51).Normalize();

        Assert.Equal(0, bbox.MinLon);
        Assert.Equal(1, bbox.MaxLon);
        Assert.Equal(51, bbox.MinLat);
        Assert.Equal(52, bbox.MaxLat);
        Assert.Equal(1, bbox.AreaDegrees);
        bbox.Validate();
    }

    [Fact]
    public void Tiler_SplitsIntoTilesWithinLimit()
    {
        var tiles = OsmBoundingBoxTiler.CreateTiles(new OsmBoundingBox(-1, 50, 1, 51));

        Assert.True(tiles.Count > 1);
        Assert.All(tiles, tile => Assert.InRange(tile.AreaDegrees, 0d, OsmBoundingBoxTiler.MaxTileAreaDegrees));
        Assert.Equal(-1, tiles.Min(tile => tile.MinLon), 8);
        Assert.Equal(1, tiles.Max(tile => tile.MaxLon), 8);
        Assert.Equal(50, tiles.Min(tile => tile.MinLat), 8);
        Assert.Equal(51, tiles.Max(tile => tile.MaxLat), 8);
    }

    [Fact]
    public void MapProjection_ProjectThenUnproject_RoundTrips()
    {
        var projection = new MapWebMercatorProjectionService();
        var viewport = new MapProjectionViewport(1200, 800, 51.5, -0.12, 0.0012d);

        var screen = projection.Project(new MapGeoCoordinate(51.501, -0.141), viewport);
        var geo = projection.Unproject(screen.X, screen.Y, viewport);

        Assert.InRange(Math.Abs(geo.Latitude - 51.501), 0d, 0.0001d);
        Assert.InRange(Math.Abs(geo.Longitude - -0.141), 0d, 0.0001d);
    }

    [Fact]
    public void Importer_NodeRetentionPercentage_ChangesReducibleNodeCount()
    {
        var full = new OsmImporter().ImportFromGeos(CreateLongRoadFixture(), new OsmImportOptions(true, 100));
        var reduced = new OsmImporter().ImportFromGeos(CreateLongRoadFixture(), new OsmImportOptions(true, 1));

        Assert.True(full.Nodes.Count > reduced.Nodes.Count);
        Assert.Contains(reduced.Nodes, node => node.Id == "osm-node-1");
        Assert.Contains(reduced.Nodes, node => node.Id == "osm-node-12");
        Assert.All(reduced.Edges, edge =>
        {
            Assert.Contains(reduced.Nodes, node => node.Id == edge.FromNodeId);
            Assert.Contains(reduced.Nodes, node => node.Id == edge.ToNodeId);
        });
    }

    [Fact]
    public async Task BoundingBoxImporter_MergesTilesAndUsesExistingImporterOptions()
    {
        var client = new FakeOsmApiClient(CreateLongRoadXml());
        var importer = new OsmBoundingBoxImporter(client);

        var network = await importer.ImportAsync(new OsmBoundingBox(0, 51, 0.1, 51.1), new OsmImportOptions(true, 10));

        Assert.NotEmpty(network.Nodes);
        Assert.NotEmpty(network.Edges);
        Assert.All(network.Nodes, node => Assert.True(node.Latitude.HasValue && node.Longitude.HasValue));
        Assert.True(network.LockLayoutToMap);
        Assert.All(network.Nodes, node =>
        {
            Assert.True(node.X.HasValue);
            Assert.True(node.Y.HasValue);
        });
    }

    [Fact]
    public void ManualCoordinateInput_UpdatesSelectionMetricsImmediately()
    {
        var vm = new WorkspaceViewModel
        {
            OsmWestText = "-0.2",
            OsmSouthText = "51.4",
            OsmEastText = "-0.1",
            OsmNorthText = "51.5"
        };

        Assert.NotNull(vm.OsmSelection);
        Assert.Equal("1 tile", vm.OsmTileCountText);
        Assert.Equal("Selected area ready. Choose Import selected area.", vm.OsmValidationMessage);
    }

    [Fact]
    public void SelectedCoordinates_ConvertToExpectedBoundingBox()
    {
        var start = new MapGeoCoordinate(51.6, -0.3);
        var end = new MapGeoCoordinate(51.4, -0.1);

        var success = WorkspaceViewModel.TryCreateBoundingBoxFromCoordinates(start, end, out var bbox, out var error);

        Assert.True(success, error);
        Assert.Equal(-0.3, bbox.MinLon, 8);
        Assert.Equal(-0.1, bbox.MaxLon, 8);
        Assert.Equal(51.4, bbox.MinLat, 8);
        Assert.Equal(51.6, bbox.MaxLat, 8);
    }

    [Fact]
    public void ReversedDragDirection_NormalizesBoundingBox()
    {
        var start = new MapGeoCoordinate(40.1, -73.5);
        var end = new MapGeoCoordinate(39.9, -73.8);

        var success = WorkspaceViewModel.TryCreateBoundingBoxFromCoordinates(start, end, out var bbox, out _);

        Assert.True(success);
        Assert.Equal(-73.8, bbox.MinLon, 8);
        Assert.Equal(-73.5, bbox.MaxLon, 8);
        Assert.Equal(39.9, bbox.MinLat, 8);
        Assert.Equal(40.1, bbox.MaxLat, 8);
    }

    [Fact]
    public void SelectedCoordinates_AreClampedToMercatorAndLongitudeLimits()
    {
        var start = new MapGeoCoordinate(95d, -240d);
        var end = new MapGeoCoordinate(-95d, 240d);

        var success = WorkspaceViewModel.TryCreateBoundingBoxFromCoordinates(start, end, out var bbox, out var error);

        Assert.True(success, error);
        Assert.Equal(-180d, bbox.MinLon, 8);
        Assert.Equal(180d, bbox.MaxLon, 8);
        Assert.Equal(OsmBoundingBox.MinLatitudeLimit, bbox.MinLat, 8);
        Assert.Equal(OsmBoundingBox.MaxLatitudeLimit, bbox.MaxLat, 8);
    }

    [Fact]
    public void EndSelection_RejectsTinyBoxesWithClearMessage()
    {
        var vm = new WorkspaceViewModel
        {
            IsOsmAreaSelectionEnabled = true
        };

        vm.BeginOsmSelection(new MapGeoCoordinate(51.5d, -0.12d));
        vm.EndOsmSelection(new MapGeoCoordinate(51.5000005d, -0.1199995d));

        Assert.Equal("Selected area is too small. Drag a larger box.", vm.OsmValidationMessage);
        Assert.False(vm.CanImportOsmSelection);
    }

    [Fact]
    public void TooLargeSelection_IsRejectedByTiler()
    {
        var bbox = new OsmBoundingBox(-1.5, 50, 1.5, 51.5);
        Assert.Throws<OsmImportException>(() => OsmBoundingBoxTiler.CreateTiles(bbox));
    }

    [Fact]
    public void ImportCommand_DisabledWithoutValidSelection()
    {
        var vm = new WorkspaceViewModel();
        vm.ClearOsmSelection();

        Assert.False(vm.CanImportOsmSelection);
        Assert.False(vm.ImportOsmSelectionCommand.CanExecute(null));
    }

    [Fact]
    public void MapRenderer_WithNoGeoNodesAndSelectionOverlay_DoesNotThrow()
    {
        using var bitmap = new SKBitmap(800, 480);
        using var canvas = new SKCanvas(bitmap);
        var renderer = new MapGraphRenderer();
        var overlay = new MapSelectionOverlay(
            new MapGeoCoordinate(51.49, -0.2),
            new MapGeoCoordinate(51.51, -0.1),
            [],
            "Selection preview");

        var ex = Record.Exception(() => renderer.Render(
            canvas,
            new GraphScene(),
            new GraphViewport(),
            new GraphSize(800, 480),
            new Dictionary<string, MapGeoCoordinate>(),
            showBackground: true,
            new MapCameraState(51.5074, -0.1278, 0.0015, false),
            overlay,
            out _));

        Assert.Null(ex);
    }

    private static IReadOnlyList<OsmSharp.OsmGeo> CreateLongRoadFixture()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(CreateLongRoadXml()));
        return new OsmSharp.Streams.XmlOsmStreamSource(stream).ToList();
    }

    private static string CreateLongRoadXml()
    {
        var builder = new StringBuilder();
        builder.AppendLine("<osm version=\"0.6\" generator=\"tests\">");
        for (var i = 1; i <= 12; i++)
        {
            builder.AppendLine($"<node id=\"{i}\" lat=\"51.{i:000}\" lon=\"-0.{i:000}\" />");
        }

        builder.AppendLine("<way id=\"10\">");
        for (var i = 1; i <= 12; i++)
        {
            builder.AppendLine($"<nd ref=\"{i}\" />");
        }

        builder.AppendLine("<tag k=\"highway\" v=\"residential\" />");
        builder.AppendLine("<tag k=\"name\" v=\"Test Road\" />");
        builder.AppendLine("</way>");
        builder.AppendLine("</osm>");
        return builder.ToString();
    }

    private sealed class FakeOsmApiClient(string xml) : IOsmApiClient
    {
        public Task<Stream> DownloadBoundingBoxAsync(OsmBoundingBox bbox, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
    }
}

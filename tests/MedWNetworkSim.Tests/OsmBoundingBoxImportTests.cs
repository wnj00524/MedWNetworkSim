using System.Text;
using MedWNetworkSim.App.Import;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.Presentation;
using MedWNetworkSim.Rendering;
using MedWNetworkSim.Rendering.Geo;
using OsmSharp;
using OsmSharp.Tags;
using SkiaSharp;
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
    public void Mapper_LongWayAtTenPercent_ReducesNodesAggressively()
    {
        var geos = CreateLinearWayGeos(100);
        var network = new OsmToSimulationMapper().Map(geos, new OsmImportOptions(true, 10));

        Assert.InRange(network.Nodes.Count, 2, 15);
    }

    [Fact]
    public void Mapper_GridNetwork_RetainsDegreeThreeOrHigherJunctions()
    {
        var geos = CreateCrossRoadGeos();
        var network = new OsmToSimulationMapper().Map(geos, new OsmImportOptions(true, 10));

        Assert.Contains(network.Nodes, node => node.Id == "osm-node-3");
    }

    [Fact]
    public void Mapper_CollapsedEdge_UsesPathDistanceSum()
    {
        var geos = CreateLinearWayGeos(20);
        var network = new OsmToSimulationMapper().Map(geos, new OsmImportOptions(true, 10));

        var first = network.Nodes.Select(node => long.Parse(node.OsmId!)).Min();
        var last = network.Nodes.Select(node => long.Parse(node.OsmId!)).Max();
        var edge = Assert.Single(network.Edges, e => e.FromNodeId == $"osm-node-{first}" && e.ToNodeId == $"osm-node-{last}");
        var expectedDistance = SumPathDistance(geos, 1, 20);
        Assert.InRange(Math.Abs(edge.Cost - expectedDistance), 0d, 0.00001d);
    }

    [Fact]
    public void Mapper_OneWayResidentialDeadEndSpur_ImportsAsBidirectionalAccess()
    {
        var geos = new List<OsmGeo>
        {
            new Node { Id = 1, Latitude = 51.5000, Longitude = 0.1000 },
            new Node { Id = 2, Latitude = 51.5002, Longitude = 0.1002 },
            new Node { Id = 3, Latitude = 51.5004, Longitude = 0.1004 },
            new Node { Id = 4, Latitude = 51.5006, Longitude = 0.1006 },
            new Node { Id = 5, Latitude = 51.5006, Longitude = 0.1012 }
        };

        geos.Add(new Way
        {
            Id = 1000,
            Nodes = [1, 2, 3],
            Tags = new TagsCollection
            {
                { "highway", "residential" },
                { "oneway", "yes" },
                { "name", "Dead End Road" }
            }
        });
        geos.Add(new Way
        {
            Id = 1001,
            Nodes = [3, 4, 5],
            Tags = new TagsCollection
            {
                { "highway", "residential" },
                { "name", "Connector Road" }
            }
        });

        var network = new OsmToSimulationMapper().Map(geos, new OsmImportOptions(true, 100));
        var spurEdges = network.Edges
            .Where(edge => edge.Id.StartsWith("osm-way-1000-", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(spurEdges);
        Assert.All(spurEdges, edge => Assert.True(edge.IsBidirectional));
    }

    [Fact]
    public void Mapper_MergeConnectivity_RepairsDirectedReachabilityInsideMainComponent()
    {
        var geos = new List<OsmGeo>
        {
            new Node { Id = 1, Latitude = 51.5000, Longitude = 0.1000 },
            new Node { Id = 2, Latitude = 51.5002, Longitude = 0.1002 },
            new Node { Id = 3, Latitude = 51.5004, Longitude = 0.1004 },
            new Node { Id = 4, Latitude = 51.5006, Longitude = 0.1006 }
        };

        geos.Add(new Way
        {
            Id = 2000,
            Nodes = [1, 2, 3],
            Tags = new TagsCollection
            {
                { "highway", "primary" },
                { "oneway", "yes" },
                { "name", "Main Corridor" }
            }
        });
        geos.Add(new Way
        {
            Id = 2001,
            Nodes = [3, 4],
            Tags = new TagsCollection
            {
                { "highway", "residential" },
                { "name", "Connector" }
            }
        });

        var network = new OsmToSimulationMapper().Map(geos, new OsmImportOptions(true, 100, ConnectivityMode: OsmConnectivityMode.Merge));

        var repairedEdge = Assert.Single(network.Edges, edge =>
            edge.Id.StartsWith("osm-way-2000-", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(edge.FromNodeId, "osm-node-1", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(edge.ToNodeId, "osm-node-2", StringComparison.OrdinalIgnoreCase));

        Assert.True(repairedEdge.IsBidirectional);
    }

    [Fact]
    public void Mapper_CullConnectivity_RemovesDisconnectedClusters()
    {
        var geos = new List<OsmGeo>
        {
            new Node { Id = 1, Latitude = 51.5000, Longitude = 0.1000 },
            new Node { Id = 2, Latitude = 51.5002, Longitude = 0.1002 },
            new Node { Id = 3, Latitude = 51.5100, Longitude = 0.1100 },
            new Node { Id = 4, Latitude = 51.5102, Longitude = 0.1102 }
        };

        geos.Add(new Way
        {
            Id = 3000,
            Nodes = [1, 2],
            Tags = new TagsCollection { { "highway", "residential" }, { "name", "Main Component" } }
        });
        geos.Add(new Way
        {
            Id = 3001,
            Nodes = [3, 4],
            Tags = new TagsCollection { { "highway", "residential" }, { "name", "Orphan Component" } }
        });

        var network = new OsmToSimulationMapper().Map(geos, new OsmImportOptions(true, 100, ConnectivityMode: OsmConnectivityMode.Cull));

        Assert.Equal(2, network.Nodes.Count);
        Assert.DoesNotContain(network.Nodes, node => string.Equals(node.Id, "osm-node-3", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(network.Nodes, node => string.Equals(node.Id, "osm-node-4", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Mapper_MergeConnectivity_BridgesDisconnectedClusters()
    {
        var geos = new List<OsmGeo>
        {
            new Node { Id = 1, Latitude = 51.5000, Longitude = 0.1000 },
            new Node { Id = 2, Latitude = 51.5002, Longitude = 0.1002 },
            new Node { Id = 3, Latitude = 51.5010, Longitude = 0.1010 },
            new Node { Id = 4, Latitude = 51.5012, Longitude = 0.1012 }
        };

        geos.Add(new Way
        {
            Id = 4000,
            Nodes = [1, 2],
            Tags = new TagsCollection { { "highway", "residential" }, { "name", "West Component" } }
        });
        geos.Add(new Way
        {
            Id = 4001,
            Nodes = [3, 4],
            Tags = new TagsCollection { { "highway", "residential" }, { "name", "East Component" } }
        });

        var network = new OsmToSimulationMapper().Map(geos, new OsmImportOptions(true, 100, ConnectivityMode: OsmConnectivityMode.Merge));

        Assert.Contains(network.Edges, edge => string.Equals(edge.RouteType, "synthetic-merge", StringComparison.OrdinalIgnoreCase) && edge.IsBidirectional);
        Assert.Single(GetWeakComponents(network));
    }

    [Fact]
    public void Mapper_EdgesAlwaysReferenceExistingNodes()
    {
        var geos = CreateLinearWayGeos(80);
        var network = new OsmToSimulationMapper().Map(geos, new OsmImportOptions(true, 5));
        var nodeIds = network.Nodes.Select(node => node.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(network.Edges, edge =>
        {
            Assert.Contains(edge.FromNodeId, nodeIds);
            Assert.Contains(edge.ToNodeId, nodeIds);
        });
    }

    [Fact]
    public void Mapper_RetentionHundred_PreservesAllRoadNodes()
    {
        var geos = CreateLinearWayGeos(40);
        var network = new OsmToSimulationMapper().Map(geos, new OsmImportOptions(true, 100));

        Assert.Equal(40, network.Nodes.Count);
    }

    [Fact]
    public void Mapper_PreserveConnectivity_DoesNotKeepDegreeTwoArticulationNodesByDefault()
    {
        var geos = CreateLinearWayGeos(60);
        var network = new OsmToSimulationMapper().Map(geos, new OsmImportOptions(true, 10, PreserveConnectivity: true));

        Assert.InRange(network.Nodes.Count, 2, 15);
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

        Assert.False(string.IsNullOrWhiteSpace(vm.OsmValidationMessage));
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

        renderer.Render(
            canvas,
            new GraphScene(),
            new GraphViewport(),
            new GraphSize(800, 480),
            new Dictionary<string, MapGeoCoordinate>(),
            showBackground: true,
            new MapCameraState(51.5074, -0.1278, 0.0015, false),
            overlay,
            out _);
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

    private static IReadOnlyList<OsmGeo> CreateLinearWayGeos(int nodeCount)
    {
        var geos = new List<OsmGeo>();
        for (long i = 1; i <= nodeCount; i++)
        {
            geos.Add(new Node
            {
                Id = i,
                Latitude = 40d + (i * 0.001d),
                Longitude = -74d + (i * 0.001d)
            });
        }

        geos.Add(new Way
        {
            Id = 100,
            Nodes = Enumerable.Range(1, nodeCount).Select(value => (long)value).ToArray(),
            Tags = new TagsCollection { { "highway", "residential" }, { "name", "Linear Way" } }
        });

        return geos;
    }

    private static IReadOnlyList<OsmGeo> CreateCrossRoadGeos()
    {
        var geos = new List<OsmGeo>
        {
            new Node { Id = 1, Latitude = 41.0000, Longitude = -73.0000 },
            new Node { Id = 2, Latitude = 41.0010, Longitude = -73.0000 },
            new Node { Id = 3, Latitude = 41.0020, Longitude = -73.0000 },
            new Node { Id = 4, Latitude = 41.0030, Longitude = -73.0000 },
            new Node { Id = 5, Latitude = 41.0020, Longitude = -73.0010 },
            new Node { Id = 6, Latitude = 41.0020, Longitude = -72.9990 }
        };

        geos.Add(new Way
        {
            Id = 201,
            Nodes = [1, 2, 3, 4],
            Tags = new TagsCollection { { "highway", "residential" }, { "name", "North South" } }
        });
        geos.Add(new Way
        {
            Id = 202,
            Nodes = [5, 3, 6],
            Tags = new TagsCollection { { "highway", "residential" }, { "name", "East West" } }
        });

        return geos;
    }

    private static double SumPathDistance(IReadOnlyList<OsmGeo> geos, long startNodeId, long endNodeId)
    {
        var nodes = geos.OfType<Node>().ToDictionary(node => node.Id!.Value);
        var total = 0d;
        for (var id = startNodeId + 1; id <= endNodeId; id++)
        {
            total += HaversineKm(nodes[id - 1], nodes[id]);
        }

        return total;
    }

    private static double HaversineKm(Node a, Node b)
    {
        const double radiusKm = 6371.0088d;
        var dLat = DegreesToRadians(b.Latitude!.Value - a.Latitude!.Value);
        var dLon = DegreesToRadians(b.Longitude!.Value - a.Longitude!.Value);
        var lat1 = DegreesToRadians(a.Latitude.Value);
        var lat2 = DegreesToRadians(b.Latitude.Value);
        var h = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2d) * Math.Sin(dLon / 2d);
        return 2d * radiusKm * Math.Asin(Math.Min(1d, Math.Sqrt(h)));
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180d;

    private static IReadOnlyList<HashSet<string>> GetWeakComponents(NetworkModel network)
    {
        var adjacency = network.Nodes.ToDictionary(node => node.Id, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var edge in network.Edges)
        {
            adjacency[edge.FromNodeId].Add(edge.ToNodeId);
            adjacency[edge.ToNodeId].Add(edge.FromNodeId);
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = new List<HashSet<string>>();
        foreach (var node in network.Nodes)
        {
            if (!visited.Add(node.Id))
            {
                continue;
            }

            var component = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { node.Id };
            var queue = new Queue<string>();
            queue.Enqueue(node.Id);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in adjacency[current])
                {
                    if (visited.Add(neighbor))
                    {
                        component.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    private sealed class FakeOsmApiClient(string xml) : IOsmApiClient
    {
        public Task<Stream> DownloadBoundingBoxAsync(OsmBoundingBox bbox, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
    }
}

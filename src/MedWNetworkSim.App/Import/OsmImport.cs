using System.Globalization;
using MedWNetworkSim.App.Geo;
using MedWNetworkSim.App.Models;
using OsmSharp;
using OsmSharp.Streams;

namespace MedWNetworkSim.App.Import;

public sealed class OsmImportException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

public enum OsmRetentionStrategy
{
    Balanced,
    PreserveShape,
    PreserveJunctionImportance
}

public sealed record OsmImportOptions(
    bool Simplify = true,
    int NodeRetentionPercentage = 10,
    OsmRetentionStrategy RetentionStrategy = OsmRetentionStrategy.Balanced,
    bool PreserveConnectivity = true,
    bool PreserveShapeAnchors = true)
{
    public int ValidatedNodeRetentionPercentage
    {
        get
        {
            if (NodeRetentionPercentage is < 1 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(NodeRetentionPercentage), "Choose a value between 1% and 100%.");
            }

            return NodeRetentionPercentage;
        }
    }
}

public interface IOsmParser
{
    bool CanParse(string path);
    IReadOnlyList<OsmGeo> Parse(string path);
}

public sealed class OsmXmlParser : IOsmParser
{
    public bool CanParse(string path) => path.EndsWith(".osm", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<OsmGeo> Parse(string path)
    {
        using var stream = File.OpenRead(path);
        return new XmlOsmStreamSource(stream).ToList();
    }
}

public sealed class OsmPbfParser : IOsmParser
{
    public bool CanParse(string path) =>
        path.EndsWith(".pbf", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".osm.pbf", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<OsmGeo> Parse(string path)
    {
        using var stream = File.OpenRead(path);
        return new PBFOsmStreamSource(stream).ToList();
    }
}

public sealed class OsmImportService(IReadOnlyList<IOsmParser> parsers, OsmToSimulationMapper mapper)
{
    public IOsmParser ResolveParser(string path) =>
        parsers.FirstOrDefault(parser => parser.CanParse(path))
        ?? throw new OsmImportException("Choose a .osm or .pbf OpenStreetMap file.");

    public NetworkModel ImportFromFile(string path, OsmImportOptions? options = null) =>
        mapper.Map(ResolveParser(path).Parse(path), options ?? new OsmImportOptions());
}

public sealed class OsmImporter
{
    private readonly OsmImportService service;

    public OsmImporter()
    {
        service = new OsmImportService([new OsmXmlParser(), new OsmPbfParser()], new OsmToSimulationMapper());
    }

    public NetworkModel ImportFromFile(string path, OsmImportOptions? options = null) =>
        service.ImportFromFile(path, options);

    public NetworkModel ImportFromGeos(IEnumerable<OsmGeo> geos, OsmImportOptions? options = null) =>
        new OsmToSimulationMapper().Map(geos, options ?? new OsmImportOptions());
}

public sealed class OsmToSimulationMapper
{
    private static readonly HashSet<string> SupportedHighways = new(StringComparer.OrdinalIgnoreCase)
    {
        "motorway", "trunk", "primary", "secondary", "tertiary", "unclassified", "residential",
        "motorway_link", "trunk_link", "primary_link", "secondary_link", "tertiary_link",
        "living_street", "service", "road"
    };

    public NetworkModel Map(IEnumerable<OsmGeo> geos, OsmImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(geos);
        ArgumentNullException.ThrowIfNull(options);
        var retention = options.ValidatedNodeRetentionPercentage;

        var nodes = new Dictionary<long, Node>( );
        var ways = new List<Way>();
        var relations = new List<Relation>();

        foreach (var geo in geos)
        {
            switch (geo)
            {
                case Node node when node.Id.HasValue:
                    nodes[node.Id.Value] = node;
                    break;
                case Way way when way.Id.HasValue && IsSupportedRoad(way):
                    ways.Add(way);
                    break;
                case Relation relation when relation.Id.HasValue:
                    relations.Add(relation);
                    break;
            }
        }

        if (ways.Count == 0)
        {
            throw new OsmImportException("No supported roads were found in the selected OSM data.");
        }

        var adjacency = BuildAdjacency(ways, nodes);
        var wayNamesByNode = BuildWayNamesByNode(ways);
        var mandatory = BuildMandatoryNodeSet(ways, nodes, adjacency, options.PreserveConnectivity);
        var allRoadNodeIds = ways
            .SelectMany(way => way.Nodes ?? [])
            .Where(nodes.ContainsKey)
            .Distinct()
            .ToList();
        var retained = BuildRetainedNodeSet(ways, nodes, adjacency, wayNamesByNode, mandatory, allRoadNodeIds, retention, options);
        var edgeModels = new List<EdgeModel>();
        var usedNodeIds = new HashSet<long>();
        var edgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var way in ways.OrderBy(way => way.Id.GetValueOrDefault()))
        {
            var rawIds = (way.Nodes ?? []).Where(nodes.ContainsKey).ToArray();
            if (rawIds.Length < 2)
            {
                continue;
            }

            var previousRetainedIndex = -1;
            for (var index = 0; index < rawIds.Length; index++)
            {
                if (!retained.Contains(rawIds[index]))
                {
                    continue;
                }

                if (previousRetainedIndex < 0)
                {
                    previousRetainedIndex = index;
                    continue;
                }

                var fromId = rawIds[previousRetainedIndex];
                var toId = rawIds[index];
                if (fromId != toId)
                {
                    var distance = CalculatePathDistanceKm(rawIds, previousRetainedIndex, index, nodes);
                    var edgeId = $"osm-way-{way.Id}-{fromId}-{toId}";
                    var key = $"{fromId}->{toId}";
                    if (edgeKeys.Add(key))
                    {
                        edgeModels.Add(new EdgeModel
                        {
                            Id = edgeId,
                            FromNodeId = ToNodeId(fromId),
                            ToNodeId = ToNodeId(toId),
                            Time = Math.Max(distance, 0.1d),
                            Cost = Math.Max(distance, 0.1d),
                            IsBidirectional = !IsOneway(way),
                            RouteType = GetTag(way, "highway"),
                            AccessNotes = BuildAccessNotes(way)
                        });
                        usedNodeIds.Add(fromId);
                        usedNodeIds.Add(toId);
                    }
                }

                previousRetainedIndex = index;
            }
        }

        var nodeModels = usedNodeIds
            .OrderBy(id => id)
            .Select(id => CreateNodeModel(id, nodes[id], wayNamesByNode))
            .ToList();
        InitializeProjectedNodePositions(nodeModels);

        if (nodeModels.Count == 0 || edgeModels.Count == 0)
        {
            throw new OsmImportException("No connected road network could be built from the selected OSM data.");
        }

        return new NetworkModel
        {
            Name = "Imported OSM network",
            Description = $"Imported from OpenStreetMap data with {retention.ToString(CultureInfo.InvariantCulture)}% target node retention ({nodeModels.Count.ToString(CultureInfo.InvariantCulture)} of {allRoadNodeIds.Count.ToString(CultureInfo.InvariantCulture)} road nodes kept).",
            TrafficTypes =
            [
                new TrafficTypeDefinition
                {
                    Name = "general",
                    RoutingPreference = RoutingPreference.TotalCost,
                    AllocationMode = AllocationMode.GreedyBestRoute
                }
            ],
            EdgeTrafficPermissionDefaults =
            [
                new EdgeTrafficPermissionRule { TrafficType = "general", Mode = EdgeTrafficPermissionMode.Permitted }
            ],
            LockLayoutToMap = nodeModels.Any(node => node.Latitude.HasValue && node.Longitude.HasValue),
            Nodes = nodeModels,
            Edges = edgeModels
        };
    }

    private static void InitializeProjectedNodePositions(IReadOnlyList<NodeModel> nodes)
    {
        var geoNodes = nodes.Where(node => node.Latitude.HasValue && node.Longitude.HasValue).ToList();
        if (geoNodes.Count == 0)
        {
            return;
        }

        var minLatitude = geoNodes.Min(node => node.Latitude!.Value);
        var maxLatitude = geoNodes.Max(node => node.Latitude!.Value);
        var minLongitude = geoNodes.Min(node => node.Longitude!.Value);
        var maxLongitude = geoNodes.Max(node => node.Longitude!.Value);
        var viewport = new GeoProjectionViewport(1600d, 900d, (minLatitude + maxLatitude) / 2d, (minLongitude + maxLongitude) / 2d, 1d);
        var projection = new WebMercatorProjectionService();
        var sw = projection.Project(new GeoCoordinate(minLatitude, minLongitude), viewport);
        var ne = projection.Project(new GeoCoordinate(maxLatitude, maxLongitude), viewport);
        var spanX = Math.Max(1d, Math.Abs(ne.X - sw.X));
        var spanY = Math.Max(1d, Math.Abs(ne.Y - sw.Y));
        var zoom = Math.Max(0.0001d, Math.Min((viewport.Width * 0.8d) / spanX, (viewport.Height * 0.8d) / spanY));
        viewport = viewport with { Zoom = zoom };

        foreach (var node in geoNodes)
        {
            var projected = projection.Project(new GeoCoordinate(node.Latitude!.Value, node.Longitude!.Value), viewport);
            node.X = projected.X;
            node.Y = projected.Y;
        }
    }

    private static bool IsSupportedRoad(Way way)
    {
        var highway = GetTag(way, "highway");
        return !string.IsNullOrWhiteSpace(highway) && SupportedHighways.Contains(highway);
    }

    private static Dictionary<long, HashSet<long>> BuildAdjacency(IEnumerable<Way> ways, IReadOnlyDictionary<long, Node> nodes)
    {
        var adjacency = new Dictionary<long, HashSet<long>>();
        foreach (var way in ways)
        {
            var ids = (way.Nodes ?? []).Where(nodes.ContainsKey).ToArray();
            for (var i = 1; i < ids.Length; i++)
            {
                if (ids[i - 1] == ids[i])
                {
                    continue;
                }

                Add(ids[i - 1], ids[i]);
                Add(ids[i], ids[i - 1]);
            }
        }

        return adjacency;

        void Add(long from, long to)
        {
            if (!adjacency.TryGetValue(from, out var set))
            {
                set = [];
                adjacency[from] = set;
            }

            set.Add(to);
        }
    }

    private static Dictionary<long, HashSet<string>> BuildWayNamesByNode(IEnumerable<Way> ways)
    {
        var names = new Dictionary<long, HashSet<string>>();
        foreach (var way in ways)
        {
            var name = GetTag(way, "name") ?? GetTag(way, "ref") ?? GetTag(way, "highway") ?? $"way-{way.Id}";
            foreach (var nodeId in way.Nodes ?? [])
            {
                if (!names.TryGetValue(nodeId, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    names[nodeId] = set;
                }

                set.Add(name);
            }
        }

        return names;
    }

    private static HashSet<long> BuildMandatoryNodeSet(
        IReadOnlyList<Way> ways,
        IReadOnlyDictionary<long, Node> nodes,
        IReadOnlyDictionary<long, HashSet<long>> adjacency,
        bool preserveConnectivity)
    {
        var mandatory = new HashSet<long>();
        foreach (var way in ways)
        {
            var ids = (way.Nodes ?? []).Where(nodes.ContainsKey).ToArray();
            if (ids.Length == 0)
            {
                continue;
            }

            mandatory.Add(ids[0]);
            mandatory.Add(ids[^1]);
        }

        foreach (var (nodeId, neighbors) in adjacency)
        {
            if (neighbors.Count >= 3 || neighbors.Count == 1)
            {
                mandatory.Add(nodeId);
            }

            if (nodes.TryGetValue(nodeId, out var node) && HasExplicitNodeName(node))
            {
                mandatory.Add(nodeId);
            }
        }

        if (preserveConnectivity)
        {
            foreach (var id in FindArticulationPoints(adjacency))
            {
                if (adjacency.TryGetValue(id, out var neighbors) && neighbors.Count >= 3)
                {
                    mandatory.Add(id);
                }
            }
        }

        return mandatory;
    }

    private static HashSet<long> BuildRetainedNodeSet(
        IReadOnlyList<Way> ways,
        IReadOnlyDictionary<long, Node> nodes,
        IReadOnlyDictionary<long, HashSet<long>> adjacency,
        IReadOnlyDictionary<long, HashSet<string>> wayNamesByNode,
        HashSet<long> mandatory,
        IReadOnlyList<long> allRoadNodeIds,
        int retentionPercentage,
        OsmImportOptions options)
    {
        if (!options.Simplify || retentionPercentage >= 100)
        {
            return allRoadNodeIds.ToHashSet();
        }

        var anchors = mandatory.Where(nodes.ContainsKey).ToHashSet();
        var retained = new HashSet<long>(anchors);
        var targetTotal = (int)Math.Ceiling(allRoadNodeIds.Count * (retentionPercentage / 100d));
        var minimumSafeTarget = Math.Min(allRoadNodeIds.Count, anchors.Count);
        targetTotal = Math.Clamp(targetTotal, minimumSafeTarget, allRoadNodeIds.Count);
        if (retained.Count >= targetTotal)
        {
            return retained;
        }

        var detailCandidates = allRoadNodeIds
            .Where(id => !retained.Contains(id))
            .OrderByDescending(id => ScoreReducibleNode(id, adjacency, wayNamesByNode, nodes, options))
            .ThenBy(id => id)
            .ToList();
        var remainingQuota = targetTotal - retained.Count;

        if (remainingQuota > 0 && options.RetentionStrategy != OsmRetentionStrategy.PreserveJunctionImportance)
        {
            var spacingCandidates = BuildEvenSpacingCandidatesByWay(ways, nodes, retained, remainingQuota, options.RetentionStrategy);
            foreach (var id in spacingCandidates)
            {
                if (remainingQuota <= 0)
                {
                    break;
                }

                if (retained.Add(id))
                {
                    remainingQuota--;
                }
            }
        }

        if (remainingQuota > 0)
        {
            foreach (var id in detailCandidates)
            {
                if (remainingQuota <= 0)
                {
                    break;
                }

                if (retained.Add(id))
                {
                    remainingQuota--;
                }
            }
        }

        if (remainingQuota > 0 && options.RetentionStrategy == OsmRetentionStrategy.PreserveJunctionImportance)
        {
            var spacingCandidates = BuildEvenSpacingCandidatesByWay(ways, nodes, retained, remainingQuota, options.RetentionStrategy);
            foreach (var id in spacingCandidates)
            {
                if (remainingQuota <= 0)
                {
                    break;
                }

                if (retained.Add(id))
                {
                    remainingQuota--;
                }
            }
        }

        return retained;
    }

    private static IReadOnlyList<long> BuildEvenSpacingCandidatesByWay(
        IReadOnlyList<Way> ways,
        IReadOnlyDictionary<long, Node> nodes,
        IReadOnlySet<long> retained,
        int remainingQuota,
        OsmRetentionStrategy strategy)
    {
        if (remainingQuota <= 0)
        {
            return [];
        }

        var reducibleByWay = ways
            .OrderBy(way => way.Id.GetValueOrDefault())
            .Select(way => (way, ids: (way.Nodes ?? []).Where(nodes.ContainsKey).Where(id => !retained.Contains(id)).ToArray()))
            .Where(item => item.ids.Length > 0)
            .ToList();
        var totalReducible = reducibleByWay.Sum(item => item.ids.Length);
        if (totalReducible == 0)
        {
            return [];
        }

        var allocations = new Dictionary<long, int>();
        var assigned = 0;
        foreach (var item in reducibleByWay)
        {
            var share = (int)Math.Floor((item.ids.Length / (double)totalReducible) * remainingQuota);
            var allocation = Math.Min(item.ids.Length, share);
            if (allocation <= 0 && remainingQuota > assigned && item.ids.Length >= 8)
            {
                allocation = 1;
            }

            allocations[item.way.Id!.Value] = allocation;
            assigned += allocation;
        }

        var stillAvailable = remainingQuota - assigned;
        if (stillAvailable > 0)
        {
            foreach (var item in reducibleByWay
                         .OrderByDescending(item => item.ids.Length)
                         .ThenBy(item => item.way.Id.GetValueOrDefault()))
            {
                if (stillAvailable <= 0)
                {
                    break;
                }

                var key = item.way.Id!.Value;
                if (allocations[key] >= item.ids.Length)
                {
                    continue;
                }

                allocations[key]++;
                stillAvailable--;
            }
        }

        var output = new List<long>();
        foreach (var item in reducibleByWay)
        {
            var wayId = item.way.Id!.Value;
            var keepCount = Math.Clamp(allocations[wayId], 0, item.ids.Length);
            if (keepCount == 0)
            {
                continue;
            }

            var selected = SelectEvenlySpaced(item.ids, keepCount);
            if (strategy == OsmRetentionStrategy.PreserveShape)
            {
                output.AddRange(selected);
            }
            else if (strategy == OsmRetentionStrategy.PreserveJunctionImportance)
            {
                output.AddRange(selected.OrderBy(id => id));
            }
            else
            {
                output.AddRange(selected.OrderBy(id => id % 3).ThenBy(id => id));
            }
        }

        return output;
    }

    private static IReadOnlyList<long> SelectEvenlySpaced(IReadOnlyList<long> ids, int keepCount)
    {
        if (keepCount >= ids.Count)
        {
            return ids.ToList();
        }

        var selected = new List<long>(keepCount);
        for (var i = 0; i < keepCount; i++)
        {
            var index = (int)Math.Round(((i + 1d) * (ids.Count + 1d) / (keepCount + 1d)) - 1d);
            selected.Add(ids[Math.Clamp(index, 0, ids.Count - 1)]);
        }

        return selected;
    }

    private static double ScoreReducibleNode(
        long id,
        IReadOnlyDictionary<long, HashSet<long>> adjacency,
        IReadOnlyDictionary<long, HashSet<string>> wayNamesByNode,
        IReadOnlyDictionary<long, Node> nodes,
        OsmImportOptions options)
    {
        _ = options;
        var degree = adjacency.TryGetValue(id, out var neighbors) ? neighbors.Count : 0;
        var score = 0d;
        if (degree >= 3)
        {
            score += 1000d;
        }
        else if (degree == 1)
        {
            score += 500d;
        }

        if (nodes.TryGetValue(id, out var node) && HasExplicitNodeName(node))
        {
            score += 100d;
        }

        if (wayNamesByNode.TryGetValue(id, out var names) && names.Count > 1)
        {
            score += 30d;
        }

        score += degree;
        score += (id % 1000) / 1000000d;
        return score;
    }

    private static bool HasExplicitNodeName(Node node) =>
        !string.IsNullOrWhiteSpace(GetTag(node, "name")) ||
        !string.IsNullOrWhiteSpace(GetTag(node, "ref")) ||
        !string.IsNullOrWhiteSpace(GetTag(node, "junction:name")) ||
        !string.IsNullOrWhiteSpace(GetTag(node, "official_name"));

    private static IReadOnlySet<long> FindArticulationPoints(IReadOnlyDictionary<long, HashSet<long>> adjacency)
    {
        var visited = new HashSet<long>();
        var discovery = new Dictionary<long, int>();
        var low = new Dictionary<long, int>();
        var parent = new Dictionary<long, long>();
        var articulation = new HashSet<long>();
        var time = 0;

        foreach (var root in adjacency.Keys.OrderBy(id => id))
        {
            if (visited.Contains(root))
            {
                continue;
            }

            var rootChildren = 0;
            var stack = new Stack<(long Node, IEnumerator<long> Children, bool Entered)>();
            stack.Push((root, adjacency[root].OrderBy(id => id).GetEnumerator(), false));
            parent[root] = long.MinValue;
            while (stack.Count > 0)
            {
                var frame = stack.Pop();
                if (!frame.Entered)
                {
                    visited.Add(frame.Node);
                    discovery[frame.Node] = low[frame.Node] = ++time;
                    stack.Push((frame.Node, frame.Children, true));
                    continue;
                }

                if (frame.Children.MoveNext())
                {
                    var child = frame.Children.Current;
                    stack.Push(frame);
                    if (!visited.Contains(child))
                    {
                        parent[child] = frame.Node;
                        if (frame.Node == root)
                        {
                            rootChildren++;
                        }

                        stack.Push((child, adjacency[child].OrderBy(id => id).GetEnumerator(), false));
                    }
                    else if (parent.GetValueOrDefault(frame.Node, long.MinValue) != child)
                    {
                        low[frame.Node] = Math.Min(low[frame.Node], discovery[child]);
                    }
                }
                else
                {
                    if (parent.TryGetValue(frame.Node, out var p) && p != long.MinValue)
                    {
                        low[p] = Math.Min(low[p], low[frame.Node]);
                        if (parent.GetValueOrDefault(p, long.MinValue) != long.MinValue && low[frame.Node] >= discovery[p] && adjacency[p].Count != 2)
                        {
                            articulation.Add(p);
                        }
                    }
                }
            }

            if (rootChildren > 1 && adjacency[root].Count != 2)
            {
                articulation.Add(root);
            }
        }

        return articulation;
    }

    private static NodeModel CreateNodeModel(long id, Node node, IReadOnlyDictionary<long, HashSet<string>> wayNamesByNode)
    {
        var directName = GetTag(node, "name") ?? GetTag(node, "ref") ?? GetTag(node, "junction:name") ?? GetTag(node, "official_name");
        var derivedName = wayNamesByNode.TryGetValue(id, out var names)
            ? string.Join(" / ", names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).Take(2))
            : null;
        var name = !string.IsNullOrWhiteSpace(directName)
            ? directName!
            : !string.IsNullOrWhiteSpace(derivedName)
                ? derivedName!
                : $"OSM {id}";

        return new NodeModel
        {
            Id = ToNodeId(id),
            Name = name,
            Latitude = node.Latitude,
            Longitude = node.Longitude,
            OsmId = id.ToString(CultureInfo.InvariantCulture),
            OsmName = directName,
            OsmHighwayType = GetTag(node, "highway"),
            PlaceType = "OSM road node",
            LoreDescription = $"Imported from OpenStreetMap node {id.ToString(CultureInfo.InvariantCulture)}."
        };
    }

    private static string ToNodeId(long osmId) => $"osm-node-{osmId.ToString(CultureInfo.InvariantCulture)}";

    private static double CalculatePathDistanceKm(IReadOnlyList<long> ids, int fromIndex, int toIndex, IReadOnlyDictionary<long, Node> nodes)
    {
        var total = 0d;
        for (var i = fromIndex + 1; i <= toIndex; i++)
        {
            total += HaversineKm(nodes[ids[i - 1]], nodes[ids[i]]);
        }

        return total;
    }

    private static double HaversineKm(Node a, Node b)
    {
        if (!a.Latitude.HasValue || !a.Longitude.HasValue || !b.Latitude.HasValue || !b.Longitude.HasValue)
        {
            return 0.1d;
        }

        const double radiusKm = 6371.0088d;
        var dLat = DegreesToRadians(b.Latitude.Value - a.Latitude.Value);
        var dLon = DegreesToRadians(b.Longitude.Value - a.Longitude.Value);
        var lat1 = DegreesToRadians(a.Latitude.Value);
        var lat2 = DegreesToRadians(b.Latitude.Value);
        var h = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2d) * Math.Sin(dLon / 2d);
        return 2d * radiusKm * Math.Asin(Math.Min(1d, Math.Sqrt(h)));
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180d;

    private static bool IsOneway(Way way)
    {
        var oneway = GetTag(way, "oneway");
        return string.Equals(oneway, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(oneway, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(oneway, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildAccessNotes(Way way)
    {
        var surface = GetTag(way, "surface");
        var access = GetTag(way, "access");
        return string.Join("; ", new[] { surface is null ? null : $"surface={surface}", access is null ? null : $"access={access}" }.Where(item => item is not null));
    }

    private static string? GetTag(OsmGeo geo, string key)
    {
        if (geo.Tags is null)
        {
            return null;
        }

        foreach (var tag in geo.Tags)
        {
            if (string.Equals(tag.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return tag.Value;
            }
        }

        return null;
    }
}

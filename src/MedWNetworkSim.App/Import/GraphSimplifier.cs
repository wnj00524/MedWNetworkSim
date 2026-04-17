namespace MedWNetworkSim.App.Import;

public sealed class GraphSimplifier
{
    public sealed record SimplifiedNode(
        long Id,
        double Latitude,
        double Longitude,
        OsmNameTags? NameTags,
        IReadOnlyList<string> ConnectedRoadLabels,
        bool IsTerminal);

    public sealed record SimplifiedEdge(
        long FromNodeId,
        long ToNodeId,
        string HighwayType,
        double CollapsedPathLengthKilometers,
        IReadOnlyList<long> RawPathNodeIds);

    public sealed record SimplifiedGraph(
        IReadOnlyDictionary<long, SimplifiedNode> Nodes,
        IReadOnlyList<SimplifiedEdge> Edges);

    private sealed record Segment(
        int Id,
        long FromNodeId,
        long ToNodeId,
        string HighwayType,
        double LengthKilometers,
        long? WayId,
        OsmNameTags? WayNameTags);

    /// <summary>
    /// Simplifies raw OSM road geometry so the simulation graph keeps only topology-significant nodes
    /// (junctions and terminals) while each simplified edge preserves true traversed distance.
    /// </summary>
    public SimplifiedGraph Simplify(OsmParsedGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var segments = graph.Edges
            .Select((edge, index) =>
            {
                var from = graph.Nodes[edge.FromNodeId];
                var to = graph.Nodes[edge.ToNodeId];
                return new Segment(
                    index,
                    edge.FromNodeId,
                    edge.ToNodeId,
                    edge.HighwayType,
                    CalculateDistanceKilometers(from.Latitude, from.Longitude, to.Latitude, to.Longitude),
                    edge.WayId,
                    edge.WayNameTags);
            })
            .ToList();

        var adjacency = BuildAdjacency(segments);
        var keepNodeIds = DetermineNodesToKeep(adjacency);

        var visitedSegments = new HashSet<int>();
        var simplifiedEdges = new List<SimplifiedEdge>();

        foreach (var startNodeId in keepNodeIds.OrderBy(id => id))
        {
            if (!adjacency.TryGetValue(startNodeId, out var incidentSegments))
            {
                continue;
            }

            foreach (var incidentSegment in incidentSegments.OrderBy(segment => segment.Id))
            {
                if (visitedSegments.Contains(incidentSegment.Id))
                {
                    continue;
                }

                var edge = WalkPath(startNodeId, incidentSegment, keepNodeIds, adjacency, visitedSegments);
                if (edge is not null)
                {
                    simplifiedEdges.Add(edge);
                }
            }
        }

        var nodeIdsInEdges = simplifiedEdges
            .SelectMany(edge => new[] { edge.FromNodeId, edge.ToNodeId })
            .Distinct()
            .ToHashSet();

        var simplifiedNodes = nodeIdsInEdges
            .ToDictionary(
                nodeId => nodeId,
                nodeId =>
                {
                    var source = graph.Nodes[nodeId];
                    var connectedLabels = BuildConnectedRoadLabels(nodeId, adjacency);
                    var degree = adjacency.TryGetValue(nodeId, out var segmentsAtNode) ? segmentsAtNode.Count : 0;
                    return new SimplifiedNode(
                        nodeId,
                        source.Latitude,
                        source.Longitude,
                        source.NameTags,
                        connectedLabels,
                        IsTerminalNode(nodeId, adjacency, degree));
                });

        return new SimplifiedGraph(simplifiedNodes, simplifiedEdges);
    }

    private static Dictionary<long, List<Segment>> BuildAdjacency(IReadOnlyList<Segment> segments)
    {
        var adjacency = new Dictionary<long, List<Segment>>();

        foreach (var segment in segments)
        {
            AddSegment(adjacency, segment.FromNodeId, segment);
            AddSegment(adjacency, segment.ToNodeId, segment);
        }

        return adjacency;
    }

    private static void AddSegment(Dictionary<long, List<Segment>> adjacency, long nodeId, Segment segment)
    {
        if (!adjacency.TryGetValue(nodeId, out var list))
        {
            list = [];
            adjacency[nodeId] = list;
        }

        list.Add(segment);
    }

    private static HashSet<long> DetermineNodesToKeep(Dictionary<long, List<Segment>> adjacency)
    {
        var keepNodeIds = adjacency
            .Where(pair => ShouldKeepNode(pair.Key, pair.Value))
            .Select(pair => pair.Key)
            .ToHashSet();

        if (keepNodeIds.Count > 0)
        {
            return keepNodeIds;
        }

        if (adjacency.Count > 0)
        {
            keepNodeIds.Add(adjacency.Keys.Min());
        }

        return keepNodeIds;
    }

    private static bool ShouldKeepNode(long nodeId, List<Segment> incidentSegments)
    {
        if (incidentSegments.Count != 2)
        {
            return true;
        }

        var distinctNeighbours = incidentSegments
            .Select(segment => GetOtherNode(segment, nodeId))
            .Distinct()
            .Count();

        // Keep degree-2 nodes when they are not a clean pass-through between two distinct neighbours.
        return distinctNeighbours != 2;
    }

    private static bool IsTerminalNode(long nodeId, Dictionary<long, List<Segment>> adjacency, int degree)
    {
        if (degree <= 1)
        {
            return true;
        }

        if (!adjacency.TryGetValue(nodeId, out var incidentSegments) || incidentSegments.Count != 2)
        {
            return false;
        }

        return incidentSegments.Select(segment => GetOtherNode(segment, nodeId)).Distinct().Count() < 2;
    }

    private static List<string> BuildConnectedRoadLabels(long nodeId, Dictionary<long, List<Segment>> adjacency)
    {
        if (!adjacency.TryGetValue(nodeId, out var segments))
        {
            return [];
        }

        return segments
            .Select(segment => PickRoadLabel(segment.WayNameTags))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
    }

    private static string? PickRoadLabel(OsmNameTags? tags)
    {
        if (tags is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(tags.Name))
        {
            return tags.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(tags.Ref))
        {
            return tags.Ref.Trim();
        }

        if (!string.IsNullOrWhiteSpace(tags.OfficialName))
        {
            return tags.OfficialName.Trim();
        }

        return null;
    }

    private static SimplifiedEdge? WalkPath(
        long startNodeId,
        Segment firstSegment,
        HashSet<long> keepNodeIds,
        Dictionary<long, List<Segment>> adjacency,
        HashSet<int> visitedSegments)
    {
        visitedSegments.Add(firstSegment.Id);

        var currentNodeId = GetOtherNode(firstSegment, startNodeId);
        var previousNodeId = startNodeId;
        var totalLength = firstSegment.LengthKilometers;
        var roadClass = firstSegment.HighwayType;
        var rawPathNodes = new List<long> { startNodeId, currentNodeId };

        while (!keepNodeIds.Contains(currentNodeId))
        {
            if (!adjacency.TryGetValue(currentNodeId, out var currentSegments) || currentSegments.Count == 0)
            {
                break;
            }

            var nextSegment = currentSegments.FirstOrDefault(segment => !visitedSegments.Contains(segment.Id) && GetOtherNode(segment, currentNodeId) != previousNodeId)
                           ?? currentSegments.FirstOrDefault(segment => !visitedSegments.Contains(segment.Id));

            if (nextSegment is null)
            {
                break;
            }

            visitedSegments.Add(nextSegment.Id);
            previousNodeId = currentNodeId;
            currentNodeId = GetOtherNode(nextSegment, currentNodeId);
            rawPathNodes.Add(currentNodeId);
            totalLength += nextSegment.LengthKilometers;
            roadClass = SelectDominantRoadClass(roadClass, nextSegment.HighwayType);
        }

        if (startNodeId == currentNodeId)
        {
            return null;
        }

        return new SimplifiedEdge(startNodeId, currentNodeId, roadClass, Math.Max(totalLength, 0.001d), rawPathNodes);
    }

    private static long GetOtherNode(Segment segment, long nodeId)
    {
        return segment.FromNodeId == nodeId
            ? segment.ToNodeId
            : segment.FromNodeId;
    }

    private static string SelectDominantRoadClass(string left, string right)
    {
        return GetCapacityScore(right) > GetCapacityScore(left)
            ? right
            : left;
    }

    private static int GetCapacityScore(string highwayType)
    {
        return highwayType.Trim().ToLowerInvariant() switch
        {
            "motorway" => 100,
            "primary" => 60,
            "secondary" => 40,
            "tertiary" => 25,
            "residential" => 10,
            _ => 10
        };
    }

    private static double CalculateDistanceKilometers(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371d;

        var latDelta = ToRadians(lat2 - lat1);
        var lonDelta = ToRadians(lon2 - lon1);
        var a = Math.Sin(latDelta / 2d) * Math.Sin(latDelta / 2d) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(lonDelta / 2d) * Math.Sin(lonDelta / 2d);
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return earthRadiusKm * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}

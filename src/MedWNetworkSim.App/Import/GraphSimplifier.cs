namespace MedWNetworkSim.App.Import;

public sealed class GraphSimplifier
{
    public sealed record SimplifiedNode(long Id, double Latitude, double Longitude);

    public sealed record SimplifiedEdge(long FromNodeId, long ToNodeId, string HighwayType, double LengthKilometers);

    public sealed record SimplifiedGraph(
        IReadOnlyDictionary<long, SimplifiedNode> Nodes,
        IReadOnlyList<SimplifiedEdge> Edges);

    private sealed record Segment(int Id, long FromNodeId, long ToNodeId, string HighwayType, double LengthKilometers);

    public SimplifiedGraph Simplify(OsmParsedGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var segments = graph.Edges
            .Select((edge, index) =>
            {
                var from = graph.Nodes[edge.FromNodeId];
                var to = graph.Nodes[edge.ToNodeId];
                return new Segment(index, edge.FromNodeId, edge.ToNodeId, edge.HighwayType, CalculateDistanceKilometers(from.Latitude, from.Longitude, to.Latitude, to.Longitude));
            })
            .ToList();

        var adjacency = BuildAdjacency(segments);
        var keepNodeIds = DetermineNodesToKeep(adjacency);

        var visitedSegments = new HashSet<int>();
        var simplifiedEdges = new List<SimplifiedEdge>();

        foreach (var startNodeId in keepNodeIds)
        {
            if (!adjacency.TryGetValue(startNodeId, out var incidentSegments))
            {
                continue;
            }

            foreach (var incidentSegment in incidentSegments)
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
                    return new SimplifiedNode(nodeId, source.Latitude, source.Longitude);
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
            .Where(pair => pair.Value.Count != 2)
            .Select(pair => pair.Key)
            .ToHashSet();

        if (keepNodeIds.Count > 0)
        {
            return keepNodeIds;
        }

        if (adjacency.Keys.FirstOrDefault() is var fallbackNode && adjacency.Count > 0)
        {
            keepNodeIds.Add(fallbackNode);
        }

        return keepNodeIds;
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

        while (!keepNodeIds.Contains(currentNodeId))
        {
            if (!adjacency.TryGetValue(currentNodeId, out var currentSegments) || currentSegments.Count == 0)
            {
                break;
            }

            var nextSegment = currentSegments.FirstOrDefault(segment => !visitedSegments.Contains(segment.Id) && GetOtherNode(segment, currentNodeId) != previousNodeId);
            if (nextSegment is null)
            {
                nextSegment = currentSegments.FirstOrDefault(segment => !visitedSegments.Contains(segment.Id));
            }

            if (nextSegment is null)
            {
                break;
            }

            visitedSegments.Add(nextSegment.Id);
            previousNodeId = currentNodeId;
            currentNodeId = GetOtherNode(nextSegment, currentNodeId);
            totalLength += nextSegment.LengthKilometers;
            roadClass = SelectDominantRoadClass(roadClass, nextSegment.HighwayType);
        }

        if (startNodeId == currentNodeId)
        {
            return null;
        }

        return new SimplifiedEdge(startNodeId, currentNodeId, roadClass, Math.Max(totalLength, 0.001d));
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

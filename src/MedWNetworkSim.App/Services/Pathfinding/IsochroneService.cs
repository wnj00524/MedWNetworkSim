using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services.Pathfinding;

/// <summary>
/// Provides pathfinding services to calculate isochrones (reachable areas from a point within a certain time or cost threshold).
/// Useful for determining coverage, accessibility, and potential bottlenecks radiating from key nodes in the network graph.
/// </summary>
public sealed class IsochroneService
{
    /// <summary>
    /// Specifies the cost metric.
    /// </summary>
    public enum CostMetric
    {
        Time
    }

    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    /// <summary>
    /// Executes the compute isochrone operation.
    /// </summary>

    public HashSet<NodeModel> ComputeIsochrone(
        NodeModel origin,
        double maxCost,
        IReadOnlyList<NodeModel> nodes,
        IReadOnlyList<EdgeModel> edges,
        CostMetric metric,
        out Dictionary<string, double> distances)
    {
        distances = new Dictionary<string, double>(Comparer);
        var nodeIndexById = new Dictionary<string, int>(nodes.Count, Comparer);
        var nodeIdsByIndex = new string[nodes.Count];
        for (var index = 0; index < nodes.Count; index++)
        {
            var nodeId = nodes[index].Id;
            if (!string.IsNullOrWhiteSpace(nodeId) && !nodeIndexById.ContainsKey(nodeId))
            {
                nodeIndexById[nodeId] = index;
                nodeIdsByIndex[index] = nodeId;
            }
        }

        if (string.IsNullOrWhiteSpace(origin.Id) || !nodeIndexById.TryGetValue(origin.Id, out var originIndex))
        {
            return [];
        }

        var adjacency = BuildAdjacency(edges, metric, nodeIndexById);
        var distanceByIndex = new double[nodes.Count];
        var visited = new bool[nodes.Count];
        Array.Fill(distanceByIndex, double.PositiveInfinity);
        var queue = new PriorityQueue<int, double>();
        distanceByIndex[originIndex] = 0d;
        queue.Enqueue(originIndex, 0d);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (visited[current])
            {
                continue;
            }

            visited[current] = true;

            var outgoing = adjacency[current];
            if (outgoing.Length == 0)
            {
                continue;
            }

            for (var segmentIndex = 0; segmentIndex < outgoing.Length; segmentIndex++)
            {
                var segment = outgoing[segmentIndex];
                var next = segment.TargetNodeIndex;
                var newCost = distanceByIndex[current] + segment.TravelTime;
                if (newCost > maxCost)
                {
                    continue;
                }

                if (newCost < distanceByIndex[next])
                {
                    distanceByIndex[next] = newCost;
                    queue.Enqueue(next, newCost);
                }
            }
        }

        var reachable = new HashSet<NodeModel>();
        for (var index = 0; index < visited.Length; index++)
        {
            if (!visited[index])
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(nodeIdsByIndex[index]))
            {
                distances[nodeIdsByIndex[index]] = distanceByIndex[index];
                reachable.Add(nodes[index]);
            }
        }

        return reachable;
    }

    private static Segment[][] BuildAdjacency(
        IReadOnlyList<EdgeModel> edges,
        CostMetric metric,
        IReadOnlyDictionary<string, int> nodeIndexById)
    {
        var result = new List<Segment>[nodeIndexById.Count];

        foreach (var edge in edges)
        {
            if (string.IsNullOrWhiteSpace(edge.FromNodeId) ||
                string.IsNullOrWhiteSpace(edge.ToNodeId) ||
                !nodeIndexById.TryGetValue(edge.FromNodeId, out var fromIndex) ||
                !nodeIndexById.TryGetValue(edge.ToNodeId, out var toIndex))
            {
                continue;
            }

            var cost = metric switch
            {
                CostMetric.Time => Math.Max(0d, edge.Time),
                _ => Math.Max(0d, edge.Time)
            };
            AddArc(fromIndex, toIndex, cost, result);
            if (edge.IsBidirectional)
            {
                AddArc(toIndex, fromIndex, cost, result);
            }
        }

        var adjacency = new Segment[nodeIndexById.Count][];
        for (var index = 0; index < adjacency.Length; index++)
        {
            adjacency[index] = result[index] is { Count: > 0 } outgoing ? [.. outgoing] : [];
        }

        return adjacency;
    }

    private static void AddArc(int fromIndex, int toIndex, double travelTime, IList<Segment>[] adjacency)
    {
        var edges = adjacency[fromIndex];
        if (edges is null)
        {
            edges = [];
            adjacency[fromIndex] = edges;
        }

        edges.Add(new Segment(toIndex, travelTime));
    }
    /// <summary>
    /// Represents the segment component.
    /// </summary>

    private readonly record struct Segment(int TargetNodeIndex, double TravelTime);
}

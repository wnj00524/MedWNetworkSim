using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services.Pathfinding;

/// <summary>
/// Computes nodes reachable from an origin within a maximum travel-time threshold.
/// </summary>
public sealed class IsochroneService
{
    public enum CostMetric
    {
        Time
    }

    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public HashSet<NodeModel> ComputeIsochrone(
        NodeModel origin,
        double maxCost,
        IReadOnlyList<NodeModel> nodes,
        IReadOnlyList<EdgeModel> edges,
        CostMetric metric,
        out Dictionary<string, double> distances)
    {
        distances = new Dictionary<string, double>(Comparer);
        var nodesById = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, node => node, Comparer);
        var adjacency = BuildAdjacency(edges, metric);

        var visited = new HashSet<string>(Comparer);
        var queue = new PriorityQueue<string, double>();

        if (string.IsNullOrWhiteSpace(origin.Id) || !nodesById.ContainsKey(origin.Id))
        {
            return [];
        }

        distances[origin.Id] = 0d;
        queue.Enqueue(origin.Id, 0d);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            if (!adjacency.TryGetValue(current, out var outgoing))
            {
                continue;
            }

            foreach (var segment in outgoing)
            {
                var next = segment.Target;
                if (!nodesById.ContainsKey(next))
                {
                    continue;
                }

                var newCost = distances[current] + segment.TravelTime;
                if (newCost > maxCost)
                {
                    continue;
                }

                if (!distances.ContainsKey(next) || newCost < distances[next])
                {
                    distances[next] = newCost;
                    queue.Enqueue(next, newCost);
                }
            }
        }

        return visited
            .Where(nodesById.ContainsKey)
            .Select(nodeId => nodesById[nodeId])
            .ToHashSet();
    }

    private static Dictionary<string, List<Segment>> BuildAdjacency(IReadOnlyList<EdgeModel> edges, CostMetric metric)
    {
        var result = new Dictionary<string, List<Segment>>(Comparer);

        foreach (var edge in edges)
        {
            if (string.IsNullOrWhiteSpace(edge.FromNodeId) || string.IsNullOrWhiteSpace(edge.ToNodeId))
            {
                continue;
            }

            var cost = metric switch
            {
                CostMetric.Time => Math.Max(0d, edge.Time),
                _ => Math.Max(0d, edge.Time)
            };
            AddArc(edge.FromNodeId, edge.ToNodeId, cost, result);
            if (edge.IsBidirectional)
            {
                AddArc(edge.ToNodeId, edge.FromNodeId, cost, result);
            }
        }

        return result;
    }

    private static void AddArc(string from, string to, double travelTime, IDictionary<string, List<Segment>> adjacency)
    {
        if (!adjacency.TryGetValue(from, out var edges))
        {
            edges = [];
            adjacency[from] = edges;
        }

        edges.Add(new Segment(to, travelTime));
    }

    private sealed record Segment(string Target, double TravelTime);
}

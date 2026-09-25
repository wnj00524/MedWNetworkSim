using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services.Pathfinding;
/// <summary>
/// Represents the multi origin isochrone origin component.
/// </summary>

public sealed class MultiOriginIsochroneOrigin
{
    /// <summary>
    /// Gets or sets the origin.
    /// </summary>
    public required NodeModel Origin { get; init; }
    /// <summary>
    /// Gets or sets the max cost.
    /// </summary>
    public required double MaxCost { get; init; }
}
/// <summary>
/// Provides business logic and operations related to multi origin isochrone.
/// </summary>

public sealed class MultiOriginIsochroneService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    /// <summary>
    /// Executes the compute operation.
    /// </summary>

    public MultiOriginIsochroneResult Compute(
        IReadOnlyCollection<NodeModel> allNodes,
        IReadOnlyCollection<EdgeModel> edges,
        IReadOnlyCollection<NodeModel> origins,
        double maxCost)
    {
        var originRequests = origins
            .Select(origin => new MultiOriginIsochroneOrigin
            {
                Origin = origin,
                MaxCost = maxCost
            })
            .ToList();
        return Compute(allNodes, edges, originRequests);
    }
    /// <summary>
    /// Executes the compute operation.
    /// </summary>

    public MultiOriginIsochroneResult Compute(
        IReadOnlyCollection<NodeModel> allNodes,
        IReadOnlyCollection<EdgeModel> edges,
        IReadOnlyCollection<MultiOriginIsochroneOrigin> origins)
    {
        var nodesById = new Dictionary<string, NodeModel>(allNodes.Count, Comparer);
        foreach (var node in allNodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Id))
            {
                nodesById[node.Id] = node;
            }
        }

        var validOrigins = new List<MultiOriginIsochroneOrigin>(origins.Count);
        var validOriginsSeen = new HashSet<string>(Comparer);
        foreach (var origin in origins)
        {
            if (!string.IsNullOrWhiteSpace(origin.Origin.Id) && nodesById.ContainsKey(origin.Origin.Id) && validOriginsSeen.Add(origin.Origin.Id))
            {
                validOrigins.Add(origin);
            }
        }

        if (validOrigins.Count == 0)
        {
            return new MultiOriginIsochroneResult
            {
                BestCostByNode = new Dictionary<NodeModel, double>(),
                BestOriginByNode = new Dictionary<NodeModel, NodeModel>(),
                CoveringOriginsByNode = new Dictionary<NodeModel, IReadOnlyList<NodeModel>>(),
                ReachableNodes = [],
                UncoveredNodes = allNodes.Where(node => !string.IsNullOrWhiteSpace(node.Id)).ToList(),
                OverlapNodes = []
            };
        }

        var bestCostById = new Dictionary<string, double>(Comparer);
        var bestOriginById = new Dictionary<string, string>(Comparer);
        var coveringOriginsById = new Dictionary<string, HashSet<string>>(Comparer);
        var adjacency = BuildAdjacency(edges);

        foreach (var originRequest in validOrigins)
        {
            var origin = originRequest.Origin;
            var originId = origin.Id;
            var costs = ComputeCostMap(originId, adjacency, Math.Max(0d, originRequest.MaxCost));
            foreach (var (nodeId, cost) in costs)
            {
                if (!coveringOriginsById.TryGetValue(nodeId, out var coveringOrigins))
                {
                    coveringOrigins = new HashSet<string>(Comparer);
                    coveringOriginsById[nodeId] = coveringOrigins;
                }

                coveringOrigins.Add(originId);
                if (!bestCostById.TryGetValue(nodeId, out var currentBest) || cost < currentBest)
                {
                    bestCostById[nodeId] = cost;
                    bestOriginById[nodeId] = originId;
                }
            }
        }

        var bestCostByNode = new Dictionary<NodeModel, double>(bestCostById.Count);
        foreach (var pair in bestCostById)
        {
            if (nodesById.TryGetValue(pair.Key, out var node))
            {
                bestCostByNode[node] = pair.Value;
            }
        }

        var bestOriginByNode = new Dictionary<NodeModel, NodeModel>(bestOriginById.Count);
        foreach (var pair in bestOriginById)
        {
            if (nodesById.TryGetValue(pair.Key, out var node) && nodesById.TryGetValue(pair.Value, out var originNode))
            {
                bestOriginByNode[node] = originNode;
            }
        }
        var coveringOriginsByNode = new Dictionary<NodeModel, IReadOnlyList<NodeModel>>(coveringOriginsById.Count);
        foreach (var pair in coveringOriginsById)
        {
            if (nodesById.TryGetValue(pair.Key, out var node))
            {
                var originsForNode = new List<NodeModel>(pair.Value.Count);
                foreach (var originId in pair.Value)
                {
                    if (nodesById.TryGetValue(originId, out var originNode))
                    {
                        originsForNode.Add(originNode);
                    }
                }
                coveringOriginsByNode[node] = originsForNode;
            }
        }

        var reachableNodes = bestCostByNode.Keys.ToHashSet();
        var uncoveredNodes = allNodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .Where(node => !reachableNodes.Contains(node))
            .ToList();
        var overlapNodes = coveringOriginsByNode
            .Where(pair => pair.Value.Count > 1)
            .Select(pair => pair.Key)
            .ToList();

        return new MultiOriginIsochroneResult
        {
            BestCostByNode = bestCostByNode,
            BestOriginByNode = bestOriginByNode,
            CoveringOriginsByNode = coveringOriginsByNode,
            ReachableNodes = reachableNodes,
            UncoveredNodes = uncoveredNodes,
            OverlapNodes = overlapNodes
        };
    }
    /// <summary>
    /// Executes the compute costs from origin operation.
    /// </summary>

    public IReadOnlyDictionary<NodeModel, double> ComputeCostsFromOrigin(
        NodeModel origin,
        IReadOnlyCollection<NodeModel> allNodes,
        IReadOnlyCollection<EdgeModel> edges,
        double maxCost)
    {
        var nodesById = new Dictionary<string, NodeModel>(allNodes.Count, Comparer);
        foreach (var node in allNodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Id))
            {
                nodesById[node.Id] = node;
            }
        }

        if (string.IsNullOrWhiteSpace(origin.Id) || !nodesById.ContainsKey(origin.Id))
        {
            return new Dictionary<NodeModel, double>();
        }

        var costMap = ComputeCostMap(origin.Id, BuildAdjacency(edges), Math.Max(0d, maxCost));
        var result = new Dictionary<NodeModel, double>(costMap.Count);
        foreach (var pair in costMap)
        {
            if (nodesById.TryGetValue(pair.Key, out var node))
            {
                result[node] = pair.Value;
            }
        }
        return result;
    }

    private static Dictionary<string, List<Segment>> BuildAdjacency(IReadOnlyCollection<EdgeModel> edges)
    {
        var adjacency = new Dictionary<string, List<Segment>>(Comparer);
        foreach (var edge in edges)
        {
            if (string.IsNullOrWhiteSpace(edge.FromNodeId) || string.IsNullOrWhiteSpace(edge.ToNodeId))
            {
                continue;
            }

            var weight = Math.Max(0d, edge.Time);
            AddArc(adjacency, edge.FromNodeId, edge.ToNodeId, weight);
            if (edge.IsBidirectional)
            {
                AddArc(adjacency, edge.ToNodeId, edge.FromNodeId, weight);
            }
        }

        return adjacency;
    }

    private static Dictionary<string, double> ComputeCostMap(
        string originId,
        IReadOnlyDictionary<string, List<Segment>> adjacency,
        double maxCost)
    {
        var bestCostByNode = new Dictionary<string, double>(Comparer)
        {
            [originId] = 0d
        };
        var queue = new PriorityQueue<string, double>();
        queue.Enqueue(originId, 0d);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var currentCost = bestCostByNode[currentId];
            if (currentCost > maxCost)
            {
                continue;
            }

            if (!adjacency.TryGetValue(currentId, out var outgoing))
            {
                continue;
            }

            foreach (var segment in outgoing)
            {
                var nextCost = currentCost + segment.TravelTime;
                if (nextCost > maxCost)
                {
                    continue;
                }

                if (!bestCostByNode.TryGetValue(segment.Target, out var existing) || nextCost < existing)
                {
                    bestCostByNode[segment.Target] = nextCost;
                    queue.Enqueue(segment.Target, nextCost);
                }
            }
        }

        return bestCostByNode;
    }

    private static void AddArc(IDictionary<string, List<Segment>> adjacency, string from, string to, double travelTime)
    {
        if (!adjacency.TryGetValue(from, out var segments))
        {
            segments = [];
            adjacency[from] = segments;
        }

        segments.Add(new Segment(to, travelTime));
    }
    /// <summary>
    /// Represents the segment component.
    /// </summary>

    private sealed record Segment(string Target, double TravelTime);
}

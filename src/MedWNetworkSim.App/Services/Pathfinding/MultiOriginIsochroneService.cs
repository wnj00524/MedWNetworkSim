using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services.Pathfinding;

public sealed class MultiOriginIsochroneService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public MultiOriginIsochroneResult Compute(
        IReadOnlyCollection<NodeModel> allNodes,
        IReadOnlyCollection<EdgeModel> edges,
        IReadOnlyCollection<NodeModel> origins,
        double maxCost)
    {
        var sanitizedMaxCost = Math.Max(0d, maxCost);
        var nodesById = allNodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, node => node, Comparer);
        var validOrigins = origins
            .Where(origin => !string.IsNullOrWhiteSpace(origin.Id) && nodesById.ContainsKey(origin.Id))
            .DistinctBy(origin => origin.Id, Comparer)
            .ToList();

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

        foreach (var origin in validOrigins)
        {
            var originId = origin.Id;
            var costs = ComputeCostMap(originId, adjacency, sanitizedMaxCost);
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

        var bestCostByNode = bestCostById
            .Where(pair => nodesById.ContainsKey(pair.Key))
            .ToDictionary(pair => nodesById[pair.Key], pair => pair.Value);
        var bestOriginByNode = bestOriginById
            .Where(pair => nodesById.ContainsKey(pair.Key) && nodesById.ContainsKey(pair.Value))
            .ToDictionary(pair => nodesById[pair.Key], pair => nodesById[pair.Value]);
        var coveringOriginsByNode = coveringOriginsById
            .Where(pair => nodesById.ContainsKey(pair.Key))
            .ToDictionary(
                pair => nodesById[pair.Key],
                pair => (IReadOnlyList<NodeModel>)pair.Value
                    .Where(nodesById.ContainsKey)
                    .Select(originId => nodesById[originId])
                    .ToList());

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

    public IReadOnlyDictionary<NodeModel, double> ComputeCostsFromOrigin(
        NodeModel origin,
        IReadOnlyCollection<NodeModel> allNodes,
        IReadOnlyCollection<EdgeModel> edges,
        double maxCost)
    {
        var nodesById = allNodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, node => node, Comparer);
        if (string.IsNullOrWhiteSpace(origin.Id) || !nodesById.ContainsKey(origin.Id))
        {
            return new Dictionary<NodeModel, double>();
        }

        var costMap = ComputeCostMap(origin.Id, BuildAdjacency(edges), Math.Max(0d, maxCost));
        return costMap
            .Where(pair => nodesById.ContainsKey(pair.Key))
            .ToDictionary(pair => nodesById[pair.Key], pair => pair.Value);
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

    private sealed record Segment(string Target, double TravelTime);
}

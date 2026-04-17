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

    public sealed record SimplificationStats(
        int BaselineNodeCount,
        int MandatoryKeptNodes,
        int OptionalKeptNodes,
        int RequestedPercentage,
        int EffectivePercentage);

    public sealed record SimplifiedGraph(
        IReadOnlyDictionary<long, SimplifiedNode> Nodes,
        IReadOnlyList<SimplifiedEdge> Edges,
        SimplificationStats? Stats = null);

    private sealed record Segment(
        int Id,
        long FromNodeId,
        long ToNodeId,
        string HighwayType,
        double LengthKilometers,
        long? WayId,
        OsmNameTags? WayNameTags);

    private sealed record BaselineGraph(
        IReadOnlyDictionary<long, SimplifiedNode> Nodes,
        IReadOnlyList<SimplifiedEdge> Edges,
        Dictionary<long, List<SimplifiedEdge>> Adjacency);

    private sealed record OptionalCandidate(
        long NodeId,
        int ChainId,
        int IndexInPath,
        double Score,
        double ShapeScore,
        bool IsNamedTransition);

    public SimplifiedGraph Simplify(OsmParsedGraph graph, GraphSimplificationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var simplificationOptions = options ?? new GraphSimplificationOptions();
        var rawSegments = BuildRawSegments(graph);
        var rawAdjacency = BuildAdjacency(rawSegments);

        var baseline = BaselineSimplify(graph, rawSegments, rawAdjacency);

        if (!simplificationOptions.EnableNodeRetentionTarget)
        {
            return new SimplifiedGraph(
                baseline.Nodes,
                baseline.Edges,
                new SimplificationStats(
                    baseline.Nodes.Count,
                    baseline.Nodes.Count,
                    0,
                    100,
                    100));
        }

        return ApplyRetentionTarget(graph, baseline, rawAdjacency, simplificationOptions);
    }

    private SimplifiedGraph ApplyRetentionTarget(
        OsmParsedGraph rawGraph,
        BaselineGraph baseline,
        Dictionary<long, List<Segment>> rawAdjacency,
        GraphSimplificationOptions options)
    {
        var mandatoryNodeIds = DetermineMandatoryNodeIds(baseline);
        var requestedPercentage = options.TargetRetainedNodePercentage;
        var requestedNodes = Math.Max(1, (int)Math.Round(rawGraph.Nodes.Count * (requestedPercentage / 100d), MidpointRounding.AwayFromZero));

        var optionalCandidates = BuildOptionalCandidates(rawGraph, baseline, rawAdjacency, options);
        var selectedOptional = new HashSet<long>();

        // First pass: enforce named-road transitions when requested.
        if (options.AlwaysKeepNamedRoadTransitions)
        {
            foreach (var namedTransition in optionalCandidates.Where(candidate => candidate.IsNamedTransition))
            {
                selectedOptional.Add(namedTransition.NodeId);
            }
        }

        // Second pass: shape and spacing anchors per chain.
        foreach (var chain in baseline.Edges.Select((edge, index) => (edge, index)))
        {
            var chainCandidates = optionalCandidates
                .Where(candidate => candidate.ChainId == chain.index)
                .OrderBy(candidate => candidate.IndexInPath)
                .ToList();
            if (chainCandidates.Count == 0)
            {
                continue;
            }

            var requiredAnchors = DetermineChainAnchorMinimum(rawGraph, chain.edge, chainCandidates.Count, options);
            if (requiredAnchors <= 0)
            {
                continue;
            }

            foreach (var anchor in SelectAnchorsWithEvenSpacing(chainCandidates, requiredAnchors))
            {
                selectedOptional.Add(anchor.NodeId);
            }
        }

        var safeMinimum = mandatoryNodeIds.Count + selectedOptional.Count;
        var effectiveTargetNodeCount = Math.Clamp(requestedNodes, safeMinimum, rawGraph.Nodes.Count);
        var additionalBudget = Math.Max(0, effectiveTargetNodeCount - mandatoryNodeIds.Count - selectedOptional.Count);

        var remainingCandidates = optionalCandidates
            .Where(candidate => !selectedOptional.Contains(candidate.NodeId))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.IndexInPath)
            .ToList();

        foreach (var candidate in remainingCandidates)
        {
            if (additionalBudget <= 0)
            {
                break;
            }

            selectedOptional.Add(candidate.NodeId);
            additionalBudget--;
        }

        var retainedNodeIds = mandatoryNodeIds
            .Concat(selectedOptional)
            .ToHashSet();

        var retainedEdges = RebuildEdgesWithRetainedAnchors(rawGraph, baseline, retainedNodeIds);
        var filteredNodes = BuildNodesForRetainedGraph(rawGraph, rawAdjacency, retainedEdges);

        ValidateRetainedGraph(baseline, filteredNodes, retainedEdges);

        var optionalKeptCount = retainedNodeIds.Count - mandatoryNodeIds.Count;
        var effectivePercentage = (int)Math.Round((retainedNodeIds.Count * 100d) / Math.Max(1, rawGraph.Nodes.Count), MidpointRounding.AwayFromZero);

        return new SimplifiedGraph(
            filteredNodes,
            retainedEdges,
            new SimplificationStats(
                baseline.Nodes.Count,
                mandatoryNodeIds.Count,
                optionalKeptCount,
                requestedPercentage,
                effectivePercentage));
    }

    private static List<OptionalCandidate> BuildOptionalCandidates(
        OsmParsedGraph rawGraph,
        BaselineGraph baseline,
        Dictionary<long, List<Segment>> rawAdjacency,
        GraphSimplificationOptions options)
    {
        var candidates = new List<OptionalCandidate>();

        foreach (var chain in baseline.Edges.Select((edge, chainId) => (edge, chainId)))
        {
            var nodes = chain.edge.RawPathNodeIds;
            if (nodes.Count <= 2)
            {
                continue;
            }

            for (var i = 1; i < nodes.Count - 1; i++)
            {
                var nodeId = nodes[i];
                var localDegree = rawAdjacency.TryGetValue(nodeId, out var incident) ? incident.Count : 0;
                var hasRoadClassChange = DetectRoadClassChange(incident);
                var hasNameTransition = DetectNameTransition(incident);
                var shapeScore = CalculateShapeContribution(rawGraph, nodes, i);

                var score = shapeScore + localDegree * 2d;
                if (hasRoadClassChange)
                {
                    score += 4d;
                }

                if (hasNameTransition)
                {
                    score += options.AlwaysKeepNamedRoadTransitions ? 50d : 3d;
                }

                score += CalculateSpacingBonus(nodes.Count, i);
                score += options.RetentionStrategy switch
                {
                    OsmRetentionStrategy.PreserveShape => shapeScore * 2.5d,
                    OsmRetentionStrategy.PreserveJunctionImportance => localDegree * 3d,
                    _ => shapeScore + localDegree
                };

                candidates.Add(new OptionalCandidate(
                    nodeId,
                    chain.chainId,
                    i,
                    score,
                    shapeScore,
                    hasNameTransition));
            }
        }

        return candidates;
    }

    private static IEnumerable<OptionalCandidate> SelectAnchorsWithEvenSpacing(IReadOnlyList<OptionalCandidate> candidates, int targetCount)
    {
        if (targetCount >= candidates.Count)
        {
            return candidates;
        }

        var selected = new List<OptionalCandidate>();
        for (var index = 1; index <= targetCount; index++)
        {
            var fractional = index * (candidates.Count + 1d) / (targetCount + 1d);
            var anchorIndex = Math.Clamp((int)Math.Round(fractional, MidpointRounding.AwayFromZero) - 1, 0, candidates.Count - 1);
            selected.Add(candidates[anchorIndex]);
        }

        return selected
            .GroupBy(candidate => candidate.NodeId)
            .Select(group => group.First());
    }

    private static int DetermineChainAnchorMinimum(
        OsmParsedGraph rawGraph,
        SimplifiedEdge chain,
        int optionalNodeCount,
        GraphSimplificationOptions options)
    {
        if (optionalNodeCount == 0)
        {
            return 0;
        }

        var endpointsDistance = CalculateDistanceKilometersForNodes(rawGraph, chain.RawPathNodeIds[0], chain.RawPathNodeIds[^1]);
        var pathDistance = Math.Max(chain.CollapsedPathLengthKilometers, 0.0001d);
        var deviationRatio = pathDistance / Math.Max(endpointsDistance, 0.0001d);
        var shapeAnchors = deviationRatio > 1.04d ? 1 : 0;
        if (deviationRatio > 1.12d)
        {
            shapeAnchors = 2;
        }

        var longSegmentAnchors = 0;
        if (options.PreserveShapeOnLongSegments && pathDistance > 2d)
        {
            longSegmentAnchors = (int)Math.Floor(pathDistance / 2d);
        }

        return Math.Clamp(Math.Max(shapeAnchors, longSegmentAnchors), 0, optionalNodeCount);
    }

    private static IReadOnlyList<SimplifiedEdge> RebuildEdgesWithRetainedAnchors(
        OsmParsedGraph rawGraph,
        BaselineGraph baseline,
        HashSet<long> retainedNodeIds)
    {
        var edges = new List<SimplifiedEdge>();

        foreach (var chain in baseline.Edges)
        {
            var path = chain.RawPathNodeIds;
            var retainedOnPath = path
                .Select((nodeId, index) => (nodeId, index))
                .Where(item => retainedNodeIds.Contains(item.nodeId) || item.index == 0 || item.index == path.Count - 1)
                .ToList();

            for (var i = 0; i < retainedOnPath.Count - 1; i++)
            {
                var from = retainedOnPath[i];
                var to = retainedOnPath[i + 1];
                var pathSlice = path.Skip(from.index).Take(to.index - from.index + 1).ToList();
                var totalLength = 0d;

                for (var segmentIndex = 0; segmentIndex < pathSlice.Count - 1; segmentIndex++)
                {
                    totalLength += CalculateDistanceKilometersForNodes(rawGraph, pathSlice[segmentIndex], pathSlice[segmentIndex + 1]);
                }

                edges.Add(new SimplifiedEdge(
                    from.nodeId,
                    to.nodeId,
                    chain.HighwayType,
                    Math.Max(totalLength, 0.001d),
                    pathSlice));
            }
        }

        return edges;
    }

    private static Dictionary<long, SimplifiedNode> BuildNodesForRetainedGraph(
        OsmParsedGraph rawGraph,
        Dictionary<long, List<Segment>> rawAdjacency,
        IReadOnlyList<SimplifiedEdge> retainedEdges)
    {
        var nodeDegree = new Dictionary<long, int>();
        foreach (var edge in retainedEdges)
        {
            nodeDegree[edge.FromNodeId] = nodeDegree.GetValueOrDefault(edge.FromNodeId) + 1;
            nodeDegree[edge.ToNodeId] = nodeDegree.GetValueOrDefault(edge.ToNodeId) + 1;
        }

        return nodeDegree.Keys.ToDictionary(
            nodeId => nodeId,
            nodeId =>
            {
                var source = rawGraph.Nodes[nodeId];
                return new SimplifiedNode(
                    nodeId,
                    source.Latitude,
                    source.Longitude,
                    source.NameTags,
                    BuildConnectedRoadLabels(nodeId, rawAdjacency),
                    IsTerminalNode(nodeId, rawAdjacency, nodeDegree.GetValueOrDefault(nodeId)));
            });
    }

    private static void ValidateRetainedGraph(BaselineGraph baseline, IReadOnlyDictionary<long, SimplifiedNode> nodes, IReadOnlyList<SimplifiedEdge> edges)
    {
        var degree = nodes.Keys.ToDictionary(nodeId => nodeId, _ => 0);
        foreach (var edge in edges)
        {
            degree[edge.FromNodeId]++;
            degree[edge.ToNodeId]++;
        }

        if (degree.Values.Any(value => value == 0))
        {
            throw new InvalidOperationException("Retention validation failed: retained graph contains orphaned nodes.");
        }

        var baselineComponents = CountComponents(baseline.Nodes.Keys, baseline.Edges);
        var retainedComponents = CountComponents(nodes.Keys, edges);
        if (baselineComponents != retainedComponents)
        {
            throw new InvalidOperationException("Retention validation failed: retained graph connectivity changed.");
        }
    }

    private static int CountComponents(IEnumerable<long> nodeIds, IReadOnlyList<SimplifiedEdge> edges)
    {
        var nodeSet = nodeIds.ToHashSet();
        if (nodeSet.Count == 0)
        {
            return 0;
        }

        var adjacency = nodeSet.ToDictionary(nodeId => nodeId, _ => new List<long>());
        foreach (var edge in edges)
        {
            if (!adjacency.ContainsKey(edge.FromNodeId) || !adjacency.ContainsKey(edge.ToNodeId))
            {
                continue;
            }

            adjacency[edge.FromNodeId].Add(edge.ToNodeId);
            adjacency[edge.ToNodeId].Add(edge.FromNodeId);
        }

        var visited = new HashSet<long>();
        var components = 0;
        foreach (var nodeId in nodeSet)
        {
            if (!visited.Add(nodeId))
            {
                continue;
            }

            components++;
            var queue = new Queue<long>();
            queue.Enqueue(nodeId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var next in adjacency[current])
                {
                    if (visited.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        return components;
    }

    private static HashSet<long> DetermineMandatoryNodeIds(BaselineGraph baseline)
    {
        var mandatory = new HashSet<long>();

        foreach (var node in baseline.Nodes.Values)
        {
            var degree = baseline.Adjacency.TryGetValue(node.Id, out var incident) ? incident.Count : 0;
            if (node.IsTerminal || degree >= 3 || degree != 2)
            {
                mandatory.Add(node.Id);
            }
        }

        foreach (var articulation in FindArticulationPoints(baseline.Nodes.Keys, baseline.Edges))
        {
            mandatory.Add(articulation);
        }

        return mandatory;
    }

    private static HashSet<long> FindArticulationPoints(IEnumerable<long> nodeIds, IReadOnlyList<SimplifiedEdge> edges)
    {
        var graph = nodeIds.ToDictionary(nodeId => nodeId, _ => new List<long>());
        foreach (var edge in edges)
        {
            graph[edge.FromNodeId].Add(edge.ToNodeId);
            graph[edge.ToNodeId].Add(edge.FromNodeId);
        }

        var disc = new Dictionary<long, int>();
        var low = new Dictionary<long, int>();
        var parent = new Dictionary<long, long?>();
        var articulation = new HashSet<long>();
        var time = 0;

        foreach (var nodeId in graph.Keys)
        {
            if (!disc.ContainsKey(nodeId))
            {
                TarjanDfsIterative(nodeId, graph, disc, low, parent, articulation, ref time);
            }
        }

        return articulation;
    }

    private static void TarjanDfsIterative(
        long node,
        Dictionary<long, List<long>> graph,
        Dictionary<long, int> disc,
        Dictionary<long, int> low,
        Dictionary<long, long?> parent,
        HashSet<long> articulation,
        ref int time)
    {
        var stack = new Stack<TarjanFrame>();
        disc[node] = ++time;
        low[node] = disc[node];
        stack.Push(new TarjanFrame(node, parent.GetValueOrDefault(node)));

        while (stack.Count > 0)
        {
            var frame = stack.Peek();
            var neighbors = graph[frame.Node];

            if (frame.NextNeighborIndex < neighbors.Count)
            {
                var neighbor = neighbors[frame.NextNeighborIndex];
                frame.NextNeighborIndex++;

                if (!disc.ContainsKey(neighbor))
                {
                    frame.Children++;
                    parent[neighbor] = frame.Node;
                    disc[neighbor] = ++time;
                    low[neighbor] = disc[neighbor];
                    stack.Push(new TarjanFrame(neighbor, frame.Node));
                }
                else if (frame.ParentNode != neighbor)
                {
                    low[frame.Node] = Math.Min(low[frame.Node], disc[neighbor]);
                }

                continue;
            }

            stack.Pop();

            if (frame.ParentNode is null)
            {
                if (frame.Children > 1)
                {
                    articulation.Add(frame.Node);
                }

                continue;
            }

            var parentNode = frame.ParentNode.Value;
            low[parentNode] = Math.Min(low[parentNode], low[frame.Node]);
            if (low[frame.Node] >= disc[parentNode])
            {
                articulation.Add(parentNode);
            }
        }
    }

    private sealed class TarjanFrame(long node, long? parentNode)
    {
        public long Node { get; } = node;
        public long? ParentNode { get; } = parentNode;
        public int NextNeighborIndex { get; set; }
        public int Children { get; set; }
    }

    private static double CalculateSpacingBonus(int pathNodeCount, int index)
    {
        var midpoint = (pathNodeCount - 1) / 2d;
        var normalizedDistance = Math.Abs(index - midpoint) / Math.Max(midpoint, 1d);
        return 1d - normalizedDistance;
    }

    private static bool DetectRoadClassChange(IReadOnlyList<Segment>? incident)
    {
        if (incident is null || incident.Count < 2)
        {
            return false;
        }

        return incident.Select(segment => segment.HighwayType).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
    }

    private static bool DetectNameTransition(IReadOnlyList<Segment>? incident)
    {
        if (incident is null || incident.Count < 2)
        {
            return false;
        }

        return incident
            .Select(segment => PickRoadLabel(segment.WayNameTags))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > 1;
    }

    private static double CalculateShapeContribution(OsmParsedGraph rawGraph, IReadOnlyList<long> pathNodes, int index)
    {
        if (index <= 0 || index >= pathNodes.Count - 1)
        {
            return 0d;
        }

        var prev = rawGraph.Nodes[pathNodes[index - 1]];
        var current = rawGraph.Nodes[pathNodes[index]];
        var next = rawGraph.Nodes[pathNodes[index + 1]];

        var area = Math.Abs((prev.Longitude * (current.Latitude - next.Latitude)) +
                            (current.Longitude * (next.Latitude - prev.Latitude)) +
                            (next.Longitude * (prev.Latitude - current.Latitude)));

        var baseLength = CalculateDistanceKilometers(prev.Latitude, prev.Longitude, next.Latitude, next.Longitude);
        return area / Math.Max(baseLength, 0.0001d);
    }

    private static double CalculateDistanceKilometersForNodes(OsmParsedGraph graph, long fromNodeId, long toNodeId)
    {
        var from = graph.Nodes[fromNodeId];
        var to = graph.Nodes[toNodeId];
        return CalculateDistanceKilometers(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
    }

    private static BaselineGraph BaselineSimplify(
        OsmParsedGraph graph,
        IReadOnlyList<Segment> segments,
        Dictionary<long, List<Segment>> adjacency)
    {
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

        var simplifiedAdjacency = simplifiedNodes.Keys.ToDictionary(nodeId => nodeId, _ => new List<SimplifiedEdge>());
        foreach (var edge in simplifiedEdges)
        {
            simplifiedAdjacency[edge.FromNodeId].Add(edge);
            simplifiedAdjacency[edge.ToNodeId].Add(edge);
        }

        return new BaselineGraph(simplifiedNodes, simplifiedEdges, simplifiedAdjacency);
    }

    private static List<Segment> BuildRawSegments(OsmParsedGraph graph)
    {
        return graph.Edges
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

using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

/// <summary>
/// Simulates movement through a network, including route scoring, capacity sharing, and bid competition.
/// </summary>
public sealed class NetworkSimulationEngine
{
    private const double Epsilon = 0.000001d;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Runs the routing simulation for every traffic type present in the network.
    /// </summary>
    /// <param name="network">The network to simulate.</param>
    /// <returns>The per-traffic routing outcomes.</returns>
    public IReadOnlyList<TrafficSimulationOutcome> Simulate(NetworkModel network)
    {
        ArgumentNullException.ThrowIfNull(network);
        network = HierarchicalNetworkProjection.ProjectForSimulation(network);

        var hasRecipeDependencies = HasStaticRecipeDependencies(network);
        var contexts = MixedRoutingAllocator.BuildStaticContexts(network, applyLocalAllocations: !hasRecipeDependencies).ToList();
        var remainingCapacityByEdgeId = network.Edges.ToDictionary(
            edge => edge.Id,
            edge => edge.Capacity ?? double.PositiveInfinity,
            Comparer);
        var remainingTranshipmentCapacityByNodeId = network.Nodes.ToDictionary(
            node => node.Id,
            node => node.TranshipmentCapacity ?? double.PositiveInfinity,
            Comparer);
        var hasFiniteCapacities = network.Edges.Any(edge => edge.Capacity.HasValue) ||
            network.Nodes.Any(node => node.TranshipmentCapacity.HasValue);

        if (hasRecipeDependencies)
        {
            var contextsByTraffic = contexts.ToDictionary(context => context.TrafficType, context => context, Comparer);
            var allocationOrder = BuildStaticRecipeCostOrder(network, contexts.Select(context => context.TrafficType).ToList());
            var sourceUnitCosts = contexts.ToDictionary(
                context => context.TrafficType,
                _ => new Dictionary<string, double>(Comparer),
                Comparer);
            var landedUnitCosts = contexts.ToDictionary(
                context => context.TrafficType,
                _ => new Dictionary<string, double>(Comparer),
                Comparer);

            foreach (var trafficType in allocationOrder)
            {
                if (!contextsByTraffic.TryGetValue(trafficType, out var context))
                {
                    continue;
                }

                SetStaticSourceUnitCosts(context, sourceUnitCosts, landedUnitCosts);
                MixedRoutingAllocator.ApplyLocalAllocations(context, period: 0);
                MixedRoutingAllocator.Allocate(network, [context], remainingCapacityByEdgeId, remainingTranshipmentCapacityByNodeId);
                landedUnitCosts[context.TrafficType] = SummarizeLandedUnitCosts(context.Allocations);
            }
        }
        else
        {
            MixedRoutingAllocator.Allocate(network, contexts, remainingCapacityByEdgeId, remainingTranshipmentCapacityByNodeId);
        }

        foreach (var context in contexts)
        {
            var unusedSupply = context.Supply.Values.Sum(value => Math.Max(0d, value));
            var unmetDemand = context.Demand.Values.Sum(value => Math.Max(0d, value));

            if (unusedSupply > Epsilon)
            {
                context.Notes.Add($"Unused supply remains after routing: {unusedSupply:0.##} unit(s).");
            }

            if (unmetDemand > Epsilon)
            {
                context.Notes.Add($"Unmet demand remains after routing: {unmetDemand:0.##} unit(s).");
            }

            if (hasFiniteCapacities && (unusedSupply > Epsilon || unmetDemand > Epsilon))
            {
                context.Notes.Add("Shared edge or node transhipment capacity limits may have prevented additional routing.");
            }

            var totalBidCost = context.Allocations.Sum(allocation => allocation.BidCostPerUnit * allocation.Quantity);
            if (totalBidCost > Epsilon)
            {
                context.Notes.Add($"Capacity bidding added {totalBidCost:0.##} in extra movement cost.");
            }

            if (context.Allocations.Count == 0 && context.TotalProduction > Epsilon && context.TotalConsumption > Epsilon)
            {
                context.Notes.Add("No feasible producer-to-consumer routes were found with the current node roles, edge directions, capacities, and bidding rules.");
            }
        }

        return contexts
            .Select(context => new TrafficSimulationOutcome
            {
                TrafficType = context.TrafficType,
                RoutingPreference = context.RoutingPreference,
                AllocationMode = context.AllocationMode,
                TotalProduction = context.TotalProduction,
                TotalConsumption = context.TotalConsumption,
                TotalDelivered = context.Allocations.Sum(allocation => allocation.Quantity),
                UnusedSupply = context.Supply.Values.Sum(value => Math.Max(0d, value)),
                UnmetDemand = context.Demand.Values.Sum(value => Math.Max(0d, value)),
                Allocations = context.Allocations.ToList(),
                Notes = context.Notes.ToList()
            })
            .ToList();
    }

    /// <summary>
    /// Aggregates route allocations into landed-cost summaries for each consumer node and traffic type.
    /// </summary>
    /// <param name="outcomes">The traffic outcomes produced by <see cref="Simulate"/>.</param>
    /// <returns>The consumer cost summaries.</returns>
    public IReadOnlyList<ConsumerCostSummary> SummarizeConsumerCosts(IEnumerable<TrafficSimulationOutcome> outcomes)
    {
        return SummarizeConsumerCosts(outcomes.SelectMany(outcome => outcome.Allocations));
    }

    /// <summary>
    /// Aggregates route allocations into landed-cost summaries for each consumer node and traffic type.
    /// </summary>
    /// <param name="allocations">The route allocations to aggregate.</param>
    /// <returns>The consumer cost summaries.</returns>
    public IReadOnlyList<ConsumerCostSummary> SummarizeConsumerCosts(IEnumerable<RouteAllocation> allocations)
    {
        return allocations
            .GroupBy(allocation => new { allocation.TrafficType, allocation.ConsumerNodeId, allocation.ConsumerName })
            .Select(group =>
            {
                var localAllocations = group.Where(allocation => allocation.IsLocalSupply).ToList();
                var importedAllocations = group.Where(allocation => !allocation.IsLocalSupply).ToList();
                var localQuantity = localAllocations.Sum(allocation => allocation.Quantity);
                var importedQuantity = importedAllocations.Sum(allocation => allocation.Quantity);
                var totalMovementCost = group.Sum(allocation => allocation.TotalMovementCost);
                var totalQuantity = group.Sum(allocation => allocation.Quantity);

                return new ConsumerCostSummary
                {
                    TrafficType = group.Key.TrafficType,
                    ConsumerNodeId = group.Key.ConsumerNodeId,
                    ConsumerName = group.Key.ConsumerName,
                    LocalQuantity = localQuantity,
                    LocalUnitCost = CalculateAverageUnitCost(localAllocations),
                    ImportedQuantity = importedQuantity,
                    ImportedUnitCost = CalculateAverageUnitCost(importedAllocations),
                    BlendedUnitCost = totalQuantity > Epsilon ? totalMovementCost / totalQuantity : 0d,
                    TotalMovementCost = totalMovementCost
                };
            })
            .OrderBy(summary => summary.TrafficType, Comparer)
            .ThenBy(summary => summary.ConsumerName, Comparer)
            .ToList();
    }

    private static double GetRequiredInputPerOutputUnit(
    ProductionInputRequirement requirement,
    string outputTrafficType)
    {
        var inputQuantity = requirement.InputQuantity;
        var outputQuantity = requirement.OutputQuantity;

        if (inputQuantity <= Epsilon &&
            requirement.QuantityPerOutputUnit.HasValue &&
            requirement.QuantityPerOutputUnit.Value > Epsilon)
        {
            inputQuantity = requirement.QuantityPerOutputUnit.Value;
            outputQuantity = 1d;
        }

        if (double.IsNaN(inputQuantity) || double.IsInfinity(inputQuantity) || inputQuantity <= Epsilon ||
            double.IsNaN(outputQuantity) || double.IsInfinity(outputQuantity) || outputQuantity <= Epsilon)
        {
            throw new InvalidOperationException(
                $"Traffic '{outputTrafficType}' has an invalid production input ratio for precursor '{requirement.TrafficType}'.");
        }

        return inputQuantity / outputQuantity;
    }

    private static double CalculateAverageUnitCost(IReadOnlyCollection<RouteAllocation> allocations)
    {
        var quantity = allocations.Sum(allocation => allocation.Quantity);
        if (quantity <= Epsilon)
        {
            return 0d;
        }

        return allocations.Sum(allocation => allocation.TotalMovementCost) / quantity;
    }

    private static bool HasStaticRecipeDependencies(NetworkModel network)
    {
        return network.Nodes
            .SelectMany(node => node.TrafficProfiles)
            .Where(profile => profile.Production > Epsilon)
            .SelectMany(profile => profile.InputRequirements)
            .Any(requirement =>
                (requirement.InputQuantity > Epsilon && requirement.OutputQuantity > Epsilon) ||
                requirement.QuantityPerOutputUnit.GetValueOrDefault() > Epsilon);
    }

    private static List<string> BuildStaticRecipeCostOrder(NetworkModel network, IReadOnlyList<string> trafficTypes)
    {
        var originalIndex = trafficTypes
            .Select((trafficType, index) => new { trafficType, index })
            .ToDictionary(item => item.trafficType, item => item.index, Comparer);
        var graph = trafficTypes.ToDictionary(trafficType => trafficType, _ => new HashSet<string>(Comparer), Comparer);
        var indegree = trafficTypes.ToDictionary(trafficType => trafficType, _ => 0, Comparer);

        foreach (var profile in network.Nodes.SelectMany(node => node.TrafficProfiles).Where(profile => profile.Production > Epsilon))
        {
            if (!graph.ContainsKey(profile.TrafficType))
            {
                graph[profile.TrafficType] = [];
                indegree[profile.TrafficType] = 0;
                originalIndex[profile.TrafficType] = originalIndex.Count;
            }

            foreach (var requirement in profile.InputRequirements.Where(requirement =>
     (requirement.InputQuantity > Epsilon && requirement.OutputQuantity > Epsilon) ||
     requirement.QuantityPerOutputUnit.GetValueOrDefault() > Epsilon))
            {
                if (!graph.ContainsKey(requirement.TrafficType))
                {
                    graph[requirement.TrafficType] = [];
                    indegree[requirement.TrafficType] = 0;
                    originalIndex[requirement.TrafficType] = originalIndex.Count;
                }

                if (graph[requirement.TrafficType].Add(profile.TrafficType))
                {
                    indegree[profile.TrafficType]++;
                }
            }
        }

        var ready = indegree
            .Where(pair => pair.Value == 0)
            .Select(pair => pair.Key)
            .OrderBy(trafficType => originalIndex.GetValueOrDefault(trafficType, int.MaxValue))
            .ThenBy(trafficType => trafficType, Comparer)
            .ToList();
        var result = new List<string>(graph.Count);
        while (ready.Count > 0)
        {
            var trafficType = ready[0];
            ready.RemoveAt(0);
            result.Add(trafficType);

            foreach (var dependent in graph[trafficType]
                .OrderBy(item => originalIndex.GetValueOrDefault(item, int.MaxValue))
                .ThenBy(item => item, Comparer))
            {
                indegree[dependent]--;
                if (indegree[dependent] == 0)
                {
                    ready.Add(dependent);
                    ready = ready
                        .OrderBy(item => originalIndex.GetValueOrDefault(item, int.MaxValue))
                        .ThenBy(item => item, Comparer)
                        .ToList();
                }
            }
        }

        if (result.Count != graph.Count)
        {
            var cyclicTraffic = indegree
                .Where(pair => pair.Value > 0)
                .Select(pair => pair.Key)
                .OrderBy(item => item, Comparer);
            throw new InvalidOperationException($"Static inherited recipe cost propagation does not support cyclic recipe dependencies. Cycle includes: {string.Join(", ", cyclicTraffic)}.");
        }

        return result;
    }

    private static void SetStaticSourceUnitCosts(
        RoutingTrafficContext context,
        IReadOnlyDictionary<string, Dictionary<string, double>> sourceUnitCosts,
        IReadOnlyDictionary<string, Dictionary<string, double>> landedUnitCosts)
    {
        context.SupplyUnitCosts.Clear();
        foreach (var pair in context.Supply)
        {
            var nodeId = pair.Key;
            if (!context.ProfilesByNodeId.TryGetValue(nodeId, out var profile) || profile is null)
            {
                context.SupplyUnitCosts[nodeId] = 0d;
                continue;
            }

            var sourceUnitCost = CalculateStaticSourceUnitCost(profile, nodeId, landedUnitCosts);
            context.SupplyUnitCosts[nodeId] = sourceUnitCost;
            sourceUnitCosts[context.TrafficType][nodeId] = sourceUnitCost;
        }
    }

    private static double CalculateStaticSourceUnitCost(
    NodeTrafficProfile profile,
    string nodeId,
    IReadOnlyDictionary<string, Dictionary<string, double>> landedUnitCosts)
    {
        var requirements = profile.InputRequirements
            .Where(requirement =>
                (requirement.InputQuantity > Epsilon && requirement.OutputQuantity > Epsilon) ||
                requirement.QuantityPerOutputUnit.GetValueOrDefault() > Epsilon)
            .ToList();

        if (requirements.Count == 0)
        {
            return 0d;
        }

        var sourceUnitCost = 0d;
        foreach (var requirement in requirements)
        {
            var precursorUnitCost = landedUnitCosts.TryGetValue(requirement.TrafficType, out var costsByNode)
                ? costsByNode.GetValueOrDefault(nodeId)
                : 0d;

            sourceUnitCost += precursorUnitCost * GetRequiredInputPerOutputUnit(requirement, profile.TrafficType);
        }

        return sourceUnitCost;
    }

    private static Dictionary<string, double> SummarizeLandedUnitCosts(IEnumerable<RouteAllocation> allocations)
    {
        return allocations
            .GroupBy(allocation => allocation.ConsumerNodeId, Comparer)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var quantity = group.Sum(allocation => allocation.Quantity);
                    return quantity > Epsilon
                        ? group.Sum(allocation => allocation.DeliveredCostPerUnit * allocation.Quantity) / quantity
                        : 0d;
                },
                Comparer);
    }

    private static TrafficContext BuildContext(NetworkModel network, TrafficTypeDefinition definition)
    {
        // Each traffic type sees its own supply/demand profile, but references the same underlying node set.
        var profilesByNodeId = network.Nodes.ToDictionary(
            node => node.Id,
            node => node.TrafficProfiles.FirstOrDefault(profile => Comparer.Equals(profile.TrafficType, definition.Name)),
            Comparer);
        var nodesById = network.Nodes.ToDictionary(node => node.Id, node => node, Comparer);
        var supply = profilesByNodeId
            .Where(pair => pair.Value?.Production > Epsilon)
            .ToDictionary(pair => pair.Key, pair => pair.Value!.Production, Comparer);
        var demand = profilesByNodeId
            .Where(pair => pair.Value?.Consumption > Epsilon)
            .ToDictionary(pair => pair.Key, pair => pair.Value!.Consumption, Comparer);
        AddImplicitRecipeDemand(network, definition.Name, demand);

        return new TrafficContext(
            definition.Name,
            definition.RoutingPreference,
            definition.AllocationMode,
            GetCapacityBidPerUnit(definition),
            nodesById,
            profilesByNodeId,
            supply,
            demand,
            supply.Values.Sum(),
            demand.Values.Sum(),
            [],
            []);
    }

    private static void AddImplicitRecipeDemand(
     NetworkModel network,
     string trafficType,
     IDictionary<string, double> demand)
    {
        foreach (var node in network.Nodes)
        {
            var implicitDemand = 0d;
            foreach (var profile in node.TrafficProfiles.Where(profile => profile.Production > Epsilon))
            {
                implicitDemand += profile.InputRequirements
                    .Where(requirement => Comparer.Equals(requirement.TrafficType, trafficType))
                    .Sum(requirement => profile.Production * GetRequiredInputPerOutputUnit(requirement, profile.TrafficType));
            }

            if (implicitDemand <= Epsilon)
            {
                continue;
            }

            demand[node.Id] = (demand.TryGetValue(node.Id, out var existing) ? existing : 0d) + implicitDemand;
        }
    }

    private static void ApplyLocalAllocations(TrafficContext context)
    {
        foreach (var nodeId in context.Supply.Keys.Intersect(context.Demand.Keys, Comparer).ToList())
        {
            var quantity = Math.Min(context.Supply[nodeId], context.Demand[nodeId]);
            if (quantity <= Epsilon)
            {
                continue;
            }

            var node = context.NodesById[nodeId];
            context.Allocations.Add(new RouteAllocation
            {
                TrafficType = context.TrafficType,
                RoutingPreference = context.RoutingPreference,
                AllocationMode = context.AllocationMode,
                ProducerNodeId = nodeId,
                ProducerName = node.Name,
                ConsumerNodeId = nodeId,
                ConsumerName = node.Name,
                Quantity = quantity,
                IsLocalSupply = true,
                TotalTime = 0d,
                TotalCost = 0d,
                BidCostPerUnit = 0d,
                DeliveredCostPerUnit = 0d,
                TotalMovementCost = 0d,
                TotalScore = 0d,
                PathNodeNames = [node.Name],
                PathNodeIds = [nodeId],
                PathEdgeIds = []
            });

            context.Supply[nodeId] -= quantity;
            context.Demand[nodeId] -= quantity;
        }
    }

    private static void AllocateGreedyBestRoutes(
        IReadOnlyList<TrafficContext> contexts,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        while (true)
        {
            // Capacity bidding is resolved globally: every greedy traffic type competes for the next best route.
            var nextCandidate = contexts
                .SelectMany(context => BuildCandidateRoutes(context, adjacency, remainingCapacityByEdgeId, remainingTranshipmentCapacityByNodeId))
                .OrderByDescending(candidate => candidate.CapacityBidPerUnit)
                .ThenBy(candidate => candidate.TotalScore)
                .ThenBy(candidate => candidate.TotalTime)
                .ThenBy(candidate => candidate.TransitCostPerUnit)
                .ThenBy(candidate => candidate.ProducerNodeId, Comparer)
                .ThenBy(candidate => candidate.ConsumerNodeId, Comparer)
                .FirstOrDefault();

            if (nextCandidate is null)
            {
                break;
            }

            var context = nextCandidate.Context;
            var remainingSupply = context.Supply.TryGetValue(nextCandidate.ProducerNodeId, out var supplyValue) ? supplyValue : 0d;
            var remainingDemand = context.Demand.TryGetValue(nextCandidate.ConsumerNodeId, out var demandValue) ? demandValue : 0d;
            var quantity = Math.Min(remainingSupply, remainingDemand);
            var routeCapacity = GetRouteRemainingCapacity(
                nextCandidate.PathEdgeIds,
                nextCandidate.PathTranshipmentNodeIds,
                remainingCapacityByEdgeId,
                remainingTranshipmentCapacityByNodeId);

            if (!double.IsPositiveInfinity(routeCapacity))
            {
                quantity = Math.Min(quantity, routeCapacity);
            }

            if (quantity <= Epsilon)
            {
                break;
            }

            AddRouteAllocation(
                context,
                nextCandidate.ProducerNodeId,
                nextCandidate.ConsumerNodeId,
                quantity,
                isLocalSupply: false,
                nextCandidate.PathNodeIds,
                nextCandidate.PathEdgeIds,
                nextCandidate.PathTranshipmentNodeIds,
                nextCandidate.TotalTime,
                nextCandidate.TransitCostPerUnit,
                nextCandidate.TotalScore,
                nextCandidate.CapacityBidPerUnit,
                remainingCapacityByEdgeId,
                remainingTranshipmentCapacityByNodeId);

            context.Supply[nextCandidate.ProducerNodeId] -= quantity;
            context.Demand[nextCandidate.ConsumerNodeId] -= quantity;
        }
    }

    private static void AllocateProportionallyByBranchDemand(
        TrafficContext context,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        foreach (var producerNodeId in context.Supply.Keys.OrderBy(nodeId => nodeId, Comparer).ToList())
        {
            while (context.Supply.TryGetValue(producerNodeId, out var supply) && supply > Epsilon)
            {
                var delivered = AllocateProportionallyFromNode(
                    context,
                    producerNodeId,
                    producerNodeId,
                    supply,
                    [producerNodeId],
                    [],
                    [],
                    0d,
                    0d,
                    0d,
                    adjacency,
                    remainingCapacityByEdgeId,
                    remainingTranshipmentCapacityByNodeId);

                if (delivered <= Epsilon)
                {
                    break;
                }

                context.Supply[producerNodeId] -= delivered;
            }
        }
    }

    private static double AllocateProportionallyFromNode(
        TrafficContext context,
        string producerNodeId,
        string currentNodeId,
        double availableSupply,
        IReadOnlyList<string> pathNodeIds,
        IReadOnlyList<string> pathEdgeIds,
        IReadOnlyList<string> pathTranshipmentNodeIds,
        double totalTime,
        double transitCostPerUnit,
        double totalScore,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        var delivered = 0d;
        var supplyAtNode = availableSupply;

        if (context.Demand.TryGetValue(currentNodeId, out var localDemand) && localDemand > Epsilon)
        {
            var localQuantity = Math.Min(supplyAtNode, localDemand);
            var routeCapacity = GetRouteRemainingCapacity(pathEdgeIds, pathTranshipmentNodeIds, remainingCapacityByEdgeId, remainingTranshipmentCapacityByNodeId);
            if (!double.IsPositiveInfinity(routeCapacity))
            {
                localQuantity = Math.Min(localQuantity, routeCapacity);
            }

            if (localQuantity > Epsilon)
            {
                AddRouteAllocation(
                    context,
                    producerNodeId,
                    currentNodeId,
                    localQuantity,
                    isLocalSupply: pathEdgeIds.Count == 0,
                    pathNodeIds,
                    pathEdgeIds,
                    pathTranshipmentNodeIds,
                    totalTime,
                    transitCostPerUnit,
                    totalScore,
                    GetCapacityBidPerUnit(context, currentNodeId),
                    remainingCapacityByEdgeId,
                    remainingTranshipmentCapacityByNodeId);

                context.Demand[currentNodeId] -= localQuantity;
                supplyAtNode -= localQuantity;
                delivered += localQuantity;
            }
        }

        while (supplyAtNode > Epsilon)
        {
            var branches = BuildBranchDemandMap(
                context,
                producerNodeId,
                currentNodeId,
                adjacency,
                remainingCapacityByEdgeId,
                remainingTranshipmentCapacityByNodeId);
            var branchShares = AllocateAcrossBranchRoutes(supplyAtNode, branches);
            if (branchShares.Count == 0)
            {
                break;
            }

            var passDelivered = 0d;
            foreach (var share in branchShares)
            {
                var branch = share.Branch;
                var nextPathNodeIds = pathNodeIds.Concat([branch.ToNodeId]).ToList();
                var nextPathEdgeIds = pathEdgeIds.Concat([branch.EdgeId]).ToList();
                var nextPathTranshipmentNodeIds = pathTranshipmentNodeIds.ToList();
                if (!Comparer.Equals(currentNodeId, producerNodeId))
                {
                    nextPathTranshipmentNodeIds.Add(currentNodeId);
                }

                var branchDelivered = AllocateProportionallyFromNode(
                    context,
                    producerNodeId,
                    branch.ToNodeId,
                    share.Quantity,
                    nextPathNodeIds,
                    nextPathEdgeIds,
                    nextPathTranshipmentNodeIds,
                    totalTime + branch.Time,
                    transitCostPerUnit + branch.Cost,
                    totalScore + Score(branch.Time, branch.Cost, context.RoutingPreference),
                    adjacency,
                    remainingCapacityByEdgeId,
                    remainingTranshipmentCapacityByNodeId);

                passDelivered += branchDelivered;
            }

            if (passDelivered <= Epsilon)
            {
                break;
            }

            supplyAtNode -= passDelivered;
            delivered += passDelivered;
        }

        return delivered;
    }

    private static List<BranchDemand> BuildBranchDemandMap(
        TrafficContext context,
        string producerNodeId,
        string currentNodeId,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        var branchesByKey = new Dictionary<string, BranchDemand>(Comparer);

        foreach (var consumerNodeId in context.Demand
                     .Where(pair => pair.Value > Epsilon && !Comparer.Equals(pair.Key, currentNodeId))
                     .Select(pair => pair.Key)
                     .OrderBy(nodeId => context.NodesById[nodeId].Name, Comparer)
                     .ThenBy(nodeId => nodeId, Comparer))
        {
            var route = FindBestRoute(
                context,
                currentNodeId,
                consumerNodeId,
                adjacency,
                remainingCapacityByEdgeId,
                remainingTranshipmentCapacityByNodeId);
            if (route is null || route.PathEdgeIds.Count == 0 || route.PathNodeIds.Count < 2)
            {
                continue;
            }

            var edgeId = route.PathEdgeIds[0];
            if (!remainingCapacityByEdgeId.TryGetValue(edgeId, out var edgeCapacity) || edgeCapacity <= Epsilon)
            {
                continue;
            }

            var firstHopCapacity = edgeCapacity;
            if (!Comparer.Equals(currentNodeId, producerNodeId) &&
                remainingTranshipmentCapacityByNodeId.TryGetValue(currentNodeId, out var transhipmentCapacity))
            {
                firstHopCapacity = Math.Min(firstHopCapacity, transhipmentCapacity);
            }

            if (firstHopCapacity <= Epsilon)
            {
                continue;
            }

            var toNodeId = route.PathNodeIds[1];
            var key = $"{edgeId}\u001f{toNodeId}";
            if (!branchesByKey.TryGetValue(key, out var branch))
            {
                var arc = adjacency.GetValueOrDefault(currentNodeId)?
                    .FirstOrDefault(item => Comparer.Equals(item.EdgeId, edgeId) && Comparer.Equals(item.ToNodeId, toNodeId));
                if (arc is null)
                {
                    continue;
                }

                branch = new BranchDemand(edgeId, toNodeId, arc.Time, arc.Cost, firstHopCapacity);
                branchesByKey[key] = branch;
            }

            branch.DownstreamDemand += context.Demand[consumerNodeId];
            branch.FirstHopCapacity = Math.Min(branch.FirstHopCapacity, firstHopCapacity);
        }

        return branchesByKey.Values
            .Where(branch => branch.DownstreamDemand > Epsilon && branch.FirstHopCapacity > Epsilon)
            .OrderBy(branch => context.NodesById[branch.ToNodeId].Name, Comparer)
            .ThenBy(branch => branch.ToNodeId, Comparer)
            .ThenBy(branch => branch.EdgeId, Comparer)
            .ToList();
    }

    private static List<BranchShare> AllocateAcrossBranchRoutes(double availableSupply, IReadOnlyList<BranchDemand> branches)
    {
        var states = branches
            .Select(branch => new BranchShareState(branch, branch.DownstreamDemand, Math.Min(branch.DownstreamDemand, branch.FirstHopCapacity)))
            .Where(state => state.RemainingCapacity > Epsilon)
            .ToList();
        var remainingSupply = availableSupply;

        while (remainingSupply > Epsilon)
        {
            var totalDemand = states.Sum(state => state.RemainingDemand);
            if (totalDemand <= Epsilon)
            {
                break;
            }

            var progress = 0d;
            foreach (var state in states.Where(state => state.RemainingDemand > Epsilon).OrderBy(state => state.Branch.ToNodeId, Comparer).ThenBy(state => state.Branch.EdgeId, Comparer))
            {
                var targetShare = remainingSupply * state.RemainingDemand / totalDemand;
                var quantity = Math.Min(targetShare, Math.Min(state.RemainingDemand, state.RemainingCapacity));
                if (quantity <= Epsilon)
                {
                    continue;
                }

                state.Quantity += quantity;
                state.RemainingDemand -= quantity;
                state.RemainingCapacity -= quantity;
                progress += quantity;
            }

            if (progress <= Epsilon)
            {
                break;
            }

            remainingSupply -= progress;
        }

        return states
            .Where(state => state.Quantity > Epsilon)
            .Select(state => new BranchShare(state.Branch, state.Quantity))
            .ToList();
    }

    private static void AddRouteAllocation(
        TrafficContext context,
        string producerNodeId,
        string consumerNodeId,
        double quantity,
        bool isLocalSupply,
        IReadOnlyList<string> pathNodeIds,
        IReadOnlyList<string> pathEdgeIds,
        IReadOnlyList<string> pathTranshipmentNodeIds,
        double totalTime,
        double transitCostPerUnit,
        double totalScore,
        double capacityBidPerUnit,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        var routeCapacity = GetRouteRemainingCapacity(pathEdgeIds, pathTranshipmentNodeIds, remainingCapacityByEdgeId, remainingTranshipmentCapacityByNodeId);
        var bidCostPerUnit = CalculateBidCostPerUnit(
            pathEdgeIds,
            pathTranshipmentNodeIds,
            remainingCapacityByEdgeId,
            remainingTranshipmentCapacityByNodeId,
            capacityBidPerUnit,
            quantity,
            routeCapacity);
        var deliveredCostPerUnit = transitCostPerUnit + bidCostPerUnit;

        context.Allocations.Add(new RouteAllocation
        {
            TrafficType = context.TrafficType,
            RoutingPreference = context.RoutingPreference,
            AllocationMode = context.AllocationMode,
            ProducerNodeId = producerNodeId,
            ProducerName = context.NodesById[producerNodeId].Name,
            ConsumerNodeId = consumerNodeId,
            ConsumerName = context.NodesById[consumerNodeId].Name,
            Quantity = quantity,
            IsLocalSupply = isLocalSupply,
            TotalTime = totalTime,
            TotalCost = transitCostPerUnit,
            BidCostPerUnit = bidCostPerUnit,
            DeliveredCostPerUnit = deliveredCostPerUnit,
            TotalMovementCost = deliveredCostPerUnit * quantity,
            TotalScore = totalScore,
            PathNodeNames = pathNodeIds.Select(nodeId => context.NodesById[nodeId].Name).ToList(),
            PathNodeIds = pathNodeIds.ToList(),
            PathEdgeIds = pathEdgeIds.ToList()
        });

        ReserveCapacity(
            pathEdgeIds,
            pathTranshipmentNodeIds,
            remainingCapacityByEdgeId,
            remainingTranshipmentCapacityByNodeId,
            quantity);
    }

    private static List<RouteCandidate> BuildCandidateRoutes(
        TrafficContext context,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        var routes = new List<RouteCandidate>();

        foreach (var producerNodeId in context.Supply.Where(pair => pair.Value > Epsilon).Select(pair => pair.Key))
        {
            foreach (var consumerNodeId in context.Demand.Where(pair => pair.Value > Epsilon).Select(pair => pair.Key))
            {
                if (Comparer.Equals(producerNodeId, consumerNodeId))
                {
                    continue;
                }

                var route = FindBestRoute(
                    context,
                    producerNodeId,
                    consumerNodeId,
                    adjacency,
                    remainingCapacityByEdgeId,
                    remainingTranshipmentCapacityByNodeId);

                if (route is not null)
                {
                    routes.Add(route);
                }
            }
        }

        return routes;
    }

    private static RouteCandidate? FindBestRoute(
        TrafficContext context,
        string producerNodeId,
        string consumerNodeId,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        // A Dijkstra pass finds the best currently-feasible route under this traffic type's scoring rule.
        var distances = new Dictionary<string, double>(Comparer)
        {
            [producerNodeId] = 0d
        };

        var previous = new Dictionary<string, PreviousStep>(Comparer);
        var queue = new PriorityQueue<string, double>();
        queue.Enqueue(producerNodeId, 0d);

        while (queue.TryDequeue(out var currentNodeId, out var currentDistance))
        {
            if (currentDistance > distances[currentNodeId] + Epsilon)
            {
                continue;
            }

            if (Comparer.Equals(currentNodeId, consumerNodeId))
            {
                break;
            }

            if (!adjacency.TryGetValue(currentNodeId, out var arcs))
            {
                continue;
            }

            foreach (var arc in arcs)
            {
                if (!remainingCapacityByEdgeId.TryGetValue(arc.EdgeId, out var remainingCapacity) ||
                    remainingCapacity <= Epsilon)
                {
                    continue;
                }

                if (IsIntermediateNode(arc.ToNodeId, producerNodeId, consumerNodeId) &&
                    remainingTranshipmentCapacityByNodeId.TryGetValue(arc.ToNodeId, out var remainingNodeCapacity) &&
                    remainingNodeCapacity <= Epsilon)
                {
                    continue;
                }

                if (!CanTraverseNode(arc.ToNodeId, producerNodeId, consumerNodeId, context.ProfilesByNodeId))
                {
                    continue;
                }

                var proposedDistance = currentDistance + Score(arc.Time, arc.Cost, context.RoutingPreference);
                if (distances.TryGetValue(arc.ToNodeId, out var existingDistance) &&
                    proposedDistance >= existingDistance - Epsilon)
                {
                    continue;
                }

                distances[arc.ToNodeId] = proposedDistance;
                previous[arc.ToNodeId] = new PreviousStep(currentNodeId, arc);
                queue.Enqueue(arc.ToNodeId, proposedDistance);
            }
        }

        if (!distances.ContainsKey(consumerNodeId))
        {
            return null;
        }

        var pathNodeIds = new List<string> { consumerNodeId };
        var pathArcs = new List<GraphArc>();
        var cursor = consumerNodeId;

        while (!Comparer.Equals(cursor, producerNodeId))
        {
            var step = previous[cursor];
            pathNodeIds.Add(step.PreviousNodeId);
            pathArcs.Add(step.Arc);
            cursor = step.PreviousNodeId;
        }

        pathNodeIds.Reverse();
        pathArcs.Reverse();
        var pathTranshipmentNodeIds = GetIntermediateNodeIds(pathNodeIds);

        return new RouteCandidate(
            context,
            producerNodeId,
            consumerNodeId,
            pathNodeIds,
            pathArcs.Select(arc => arc.EdgeId).ToList(),
            pathTranshipmentNodeIds,
            pathArcs.Sum(arc => arc.Time),
            pathArcs.Sum(arc => arc.Cost),
            pathArcs.Sum(arc => Score(arc.Time, arc.Cost, context.RoutingPreference)),
            GetCapacityBidPerUnit(context, consumerNodeId));
    }

    private static bool CanTraverseNode(
        string nodeId,
        string producerNodeId,
        string consumerNodeId,
        IReadOnlyDictionary<string, NodeTrafficProfile?> profilesByNodeId)
    {
        if (Comparer.Equals(nodeId, producerNodeId) || Comparer.Equals(nodeId, consumerNodeId))
        {
            return true;
        }

        // Intermediate nodes must explicitly allow transhipment for the current traffic type.
        return profilesByNodeId.TryGetValue(nodeId, out var profile) && profile?.CanTransship == true;
    }

    private static Dictionary<string, List<GraphArc>> BuildAdjacency(NetworkModel network)
    {
        var adjacency = new Dictionary<string, List<GraphArc>>(Comparer);

        void AddArc(string fromNodeId, string toNodeId, EdgeModel edge)
        {
            if (!adjacency.TryGetValue(fromNodeId, out var arcs))
            {
                arcs = [];
                adjacency[fromNodeId] = arcs;
            }

            arcs.Add(new GraphArc(edge.Id, fromNodeId, toNodeId, edge.Time, edge.Cost));
        }

        foreach (var edge in network.Edges)
        {
            AddArc(edge.FromNodeId, edge.ToNodeId, edge);
            if (edge.IsBidirectional)
            {
                AddArc(edge.ToNodeId, edge.FromNodeId, edge);
            }
        }

        return adjacency;
    }

    private static bool IsIntermediateNode(string nodeId, string producerNodeId, string consumerNodeId)
    {
        return !Comparer.Equals(nodeId, producerNodeId) && !Comparer.Equals(nodeId, consumerNodeId);
    }

    private static IReadOnlyList<string> GetIntermediateNodeIds(IReadOnlyList<string> pathNodeIds)
    {
        if (pathNodeIds.Count <= 2)
        {
            return [];
        }

        var intermediateNodeIds = new List<string>(pathNodeIds.Count - 2);
        for (var index = 1; index < pathNodeIds.Count - 1; index++)
        {
            intermediateNodeIds.Add(pathNodeIds[index]);
        }

        return intermediateNodeIds;
    }

    private static double GetRouteRemainingCapacity(
        IReadOnlyList<string> pathEdgeIds,
        IReadOnlyList<string> pathTranshipmentNodeIds,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        var edgeCapacity = GetPathRemainingCapacity(pathEdgeIds, remainingCapacityByEdgeId);
        var nodeCapacity = GetPathRemainingCapacity(pathTranshipmentNodeIds, remainingTranshipmentCapacityByNodeId);
        return Math.Min(edgeCapacity, nodeCapacity);
    }

    private static double GetPathRemainingCapacity(
        IReadOnlyList<string> pathResourceIds,
        IDictionary<string, double> remainingCapacityById)
    {
        if (pathResourceIds.Count == 0)
        {
            return double.PositiveInfinity;
        }

        return pathResourceIds
            .Select(resourceId => remainingCapacityById.TryGetValue(resourceId, out var remainingCapacity) ? remainingCapacity : 0d)
            .DefaultIfEmpty(0d)
            .Min();
    }

    private static double CalculateBidCostPerUnit(
        IReadOnlyList<string> pathEdgeIds,
        IReadOnlyList<string> pathTranshipmentNodeIds,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId,
        double capacityBidPerUnit,
        double quantity,
        double routeCapacity)
    {
        // Bid cost is only charged when the chosen movement fully consumes one or more finite bottlenecks on the route.
        if (capacityBidPerUnit <= Epsilon ||
            quantity <= Epsilon ||
            double.IsPositiveInfinity(routeCapacity) ||
            quantity < routeCapacity - Epsilon)
        {
            return 0d;
        }

        var bottleneckResourceCount =
            CountBottleneckResources(pathEdgeIds, remainingCapacityByEdgeId, routeCapacity) +
            CountBottleneckResources(pathTranshipmentNodeIds, remainingTranshipmentCapacityByNodeId, routeCapacity);

        return bottleneckResourceCount * capacityBidPerUnit;
    }

    private static int CountBottleneckResources(
        IEnumerable<string> pathResourceIds,
        IDictionary<string, double> remainingCapacityById,
        double routeCapacity)
    {
        return pathResourceIds.Count(resourceId =>
            remainingCapacityById.TryGetValue(resourceId, out var remainingCapacity) &&
            !double.IsPositiveInfinity(remainingCapacity) &&
            remainingCapacity <= routeCapacity + Epsilon);
    }

    private static void ReserveCapacity(
        IEnumerable<string> pathEdgeIds,
        IEnumerable<string> pathTranshipmentNodeIds,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId,
        double quantity)
    {
        ReserveCapacity(pathEdgeIds, remainingCapacityByEdgeId, quantity);
        ReserveCapacity(pathTranshipmentNodeIds, remainingTranshipmentCapacityByNodeId, quantity);
    }

    private static void ReserveCapacity(
        IEnumerable<string> pathResourceIds,
        IDictionary<string, double> remainingCapacityById,
        double quantity)
    {
        foreach (var resourceId in pathResourceIds)
        {
            if (!remainingCapacityById.TryGetValue(resourceId, out var remainingCapacity) ||
                double.IsPositiveInfinity(remainingCapacity))
            {
                continue;
            }

            remainingCapacityById[resourceId] = Math.Max(0d, remainingCapacity - quantity);
        }
    }

    private static List<string> GetOrderedTrafficNames(NetworkModel network)
    {
        var orderedTrafficNames = new List<string>();
        var seen = new HashSet<string>(Comparer);

        foreach (var definition in network.TrafficTypes)
        {
            if (!string.IsNullOrWhiteSpace(definition.Name) && seen.Add(definition.Name))
            {
                orderedTrafficNames.Add(definition.Name);
            }
        }

        var undeclaredTrafficNames = network.Nodes
            .SelectMany(node => node.TrafficProfiles)
            .Select(profile => profile.TrafficType)
            .Concat(network.Nodes
                .SelectMany(node => node.TrafficProfiles)
                .SelectMany(profile => profile.InputRequirements)
                .Select(requirement => requirement.TrafficType))
            .Where(name => !string.IsNullOrWhiteSpace(name) && !seen.Contains(name))
            .Distinct(Comparer)
            .OrderBy(name => name, Comparer);

        orderedTrafficNames.AddRange(undeclaredTrafficNames);
        return orderedTrafficNames;
    }

    private static double GetCapacityBidPerUnit(TrafficTypeDefinition definition)
    {
        if (definition.CapacityBidPerUnit.HasValue)
        {
            return Math.Max(0d, definition.CapacityBidPerUnit.Value);
        }

        return definition.RoutingPreference == RoutingPreference.Speed ? 1d : 0d;
    }

    private static double GetCapacityBidPerUnit(TrafficContext context, string consumerNodeId)
    {
        var baseBid = context.CapacityBidPerUnit;
        var consumerPremium = context.ProfilesByNodeId.TryGetValue(consumerNodeId, out var profile)
            ? Math.Max(0d, profile?.ConsumerPremiumPerUnit ?? 0d)
            : 0d;
        return baseBid + consumerPremium;
    }

    private static double Score(double time, double cost, RoutingPreference routingPreference)
    {
        return routingPreference switch
        {
            RoutingPreference.Speed => time,
            RoutingPreference.Cost => cost,
            _ => time + cost
        };
    }

    private sealed record GraphArc(
        string EdgeId,
        string FromNodeId,
        string ToNodeId,
        double Time,
        double Cost);

    private sealed record PreviousStep(string PreviousNodeId, GraphArc Arc);

    private sealed record RouteCandidate(
        TrafficContext Context,
        string ProducerNodeId,
        string ConsumerNodeId,
        IReadOnlyList<string> PathNodeIds,
        IReadOnlyList<string> PathEdgeIds,
        IReadOnlyList<string> PathTranshipmentNodeIds,
        double TotalTime,
        double TransitCostPerUnit,
        double TotalScore,
        double CapacityBidPerUnit);

    private sealed record TrafficContext(
        string TrafficType,
        RoutingPreference RoutingPreference,
        AllocationMode AllocationMode,
        double CapacityBidPerUnit,
        IReadOnlyDictionary<string, NodeModel> NodesById,
        IReadOnlyDictionary<string, NodeTrafficProfile?> ProfilesByNodeId,
        IDictionary<string, double> Supply,
        IDictionary<string, double> Demand,
        double TotalProduction,
        double TotalConsumption,
        List<RouteAllocation> Allocations,
        List<string> Notes);

    private sealed class BranchDemand(
        string edgeId,
        string toNodeId,
        double time,
        double cost,
        double firstHopCapacity)
    {
        public string EdgeId { get; } = edgeId;

        public string ToNodeId { get; } = toNodeId;

        public double Time { get; } = time;

        public double Cost { get; } = cost;

        public double FirstHopCapacity { get; set; } = firstHopCapacity;

        public double DownstreamDemand { get; set; }
    }

    private sealed record BranchShare(BranchDemand Branch, double Quantity);

    private sealed class BranchShareState(BranchDemand branch, double remainingDemand, double remainingCapacity)
    {
        public BranchDemand Branch { get; } = branch;

        public double RemainingDemand { get; set; } = remainingDemand;

        public double RemainingCapacity { get; set; } = remainingCapacity;

        public double Quantity { get; set; }
    }
}

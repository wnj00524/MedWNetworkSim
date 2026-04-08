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

        // Build shared graph data once, then route every traffic type against the same pool of edge and node transhipment capacity.
        var adjacency = BuildAdjacency(network);
        var definitionsByTraffic = network.TrafficTypes
            .ToDictionary(definition => definition.Name, definition => definition, Comparer);
        var contexts = GetOrderedTrafficNames(network)
            .Select(trafficType =>
            {
                var definition = definitionsByTraffic.GetValueOrDefault(trafficType)
                    ?? new TrafficTypeDefinition
                    {
                        Name = trafficType,
                        RoutingPreference = RoutingPreference.TotalCost
                    };

                return BuildContext(network, definition);
            })
            .ToList();
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

        foreach (var context in contexts)
        {
            if (context.TotalProduction <= Epsilon)
            {
                context.Notes.Add("No producer nodes were defined for this traffic type.");
            }

            if (context.TotalConsumption <= Epsilon)
            {
                context.Notes.Add("No consumer nodes were defined for this traffic type.");
            }

            // Same-node supply satisfies same-node demand before any network routing is attempted.
            ApplyLocalAllocations(context);
        }

        while (true)
        {
            // Capacity bidding is resolved globally: every traffic type competes for the next best route.
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

            var bidCostPerUnit = CalculateBidCostPerUnit(
                nextCandidate.PathEdgeIds,
                nextCandidate.PathTranshipmentNodeIds,
                remainingCapacityByEdgeId,
                remainingTranshipmentCapacityByNodeId,
                nextCandidate.CapacityBidPerUnit,
                quantity,
                routeCapacity);
            var deliveredCostPerUnit = nextCandidate.TransitCostPerUnit + bidCostPerUnit;

            context.Allocations.Add(new RouteAllocation
            {
                TrafficType = context.TrafficType,
                RoutingPreference = context.RoutingPreference,
                ProducerNodeId = nextCandidate.ProducerNodeId,
                ProducerName = context.NodesById[nextCandidate.ProducerNodeId].Name,
                ConsumerNodeId = nextCandidate.ConsumerNodeId,
                ConsumerName = context.NodesById[nextCandidate.ConsumerNodeId].Name,
                Quantity = quantity,
                IsLocalSupply = false,
                TotalTime = nextCandidate.TotalTime,
                TotalCost = nextCandidate.TransitCostPerUnit,
                BidCostPerUnit = bidCostPerUnit,
                DeliveredCostPerUnit = deliveredCostPerUnit,
                TotalMovementCost = deliveredCostPerUnit * quantity,
                TotalScore = nextCandidate.TotalScore,
                PathNodeNames = nextCandidate.PathNodeIds.Select(nodeId => context.NodesById[nodeId].Name).ToList()
            });

            context.Supply[nextCandidate.ProducerNodeId] -= quantity;
            context.Demand[nextCandidate.ConsumerNodeId] -= quantity;
            ReserveCapacity(
                nextCandidate.PathEdgeIds,
                nextCandidate.PathTranshipmentNodeIds,
                remainingCapacityByEdgeId,
                remainingTranshipmentCapacityByNodeId,
                quantity);
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
        return outcomes
            .SelectMany(outcome => outcome.Allocations)
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

    private static double CalculateAverageUnitCost(IReadOnlyCollection<RouteAllocation> allocations)
    {
        var quantity = allocations.Sum(allocation => allocation.Quantity);
        if (quantity <= Epsilon)
        {
            return 0d;
        }

        return allocations.Sum(allocation => allocation.TotalMovementCost) / quantity;
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

        return new TrafficContext(
            definition.Name,
            definition.RoutingPreference,
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
                PathNodeNames = [node.Name]
            });

            context.Supply[nodeId] -= quantity;
            context.Demand[nodeId] -= quantity;
        }
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
            context.CapacityBidPerUnit);
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
        double CapacityBidPerUnit,
        IReadOnlyDictionary<string, NodeModel> NodesById,
        IReadOnlyDictionary<string, NodeTrafficProfile?> ProfilesByNodeId,
        IDictionary<string, double> Supply,
        IDictionary<string, double> Demand,
        double TotalProduction,
        double TotalConsumption,
        List<RouteAllocation> Allocations,
        List<string> Notes);
}

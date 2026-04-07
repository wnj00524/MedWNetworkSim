using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public sealed class NetworkSimulationEngine
{
    private const double Epsilon = 0.000001d;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public IReadOnlyList<TrafficSimulationOutcome> Simulate(NetworkModel network)
    {
        ArgumentNullException.ThrowIfNull(network);

        var definitionsByTraffic = network.TrafficTypes
            .ToDictionary(definition => definition.Name, definition => definition, Comparer);

        var trafficNames = network.TrafficTypes
            .Select(definition => definition.Name)
            .Concat(network.Nodes.SelectMany(node => node.TrafficProfiles).Select(profile => profile.TrafficType))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(Comparer)
            .OrderBy(name => name, Comparer)
            .ToList();

        return trafficNames
            .Select(trafficName =>
            {
                var definition = definitionsByTraffic.GetValueOrDefault(trafficName)
                    ?? new TrafficTypeDefinition
                    {
                        Name = trafficName,
                        RoutingPreference = RoutingPreference.TotalCost
                    };

                return SimulateTraffic(network, trafficName, definition.RoutingPreference);
            })
            .ToList();
    }

    private static TrafficSimulationOutcome SimulateTraffic(
        NetworkModel network,
        string trafficType,
        RoutingPreference routingPreference)
    {
        var nodesById = network.Nodes.ToDictionary(node => node.Id, node => node, Comparer);
        var profilesByNodeId = network.Nodes.ToDictionary(
            node => node.Id,
            node => node.TrafficProfiles.FirstOrDefault(profile => Comparer.Equals(profile.TrafficType, trafficType)),
            Comparer);

        var supply = profilesByNodeId
            .Where(pair => pair.Value?.Production > Epsilon)
            .ToDictionary(pair => pair.Key, pair => pair.Value!.Production, Comparer);

        var demand = profilesByNodeId
            .Where(pair => pair.Value?.Consumption > Epsilon)
            .ToDictionary(pair => pair.Key, pair => pair.Value!.Consumption, Comparer);

        var totalProduction = supply.Values.Sum();
        var totalConsumption = demand.Values.Sum();
        var notes = new List<string>();
        var allocations = new List<RouteAllocation>();

        if (totalProduction <= Epsilon)
        {
            notes.Add("No producer nodes were defined for this traffic type.");
        }

        if (totalConsumption <= Epsilon)
        {
            notes.Add("No consumer nodes were defined for this traffic type.");
        }

        ApplyLocalAllocations(trafficType, routingPreference, nodesById, supply, demand, allocations);

        if (supply.Values.Sum() > Epsilon && demand.Values.Sum() > Epsilon)
        {
            var adjacency = BuildAdjacency(network);
            var candidateRoutes = BuildCandidateRoutes(
                trafficType,
                routingPreference,
                nodesById,
                profilesByNodeId,
                adjacency,
                supply,
                demand);

            foreach (var route in candidateRoutes)
            {
                if (!supply.TryGetValue(route.ProducerNodeId, out var remainingSupply) ||
                    !demand.TryGetValue(route.ConsumerNodeId, out var remainingDemand))
                {
                    continue;
                }

                var quantity = Math.Min(remainingSupply, remainingDemand);
                if (quantity <= Epsilon)
                {
                    continue;
                }

                allocations.Add(new RouteAllocation
                {
                    TrafficType = trafficType,
                    RoutingPreference = routingPreference,
                    ProducerNodeId = route.ProducerNodeId,
                    ProducerName = nodesById[route.ProducerNodeId].Name,
                    ConsumerNodeId = route.ConsumerNodeId,
                    ConsumerName = nodesById[route.ConsumerNodeId].Name,
                    Quantity = quantity,
                    TotalTime = route.TotalTime,
                    TotalCost = route.TotalCost,
                    TotalScore = route.TotalScore,
                    PathNodeNames = route.PathNodeIds.Select(nodeId => nodesById[nodeId].Name).ToList()
                });

                supply[route.ProducerNodeId] -= quantity;
                demand[route.ConsumerNodeId] -= quantity;
            }
        }

        var unusedSupply = supply.Values.Sum(value => Math.Max(0d, value));
        var unmetDemand = demand.Values.Sum(value => Math.Max(0d, value));

        if (unusedSupply > Epsilon)
        {
            notes.Add($"Unused supply remains after routing: {unusedSupply:0.##} unit(s).");
        }

        if (unmetDemand > Epsilon)
        {
            notes.Add($"Unmet demand remains after routing: {unmetDemand:0.##} unit(s).");
        }

        if (allocations.Count == 0 && totalProduction > Epsilon && totalConsumption > Epsilon)
        {
            notes.Add("No feasible producer-to-consumer routes were found with the current node roles and edge directions.");
        }

        return new TrafficSimulationOutcome
        {
            TrafficType = trafficType,
            RoutingPreference = routingPreference,
            TotalProduction = totalProduction,
            TotalConsumption = totalConsumption,
            TotalDelivered = allocations.Sum(allocation => allocation.Quantity),
            UnusedSupply = unusedSupply,
            UnmetDemand = unmetDemand,
            Allocations = allocations,
            Notes = notes
        };
    }

    private static void ApplyLocalAllocations(
        string trafficType,
        RoutingPreference routingPreference,
        IReadOnlyDictionary<string, NodeModel> nodesById,
        IDictionary<string, double> supply,
        IDictionary<string, double> demand,
        ICollection<RouteAllocation> allocations)
    {
        foreach (var nodeId in supply.Keys.Intersect(demand.Keys, Comparer).ToList())
        {
            var quantity = Math.Min(supply[nodeId], demand[nodeId]);
            if (quantity <= Epsilon)
            {
                continue;
            }

            var node = nodesById[nodeId];
            allocations.Add(new RouteAllocation
            {
                TrafficType = trafficType,
                RoutingPreference = routingPreference,
                ProducerNodeId = nodeId,
                ProducerName = node.Name,
                ConsumerNodeId = nodeId,
                ConsumerName = node.Name,
                Quantity = quantity,
                TotalTime = 0d,
                TotalCost = 0d,
                TotalScore = 0d,
                PathNodeNames = [node.Name]
            });

            supply[nodeId] -= quantity;
            demand[nodeId] -= quantity;
        }
    }

    private static List<CandidateRoute> BuildCandidateRoutes(
        string trafficType,
        RoutingPreference routingPreference,
        IReadOnlyDictionary<string, NodeModel> nodesById,
        IReadOnlyDictionary<string, NodeTrafficProfile?> profilesByNodeId,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IReadOnlyDictionary<string, double> supply,
        IReadOnlyDictionary<string, double> demand)
    {
        var routes = new List<CandidateRoute>();

        foreach (var producerNodeId in supply.Where(pair => pair.Value > Epsilon).Select(pair => pair.Key))
        {
            foreach (var consumerNodeId in demand.Where(pair => pair.Value > Epsilon).Select(pair => pair.Key))
            {
                if (Comparer.Equals(producerNodeId, consumerNodeId))
                {
                    continue;
                }

                var route = FindBestRoute(
                    producerNodeId,
                    consumerNodeId,
                    routingPreference,
                    profilesByNodeId,
                    adjacency);

                if (route is not null)
                {
                    routes.Add(route with { TrafficType = trafficType });
                }
            }
        }

        return routes
            .OrderBy(route => route.TotalScore)
            .ThenBy(route => route.TotalTime)
            .ThenBy(route => route.TotalCost)
            .ThenBy(route => route.ProducerNodeId, Comparer)
            .ThenBy(route => route.ConsumerNodeId, Comparer)
            .ToList();
    }

    private static CandidateRoute? FindBestRoute(
        string producerNodeId,
        string consumerNodeId,
        RoutingPreference routingPreference,
        IReadOnlyDictionary<string, NodeTrafficProfile?> profilesByNodeId,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency)
    {
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
                if (!CanTraverseNode(arc.ToNodeId, producerNodeId, consumerNodeId, profilesByNodeId))
                {
                    continue;
                }

                var proposedDistance = currentDistance + Score(arc.Time, arc.Cost, routingPreference);
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

        return new CandidateRoute(
            string.Empty,
            producerNodeId,
            consumerNodeId,
            pathNodeIds,
            pathArcs.Sum(arc => arc.Time),
            pathArcs.Sum(arc => arc.Cost),
            pathArcs.Sum(arc => Score(arc.Time, arc.Cost, routingPreference)));
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

    private sealed record CandidateRoute(
        string TrafficType,
        string ProducerNodeId,
        string ConsumerNodeId,
        IReadOnlyList<string> PathNodeIds,
        double TotalTime,
        double TotalCost,
        double TotalScore);
}

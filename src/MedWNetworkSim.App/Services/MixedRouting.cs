using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public interface IRouteChoiceStrategy
{
    void Initialize(RoutingTrafficContext context, NetworkState networkState, IReadOnlyDictionary<string, List<GraphArc>> adjacency);

    List<FlowProposal> ProposeFlows(RoutingTrafficContext context, NetworkState networkState, int round);
}

public interface ICapacityResolutionPolicy
{
    List<CommittedFlow> Resolve(IEnumerable<FlowProposal> proposals, NetworkState networkState);
}

public interface ICongestionCostModel
{
    double GetEffectiveTime(double baseTime, double used, double capacity, double alpha);

    double GetEffectiveCost(double baseCost, double used, double capacity, double gamma);
}

public sealed class NetworkState
{
    public Dictionary<string, double> RemainingEdgeCapacity { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> RemainingNodeCapacity { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> EdgeLoad { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> NodeLoad { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> EdgeCapacity { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> NodeCapacity { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record FlowProposal(
    RoutingTrafficContext TrafficType,
    string ProducerNodeId,
    string ConsumerNodeId,
    double Quantity,
    IReadOnlyList<string> Path,
    IReadOnlyList<string> PathEdgeIds,
    IReadOnlyList<string> PathTranshipmentNodeIds,
    double BaseTime,
    double BaseCost,
    double EffectiveTime,
    double EffectiveCost,
    double Score,
    double Priority);

public sealed record CommittedFlow(
    RoutingTrafficContext TrafficType,
    string ProducerNodeId,
    string ConsumerNodeId,
    double Quantity,
    IReadOnlyList<string> Path,
    IReadOnlyList<string> PathEdgeIds,
    IReadOnlyList<string> PathTranshipmentNodeIds,
    double BaseTime,
    double BaseCost,
    double EffectiveTime,
    double EffectiveCost,
    double Score,
    double Priority);

public sealed class RoutingTrafficContext
{
    public string TrafficType { get; init; } = string.Empty;
    public RoutingPreference RoutingPreference { get; init; }
    public AllocationMode AllocationMode { get; init; }
    public RouteChoiceModel RouteChoiceModel { get; init; }
    public FlowSplitPolicy FlowSplitPolicy { get; init; }
    public RouteChoiceSettings RouteChoiceSettings { get; init; } = new();
    public double CapacityBidPerUnit { get; init; }
    public int Seed { get; init; }
    public IReadOnlyDictionary<string, NodeModel> NodesById { get; init; } = new Dictionary<string, NodeModel>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, NodeTrafficProfile?> ProfilesByNodeId { get; init; } = new Dictionary<string, NodeTrafficProfile?>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> Supply { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> Demand { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public double TotalProduction { get; init; }
    public double TotalConsumption { get; init; }
    public List<RouteAllocation> Allocations { get; init; } = [];
    public List<string> Notes { get; init; } = [];
    public Dictionary<string, double> CommittedSupply { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> CommittedDemand { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? LastPathKey { get; set; }
}

public sealed record GraphArc(string EdgeId, string FromNodeId, string ToNodeId, double Time, double Cost);

public sealed class VolumeCapacityCongestionCostModel : ICongestionCostModel
{
    private const double Beta = 4d;

    public double GetEffectiveTime(double baseTime, double used, double capacity, double alpha)
    {
        if (double.IsPositiveInfinity(capacity) || capacity <= 0d)
        {
            return baseTime;
        }

        var util = Math.Max(0d, used) / capacity;
        return baseTime * (1d + alpha * Math.Pow(util, Beta));
    }

    public double GetEffectiveCost(double baseCost, double used, double capacity, double gamma)
    {
        if (double.IsPositiveInfinity(capacity) || capacity <= 0d)
        {
            return baseCost;
        }

        var util = Math.Max(0d, used) / capacity;
        return baseCost + (gamma * util);
    }
}

public sealed class PriorityWeightedCapacityResolutionPolicy : ICapacityResolutionPolicy
{
    private const double Epsilon = 0.000001d;

    public List<CommittedFlow> Resolve(IEnumerable<FlowProposal> proposals, NetworkState networkState)
    {
        var ordered = proposals
            .Where(proposal => proposal.Quantity > Epsilon)
            .OrderByDescending(proposal => Math.Max(Epsilon, proposal.Priority) + Math.Max(0d, proposal.TrafficType.CapacityBidPerUnit))
            .ThenBy(proposal => proposal.Score)
            .ThenBy(proposal => proposal.TrafficType.TrafficType, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = new List<CommittedFlow>();

        foreach (var proposal in ordered)
        {
            var routeCapacity = MixedRoutingAllocator.GetRouteRemainingCapacity(
                proposal.PathEdgeIds,
                proposal.PathTranshipmentNodeIds,
                networkState.RemainingEdgeCapacity,
                networkState.RemainingNodeCapacity);
            var quantity = Math.Min(proposal.Quantity, routeCapacity);
            if (quantity <= Epsilon)
            {
                continue;
            }

            result.Add(new CommittedFlow(
                proposal.TrafficType,
                proposal.ProducerNodeId,
                proposal.ConsumerNodeId,
                quantity,
                proposal.Path,
                proposal.PathEdgeIds,
                proposal.PathTranshipmentNodeIds,
                proposal.BaseTime,
                proposal.BaseCost,
                proposal.EffectiveTime,
                proposal.EffectiveCost,
                proposal.Score,
                proposal.Priority));

            MixedRoutingAllocator.ReserveCapacity(
                proposal.PathEdgeIds,
                proposal.PathTranshipmentNodeIds,
                networkState,
                quantity);
        }

        return result;
    }
}

public sealed class SystemOptimalRouteChoiceStrategy : IRouteChoiceStrategy
{
    public void Initialize(RoutingTrafficContext context, NetworkState networkState, IReadOnlyDictionary<string, List<GraphArc>> adjacency)
    {
    }

    public List<FlowProposal> ProposeFlows(RoutingTrafficContext context, NetworkState networkState, int round)
    {
        return MixedRoutingAllocator.ProposeByScore(context, networkState, deterministicBest: true, round);
    }
}

public sealed class StochasticUserResponsiveRouteChoiceStrategy : IRouteChoiceStrategy
{
    public void Initialize(RoutingTrafficContext context, NetworkState networkState, IReadOnlyDictionary<string, List<GraphArc>> adjacency)
    {
    }

    public List<FlowProposal> ProposeFlows(RoutingTrafficContext context, NetworkState networkState, int round)
    {
        return MixedRoutingAllocator.ProposeByScore(context, networkState, deterministicBest: false, round);
    }
}

public static partial class MixedRoutingAllocator
{
    private const double Epsilon = 0.000001d;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private static readonly ICongestionCostModel CongestionCostModel = new VolumeCapacityCongestionCostModel();
    private static readonly ICapacityResolutionPolicy CapacityResolutionPolicy = new PriorityWeightedCapacityResolutionPolicy();
    private static IReadOnlyDictionary<string, List<GraphArc>> adjacency = new Dictionary<string, List<GraphArc>>(Comparer);

    public static IReadOnlyList<RoutingTrafficContext> BuildStaticContexts(NetworkModel network)
    {
        var definitionsByTraffic = network.TrafficTypes.ToDictionary(definition => definition.Name, definition => definition, Comparer);
        return GetOrderedTrafficNames(network)
            .Select((trafficType, index) =>
            {
                var definition = definitionsByTraffic.GetValueOrDefault(trafficType)
                    ?? new TrafficTypeDefinition { Name = trafficType };
                return BuildStaticContext(network, definition, network.SimulationSeed + (index * 997));
            })
            .ToList();
    }

    public static IReadOnlyList<RouteAllocation> Allocate(
        NetworkModel network,
        IReadOnlyList<RoutingTrafficContext> contexts,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId,
        int period = 0)
    {
        adjacency = BuildAdjacency(network);
        var state = new NetworkState
        {
            RemainingEdgeCapacity = remainingCapacityByEdgeId.ToDictionary(pair => pair.Key, pair => pair.Value, Comparer),
            RemainingNodeCapacity = remainingTranshipmentCapacityByNodeId.ToDictionary(pair => pair.Key, pair => pair.Value, Comparer),
            EdgeCapacity = network.Edges.ToDictionary(edge => edge.Id, edge => edge.Capacity ?? double.PositiveInfinity, Comparer),
            NodeCapacity = network.Nodes.ToDictionary(node => node.Id, node => node.TranshipmentCapacity ?? double.PositiveInfinity, Comparer)
        };

        foreach (var edge in network.Edges)
        {
            state.EdgeLoad[edge.Id] = Math.Max(0d, state.EdgeCapacity.GetValueOrDefault(edge.Id) - state.RemainingEdgeCapacity.GetValueOrDefault(edge.Id));
        }

        foreach (var node in network.Nodes)
        {
            state.NodeLoad[node.Id] = Math.Max(0d, state.NodeCapacity.GetValueOrDefault(node.Id) - state.RemainingNodeCapacity.GetValueOrDefault(node.Id));
        }

        foreach (var context in contexts)
        {
            context.Notes.Add(context.RouteChoiceModel == RouteChoiceModel.SystemOptimal
                ? "System-optimal route choice is active: proposals internalize congestion in a shared capacity pool."
                : "Stochastic user-responsive route choice is active: proposals use seeded probabilistic route selection with congestion perception.");
        }

        var strategies = contexts.ToDictionary(
            context => context,
            context => CreateStrategy(context));
        foreach (var pair in strategies)
        {
            pair.Value.Initialize(pair.Key, state, adjacency);
        }

        var maxRounds = Math.Max(1, contexts.Select(context => context.RouteChoiceSettings.IterationCount).DefaultIfEmpty(4).Max()) * 64;
        for (var round = 0; round < maxRounds && contexts.Any(HasRemainingTraffic); round++)
        {
            var proposals = contexts
                .Where(HasRemainingTraffic)
                .SelectMany(context => strategies[context].ProposeFlows(context, state, round))
                .ToList();
            if (proposals.Count == 0)
            {
                break;
            }

            var committed = CapacityResolutionPolicy.Resolve(proposals, state);
            if (committed.Count == 0)
            {
                break;
            }

            foreach (var flow in committed)
            {
                CommitFlow(flow, period);
            }
        }

        return contexts.SelectMany(context => context.Allocations).ToList();
    }

    public static List<FlowProposal> ProposeByScore(RoutingTrafficContext context, NetworkState state, bool deterministicBest, int round)
    {
        var candidates = BuildCandidateRoutes(context, state);
        if (candidates.Count == 0)
        {
            return [];
        }

        var ranked = deterministicBest
            ? candidates.OrderBy(candidate => candidate.Score).ThenBy(candidate => candidate.PathKey, Comparer).ToList()
            : RankStochastic(context, candidates, round);
        var proposals = new List<FlowProposal>();

        if (context.FlowSplitPolicy == FlowSplitPolicy.SinglePath)
        {
            var route = ranked[0];
            var quantity = GetProposalQuantity(context, route);
            if (quantity > Epsilon)
            {
                proposals.Add(ToProposal(context, route, quantity));
            }

            return proposals;
        }

        var remainingSupplyByProducer = context.Supply.ToDictionary(pair => pair.Key, pair => pair.Value, Comparer);
        var remainingDemandByConsumer = context.Demand.ToDictionary(pair => pair.Key, pair => pair.Value, Comparer);
        var maxRoutes = Math.Max(1, context.RouteChoiceSettings.MaxCandidateRoutes);
        foreach (var route in ranked.Take(maxRoutes))
        {
            var available = Math.Min(
                remainingSupplyByProducer.GetValueOrDefault(route.ProducerNodeId),
                remainingDemandByConsumer.GetValueOrDefault(route.ConsumerNodeId));
            if (available <= Epsilon)
            {
                continue;
            }

            var share = ranked.Count == 1
                ? available
                : deterministicBest
                ? available / Math.Max(1d, maxRoutes - proposals.Count)
                : available * Math.Max(0.05d, route.Probability);
            var quantity = Math.Min(available, Math.Max(Epsilon, share));
            proposals.Add(ToProposal(context, route, quantity));
            remainingSupplyByProducer[route.ProducerNodeId] -= quantity;
            remainingDemandByConsumer[route.ConsumerNodeId] -= quantity;
        }

        return proposals;
    }

    private static void CommitFlow(CommittedFlow flow, int period)
    {
        var context = flow.TrafficType;
        var quantity = Math.Min(
            flow.Quantity,
            Math.Min(context.Supply.GetValueOrDefault(flow.ProducerNodeId), context.Demand.GetValueOrDefault(flow.ConsumerNodeId)));
        if (quantity <= Epsilon)
        {
            return;
        }

        var bidCostPerUnit = CalculateBidCostPerUnit(
            flow.PathEdgeIds,
            flow.PathTranshipmentNodeIds,
            context.CapacityBidPerUnit,
            quantity,
            flow.Priority);
        var deliveredCostPerUnit = flow.EffectiveCost + bidCostPerUnit;
        context.Allocations.Add(new RouteAllocation
        {
            Period = period,
            TrafficType = context.TrafficType,
            RoutingPreference = context.RoutingPreference,
            AllocationMode = context.AllocationMode,
            ProducerNodeId = flow.ProducerNodeId,
            ProducerName = context.NodesById[flow.ProducerNodeId].Name,
            ConsumerNodeId = flow.ConsumerNodeId,
            ConsumerName = context.NodesById[flow.ConsumerNodeId].Name,
            Quantity = quantity,
            IsLocalSupply = false,
            TotalTime = flow.EffectiveTime,
            TotalCost = flow.EffectiveCost,
            BidCostPerUnit = bidCostPerUnit,
            DeliveredCostPerUnit = deliveredCostPerUnit,
            TotalMovementCost = deliveredCostPerUnit * quantity,
            TotalScore = flow.Score,
            PathNodeNames = flow.Path.Select(nodeId => context.NodesById[nodeId].Name).ToList(),
            PathNodeIds = flow.Path.ToList(),
            PathEdgeIds = flow.PathEdgeIds.ToList()
        });

        context.Supply[flow.ProducerNodeId] -= quantity;
        context.Demand[flow.ConsumerNodeId] -= quantity;
        context.CommittedSupply[flow.ProducerNodeId] = context.CommittedSupply.GetValueOrDefault(flow.ProducerNodeId) + quantity;
        context.CommittedDemand[flow.ConsumerNodeId] = context.CommittedDemand.GetValueOrDefault(flow.ConsumerNodeId) + quantity;
        context.LastPathKey = string.Join(">", flow.PathEdgeIds);
    }

    public static void ReserveCapacity(
        IEnumerable<string> pathEdgeIds,
        IEnumerable<string> pathTranshipmentNodeIds,
        NetworkState state,
        double quantity)
    {
        ReserveCapacity(pathEdgeIds, state.RemainingEdgeCapacity, state.EdgeLoad, quantity);
        ReserveCapacity(pathTranshipmentNodeIds, state.RemainingNodeCapacity, state.NodeLoad, quantity);
    }

    public static double GetRouteRemainingCapacity(
        IReadOnlyList<string> pathEdgeIds,
        IReadOnlyList<string> pathTranshipmentNodeIds,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        return Math.Min(
            GetPathRemainingCapacity(pathEdgeIds, remainingCapacityByEdgeId),
            GetPathRemainingCapacity(pathTranshipmentNodeIds, remainingTranshipmentCapacityByNodeId));
    }

    public static Dictionary<string, List<GraphArc>> BuildAdjacency(NetworkModel network)
    {
        var result = new Dictionary<string, List<GraphArc>>(Comparer);
        foreach (var edge in network.Edges)
        {
            AddArc(edge.FromNodeId, edge.ToNodeId, edge, result);
            if (edge.IsBidirectional)
            {
                AddArc(edge.ToNodeId, edge.FromNodeId, edge, result);
            }
        }

        return result;
    }

    private static RoutingTrafficContext BuildStaticContext(NetworkModel network, TrafficTypeDefinition definition, int seed)
    {
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

        var context = new RoutingTrafficContext
        {
            TrafficType = definition.Name,
            RoutingPreference = definition.RoutingPreference,
            AllocationMode = definition.AllocationMode,
            RouteChoiceModel = definition.RouteChoiceModel,
            FlowSplitPolicy = definition.FlowSplitPolicy,
            RouteChoiceSettings = definition.RouteChoiceSettings,
            CapacityBidPerUnit = GetCapacityBidPerUnit(definition),
            Seed = seed,
            NodesById = nodesById,
            ProfilesByNodeId = profilesByNodeId,
            Supply = supply,
            Demand = demand,
            TotalProduction = supply.Values.Sum(),
            TotalConsumption = demand.Values.Sum()
        };

        ApplyLocalAllocations(context, period: 0);
        return context;
    }

    public static void ApplyLocalAllocations(RoutingTrafficContext context, int period)
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
                Period = period,
                TrafficType = context.TrafficType,
                RoutingPreference = context.RoutingPreference,
                AllocationMode = context.AllocationMode,
                ProducerNodeId = nodeId,
                ProducerName = node.Name,
                ConsumerNodeId = nodeId,
                ConsumerName = node.Name,
                Quantity = quantity,
                IsLocalSupply = true,
                PathNodeNames = [node.Name],
                PathNodeIds = [nodeId],
                PathEdgeIds = []
            });
            context.Supply[nodeId] -= quantity;
            context.Demand[nodeId] -= quantity;
        }
    }

    private static List<RouteCandidate> BuildCandidateRoutes(RoutingTrafficContext context, NetworkState state)
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

                routes.AddRange(FindCandidateRoutes(context, producerNodeId, consumerNodeId, state));
            }
        }

        return routes
            .GroupBy(route => route.PathKey, Comparer)
            .Select(group => group.OrderBy(route => route.Score).First())
            .OrderBy(route => route.Score)
            .Take(Math.Max(1, context.RouteChoiceSettings.MaxCandidateRoutes))
            .ToList();
    }

    private static List<RouteCandidate> FindCandidateRoutes(
        RoutingTrafficContext context,
        string producerNodeId,
        string consumerNodeId,
        NetworkState state)
    {
        var result = new List<RouteCandidate>();
        var queue = new PriorityQueue<RouteSearchState, double>();
        queue.Enqueue(new RouteSearchState(producerNodeId, [producerNodeId], [], 0d, 0d, 0d), 0d);
        var expansions = 0;
        var maxCandidates = Math.Max(1, context.RouteChoiceSettings.MaxCandidateRoutes);
        var maxDepth = Math.Max(3, context.NodesById.Count);

        while (queue.TryDequeue(out var current, out _) && result.Count < maxCandidates && expansions < 5000)
        {
            expansions++;
            if (Comparer.Equals(current.NodeId, consumerNodeId))
            {
                result.Add(ToCandidate(context, producerNodeId, consumerNodeId, current, state));
                continue;
            }

            if (current.PathNodeIds.Count > maxDepth || !adjacency.TryGetValue(current.NodeId, out var arcs))
            {
                continue;
            }

            foreach (var arc in arcs)
            {
                if (current.PathNodeIds.Contains(arc.ToNodeId, Comparer) ||
                    state.RemainingEdgeCapacity.GetValueOrDefault(arc.EdgeId) <= Epsilon)
                {
                    continue;
                }

                if (IsIntermediateNode(arc.ToNodeId, producerNodeId, consumerNodeId) &&
                    state.RemainingNodeCapacity.GetValueOrDefault(arc.ToNodeId) <= Epsilon)
                {
                    continue;
                }

                if (!CanTraverseNode(arc.ToNodeId, producerNodeId, consumerNodeId, context.ProfilesByNodeId))
                {
                    continue;
                }

                var effectiveTime = GetEffectiveArcTime(context, arc, state);
                var effectiveCost = GetEffectiveArcCost(context, arc, state);
                var score = current.Score + Score(effectiveTime, effectiveCost, context.RoutingPreference);
                var next = new RouteSearchState(
                    arc.ToNodeId,
                    current.PathNodeIds.Concat([arc.ToNodeId]).ToList(),
                    current.PathEdgeIds.Concat([arc.EdgeId]).ToList(),
                    current.BaseTime + arc.Time,
                    current.BaseCost + arc.Cost,
                    score);
                queue.Enqueue(next, score);
            }
        }

        return result;
    }

    private static RouteCandidate ToCandidate(
        RoutingTrafficContext context,
        string producerNodeId,
        string consumerNodeId,
        RouteSearchState route,
        NetworkState state)
    {
        var transhipmentNodeIds = GetIntermediateNodeIds(route.PathNodeIds);
        var arcs = route.PathEdgeIds.Select(FindArc).Where(arc => arc is not null).Cast<GraphArc>().ToList();
        var effectiveTime = arcs.Sum(arc => GetEffectiveArcTime(context, arc, state));
        var effectiveCost = arcs.Sum(arc => GetEffectiveArcCost(context, arc, state));
        var score = Score(effectiveTime, effectiveCost, context.RoutingPreference);
        var pathKey = string.Join(">", route.PathEdgeIds);
        if (!string.IsNullOrEmpty(context.LastPathKey) && Comparer.Equals(pathKey, context.LastPathKey))
        {
            score *= Math.Max(0d, 1d - context.RouteChoiceSettings.Stickiness);
        }

        return new RouteCandidate(
            producerNodeId,
            consumerNodeId,
            route.PathNodeIds,
            route.PathEdgeIds,
            transhipmentNodeIds,
            route.BaseTime,
            route.BaseCost,
            effectiveTime,
            effectiveCost,
            score,
            pathKey,
            1d);
    }

    private static List<RouteCandidate> RankStochastic(RoutingTrafficContext context, IReadOnlyList<RouteCandidate> candidates, int round)
    {
        var rng = new Random(HashCode.Combine(context.Seed, round, StringComparer.OrdinalIgnoreCase.GetHashCode(context.TrafficType)));
        var diversity = Math.Max(0.01d, context.RouteChoiceSettings.RouteDiversity);
        var lambda = 1d / diversity;
        var bestActual = candidates.Min(candidate => candidate.Score);
        var perceived = candidates.Select(candidate =>
        {
            var noiseScale = Math.Max(0d, 1d - context.RouteChoiceSettings.InformationAccuracy);
            var noise = (rng.NextDouble() - 0.5d) * noiseScale * Math.Max(1d, candidate.Score);
            var score = candidate.Score + noise;
            if (!string.IsNullOrEmpty(context.LastPathKey) &&
                !Comparer.Equals(candidate.PathKey, context.LastPathKey) &&
                Math.Abs(candidate.Score - bestActual) <= context.RouteChoiceSettings.RerouteThreshold)
            {
                score += context.RouteChoiceSettings.Stickiness * Math.Max(1d, candidate.Score);
            }

            return candidate with { Probability = Math.Exp(-lambda * Math.Max(0d, score)) };
        }).ToList();
        var total = perceived.Sum(candidate => candidate.Probability);
        if (total <= Epsilon)
        {
            return perceived.OrderBy(candidate => candidate.Score).ToList();
        }

        perceived = perceived.Select(candidate => candidate with { Probability = candidate.Probability / total }).ToList();
        if (context.FlowSplitPolicy == FlowSplitPolicy.SinglePath)
        {
            var roll = rng.NextDouble();
            var cumulative = 0d;
            foreach (var candidate in perceived.OrderBy(candidate => candidate.PathKey, Comparer))
            {
                cumulative += candidate.Probability;
                if (roll <= cumulative)
                {
                    return [candidate];
                }
            }
        }

        return perceived.OrderByDescending(candidate => candidate.Probability).ThenBy(candidate => candidate.PathKey, Comparer).ToList();
    }

    private static FlowProposal ToProposal(RoutingTrafficContext context, RouteCandidate route, double quantity)
    {
        return new FlowProposal(
            context,
            route.ProducerNodeId,
            route.ConsumerNodeId,
            quantity,
            route.PathNodeIds,
            route.PathEdgeIds,
            route.PathTranshipmentNodeIds,
            route.BaseTime,
            route.BaseCost,
            route.EffectiveTime,
            route.EffectiveCost,
            route.Score,
            Math.Max(Epsilon, context.RouteChoiceSettings.Priority));
    }

    private static double GetProposalQuantity(RoutingTrafficContext context, RouteCandidate route)
    {
        return Math.Min(
            context.Supply.GetValueOrDefault(route.ProducerNodeId),
            context.Demand.GetValueOrDefault(route.ConsumerNodeId));
    }

    private static IRouteChoiceStrategy CreateStrategy(RoutingTrafficContext context)
    {
        return context.RouteChoiceModel == RouteChoiceModel.SystemOptimal
            ? new SystemOptimalRouteChoiceStrategy()
            : new StochasticUserResponsiveRouteChoiceStrategy();
    }

    private static bool HasRemainingTraffic(RoutingTrafficContext context)
    {
        return context.Supply.Values.Any(value => value > Epsilon) && context.Demand.Values.Any(value => value > Epsilon);
    }

    private static void AddImplicitRecipeDemand(NetworkModel network, string trafficType, IDictionary<string, double> demand)
    {
        foreach (var node in network.Nodes)
        {
            var implicitDemand = node.TrafficProfiles
                .Where(profile => profile.Production > Epsilon)
                .SelectMany(profile => profile.InputRequirements.Select(requirement => new { profile.Production, requirement }))
                .Where(item => Comparer.Equals(item.requirement.TrafficType, trafficType))
                .Sum(item => item.Production * item.requirement.QuantityPerOutputUnit);
            if (implicitDemand > Epsilon)
            {
                demand[node.Id] = GetOrZero(demand, node.Id) + implicitDemand;
            }
        }
    }

    private static double GetEffectiveArcTime(RoutingTrafficContext context, GraphArc arc, NetworkState state)
    {
        var alpha = context.RouteChoiceModel == RouteChoiceModel.SystemOptimal && !context.RouteChoiceSettings.InternalizeCongestion
            ? 0d
            : context.RouteChoiceSettings.CongestionSensitivity;
        return CongestionCostModel.GetEffectiveTime(
            arc.Time,
            state.EdgeLoad.GetValueOrDefault(arc.EdgeId),
            state.EdgeCapacity.GetValueOrDefault(arc.EdgeId, double.PositiveInfinity),
            alpha);
    }

    private static double GetEffectiveArcCost(RoutingTrafficContext context, GraphArc arc, NetworkState state)
    {
        var gamma = context.RouteChoiceModel == RouteChoiceModel.SystemOptimal && !context.RouteChoiceSettings.InternalizeCongestion
            ? 0d
            : context.RouteChoiceSettings.CongestionSensitivity;
        return CongestionCostModel.GetEffectiveCost(
            arc.Cost,
            state.EdgeLoad.GetValueOrDefault(arc.EdgeId),
            state.EdgeCapacity.GetValueOrDefault(arc.EdgeId, double.PositiveInfinity),
            gamma);
    }

    private static GraphArc? FindArc(string edgeId)
    {
        return adjacency.Values.SelectMany(arcs => arcs).FirstOrDefault(arc => Comparer.Equals(arc.EdgeId, edgeId));
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

    private static bool IsIntermediateNode(string nodeId, string producerNodeId, string consumerNodeId)
    {
        return !Comparer.Equals(nodeId, producerNodeId) && !Comparer.Equals(nodeId, consumerNodeId);
    }

    private static IReadOnlyList<string> GetIntermediateNodeIds(IReadOnlyList<string> pathNodeIds)
    {
        return pathNodeIds.Count <= 2 ? [] : pathNodeIds.Skip(1).Take(pathNodeIds.Count - 2).ToList();
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

    private static void ReserveCapacity(
        IEnumerable<string> pathResourceIds,
        IDictionary<string, double> remainingCapacityById,
        IDictionary<string, double> loadById,
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
            loadById[resourceId] = GetOrZero(loadById, resourceId) + quantity;
        }
    }

    private static double GetPathRemainingCapacity(
        IReadOnlyList<string> pathResourceIds,
        IDictionary<string, double> remainingCapacityById)
    {
        return pathResourceIds.Count == 0
            ? double.PositiveInfinity
            : pathResourceIds.Select(resourceId => GetOrZero(remainingCapacityById, resourceId)).DefaultIfEmpty(0d).Min();
    }

    private static double GetOrZero(IDictionary<string, double> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : 0d;
    }

    private static double CalculateBidCostPerUnit(
        IReadOnlyList<string> pathEdgeIds,
        IReadOnlyList<string> pathTranshipmentNodeIds,
        double bid,
        double quantity,
        double priority)
    {
        if (bid <= Epsilon || quantity <= Epsilon)
        {
            return 0d;
        }

        return (pathEdgeIds.Count + pathTranshipmentNodeIds.Count) * bid / Math.Max(1d, priority);
    }

    private static double GetCapacityBidPerUnit(TrafficTypeDefinition definition)
    {
        if (definition.CapacityBidPerUnit.HasValue)
        {
            return Math.Max(0d, definition.CapacityBidPerUnit.Value);
        }

        return definition.RoutingPreference == RoutingPreference.Speed ? 1d : 0d;
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

        orderedTrafficNames.AddRange(network.Nodes
            .SelectMany(node => node.TrafficProfiles)
            .Select(profile => profile.TrafficType)
            .Concat(network.Nodes
                .SelectMany(node => node.TrafficProfiles)
                .SelectMany(profile => profile.InputRequirements)
                .Select(requirement => requirement.TrafficType))
            .Where(name => !string.IsNullOrWhiteSpace(name) && !seen.Contains(name))
            .Distinct(Comparer)
            .OrderBy(name => name, Comparer));
        return orderedTrafficNames;
    }

    private static void AddArc(string fromNodeId, string toNodeId, EdgeModel edge, IDictionary<string, List<GraphArc>> result)
    {
        if (!result.TryGetValue(fromNodeId, out var arcs))
        {
            arcs = [];
            result[fromNodeId] = arcs;
        }

        arcs.Add(new GraphArc(edge.Id, fromNodeId, toNodeId, edge.Time, edge.Cost));
    }

    private sealed record RouteSearchState(
        string NodeId,
        IReadOnlyList<string> PathNodeIds,
        IReadOnlyList<string> PathEdgeIds,
        double BaseTime,
        double BaseCost,
        double Score);

    private sealed record RouteCandidate(
        string ProducerNodeId,
        string ConsumerNodeId,
        IReadOnlyList<string> PathNodeIds,
        IReadOnlyList<string> PathEdgeIds,
        IReadOnlyList<string> PathTranshipmentNodeIds,
        double BaseTime,
        double BaseCost,
        double EffectiveTime,
        double EffectiveCost,
        double Score,
        string PathKey,
        double Probability);
}

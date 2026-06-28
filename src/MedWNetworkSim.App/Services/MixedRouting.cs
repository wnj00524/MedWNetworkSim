using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;
/// <summary>
/// Defines the contract and required members for iroute choice strategy implementations.
/// </summary>

public interface IRouteChoiceStrategy
{
    void Initialize(RoutingTrafficContext context, NetworkState networkState, AllocationContext allocationContext);

    List<FlowProposal> ProposeFlows(RoutingTrafficContext context, NetworkState networkState, AllocationContext allocationContext, int round);
}
/// <summary>
/// Defines the contract and required members for icapacity resolution policy implementations.
/// </summary>

public interface ICapacityResolutionPolicy
{
    List<CommittedFlow> Resolve(IEnumerable<FlowProposal> proposals, NetworkState networkState);
}
/// <summary>
/// Represents a data model for icongestion cost entities within the simulation.
/// </summary>

public interface ICongestionCostModel
{
    double GetEffectiveTime(double baseTime, double used, double capacity, double alpha);

    double GetEffectiveCost(double baseCost, double used, double capacity, double gamma);
}
/// <summary>
/// Represents the network state component.
/// </summary>

public sealed class NetworkState
{
    /// <summary>
    /// Gets or sets the remaining edge capacity.
    /// </summary>
    public Dictionary<string, double> RemainingEdgeCapacity { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the remaining node capacity.
    /// </summary>

    public Dictionary<string, double> RemainingNodeCapacity { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the remaining edge traffic capacity.
    /// </summary>

    public Dictionary<EdgeTrafficResourceKey, double> RemainingEdgeTrafficCapacity { get; init; } = new(EdgeTrafficResourceKey.Comparer);
    /// <summary>
    /// Gets or sets the edge load.
    /// </summary>

    public Dictionary<string, double> EdgeLoad { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the node load.
    /// </summary>

    public Dictionary<string, double> NodeLoad { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the edge capacity.
    /// </summary>

    public Dictionary<string, double> EdgeCapacity { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the node capacity.
    /// </summary>

    public Dictionary<string, double> NodeCapacity { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the edge traffic load.
    /// </summary>

    public Dictionary<EdgeTrafficResourceKey, double> EdgeTrafficLoad { get; init; } = new(EdgeTrafficResourceKey.Comparer);
}
/// <summary>
/// Represents the flow proposal component.
/// </summary>

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
/// <summary>
/// Represents the committed flow component.
/// </summary>

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
/// <summary>
/// Represents the routing traffic context component.
/// </summary>

public sealed class RoutingTrafficContext
{
    /// <summary>
    /// Gets or sets the traffic type.
    /// </summary>
    public string TrafficType { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the routing preference.
    /// </summary>
    public RoutingPreference RoutingPreference { get; init; }
    /// <summary>
    /// Gets or sets the allocation mode.
    /// </summary>
    public AllocationMode AllocationMode { get; init; }
    /// <summary>
    /// Gets or sets the route choice model.
    /// </summary>
    public RouteChoiceModel RouteChoiceModel { get; init; }
    /// <summary>
    /// Gets or sets the flow split policy.
    /// </summary>
    public FlowSplitPolicy FlowSplitPolicy { get; init; }
    /// <summary>
    /// Gets or sets the route choice settings.
    /// </summary>
    public RouteChoiceSettings RouteChoiceSettings { get; init; } = new();
    /// <summary>
    /// Gets or sets the capacity bid per unit.
    /// </summary>
    public double CapacityBidPerUnit { get; init; }
    /// <summary>
    /// Gets or sets the seed.
    /// </summary>
    public int Seed { get; init; }
    /// <summary>
    /// Gets or sets the nodes by id.
    /// </summary>
    public IReadOnlyDictionary<string, NodeModel> NodesById { get; init; } = new Dictionary<string, NodeModel>(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the profiles by node id.
    /// </summary>
    public IReadOnlyDictionary<string, NodeTrafficProfile?> ProfilesByNodeId { get; init; } = new Dictionary<string, NodeTrafficProfile?>(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the meeting demand eligible node ids.
    /// </summary>
    public IReadOnlySet<string> MeetingDemandEligibleNodeIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the supply.
    /// </summary>
    public Dictionary<string, double> Supply { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the supply unit costs.
    /// </summary>
    public Dictionary<string, double> SupplyUnitCosts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the demand.
    /// </summary>
    public Dictionary<string, double> Demand { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the total production.
    /// </summary>
    public double TotalProduction { get; init; }
    /// <summary>
    /// Gets or sets the total consumption.
    /// </summary>
    public double TotalConsumption { get; init; }
    /// <summary>
    /// Gets the collection of allocations associated with this entity.
    /// </summary>
    public List<RouteAllocation> Allocations { get; init; } = [];
    /// <summary>
    /// Gets the collection of notes associated with this entity.
    /// </summary>
    public List<string> Notes { get; init; } = [];
    /// <summary>
    /// Gets or sets the committed supply.
    /// </summary>
    public Dictionary<string, double> CommittedSupply { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the committed demand.
    /// </summary>
    public Dictionary<string, double> CommittedDemand { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the last path key.
    /// </summary>
    public string? LastPathKey { get; set; }
    /// <summary>
    /// Gets or sets the permission limited demand.
    /// </summary>
    public double PermissionLimitedDemand { get; set; }
    /// <summary>
    /// Gets or sets the no permitted path demand.
    /// </summary>
    public double NoPermittedPathDemand { get; set; }
    /// <summary>
    /// Gets or sets the capacity blocked demand.
    /// </summary>
    public double CapacityBlockedDemand { get; set; }
}
/// <summary>
/// Represents the graph arc component.
/// </summary>

public readonly record struct GraphArc(string EdgeId, string FromNodeId, string ToNodeId, double Time, double Cost);

public sealed record AllocationContext(
    IReadOnlyDictionary<string, List<GraphArc>> Adjacency,
    IReadOnlyDictionary<string, EdgeModel> EdgesById);
/// <summary>
/// Represents a data model for volume capacity congestion cost entities within the simulation.
/// </summary>

public sealed class VolumeCapacityCongestionCostModel : ICongestionCostModel
{
    private const double Beta = 4d;
    /// <summary>
    /// Retrieves the effective time based on the provided parameters.
    /// </summary>

    public double GetEffectiveTime(double baseTime, double used, double capacity, double alpha)
    {
        if (double.IsPositiveInfinity(capacity) || capacity <= 0d)
        {
            return baseTime;
        }

        var util = Math.Max(0d, used) / capacity;
        return baseTime * (1d + alpha * Math.Pow(util, Beta));
    }
    /// <summary>
    /// Retrieves the effective cost based on the provided parameters.
    /// </summary>

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
/// <summary>
/// Represents the priority weighted capacity resolution policy component.
/// </summary>

public sealed class PriorityWeightedCapacityResolutionPolicy : ICapacityResolutionPolicy
{
    private const double Epsilon = 0.000001d;
    /// <summary>
    /// Executes the resolve operation.
    /// </summary>

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
                proposal.TrafficType.TrafficType,
                proposal.PathEdgeIds,
                proposal.PathTranshipmentNodeIds,
                networkState);
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
                proposal.TrafficType.TrafficType,
                proposal.PathEdgeIds,
                proposal.PathTranshipmentNodeIds,
                networkState,
                quantity);
        }

        return result;
    }
}
/// <summary>
/// Represents the system optimal route choice strategy component.
/// </summary>

public sealed class SystemOptimalRouteChoiceStrategy : IRouteChoiceStrategy
{
    /// <summary>
    /// Executes the initialize operation.
    /// </summary>
    public void Initialize(RoutingTrafficContext context, NetworkState networkState, AllocationContext allocationContext)
    {
    }
    /// <summary>
    /// Executes the propose flows operation.
    /// </summary>

    public List<FlowProposal> ProposeFlows(RoutingTrafficContext context, NetworkState networkState, AllocationContext allocationContext, int round)
    {
        return MixedRoutingAllocator.ProposeByScore(context, networkState, allocationContext, deterministicBest: true, round);
    }
}
/// <summary>
/// Represents the stochastic user responsive route choice strategy component.
/// </summary>

public sealed class StochasticUserResponsiveRouteChoiceStrategy : IRouteChoiceStrategy
{
    /// <summary>
    /// Executes the initialize operation.
    /// </summary>
    public void Initialize(RoutingTrafficContext context, NetworkState networkState, AllocationContext allocationContext)
    {
    }
    /// <summary>
    /// Executes the propose flows operation.
    /// </summary>

    public List<FlowProposal> ProposeFlows(RoutingTrafficContext context, NetworkState networkState, AllocationContext allocationContext, int round)
    {
        return MixedRoutingAllocator.ProposeByScore(context, networkState, allocationContext, deterministicBest: false, round);
    }
}

/// <summary>
/// Handles complex, heterogeneous routing capacity allocations and score-based pathfinding strategies.
/// Mixed routing coordinates scenarios where different traffic types or agents might employ disparate
/// pathfinding heuristics simultaneously across the shared network graph, ensuring proper capacity reservations.
/// </summary>
public static partial class MixedRoutingAllocator
{
    private const double Epsilon = 0.000001d;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private static readonly ICongestionCostModel CongestionCostModel = new VolumeCapacityCongestionCostModel();
    private static readonly ICapacityResolutionPolicy CapacityResolutionPolicy = new PriorityWeightedCapacityResolutionPolicy();
    private static readonly IAdaptiveRoutingMemory AdaptiveRoutingMemory = new AdaptiveRoutingMemory();
    /// <summary>
    /// Executes the build static contexts operation.
    /// </summary>

    public static IReadOnlyList<RoutingTrafficContext> BuildStaticContexts(NetworkModel network, bool applyLocalAllocations = true)
    {
        var definitionsByTraffic = network.TrafficTypes.ToDictionary(definition => definition.Name, definition => definition, Comparer);
        return GetOrderedTrafficNames(network)
            .Select((trafficType, index) =>
            {
                var definition = definitionsByTraffic.GetValueOrDefault(trafficType)
                    ?? new TrafficTypeDefinition { Name = trafficType };
                return BuildStaticContext(network, definition, network.SimulationSeed + (index * 997), applyLocalAllocations);
            })
            .ToList();
    }
    /// <summary>
    /// Executes the allocate operation.
    /// </summary>

    public static IReadOnlyList<RouteAllocation> Allocate(
        NetworkModel network,
        IReadOnlyList<RoutingTrafficContext> contexts,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId,
        IReadOnlyDictionary<EdgeTrafficResourceKey, double>? occupiedEdgeTrafficByKey = null,
        int period = 0,
        CompiledNetworkSimulationContext? compiledContext = null)
    {
        if (compiledContext is not null)
        {
            return AllocateIndexed(
                network,
                contexts,
                remainingCapacityByEdgeId,
                remainingTranshipmentCapacityByNodeId,
                occupiedEdgeTrafficByKey,
                period,
                compiledContext);
        }

        var permissionResolver = new EdgeTrafficPermissionResolver();
        IReadOnlyDictionary<string, EdgeModel> edgesById = network.Edges.ToDictionary(edge => edge.Id, edge => edge, Comparer);
        var adjacency = BuildAdjacency(network);
        var allocationContext = new AllocationContext(adjacency, edgesById);
        var state = new NetworkState
        {
            RemainingEdgeCapacity = remainingCapacityByEdgeId.ToDictionary(pair => pair.Key, pair => pair.Value, Comparer),
            RemainingNodeCapacity = remainingTranshipmentCapacityByNodeId.ToDictionary(pair => pair.Key, pair => pair.Value, Comparer),
            RemainingEdgeTrafficCapacity = permissionResolver.BuildInitialRemainingAllowances(
                network,
                contexts.Select(context => context.TrafficType),
                occupiedEdgeTrafficByKey),
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

        foreach (var pair in state.RemainingEdgeTrafficCapacity)
        {
            var allowed = permissionResolver.GetAllowedCapacity(
                edgesById[pair.Key.EdgeId],
                permissionResolver.Resolve(network, edgesById[pair.Key.EdgeId], pair.Key.TrafficType));
            var occupied = occupiedEdgeTrafficByKey?.TryGetValue(pair.Key, out var value) == true ? value : 0d;
            state.EdgeTrafficLoad[pair.Key] = double.IsPositiveInfinity(allowed)
                ? Math.Max(0d, occupied)
                : Math.Max(0d, allowed - pair.Value);
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
            pair.Value.Initialize(pair.Key, state, allocationContext);
        }

        var maxRounds = Math.Max(1, contexts.Select(context => context.RouteChoiceSettings.IterationCount).DefaultIfEmpty(4).Max()) * 64;
        for (var round = 0; round < maxRounds && contexts.Any(HasRemainingTraffic); round++)
        {
            var proposals = contexts
                .Where(HasRemainingTraffic)
                .SelectMany(context => strategies[context].ProposeFlows(context, state, allocationContext, round))
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

        foreach (var context in contexts)
        {
            RecordAdaptiveObservations(context, state);
            ClassifyRemainingRestrictions(network, context, state, allocationContext);
        }

        CopyRemainingCapacity(state.RemainingEdgeCapacity, remainingCapacityByEdgeId);
        CopyRemainingCapacity(state.RemainingNodeCapacity, remainingTranshipmentCapacityByNodeId);

        return contexts.SelectMany(context => context.Allocations).ToList();
    }

    private static void CopyRemainingCapacity(
        IReadOnlyDictionary<string, double> source,
        IDictionary<string, double> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }
    /// <summary>
    /// Executes the propose by score operation.
    /// </summary>

    public static List<FlowProposal> ProposeByScore(RoutingTrafficContext context, NetworkState state, AllocationContext allocationContext, bool deterministicBest, int round)
    {
        var candidates = BuildCandidateRoutes(context, state, allocationContext);
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
        var sourceUnitCostPerUnit = context.SupplyUnitCosts.GetValueOrDefault(flow.ProducerNodeId);
        var deliveredCostPerUnit = sourceUnitCostPerUnit + flow.EffectiveCost + bidCostPerUnit;
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
            SourceUnitCostPerUnit = sourceUnitCostPerUnit,
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
    /// <summary>
    /// Executes the reserve capacity operation.
    /// </summary>

    public static void ReserveCapacity(
        string trafficType,
        IEnumerable<string> pathEdgeIds,
        IEnumerable<string> pathTranshipmentNodeIds,
        NetworkState state,
        double quantity)
    {
        ReserveCapacity(pathEdgeIds, state.RemainingEdgeCapacity, state.EdgeLoad, quantity);
        ReserveTrafficCapacity(trafficType, pathEdgeIds, state.RemainingEdgeTrafficCapacity, state.EdgeTrafficLoad, quantity);
        ReserveCapacity(pathTranshipmentNodeIds, state.RemainingNodeCapacity, state.NodeLoad, quantity);
    }
    /// <summary>
    /// Retrieves the route remaining capacity based on the provided parameters.
    /// </summary>

    public static double GetRouteRemainingCapacity(
        string trafficType,
        IReadOnlyList<string> pathEdgeIds,
        IReadOnlyList<string> pathTranshipmentNodeIds,
        NetworkState state)
    {
        return Math.Min(
            Math.Min(
                GetPathRemainingCapacity(pathEdgeIds, state.RemainingEdgeCapacity),
                GetTrafficPathRemainingCapacity(trafficType, pathEdgeIds, state.RemainingEdgeTrafficCapacity)),
            GetPathRemainingCapacity(pathTranshipmentNodeIds, state.RemainingNodeCapacity));
    }
    /// <summary>
    /// Executes the build adjacency operation.
    /// </summary>

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

    public static Dictionary<string, List<GraphArc>> BuildAdjacency(CompiledNetworkSimulationContext compiledContext)
    {
        var result = new Dictionary<string, List<GraphArc>>(compiledContext.NodesByIndex.Length, Comparer);
        for (var fromIndex = 0; fromIndex < compiledContext.AdjacencyByNodeIndex.Length; fromIndex++)
        {
            var sourceArcs = compiledContext.AdjacencyByNodeIndex[fromIndex];
            if (sourceArcs.Length == 0)
            {
                continue;
            }

            var arcs = new List<GraphArc>(sourceArcs.Length);
            for (var arcIndex = 0; arcIndex < sourceArcs.Length; arcIndex++)
            {
                var arc = sourceArcs[arcIndex];
                arcs.Add(new GraphArc(
                    compiledContext.EdgeIdsByIndex[arc.EdgeIndex],
                    compiledContext.NodeIdsByIndex[arc.FromNodeIndex],
                    compiledContext.NodeIdsByIndex[arc.ToNodeIndex],
                    arc.Time,
                    arc.Cost));
            }

            result[compiledContext.NodeIdsByIndex[fromIndex]] = arcs;
        }

        return result;
    }

    private static RoutingTrafficContext BuildStaticContext(NetworkModel network, TrafficTypeDefinition definition, int seed, bool applyLocalAllocations)
    {
        var profilesByNodeId = network.Nodes.ToDictionary(
            node => node.Id,
            node => node.TrafficProfiles.FirstOrDefault(profile => Comparer.Equals(profile.TrafficType, definition.Name)),
            Comparer);
        var nodesById = network.Nodes.ToDictionary(node => node.Id, node => node, Comparer);
        var permittedSellerNodeIds = LocalTrafficPermissionResolver.BuildPermittedSellerNodeSet(network, definition.Name);
        var enforceSellLocal = LocalTrafficPermissionResolver.IsEnforced(network);
        var blockedLocalSupply = 0d;
        var supply = profilesByNodeId
            .Where(pair => pair.Value?.Production > Epsilon)
            .Where(pair =>
            {
                if (!enforceSellLocal || permittedSellerNodeIds.Contains(pair.Key))
                {
                    return true;
                }

                blockedLocalSupply += pair.Value!.Production;
                return false;
            })
            .ToDictionary(pair => pair.Key, pair => pair.Value!.Production, Comparer);
        var supplyUnitCosts = supply.ToDictionary(
            pair => pair.Key,
            pair => ResolveBaseProductionCost(profilesByNodeId.GetValueOrDefault(pair.Key), definition),
            Comparer);
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
            MeetingDemandEligibleNodeIds = demand.Keys
                .Where(nodeId => LocalTrafficPermissionResolver.CanReceiveMeetingNodeDemand(network, nodeId, definition.Name))
                .ToHashSet(Comparer),
            Supply = supply,
            SupplyUnitCosts = supplyUnitCosts,
            Demand = demand,
            TotalProduction = supply.Values.Sum(),
            TotalConsumption = demand.Values.Sum()
        };

        if (enforceSellLocal)
        {
            context.Notes.Add(blockedLocalSupply > Epsilon
                ? $"Agent mode Sell local is active: {blockedLocalSupply:0.##} unit(s) of supply were withheld because the node owner lacks explicit SellLocal permission."
                : "Agent mode Sell local is active: only supply from actors with explicit SellLocal permission can fulfil demand.");
        }

        if (LocalTrafficPermissionResolver.ShouldLimitMeetingNodeDemand(network))
        {
            foreach (var nodeId in demand.Keys
                .Where(nodeId => !LocalTrafficPermissionResolver.CanReceiveMeetingNodeDemand(network, nodeId, definition.Name))
                .OrderBy(nodeId => nodeId, Comparer))
            {
                var nodeName = nodesById.TryGetValue(nodeId, out var node) ? node.Name : nodeId;
                context.Notes.Add($"Sell local meeting-demand limit is active: demand at {nodeName} cannot be satisfied because no controlling actor has SellLocal permission.");
            }
        }

        if (applyLocalAllocations)
        {
            ApplyLocalAllocations(context, network, period: 0);
        }

        return context;
    }
    /// <summary>
    /// Executes the apply local allocations operation.
    /// </summary>

    public static void ApplyLocalAllocations(RoutingTrafficContext context, NetworkModel network, int period)
    {
        foreach (var nodeId in context.Supply.Keys.Intersect(context.Demand.Keys, Comparer).ToList())
        {
            if (!LocalTrafficPermissionResolver.CanReceiveMeetingNodeDemand(network, nodeId, context.TrafficType))
            {
                var nodeName = context.NodesById.TryGetValue(nodeId, out var localNode) ? localNode.Name : nodeId;
                context.Notes.Add($"Sell local meeting-demand limit is active: local demand at {nodeName} was not satisfied because no controlling actor has SellLocal permission.");
                continue;
            }

            var quantity = Math.Min(context.Supply[nodeId], context.Demand[nodeId]);
            if (quantity <= Epsilon)
            {
                continue;
            }

            var node = context.NodesById[nodeId];
            var sourceUnitCostPerUnit = context.SupplyUnitCosts.GetValueOrDefault(nodeId);
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
                SourceUnitCostPerUnit = sourceUnitCostPerUnit,
                DeliveredCostPerUnit = sourceUnitCostPerUnit,
                TotalMovementCost = sourceUnitCostPerUnit * quantity,
                PathNodeNames = [node.Name],
                PathNodeIds = [nodeId],
                PathEdgeIds = []
            });
            context.Supply[nodeId] -= quantity;
            context.Demand[nodeId] -= quantity;
        }
    }

    private static List<RouteCandidate> BuildCandidateRoutes(RoutingTrafficContext context, NetworkState state, AllocationContext allocationContext)
    {
        var routes = new List<RouteCandidate>();

        // Bolt: Replaced LINQ with foreach to prevent delegate allocations in the hot loop
        var activeProducers = new List<string>();
        foreach (var pair in context.Supply)
        {
            if (pair.Value > Epsilon) activeProducers.Add(pair.Key);
        }

        var activeConsumers = new HashSet<string>(Comparer);
        foreach (var pair in context.Demand)
        {
            if (pair.Value > Epsilon && context.MeetingDemandEligibleNodeIds.Contains(pair.Key))
            {
                activeConsumers.Add(pair.Key);
            }
        }

        foreach (var producerNodeId in activeProducers)
        {
            // Bolt: Replaced O(C) LINQ iteration with O(1) HashSet copy and remove to prevent O(P * C) bottleneck
            var targetConsumers = new HashSet<string>(activeConsumers, Comparer);
            targetConsumers.Remove(producerNodeId);

            if (targetConsumers.Count == 0)
            {
                continue;
            }

            foreach (var consumerNodeId in targetConsumers)
            {
                routes.AddRange(FindCandidateRoutes(context, producerNodeId, consumerNodeId, state, allocationContext));
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
        NetworkState state,
        AllocationContext allocationContext)
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
                result.Add(ToCandidate(context, producerNodeId, consumerNodeId, current, state, allocationContext));
                continue;
            }

            if (current.PathNodeIds.Count > maxDepth || !allocationContext.Adjacency.TryGetValue(current.NodeId, out var arcs))
            {
                continue;
            }

            foreach (var arc in arcs)
            {
                if (current.PathNodeIds.Contains(arc.ToNodeId, Comparer) ||
                    state.RemainingEdgeCapacity.GetValueOrDefault(arc.EdgeId) <= Epsilon ||
                    state.RemainingEdgeTrafficCapacity.GetValueOrDefault(new EdgeTrafficResourceKey(arc.EdgeId, context.TrafficType)) <= Epsilon)
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
        NetworkState state,
        AllocationContext allocationContext)
    {
        var transhipmentNodeIds = GetIntermediateNodeIds(route.PathNodeIds);
        var effectiveTime = 0d;
        var effectiveCost = 0d;
        for (var index = 0; index < route.PathEdgeIds.Count; index++)
        {
            if (TryFindArc(route.PathEdgeIds[index], allocationContext, out var arc))
            {
                // Bolt: Accumulated values inline to eliminate list allocation and LINQ .Sum() overhead
                effectiveTime += GetEffectiveArcTime(context, arc, state);
                effectiveCost += GetEffectiveArcCost(context, arc, state);
            }
        }
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

    private static void ClassifyRemainingRestrictions(NetworkModel network, RoutingTrafficContext context, NetworkState state, AllocationContext allocationContext)
    {
        if (!HasRemainingTraffic(context))
        {
            return;
        }

        foreach (var producerNodeId in context.Supply.Where(pair => pair.Value > Epsilon).Select(pair => pair.Key))
        {
            foreach (var consumerNodeId in context.Demand.Where(pair => pair.Value > Epsilon).Select(pair => pair.Key))
            {
                if (Comparer.Equals(producerNodeId, consumerNodeId))
                {
                    continue;
                }

                var quantity = Math.Min(
                    context.Supply.GetValueOrDefault(producerNodeId),
                    context.Demand.GetValueOrDefault(consumerNodeId));
                if (quantity <= Epsilon)
                {
                    continue;
                }

                if (!HasAnyFeasibleRoute(network, context, producerNodeId, consumerNodeId, state, allocationContext, RouteConstraintMode.BlockedOnly))
                {
                    context.NoPermittedPathDemand += quantity;
                }
                else if (!HasAnyFeasibleRoute(network, context, producerNodeId, consumerNodeId, state, allocationContext, RouteConstraintMode.PermissionLimited))
                {
                    context.PermissionLimitedDemand += quantity;
                }
                else if (!HasAnyFeasibleRoute(network, context, producerNodeId, consumerNodeId, state, allocationContext, RouteConstraintMode.AllConstraints))
                {
                    context.CapacityBlockedDemand += quantity;
                }
            }
        }
    }

    private static void AddImplicitRecipeDemand(NetworkModel network, string trafficType, IDictionary<string, double> demand)
    {
        foreach (var node in network.Nodes)
        {
            var implicitDemand = node.TrafficProfiles
                .Where(profile => profile.Production > Epsilon)
                .SelectMany(profile => profile.InputRequirements.Select(requirement => new { profile.Production, requirement }))
                .Where(item => Comparer.Equals(item.requirement.TrafficType, trafficType))
                .Sum(item =>
                {
                    var requirement = item.requirement;

                    var quantityPerOutputUnit =
                        requirement.OutputQuantity > Epsilon
                            ? requirement.InputQuantity / requirement.OutputQuantity
                            : requirement.QuantityPerOutputUnit.GetValueOrDefault();

                    return item.Production * quantityPerOutputUnit;
                });

            if (implicitDemand > Epsilon)
            {
                demand[node.Id] = GetOrZero(demand, node.Id) + implicitDemand;
            }
        }
    }


    private static void RecordAdaptiveObservations(RoutingTrafficContext context, NetworkState state)
    {
        if (!context.RouteChoiceSettings.AdaptiveRoutingEnabled)
        {
            return;
        }

        foreach (var pair in state.EdgeLoad)
        {
            var capacity = state.EdgeCapacity.GetValueOrDefault(pair.Key, double.PositiveInfinity);
            var util = double.IsPositiveInfinity(capacity) || capacity <= 0d ? 0d : pair.Value / capacity;
            if (!Guid.TryParse(pair.Key, out var edgeId))
            {
                continue;
            }

            AdaptiveRoutingMemory.RecordObservation(edgeId, observedDelay: pair.Value, utilisation: util);
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
        var congestionCost = CongestionCostModel.GetEffectiveCost(
            arc.Cost,
            state.EdgeLoad.GetValueOrDefault(arc.EdgeId),
            state.EdgeCapacity.GetValueOrDefault(arc.EdgeId, double.PositiveInfinity),
            gamma);

        if (!context.RouteChoiceSettings.AdaptiveRoutingEnabled || !Guid.TryParse(arc.EdgeId, out var edgeId))
        {
            return congestionCost;
        }

        return congestionCost + AdaptiveRoutingMemory.GetAdaptivePenalty(edgeId);
    }

    private static bool TryFindArc(string edgeId, AllocationContext allocationContext, out GraphArc arc)
    {
        foreach (var pair in allocationContext.Adjacency)
        {
            var arcs = pair.Value;
            for (var index = 0; index < arcs.Count; index++)
            {
                if (Comparer.Equals(arcs[index].EdgeId, edgeId))
                {
                    arc = arcs[index];
                    return true;
                }
            }
        }

        arc = default;
        return false;
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

    private static bool HasAnyFeasibleRoute(
        NetworkModel network,
        RoutingTrafficContext context,
        string producerNodeId,
        string consumerNodeId,
        NetworkState state,
        AllocationContext allocationContext,
        RouteConstraintMode constraintMode)
    {
        var permissionResolver = new EdgeTrafficPermissionResolver();
        var edgeLookup = allocationContext.EdgesById;
        var visited = new HashSet<string>(Comparer) { producerNodeId };
        var queue = new Queue<string>();
        queue.Enqueue(producerNodeId);

        while (queue.Count > 0)
        {
            var currentNodeId = queue.Dequeue();
            if (Comparer.Equals(currentNodeId, consumerNodeId))
            {
                return true;
            }

            if (!allocationContext.Adjacency.TryGetValue(currentNodeId, out var arcs))
            {
                continue;
            }

            foreach (var arc in arcs)
            {
                if (!edgeLookup.TryGetValue(arc.EdgeId, out var edge))
                {
                    continue;
                }

                var effectivePermission = permissionResolver.Resolve(network, edge, context.TrafficType);
                if (effectivePermission.Mode == EdgeTrafficPermissionMode.Blocked)
                {
                    continue;
                }

                if (constraintMode is RouteConstraintMode.PermissionLimited or RouteConstraintMode.AllConstraints &&
                    state.RemainingEdgeTrafficCapacity.GetValueOrDefault(new EdgeTrafficResourceKey(arc.EdgeId, context.TrafficType)) <= Epsilon)
                {
                    continue;
                }

                if (constraintMode == RouteConstraintMode.AllConstraints)
                {
                    if (state.RemainingEdgeCapacity.GetValueOrDefault(arc.EdgeId) <= Epsilon)
                    {
                        continue;
                    }

                    if (IsIntermediateNode(arc.ToNodeId, producerNodeId, consumerNodeId) &&
                        state.RemainingNodeCapacity.GetValueOrDefault(arc.ToNodeId) <= Epsilon)
                    {
                        continue;
                    }
                }

                if (!CanTraverseNode(arc.ToNodeId, producerNodeId, consumerNodeId, context.ProfilesByNodeId) ||
                    !visited.Add(arc.ToNodeId))
                {
                    continue;
                }

                queue.Enqueue(arc.ToNodeId);
            }
        }

        return false;
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

    private static void ReserveTrafficCapacity(
        string trafficType,
        IEnumerable<string> pathEdgeIds,
        IDictionary<EdgeTrafficResourceKey, double> remainingCapacityById,
        IDictionary<EdgeTrafficResourceKey, double> loadById,
        double quantity)
    {
        foreach (var edgeId in pathEdgeIds)
        {
            var key = new EdgeTrafficResourceKey(edgeId, trafficType);
            if (!remainingCapacityById.TryGetValue(key, out var remainingCapacity) ||
                double.IsPositiveInfinity(remainingCapacity))
            {
                continue;
            }

            remainingCapacityById[key] = Math.Max(0d, remainingCapacity - quantity);
            loadById[key] = (loadById.TryGetValue(key, out var existing) ? existing : 0d) + quantity;
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

    private static double GetTrafficPathRemainingCapacity(
        string trafficType,
        IReadOnlyList<string> pathEdgeIds,
        IDictionary<EdgeTrafficResourceKey, double> remainingCapacityById)
    {
        return pathEdgeIds.Count == 0
            ? double.PositiveInfinity
            : pathEdgeIds
                .Select(edgeId =>
                {
                    var key = new EdgeTrafficResourceKey(edgeId, trafficType);
                    return remainingCapacityById.TryGetValue(key, out var remainingCapacity)
                        ? remainingCapacity
                        : double.PositiveInfinity;
                })
                .DefaultIfEmpty(double.PositiveInfinity)
                .Min();
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

    private static double ResolveBaseProductionCost(NodeTrafficProfile? profile, TrafficTypeDefinition definition)
    {
        if (profile?.ProductionCostPerUnit is { } profileCost)
        {
            return Math.Max(0d, profileCost);
        }

        return Math.Max(0d, definition.DefaultUnitProductionCost);
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
    /// <summary>
    /// Represents the route search state component.
    /// </summary>

    private sealed record RouteSearchState(
        string NodeId,
        IReadOnlyList<string> PathNodeIds,
        IReadOnlyList<string> PathEdgeIds,
        double BaseTime,
        double BaseCost,
        double Score);
    /// <summary>
    /// Represents the route candidate component.
    /// </summary>

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
    /// <summary>
    /// Specifies the route constraint mode.
    /// </summary>

    private enum RouteConstraintMode
    {
        BlockedOnly,
        PermissionLimited,
        AllConstraints
    }
}

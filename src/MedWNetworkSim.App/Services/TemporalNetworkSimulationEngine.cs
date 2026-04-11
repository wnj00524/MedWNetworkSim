using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public sealed class TemporalNetworkSimulationEngine
{
    private const double Epsilon = 0.000001d;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public TemporalSimulationState Initialize(NetworkModel network)
    {
        ArgumentNullException.ThrowIfNull(network);

        var state = new TemporalSimulationState();
        foreach (var node in network.Nodes)
        {
            foreach (var profile in node.TrafficProfiles)
            {
                state.GetOrCreateNodeTrafficState(node.Id, profile.TrafficType);
            }
        }

        return state;
    }

    public TemporalSimulationStepResult Advance(NetworkModel network, TemporalSimulationState? currentState)
    {
        ArgumentNullException.ThrowIfNull(network);

        var state = currentState ?? Initialize(network);
        var nextPeriod = state.CurrentPeriod + 1;
        var effectivePeriod = GetEffectivePeriod(nextPeriod, network.TimelineLoopLength);
        var nodeStates = state.NodeStates.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            TemporalNodeTrafficKey.Comparer);
        var movements = state.InFlightMovements.Select(movement => movement.Clone()).ToList();
        var nodeLookup = network.Nodes.ToDictionary(node => node.Id, node => node, Comparer);
        var edgeLookup = network.Edges.ToDictionary(edge => edge.Id, edge => edge, Comparer);
        var definitionsByTraffic = network.TrafficTypes.ToDictionary(definition => definition.Name, definition => definition, Comparer);
        var occupiedEdgeCapacity = state.OccupiedEdgeCapacity.ToDictionary(pair => pair.Key, pair => pair.Value, Comparer);
        var occupiedTranshipmentCapacity = state.OccupiedTranshipmentCapacity.ToDictionary(pair => pair.Key, pair => pair.Value, Comparer);

        ValidateResourceOccupancy(edgeLookup, nodeLookup, occupiedEdgeCapacity, occupiedTranshipmentCapacity);
        ValidateMovementResourceClaims(movements, occupiedEdgeCapacity, occupiedTranshipmentCapacity);

        AddScheduledNodeChanges(network, nodeStates, effectivePeriod);

        var availableResources = BuildAvailableResourceCapacity(network, movements, occupiedEdgeCapacity, occupiedTranshipmentCapacity);
        var plannedAllocations = PlanNewAllocations(
            network,
            definitionsByTraffic,
            nodeStates,
            nextPeriod,
            effectivePeriod,
            availableResources.EdgeCapacityById,
            availableResources.TranshipmentCapacityByNodeId);

        var edgeFlowById = new Dictionary<string, EdgeFlowVisualSummary>(Comparer);
        var nodeFlowById = new Dictionary<string, NodeFlowVisualSummary>(Comparer);

        foreach (var allocation in plannedAllocations)
        {
            var movement = new TemporalInFlightMovement
            {
                TrafficType = allocation.TrafficType,
                Quantity = allocation.Quantity,
                PathNodeIds = allocation.PathNodeIds.ToList(),
                PathNodeNames = allocation.PathNodeNames.ToList(),
                PathEdgeIds = allocation.PathEdgeIds.ToList(),
                CurrentEdgeIndex = 0,
                RemainingPeriodsOnCurrentEdge = GetEdgePeriods(edgeLookup[allocation.PathEdgeIds[0]])
            };

            ClaimCurrentMovementResources(edgeLookup, nodeLookup, movement, occupiedEdgeCapacity, occupiedTranshipmentCapacity);
            movements.Add(movement);
        }

        var edgeOccupancySnapshot = SnapshotResourceOccupancy(occupiedEdgeCapacity);
        var transhipmentOccupancySnapshot = SnapshotResourceOccupancy(occupiedTranshipmentCapacity);

        foreach (var movement in movements.ToList())
        {
            if (movement.PathEdgeIds.Count == 0 || movement.CurrentEdgeIndex >= movement.PathEdgeIds.Count)
            {
                continue;
            }

            if (movement.IsWaitingBetweenEdges)
            {
                if (!TryMoveMovementToNextEdge(edgeLookup, nodeLookup, movement, occupiedEdgeCapacity, occupiedTranshipmentCapacity))
                {
                    continue;
                }
            }

            var edgeId = movement.PathEdgeIds[movement.CurrentEdgeIndex];
            if (!edgeLookup.TryGetValue(edgeId, out var edge))
            {
                continue;
            }

            AddEdgeFlow(edgeFlowById, edge, movement);
            AddNodeDeparture(nodeFlowById, movement.PathNodeIds[movement.CurrentEdgeIndex], movement.Quantity);
            AddNodeArrival(nodeFlowById, movement.PathNodeIds[movement.CurrentEdgeIndex + 1], movement.Quantity);
            movement.RemainingPeriodsOnCurrentEdge -= 1;

            if (movement.RemainingPeriodsOnCurrentEdge > 0)
            {
                continue;
            }

            if (movement.CurrentEdgeIndex == movement.PathEdgeIds.Count - 1)
            {
                ReleaseCurrentMovementResources(movement, occupiedEdgeCapacity, occupiedTranshipmentCapacity);
                CompleteArrival(nodeStates, nodeLookup, movement);
                movements.Remove(movement);
                continue;
            }

            TryMoveMovementToNextEdge(edgeLookup, nodeLookup, movement, occupiedEdgeCapacity, occupiedTranshipmentCapacity);
        }

        ValidateResourceOccupancy(edgeLookup, nodeLookup, occupiedEdgeCapacity, occupiedTranshipmentCapacity);
        ValidateMovementResourceClaims(movements, occupiedEdgeCapacity, occupiedTranshipmentCapacity);

        state.CurrentPeriod = nextPeriod;
        state.NodeStates.Clear();
        foreach (var pair in nodeStates)
        {
            state.NodeStates[pair.Key] = pair.Value;
        }

        state.InFlightMovements.Clear();
        state.InFlightMovements.AddRange(movements);
        state.OccupiedEdgeCapacity.Clear();
        foreach (var pair in occupiedEdgeCapacity.Where(pair => pair.Value > Epsilon))
        {
            state.OccupiedEdgeCapacity[pair.Key] = pair.Value;
        }

        state.OccupiedTranshipmentCapacity.Clear();
        foreach (var pair in occupiedTranshipmentCapacity.Where(pair => pair.Value > Epsilon))
        {
            state.OccupiedTranshipmentCapacity[pair.Key] = pair.Value;
        }

        var nodeSnapshots = nodeStates.ToDictionary(
            pair => pair.Key,
            pair => new TemporalNodeStateSnapshot(pair.Value.AvailableSupply, pair.Value.DemandBacklog, pair.Value.StoreInventory),
            TemporalNodeTrafficKey.Comparer);

        return new TemporalSimulationStepResult(
            nextPeriod,
            plannedAllocations,
            edgeFlowById,
            nodeFlowById,
            nodeSnapshots,
            edgeOccupancySnapshot,
            transhipmentOccupancySnapshot,
            effectivePeriod,
            movements.Count);
    }

    private static void AddScheduledNodeChanges(
        NetworkModel network,
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates,
        int period)
    {
        var profilesByNodeAndTraffic = network.Nodes.ToDictionary(
            node => node.Id,
            node => node.TrafficProfiles.ToDictionary(profile => profile.TrafficType, profile => profile, Comparer),
            Comparer);

        foreach (var node in network.Nodes)
        {
            foreach (var profile in node.TrafficProfiles)
            {
                var key = new TemporalNodeTrafficKey(node.Id, profile.TrafficType);
                if (!nodeStates.TryGetValue(key, out var state))
                {
                    state = new TemporalNodeTrafficState();
                    nodeStates[key] = state;
                }

                if (profile.Production > Epsilon && IsProductionActive(profile, period))
                {
                    AddImplicitRecipeDemand(
                        node.Id,
                        profile,
                        nodeStates,
                        profilesByNodeAndTraffic.GetValueOrDefault(node.Id) ?? new Dictionary<string, NodeTrafficProfile>(Comparer));
                    state.AvailableSupply += CalculateAndConsumeProductionInputs(
                        node.Id,
                        profile,
                        nodeStates,
                        profilesByNodeAndTraffic.GetValueOrDefault(node.Id) ?? new Dictionary<string, NodeTrafficProfile>(Comparer));
                }

                if (profile.Consumption > Epsilon && IsConsumptionActive(profile, period) && !profile.IsStore)
                {
                    state.DemandBacklog += profile.Consumption;
                }
            }
        }
    }

    private static List<RouteAllocation> PlanNewAllocations(
        NetworkModel network,
        IReadOnlyDictionary<string, TrafficTypeDefinition> definitionsByTraffic,
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates,
        int period,
        int effectivePeriod,
        IReadOnlyDictionary<string, double> availableCapacityByEdgeId,
        IReadOnlyDictionary<string, double> availableTranshipmentCapacityByNodeId)
    {
        var adjacency = BuildAdjacency(network);
        var remainingCapacityByEdgeId = availableCapacityByEdgeId.ToDictionary(pair => pair.Key, pair => pair.Value, Comparer);
        var remainingTranshipmentCapacityByNodeId = availableTranshipmentCapacityByNodeId.ToDictionary(pair => pair.Key, pair => pair.Value, Comparer);
        var contexts = GetOrderedTrafficNames(network)
            .Select(trafficType =>
            {
                definitionsByTraffic.TryGetValue(trafficType, out var definition);
                return BuildTemporalContext(
                    network,
                    definition ?? new TrafficTypeDefinition { Name = trafficType, RoutingPreference = RoutingPreference.TotalCost },
                    nodeStates,
                    period,
                    effectivePeriod);
            })
            .ToList();

        foreach (var context in contexts)
        {
            ApplyLocalAllocations(context, nodeStates);
        }

        while (true)
        {
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
            var remainingSupply = context.Supply.TryGetValue(nextCandidate.ProducerNodeId, out var supplyValue)
                ? supplyValue
                : 0d;
            var remainingDemand = context.Demand.TryGetValue(nextCandidate.ConsumerNodeId, out var demandValue)
                ? demandValue
                : 0d;
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
                Period = period,
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
                PathNodeNames = nextCandidate.PathNodeIds.Select(nodeId => context.NodesById[nodeId].Name).ToList(),
                PathNodeIds = nextCandidate.PathNodeIds.ToList(),
                PathEdgeIds = nextCandidate.PathEdgeIds.ToList()
            });

            context.Supply[nextCandidate.ProducerNodeId] -= quantity;
            context.Demand[nextCandidate.ConsumerNodeId] -= quantity;
            context.CommittedSupply[nextCandidate.ProducerNodeId] += quantity;
            context.CommittedDemand[nextCandidate.ConsumerNodeId] += quantity;
            ReserveCapacity(nextCandidate.PathEdgeIds, nextCandidate.PathTranshipmentNodeIds, remainingCapacityByEdgeId, remainingTranshipmentCapacityByNodeId, quantity);
        }

        foreach (var context in contexts)
        {
            ApplyCommittedState(context, nodeStates);
        }

        return contexts.SelectMany(context => context.Allocations).ToList();
    }

    private static TemporalTrafficContext BuildTemporalContext(
        NetworkModel network,
        TrafficTypeDefinition definition,
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates,
        int period,
        int effectivePeriod)
    {
        var profilesByNodeId = network.Nodes.ToDictionary(
            node => node.Id,
            node => node.TrafficProfiles.FirstOrDefault(profile => Comparer.Equals(profile.TrafficType, definition.Name)),
            Comparer);
        var nodesById = network.Nodes.ToDictionary(node => node.Id, node => node, Comparer);
        var supply = new Dictionary<string, double>(Comparer);
        var demand = new Dictionary<string, double>(Comparer);
        var committedSupply = new Dictionary<string, double>(Comparer);
        var committedDemand = new Dictionary<string, double>(Comparer);
        var storeSupplyNodes = new HashSet<string>(Comparer);
        var storeDemandNodes = new HashSet<string>(Comparer);
        var recipeInputDemandNodes = new HashSet<string>(Comparer);

        foreach (var node in network.Nodes)
        {
            var profile = profilesByNodeId[node.Id];
            var key = new TemporalNodeTrafficKey(node.Id, definition.Name);
            nodeStates.TryGetValue(key, out var nodeState);
            nodeState ??= new TemporalNodeTrafficState();

            var availableSupply = nodeState.AvailableSupply;
            if (profile?.IsStore == true &&
                profile.Production > Epsilon &&
                IsProductionActive(profile, effectivePeriod))
            {
                availableSupply += Math.Min(nodeState.StoreInventory, profile.Production);
                storeSupplyNodes.Add(node.Id);
            }

            if (availableSupply > Epsilon)
            {
                supply[node.Id] = availableSupply;
                committedSupply[node.Id] = 0d;
            }

            var availableDemand = 0d;
            if (profile?.IsStore == true &&
                profile.Consumption > Epsilon &&
                IsConsumptionActive(profile, effectivePeriod))
            {
                var spareCapacity = profile.StoreCapacity.HasValue
                    ? Math.Max(0d, profile.StoreCapacity.Value - nodeState.StoreInventory - nodeState.ReservedStoreReceipts)
                    : double.PositiveInfinity;
                if (spareCapacity > Epsilon)
                {
                    availableDemand = Math.Min(profile.Consumption, spareCapacity);
                    storeDemandNodes.Add(node.Id);
                }
            }
            else if (nodeState.DemandBacklog > Epsilon)
            {
                availableDemand = nodeState.DemandBacklog;
            }

            if (availableDemand > Epsilon)
            {
                demand[node.Id] = availableDemand;
                committedDemand[node.Id] = 0d;
                if (IsRecipeInputTraffic(node, definition.Name))
                {
                    recipeInputDemandNodes.Add(node.Id);
                }
            }
        }

        return new TemporalTrafficContext(
            definition.Name,
            definition.RoutingPreference,
            GetCapacityBidPerUnit(definition),
            nodesById,
            profilesByNodeId,
            supply,
            demand,
            committedSupply,
            committedDemand,
            storeSupplyNodes,
            storeDemandNodes,
            recipeInputDemandNodes,
            []);
    }

    private static void ApplyLocalAllocations(
        TemporalTrafficContext context,
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates)
    {
        foreach (var nodeId in context.Supply.Keys.Intersect(context.Demand.Keys, Comparer).ToList())
        {
            if (context.StoreSupplyNodes.Contains(nodeId) ||
                context.StoreDemandNodes.Contains(nodeId) ||
                context.RecipeInputDemandNodes.Contains(nodeId))
            {
                continue;
            }

            var quantity = Math.Min(context.Supply[nodeId], context.Demand[nodeId]);
            if (quantity <= Epsilon)
            {
                continue;
            }

            context.Supply[nodeId] -= quantity;
            context.Demand[nodeId] -= quantity;

            var state = nodeStates[new TemporalNodeTrafficKey(nodeId, context.TrafficType)];
            state.AvailableSupply = Math.Max(0d, state.AvailableSupply - quantity);
            state.DemandBacklog = Math.Max(0d, state.DemandBacklog - quantity);
        }
    }

    private static void ApplyCommittedState(
        TemporalTrafficContext context,
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates)
    {
        foreach (var pair in context.CommittedSupply)
        {
            if (pair.Value <= Epsilon)
            {
                continue;
            }

            var state = nodeStates[new TemporalNodeTrafficKey(pair.Key, context.TrafficType)];
            state.AvailableSupply = Math.Max(0d, state.AvailableSupply - pair.Value);
            if (context.StoreSupplyNodes.Contains(pair.Key))
            {
                state.StoreInventory = Math.Max(0d, state.StoreInventory - pair.Value);
            }
        }

        foreach (var pair in context.CommittedDemand)
        {
            if (pair.Value <= Epsilon)
            {
                continue;
            }

            var key = new TemporalNodeTrafficKey(pair.Key, context.TrafficType);
            if (!nodeStates.TryGetValue(key, out var state))
            {
                state = new TemporalNodeTrafficState();
                nodeStates[key] = state;
            }

            if (context.StoreDemandNodes.Contains(pair.Key))
            {
                state.ReservedStoreReceipts += pair.Value;
            }
            else
            {
                state.DemandBacklog = Math.Max(0d, state.DemandBacklog - pair.Value);
            }
        }
    }

    private static void CompleteArrival(
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates,
        IReadOnlyDictionary<string, NodeModel> nodeLookup,
        TemporalInFlightMovement movement)
    {
        var finalNodeId = movement.PathNodeIds[^1];
        var key = new TemporalNodeTrafficKey(finalNodeId, movement.TrafficType);
        if (!nodeStates.TryGetValue(key, out var nodeState))
        {
            nodeState = new TemporalNodeTrafficState();
            nodeStates[key] = nodeState;
        }

        var profile = nodeLookup[finalNodeId].TrafficProfiles
            .FirstOrDefault(candidate => Comparer.Equals(candidate.TrafficType, movement.TrafficType));

        if (profile?.IsStore == true)
        {
            nodeState.StoreInventory += movement.Quantity;
            nodeState.ReservedStoreReceipts = Math.Max(0d, nodeState.ReservedStoreReceipts - movement.Quantity);
            return;
        }

        if (IsRecipeInputTraffic(nodeLookup[finalNodeId], movement.TrafficType))
        {
            nodeState.AvailableSupply += movement.Quantity;
        }
    }

    private static bool IsRecipeInputTraffic(NodeModel node, string trafficType)
    {
        return node.TrafficProfiles
            .Where(profile => profile.Production > Epsilon)
            .SelectMany(profile => profile.InputRequirements)
            .Any(requirement => Comparer.Equals(requirement.TrafficType, trafficType));
    }

    private static double CalculateAndConsumeProductionInputs(
        string nodeId,
        NodeTrafficProfile outputProfile,
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates,
        IReadOnlyDictionary<string, NodeTrafficProfile> profilesByTrafficType)
    {
        var outputQuantity = outputProfile.Production;
        if (outputProfile.InputRequirements.Count == 0)
        {
            return outputQuantity;
        }

        foreach (var requirement in outputProfile.InputRequirements)
        {
            var availableInput = GetLocalInputQuantity(nodeId, requirement.TrafficType, nodeStates, profilesByTrafficType);
            outputQuantity = Math.Min(outputQuantity, availableInput / requirement.QuantityPerOutputUnit);
        }

        if (outputQuantity < Epsilon)
        {
            return 0d;
        }

        foreach (var requirement in outputProfile.InputRequirements)
        {
            ConsumeLocalInputQuantity(
                nodeId,
                requirement.TrafficType,
                outputQuantity * requirement.QuantityPerOutputUnit,
                nodeStates,
                profilesByTrafficType);
        }

        return outputQuantity;
    }

    private static void AddImplicitRecipeDemand(
        string nodeId,
        NodeTrafficProfile outputProfile,
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates,
        IReadOnlyDictionary<string, NodeTrafficProfile> profilesByTrafficType)
    {
        if (outputProfile.InputRequirements.Count == 0)
        {
            return;
        }

        foreach (var requirement in outputProfile.InputRequirements)
        {
            var requiredInput = outputProfile.Production * requirement.QuantityPerOutputUnit;
            var availableInput = GetLocalInputQuantity(nodeId, requirement.TrafficType, nodeStates, profilesByTrafficType);
            var unmetInput = Math.Max(0d, requiredInput - availableInput);
            if (unmetInput <= Epsilon)
            {
                continue;
            }

            var key = new TemporalNodeTrafficKey(nodeId, requirement.TrafficType);
            if (!nodeStates.TryGetValue(key, out var inputState))
            {
                inputState = new TemporalNodeTrafficState();
                nodeStates[key] = inputState;
            }

            inputState.DemandBacklog += unmetInput;
        }
    }

    private static double GetLocalInputQuantity(
        string nodeId,
        string trafficType,
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates,
        IReadOnlyDictionary<string, NodeTrafficProfile> profilesByTrafficType)
    {
        var key = new TemporalNodeTrafficKey(nodeId, trafficType);
        nodeStates.TryGetValue(key, out var state);
        var available = state?.AvailableSupply ?? 0d;
        if (profilesByTrafficType.TryGetValue(trafficType, out var profile) && profile.IsStore)
        {
            available += state?.StoreInventory ?? 0d;
        }

        return available;
    }

    private static void ConsumeLocalInputQuantity(
        string nodeId,
        string trafficType,
        double quantity,
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates,
        IReadOnlyDictionary<string, NodeTrafficProfile> profilesByTrafficType)
    {
        var key = new TemporalNodeTrafficKey(nodeId, trafficType);
        if (!nodeStates.TryGetValue(key, out var state))
        {
            throw new InvalidOperationException($"Node '{nodeId}' cannot consume missing precursor traffic '{trafficType}'.");
        }

        var remaining = quantity;
        var supplyConsumed = Math.Min(state.AvailableSupply, remaining);
        state.AvailableSupply -= supplyConsumed;
        remaining -= supplyConsumed;

        if (remaining > Epsilon && profilesByTrafficType.TryGetValue(trafficType, out var profile) && profile.IsStore)
        {
            var storeConsumed = Math.Min(state.StoreInventory, remaining);
            state.StoreInventory -= storeConsumed;
            remaining -= storeConsumed;
        }

        if (remaining > Epsilon)
        {
            throw new InvalidOperationException($"Node '{nodeId}' would over-consume precursor traffic '{trafficType}'.");
        }
    }

    private static AvailableResourceCapacity BuildAvailableResourceCapacity(
        NetworkModel network,
        IReadOnlyList<TemporalInFlightMovement> movements,
        IReadOnlyDictionary<string, double> occupiedEdgeCapacity,
        IReadOnlyDictionary<string, double> occupiedTranshipmentCapacity)
    {
        var pendingEdgeClaims = new Dictionary<string, double>(Comparer);
        var pendingTranshipmentClaims = new Dictionary<string, double>(Comparer);

        foreach (var movement in movements)
        {
            if (!movement.IsWaitingBetweenEdges && movement.RemainingPeriodsOnCurrentEdge > 1)
            {
                continue;
            }

            var nextEdgeIndex = movement.CurrentEdgeIndex + 1;
            if (nextEdgeIndex >= movement.PathEdgeIds.Count)
            {
                continue;
            }

            var nextEdgeId = movement.PathEdgeIds[nextEdgeIndex];
            AddResourceQuantity(pendingEdgeClaims, nextEdgeId, movement.Quantity);

            var nextTranshipmentNodeId = GetTranshipmentNodeForEdgeIndex(movement, nextEdgeIndex);
            if (nextTranshipmentNodeId is not null)
            {
                AddResourceQuantity(pendingTranshipmentClaims, nextTranshipmentNodeId, movement.Quantity);
            }
        }

        return new AvailableResourceCapacity(
            network.Edges.ToDictionary(
                edge => edge.Id,
                edge => GetAvailableCapacity(edge.Capacity, edge.Id, occupiedEdgeCapacity, pendingEdgeClaims),
                Comparer),
            network.Nodes.ToDictionary(
                node => node.Id,
                node => GetAvailableCapacity(node.TranshipmentCapacity, node.Id, occupiedTranshipmentCapacity, pendingTranshipmentClaims),
                Comparer));
    }

    private static double GetAvailableCapacity(
        double? nominalCapacity,
        string resourceId,
        IReadOnlyDictionary<string, double> occupiedCapacity,
        IReadOnlyDictionary<string, double> pendingClaims)
    {
        if (!nominalCapacity.HasValue)
        {
            return double.PositiveInfinity;
        }

        var occupied = occupiedCapacity.TryGetValue(resourceId, out var occupiedValue) ? occupiedValue : 0d;
        var pending = pendingClaims.TryGetValue(resourceId, out var pendingValue) ? pendingValue : 0d;
        return Math.Max(0d, nominalCapacity.Value - occupied - pending);
    }

    private static bool TryMoveMovementToNextEdge(
        IReadOnlyDictionary<string, EdgeModel> edgeLookup,
        IReadOnlyDictionary<string, NodeModel> nodeLookup,
        TemporalInFlightMovement movement,
        IDictionary<string, double> occupiedEdgeCapacity,
        IDictionary<string, double> occupiedTranshipmentCapacity)
    {
        var nextEdgeIndex = movement.CurrentEdgeIndex + 1;
        if (nextEdgeIndex >= movement.PathEdgeIds.Count)
        {
            return false;
        }

        var releasedEdgeId = movement.IsWaitingBetweenEdges ? null : movement.PathEdgeIds[movement.CurrentEdgeIndex];
        var releasedTranshipmentNodeId = movement.IsWaitingBetweenEdges ? null : GetCurrentTranshipmentNodeId(movement);

        if (!CanClaimMovementResourcesForEdgeIndex(
            edgeLookup,
            nodeLookup,
            movement,
            nextEdgeIndex,
            occupiedEdgeCapacity,
            occupiedTranshipmentCapacity,
            releasedEdgeId,
            releasedTranshipmentNodeId))
        {
            if (!movement.IsWaitingBetweenEdges)
            {
                ReleaseCurrentMovementResources(movement, occupiedEdgeCapacity, occupiedTranshipmentCapacity);
                movement.IsWaitingBetweenEdges = true;
                movement.RemainingPeriodsOnCurrentEdge = 0;
            }

            return false;
        }

        if (!movement.IsWaitingBetweenEdges)
        {
            ReleaseCurrentMovementResources(movement, occupiedEdgeCapacity, occupiedTranshipmentCapacity);
        }

        movement.CurrentEdgeIndex = nextEdgeIndex;
        movement.RemainingPeriodsOnCurrentEdge = GetEdgePeriods(edgeLookup[movement.PathEdgeIds[movement.CurrentEdgeIndex]]);
        movement.IsWaitingBetweenEdges = false;
        ClaimCurrentMovementResources(edgeLookup, nodeLookup, movement, occupiedEdgeCapacity, occupiedTranshipmentCapacity);
        return true;
    }

    private static void ClaimCurrentMovementResources(
        IReadOnlyDictionary<string, EdgeModel> edgeLookup,
        IReadOnlyDictionary<string, NodeModel> nodeLookup,
        TemporalInFlightMovement movement,
        IDictionary<string, double> occupiedEdgeCapacity,
        IDictionary<string, double> occupiedTranshipmentCapacity)
    {
        if (movement.CurrentEdgeIndex < 0 || movement.CurrentEdgeIndex >= movement.PathEdgeIds.Count)
        {
            throw new InvalidOperationException("Cannot claim resources for a movement without a current edge.");
        }

        var edgeId = movement.PathEdgeIds[movement.CurrentEdgeIndex];
        if (!edgeLookup.TryGetValue(edgeId, out var edge))
        {
            throw new InvalidOperationException($"Movement references missing edge '{edgeId}'.");
        }

        if (!CanClaimMovementResourcesForEdgeIndex(
            edgeLookup,
            nodeLookup,
            movement,
            movement.CurrentEdgeIndex,
            occupiedEdgeCapacity,
            occupiedTranshipmentCapacity))
        {
            throw new InvalidOperationException("Movement cannot claim current resources without exceeding capacity.");
        }

        AddResourceQuantity(occupiedEdgeCapacity, edgeId, movement.Quantity);

        var transhipmentNodeId = GetCurrentTranshipmentNodeId(movement);
        if (transhipmentNodeId is not null)
        {
            AddResourceQuantity(occupiedTranshipmentCapacity, transhipmentNodeId, movement.Quantity);
        }
    }

    private static bool CanClaimMovementResourcesForEdgeIndex(
        IReadOnlyDictionary<string, EdgeModel> edgeLookup,
        IReadOnlyDictionary<string, NodeModel> nodeLookup,
        TemporalInFlightMovement movement,
        int edgeIndex,
        IDictionary<string, double> occupiedEdgeCapacity,
        IDictionary<string, double> occupiedTranshipmentCapacity,
        string? releasedEdgeId = null,
        string? releasedTranshipmentNodeId = null)
    {
        if (edgeIndex < 0 || edgeIndex >= movement.PathEdgeIds.Count)
        {
            return false;
        }

        var edgeId = movement.PathEdgeIds[edgeIndex];
        if (!edgeLookup.TryGetValue(edgeId, out var edge))
        {
            return false;
        }

        var releasedEdgeQuantity = Comparer.Equals(edgeId, releasedEdgeId) ? movement.Quantity : 0d;
        if (!CanClaimResource(edge.Capacity, edgeId, movement.Quantity, occupiedEdgeCapacity, releasedEdgeQuantity))
        {
            return false;
        }

        var transhipmentNodeId = GetTranshipmentNodeForEdgeIndex(movement, edgeIndex);
        if (transhipmentNodeId is null)
        {
            return true;
        }

        if (!nodeLookup.TryGetValue(transhipmentNodeId, out var node))
        {
            return false;
        }

        var releasedTranshipmentQuantity = Comparer.Equals(transhipmentNodeId, releasedTranshipmentNodeId) ? movement.Quantity : 0d;
        return CanClaimResource(
            node.TranshipmentCapacity,
            transhipmentNodeId,
            movement.Quantity,
            occupiedTranshipmentCapacity,
            releasedTranshipmentQuantity);
    }

    private static bool CanClaimResource(
        double? nominalCapacity,
        string resourceId,
        double quantity,
        IDictionary<string, double> occupiedCapacity,
        double releasedQuantity = 0d)
    {
        if (!nominalCapacity.HasValue)
        {
            return true;
        }

        var occupied = occupiedCapacity.TryGetValue(resourceId, out var occupiedValue) ? occupiedValue : 0d;
        return Math.Max(0d, occupied - releasedQuantity) + quantity <= nominalCapacity.Value + Epsilon;
    }

    private static void ReleaseCurrentMovementResources(
        TemporalInFlightMovement movement,
        IDictionary<string, double> occupiedEdgeCapacity,
        IDictionary<string, double> occupiedTranshipmentCapacity)
    {
        if (movement.CurrentEdgeIndex < 0 || movement.CurrentEdgeIndex >= movement.PathEdgeIds.Count)
        {
            return;
        }

        ReleaseResourceQuantity(occupiedEdgeCapacity, movement.PathEdgeIds[movement.CurrentEdgeIndex], movement.Quantity);

        var transhipmentNodeId = GetCurrentTranshipmentNodeId(movement);
        if (transhipmentNodeId is not null)
        {
            ReleaseResourceQuantity(occupiedTranshipmentCapacity, transhipmentNodeId, movement.Quantity);
        }
    }

    private static string? GetCurrentTranshipmentNodeId(TemporalInFlightMovement movement)
    {
        return GetTranshipmentNodeForEdgeIndex(movement, movement.CurrentEdgeIndex);
    }

    private static string? GetTranshipmentNodeForEdgeIndex(TemporalInFlightMovement movement, int edgeIndex)
    {
        if (edgeIndex < 0 || edgeIndex >= movement.PathEdgeIds.Count - 1 || edgeIndex + 1 >= movement.PathNodeIds.Count)
        {
            return null;
        }

        return movement.PathNodeIds[edgeIndex + 1];
    }

    private static void AddResourceQuantity(IDictionary<string, double> occupiedCapacity, string resourceId, double quantity)
    {
        occupiedCapacity[resourceId] = (occupiedCapacity.TryGetValue(resourceId, out var existing) ? existing : 0d) + quantity;
    }

    private static void ReleaseResourceQuantity(IDictionary<string, double> occupiedCapacity, string resourceId, double quantity)
    {
        if (!occupiedCapacity.TryGetValue(resourceId, out var existing))
        {
            throw new InvalidOperationException($"Cannot release unclaimed resource '{resourceId}'.");
        }

        var remaining = existing - quantity;
        if (remaining < -Epsilon)
        {
            throw new InvalidOperationException($"Resource '{resourceId}' occupancy would become negative.");
        }

        if (remaining <= Epsilon)
        {
            occupiedCapacity.Remove(resourceId);
            return;
        }

        occupiedCapacity[resourceId] = remaining;
    }

    private static IReadOnlyDictionary<string, double> SnapshotResourceOccupancy(IReadOnlyDictionary<string, double> occupiedCapacity)
    {
        return occupiedCapacity
            .Where(pair => pair.Value > Epsilon)
            .ToDictionary(pair => pair.Key, pair => pair.Value, Comparer);
    }

    private static void ValidateResourceOccupancy(
        IReadOnlyDictionary<string, EdgeModel> edgeLookup,
        IReadOnlyDictionary<string, NodeModel> nodeLookup,
        IEnumerable<KeyValuePair<string, double>> occupiedEdgeCapacity,
        IEnumerable<KeyValuePair<string, double>> occupiedTranshipmentCapacity)
    {
        ValidateResourceOccupancy(
            occupiedEdgeCapacity,
            edgeLookup,
            edge => edge.Capacity,
            "edge");
        ValidateResourceOccupancy(
            occupiedTranshipmentCapacity,
            nodeLookup,
            node => node.TranshipmentCapacity,
            "transhipment node");
    }

    private static void ValidateResourceOccupancy<TResource>(
        IEnumerable<KeyValuePair<string, double>> occupiedCapacity,
        IReadOnlyDictionary<string, TResource> resourcesById,
        Func<TResource, double?> getCapacity,
        string resourceKind)
    {
        foreach (var pair in occupiedCapacity)
        {
            if (!resourcesById.TryGetValue(pair.Key, out var resource))
            {
                throw new InvalidOperationException($"Occupied {resourceKind} resource '{pair.Key}' does not exist.");
            }

            var capacity = getCapacity(resource);
            if (pair.Value < -Epsilon)
            {
                throw new InvalidOperationException($"Occupied {resourceKind} resource '{pair.Key}' cannot be negative.");
            }

            if (capacity.HasValue && pair.Value > capacity.Value + Epsilon)
            {
                throw new InvalidOperationException(
                    $"Occupied {resourceKind} resource '{pair.Key}' exceeds capacity {capacity.Value} with occupancy {pair.Value}.");
            }
        }
    }

    private static void ValidateMovementResourceClaims(
        IReadOnlyList<TemporalInFlightMovement> movements,
        IReadOnlyDictionary<string, double> occupiedEdgeCapacity,
        IReadOnlyDictionary<string, double> occupiedTranshipmentCapacity)
    {
        var expectedEdgeCapacity = new Dictionary<string, double>(Comparer);
        var expectedTranshipmentCapacity = new Dictionary<string, double>(Comparer);

        foreach (var movement in movements)
        {
            if (movement.IsWaitingBetweenEdges)
            {
                if (movement.CurrentEdgeIndex < 0 || movement.CurrentEdgeIndex >= movement.PathEdgeIds.Count - 1)
                {
                    throw new InvalidOperationException("Waiting in-flight movement has no valid next edge.");
                }

                if (movement.RemainingPeriodsOnCurrentEdge != 0)
                {
                    throw new InvalidOperationException("Waiting in-flight movement cannot have remaining edge travel time.");
                }

                continue;
            }

            if (movement.CurrentEdgeIndex < 0 || movement.CurrentEdgeIndex >= movement.PathEdgeIds.Count)
            {
                throw new InvalidOperationException("In-flight movement has no valid current edge claim.");
            }

            AddResourceQuantity(expectedEdgeCapacity, movement.PathEdgeIds[movement.CurrentEdgeIndex], movement.Quantity);

            var transhipmentNodeId = GetCurrentTranshipmentNodeId(movement);
            if (transhipmentNodeId is not null)
            {
                AddResourceQuantity(expectedTranshipmentCapacity, transhipmentNodeId, movement.Quantity);
            }
        }

        ValidateExpectedOccupancy(expectedEdgeCapacity, occupiedEdgeCapacity, "edge");
        ValidateExpectedOccupancy(expectedTranshipmentCapacity, occupiedTranshipmentCapacity, "transhipment node");
    }

    private static void ValidateExpectedOccupancy(
        IReadOnlyDictionary<string, double> expectedCapacity,
        IReadOnlyDictionary<string, double> actualCapacity,
        string resourceKind)
    {
        foreach (var pair in expectedCapacity)
        {
            var actual = actualCapacity.TryGetValue(pair.Key, out var value) ? value : 0d;
            if (Math.Abs(actual - pair.Value) > Epsilon)
            {
                throw new InvalidOperationException(
                    $"Occupied {resourceKind} resource '{pair.Key}' should be {pair.Value}, but was {actual}.");
            }
        }

        foreach (var pair in actualCapacity)
        {
            var expected = expectedCapacity.TryGetValue(pair.Key, out var value) ? value : 0d;
            if (Math.Abs(expected - pair.Value) > Epsilon)
            {
                throw new InvalidOperationException(
                    $"Occupied {resourceKind} resource '{pair.Key}' is orphaned with occupancy {pair.Value}.");
            }
        }
    }

    private static void AddEdgeFlow(IDictionary<string, EdgeFlowVisualSummary> edgeFlowById, EdgeModel edge, TemporalInFlightMovement movement)
    {
        var existing = edgeFlowById.TryGetValue(edge.Id, out var summary)
            ? summary
            : EdgeFlowVisualSummary.Empty;
        var pathFromNodeId = movement.PathNodeIds[movement.CurrentEdgeIndex];
        var pathToNodeId = movement.PathNodeIds[movement.CurrentEdgeIndex + 1];
        if (Comparer.Equals(pathFromNodeId, edge.FromNodeId) && Comparer.Equals(pathToNodeId, edge.ToNodeId))
        {
            edgeFlowById[edge.Id] = existing with { ForwardQuantity = existing.ForwardQuantity + movement.Quantity };
            return;
        }

        edgeFlowById[edge.Id] = existing with { ReverseQuantity = existing.ReverseQuantity + movement.Quantity };
    }

    private static void AddNodeDeparture(IDictionary<string, NodeFlowVisualSummary> nodeFlowById, string nodeId, double quantity)
    {
        var existing = nodeFlowById.TryGetValue(nodeId, out var summary)
            ? summary
            : NodeFlowVisualSummary.Empty;
        nodeFlowById[nodeId] = existing with { OutboundQuantity = existing.OutboundQuantity + quantity };
    }

    private static void AddNodeArrival(IDictionary<string, NodeFlowVisualSummary> nodeFlowById, string nodeId, double quantity)
    {
        var existing = nodeFlowById.TryGetValue(nodeId, out var summary)
            ? summary
            : NodeFlowVisualSummary.Empty;
        nodeFlowById[nodeId] = existing with { InboundQuantity = existing.InboundQuantity + quantity };
    }

    public static int GetEffectivePeriod(int absolutePeriod, int? loopLength)
    {
        if (!loopLength.HasValue || loopLength.Value < 1)
        {
            return absolutePeriod;
        }

        return ((absolutePeriod - 1) % loopLength.Value) + 1;
    }

    private static bool IsWithinAnyWindow(int period, IReadOnlyList<PeriodWindow> windows)
    {
        if (windows.Count == 0)
        {
            return true;
        }

        return windows.Any(window => IsWithinWindow(period, window));
    }

    private static bool IsProductionActive(NodeTrafficProfile profile, int period)
    {
        return IsWithinAnyWindow(period, profile.ProductionWindows, profile.ProductionStartPeriod, profile.ProductionEndPeriod);
    }

    private static bool IsConsumptionActive(NodeTrafficProfile profile, int period)
    {
        return IsWithinAnyWindow(period, profile.ConsumptionWindows, profile.ConsumptionStartPeriod, profile.ConsumptionEndPeriod);
    }

    private static bool IsWithinAnyWindow(int period, IReadOnlyList<PeriodWindow> windows, int? legacyStartPeriod, int? legacyEndPeriod)
    {
        if (windows.Count > 0)
        {
            return IsWithinAnyWindow(period, windows);
        }

        return IsWithinWindow(period, new PeriodWindow
        {
            StartPeriod = legacyStartPeriod,
            EndPeriod = legacyEndPeriod
        });
    }

    private static bool IsWithinWindow(int period, PeriodWindow window)
    {
        if (window.StartPeriod.HasValue && period < window.StartPeriod.Value)
        {
            return false;
        }

        if (window.EndPeriod.HasValue && period > window.EndPeriod.Value)
        {
            return false;
        }

        return true;
    }

    private static int GetEdgePeriods(EdgeModel edge)
    {
        return Math.Max(1, (int)Math.Ceiling(edge.Time));
    }

    private static List<RouteCandidate> BuildCandidateRoutes(
        TemporalTrafficContext context,
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
        TemporalTrafficContext context,
        string producerNodeId,
        string consumerNodeId,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        var distances = new Dictionary<string, double>(Comparer) { [producerNodeId] = 0d };
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
                if (!remainingCapacityByEdgeId.TryGetValue(arc.EdgeId, out var remainingCapacity) || remainingCapacity <= Epsilon)
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

    private static double GetPathRemainingCapacity(IReadOnlyList<string> pathResourceIds, IDictionary<string, double> remainingCapacityById)
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

    private static void ReserveCapacity(IEnumerable<string> pathResourceIds, IDictionary<string, double> remainingCapacityById, double quantity)
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

    private static double GetCapacityBidPerUnit(TemporalTrafficContext context, string consumerNodeId)
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

    public sealed class TemporalSimulationState
    {
        public int CurrentPeriod { get; set; }

        public Dictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> NodeStates { get; } = new(TemporalNodeTrafficKey.Comparer);

        public List<TemporalInFlightMovement> InFlightMovements { get; } = [];

        public Dictionary<string, double> OccupiedEdgeCapacity { get; } = new(Comparer);

        public Dictionary<string, double> OccupiedTranshipmentCapacity { get; } = new(Comparer);

        public TemporalNodeTrafficState GetOrCreateNodeTrafficState(string nodeId, string trafficType)
        {
            var key = new TemporalNodeTrafficKey(nodeId, trafficType);
            if (!NodeStates.TryGetValue(key, out var state))
            {
                state = new TemporalNodeTrafficState();
                NodeStates[key] = state;
            }

            return state;
        }
    }

    public sealed record TemporalSimulationStepResult(
        int Period,
        IReadOnlyList<RouteAllocation> Allocations,
        IReadOnlyDictionary<string, EdgeFlowVisualSummary> EdgeFlows,
        IReadOnlyDictionary<string, NodeFlowVisualSummary> NodeFlows,
        IReadOnlyDictionary<TemporalNodeTrafficKey, TemporalNodeStateSnapshot> NodeStates,
        IReadOnlyDictionary<string, double> EdgeOccupancy,
        IReadOnlyDictionary<string, double> TranshipmentOccupancy,
        int EffectivePeriod,
        int InFlightMovementCount);

    public readonly record struct TemporalNodeStateSnapshot(double AvailableSupply, double DemandBacklog, double StoreInventory);

    public readonly record struct TemporalNodeTrafficKey(string NodeId, string TrafficType)
    {
        public static IEqualityComparer<TemporalNodeTrafficKey> Comparer { get; } = new TemporalNodeTrafficKeyComparer();

        private sealed class TemporalNodeTrafficKeyComparer : IEqualityComparer<TemporalNodeTrafficKey>
        {
            public bool Equals(TemporalNodeTrafficKey x, TemporalNodeTrafficKey y)
            {
                return string.Equals(x.NodeId, y.NodeId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.TrafficType, y.TrafficType, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(TemporalNodeTrafficKey obj)
            {
                return HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.NodeId),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TrafficType));
            }
        }
    }

    public sealed class TemporalNodeTrafficState
    {
        public double AvailableSupply { get; set; }

        public double DemandBacklog { get; set; }

        public double StoreInventory { get; set; }

        public double ReservedStoreReceipts { get; set; }

        public TemporalNodeTrafficState Clone()
        {
            return new TemporalNodeTrafficState
            {
                AvailableSupply = AvailableSupply,
                DemandBacklog = DemandBacklog,
                StoreInventory = StoreInventory,
                ReservedStoreReceipts = ReservedStoreReceipts
            };
        }
    }

    public sealed class TemporalInFlightMovement
    {
        public string TrafficType { get; init; } = string.Empty;

        public double Quantity { get; init; }

        public List<string> PathNodeIds { get; init; } = [];

        public List<string> PathNodeNames { get; init; } = [];

        public List<string> PathEdgeIds { get; init; } = [];

        public int CurrentEdgeIndex { get; set; }

        public int RemainingPeriodsOnCurrentEdge { get; set; }

        public bool IsWaitingBetweenEdges { get; set; }

        public TemporalInFlightMovement Clone()
        {
            return new TemporalInFlightMovement
            {
                TrafficType = TrafficType,
                Quantity = Quantity,
                PathNodeIds = PathNodeIds.ToList(),
                PathNodeNames = PathNodeNames.ToList(),
                PathEdgeIds = PathEdgeIds.ToList(),
                CurrentEdgeIndex = CurrentEdgeIndex,
                RemainingPeriodsOnCurrentEdge = RemainingPeriodsOnCurrentEdge,
                IsWaitingBetweenEdges = IsWaitingBetweenEdges
            };
        }
    }

    public readonly record struct EdgeFlowVisualSummary(double ForwardQuantity, double ReverseQuantity)
    {
        public static EdgeFlowVisualSummary Empty => new(0d, 0d);
    }

    public readonly record struct NodeFlowVisualSummary(double OutboundQuantity, double InboundQuantity)
    {
        public static NodeFlowVisualSummary Empty => new(0d, 0d);
    }

    private sealed record GraphArc(string EdgeId, string FromNodeId, string ToNodeId, double Time, double Cost);

    private sealed record PreviousStep(string PreviousNodeId, GraphArc Arc);

    private sealed record AvailableResourceCapacity(
        IReadOnlyDictionary<string, double> EdgeCapacityById,
        IReadOnlyDictionary<string, double> TranshipmentCapacityByNodeId);

    private sealed record RouteCandidate(
        TemporalTrafficContext Context,
        string ProducerNodeId,
        string ConsumerNodeId,
        IReadOnlyList<string> PathNodeIds,
        IReadOnlyList<string> PathEdgeIds,
        IReadOnlyList<string> PathTranshipmentNodeIds,
        double TotalTime,
        double TransitCostPerUnit,
        double TotalScore,
        double CapacityBidPerUnit);

    private sealed record TemporalTrafficContext(
        string TrafficType,
        RoutingPreference RoutingPreference,
        double CapacityBidPerUnit,
        IReadOnlyDictionary<string, NodeModel> NodesById,
        IReadOnlyDictionary<string, NodeTrafficProfile?> ProfilesByNodeId,
        IDictionary<string, double> Supply,
        IDictionary<string, double> Demand,
        IDictionary<string, double> CommittedSupply,
        IDictionary<string, double> CommittedDemand,
        ISet<string> StoreSupplyNodes,
        ISet<string> StoreDemandNodes,
        ISet<string> RecipeInputDemandNodes,
        List<RouteAllocation> Allocations);
}

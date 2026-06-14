using MedWNetworkSim.App.Models;
using System.Collections.Frozen;

namespace MedWNetworkSim.App.Services;

/// <summary>
/// An advanced simulation engine orchestrating dynamic, multi-period network operations over time.
/// While the standard <see cref="NetworkSimulationEngine"/> resolves immediate routing, this temporal engine manages chronological events,
/// long-running processes (in-flight movements), storage backlogs, scenario progression across multiple time windows,
/// and adaptive routing strategies.
/// </summary>
public sealed class TemporalNetworkSimulationEngine
{
    private readonly SimulationClock clock = new();
    private readonly ISimulationEventQueue eventQueue = new SimulationEventQueue();
    private readonly SimulationExecutionCache executionCache = new();
    private readonly TrafficEconomicSettlementService settlementService = new();
    /// <summary>
    /// Gets or sets the clock.
    /// </summary>

    public SimulationClock Clock => clock;
    /// <summary>
    /// Gets or sets the event queue.
    /// </summary>

    public ISimulationEventQueue EventQueue => eventQueue;
    private const double Epsilon = 0.000001d;
    private const double PerishabilityPriorityBidFactor = 100d;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    /// <summary>
    /// Executes the initialize operation.
    /// </summary>

    public TemporalSimulationState Initialize(NetworkModel network)
    {
        ArgumentNullException.ThrowIfNull(network);
        network = executionCache.GetStaticContext(network).EffectiveNetwork;

        var state = new TemporalSimulationState();
        var definitionsByTraffic = network.TrafficTypes.ToDictionary(definition => definition.Name, definition => definition, Comparer);
        foreach (var node in network.Nodes)
        {
            foreach (var profile in node.TrafficProfiles)
            {
                var nodeState = state.GetOrCreateNodeTrafficState(node.Id, profile.TrafficType);
                if (profile.IsStore && profile.Inventory > Epsilon)
                {
                    var initialInventory = profile.StoreCapacity.HasValue
                        ? Math.Min(profile.Inventory, profile.StoreCapacity.Value)
                        : profile.Inventory;
                    nodeState.BlendStoreInventory(
                        initialInventory,
                        profile.UnitPrice,
                        GetPerishabilityPeriods(definitionsByTraffic, profile.TrafficType));
                }
            }
        }

        return state;
    }
    /// <summary>
    /// Executes the advance operation.
    /// </summary>

    public TemporalSimulationStepResult Advance(NetworkModel network, TemporalSimulationState? currentState)
    {
        return Advance(network, currentState, new SimulationRunOptions());
    }
    /// <summary>
    /// Executes the advance operation.
    /// </summary>

    public TemporalSimulationStepResult Advance(NetworkModel network, TemporalSimulationState? currentState, SimulationRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(network);
        options ??= new SimulationRunOptions();
        clock.DeltaTime = options.DeltaTime > 0d ? options.DeltaTime : 1d;
        var state = currentState ?? Initialize(network);
        var context = new SimulationContext
        {
            Network = network,
            TemporalState = state,
            Options = options
        };

        foreach (var scheduled in eventQueue.DequeueDueEvents(clock.CurrentTime))
        {
            scheduled.Execute(context);
        }
        var nextPeriod = state.CurrentPeriod + 1;
        var effectivePeriod = GetEffectivePeriod(nextPeriod, network.TimelineLoopLength);
        var compiledContext = executionCache.GetTemporalContext(network, effectivePeriod);
        var effectiveNetwork = compiledContext.EffectiveNetwork;
        var copyStateBeforeAdvance = options.CopyStateBeforeAdvance;
        var nodeStates = state.NodeStates;
        var movements = state.InFlightMovements;
        var occupiedEdgeCapacity = state.OccupiedEdgeCapacity;
        var occupiedEdgeTrafficCapacity = state.OccupiedEdgeTrafficCapacity;
        var occupiedTranshipmentCapacity = state.OccupiedTranshipmentCapacity;

        if (copyStateBeforeAdvance)
        {
            // Bolt: Replaced LINQ ToDictionary() with manual loop to avoid enumerator and delegate allocations on hot path.
            var newNodeStates = new Dictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState>(state.NodeStates.Count, TemporalNodeTrafficKey.Comparer);
            foreach (var pair in state.NodeStates)
            {
                newNodeStates[pair.Key] = pair.Value.Clone();
            }
            nodeStates = newNodeStates;

            // Bolt: Replaced LINQ Select().ToList() with manual loop to avoid enumerator and delegate allocations on hot path.
            var newMovements = new List<TemporalInFlightMovement>(state.InFlightMovements.Count);
            foreach (var movement in state.InFlightMovements)
            {
                newMovements.Add(movement.Clone());
            }
            movements = newMovements;

            var newOccupiedEdgeCapacity = new Dictionary<string, double>(state.OccupiedEdgeCapacity.Count, Comparer);
            foreach (var pair in state.OccupiedEdgeCapacity)
            {
                newOccupiedEdgeCapacity[pair.Key] = pair.Value;
            }
            occupiedEdgeCapacity = newOccupiedEdgeCapacity;

            var newOccupiedEdgeTrafficCapacity = new Dictionary<EdgeTrafficResourceKey, double>(state.OccupiedEdgeTrafficCapacity.Count, EdgeTrafficResourceKey.Comparer);
            foreach (var pair in state.OccupiedEdgeTrafficCapacity)
            {
                newOccupiedEdgeTrafficCapacity[pair.Key] = pair.Value;
            }
            occupiedEdgeTrafficCapacity = newOccupiedEdgeTrafficCapacity;

            var newOccupiedTranshipmentCapacity = new Dictionary<string, double>(state.OccupiedTranshipmentCapacity.Count, Comparer);
            foreach (var pair in state.OccupiedTranshipmentCapacity)
            {
                newOccupiedTranshipmentCapacity[pair.Key] = pair.Value;
            }
            occupiedTranshipmentCapacity = newOccupiedTranshipmentCapacity;
        }

        var newlyAllocatedMovements = new List<TemporalInFlightMovement>();
        var nodeLookup = compiledContext.NodesById;
        var edgeLookup = compiledContext.EdgesById;
        var definitionsByTraffic = compiledContext.TrafficDefinitionsByName;
        // Pressure is a per-period derived metric, not a persisted simulation state variable.
        // Each Advance(...) call starts with fresh accumulators and only records current-step adverse conditions.
        var nodePressure = new Dictionary<string, PressureAccumulator>(Comparer);
        var edgePressure = new Dictionary<string, PressureAccumulator>(Comparer);
        var pressureEvents = new List<PressureEvent>();

        ExpireNodeTraffic(nodeStates, nodePressure, pressureEvents, nextPeriod);
        ExpireInFlightMovements(movements, occupiedEdgeCapacity, occupiedEdgeTrafficCapacity, occupiedTranshipmentCapacity, edgePressure, nodePressure, pressureEvents, nextPeriod);

        if (options.EnableInvariantValidation)
        {
            ValidateResourceOccupancy(edgeLookup, nodeLookup, occupiedEdgeCapacity, occupiedTranshipmentCapacity);
            ValidateMovementResourceClaims(effectiveNetwork, edgeLookup, movements, occupiedEdgeCapacity, occupiedEdgeTrafficCapacity, occupiedTranshipmentCapacity);
        }

        AddScheduledNodeChanges(effectiveNetwork, definitionsByTraffic, nodeStates, effectivePeriod);

        var availableResources = BuildAvailableResourceCapacity(effectiveNetwork, movements, occupiedEdgeCapacity, occupiedTranshipmentCapacity);
        var plannedAllocations = PlanNewAllocations(
            compiledContext,
            nodeStates,
            nextPeriod,
            effectivePeriod,
            availableResources.EdgeCapacityById,
            availableResources.TranshipmentCapacityByNodeId,
            occupiedEdgeTrafficCapacity);

        var edgeFlowById = new Dictionary<string, EdgeFlowVisualSummary>(Comparer);
        var nodeFlowById = new Dictionary<string, NodeFlowVisualSummary>(Comparer);

        var newlyStartedMovements = new List<TemporalInFlightMovement>();

        foreach (var allocation in plannedAllocations)
        {
            var perishabilityPeriods = GetPerishabilityPeriods(definitionsByTraffic, allocation.TrafficType);

            var movement = new TemporalInFlightMovement
            {
                TrafficType = allocation.TrafficType,
                Quantity = allocation.Quantity,
                PathNodeIds = allocation.PathNodeIds.ToList(),
                PathNodeNames = allocation.PathNodeNames.ToList(),
                PathEdgeIds = allocation.PathEdgeIds.ToList(),
                SourceUnitCostPerUnit = allocation.SourceUnitCostPerUnit,
                LandedUnitCostPerUnit = allocation.DeliveredCostPerUnit,
                CurrentEdgeIndex = 0,
                RemainingPeriodsOnCurrentEdge = GetEdgePeriods(edgeLookup[allocation.PathEdgeIds[0]]),
                RemainingShelfLifePeriods = perishabilityPeriods
            };

            ClaimCurrentMovementResources(effectiveNetwork, edgeLookup, nodeLookup, movement, occupiedEdgeCapacity, occupiedEdgeTrafficCapacity, occupiedTranshipmentCapacity);
            newlyStartedMovements.Add(movement);
        }



        for (var movementIndex = movements.Count - 1; movementIndex >= 0; movementIndex--)
        {
            var movement = movements[movementIndex];
            if (movement.PathEdgeIds.Count == 0 || movement.CurrentEdgeIndex >= movement.PathEdgeIds.Count)
            {
                continue;
            }

            if (movement.IsWaitingBetweenEdges)
            {
                if (!TryMoveMovementToNextEdge(effectiveNetwork, edgeLookup, nodeLookup, movement, occupiedEdgeCapacity, occupiedEdgeTrafficCapacity, occupiedTranshipmentCapacity))
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
            movement.RemainingPeriodsOnCurrentEdge -= 1;

            if (movement.RemainingPeriodsOnCurrentEdge > 0)
            {
                continue;
            }

            var arrivalNodeId = movement.PathNodeIds[movement.CurrentEdgeIndex + 1];
            AddNodeArrival(nodeFlowById, arrivalNodeId, movement.Quantity);

            if (movement.CurrentEdgeIndex == movement.PathEdgeIds.Count - 1)
            {
                ReleaseCurrentMovementResources(movement, occupiedEdgeCapacity, occupiedEdgeTrafficCapacity, occupiedTranshipmentCapacity);
                CompleteArrival(nodeStates, nodeLookup, definitionsByTraffic, movement);
                movements.RemoveAt(movementIndex);
                continue;
            }

            TryMoveMovementToNextEdge(effectiveNetwork, edgeLookup, nodeLookup, movement, occupiedEdgeCapacity, occupiedEdgeTrafficCapacity, occupiedTranshipmentCapacity);
        }

        movements.AddRange(newlyStartedMovements);

        movements.AddRange(newlyAllocatedMovements);

        if (options.EnableInvariantValidation)
        {
            ValidateResourceOccupancy(edgeLookup, nodeLookup, occupiedEdgeCapacity, occupiedTranshipmentCapacity);
            ValidateMovementResourceClaims(effectiveNetwork, edgeLookup, movements, occupiedEdgeCapacity, occupiedEdgeTrafficCapacity, occupiedTranshipmentCapacity);
        }

        var edgeOccupancySnapshot = SnapshotResourceOccupancy(occupiedEdgeCapacity);
        var transhipmentOccupancySnapshot = SnapshotResourceOccupancy(occupiedTranshipmentCapacity);
        ApplyCapacityPressure(effectiveNetwork, edgeOccupancySnapshot, transhipmentOccupancySnapshot, edgePressure, nodePressure, pressureEvents, nextPeriod);

        foreach (var pair in nodeStates)
        {
            if (pair.Value.DemandBacklog <= Epsilon)
            {
                continue;
            }

            AddNodePressure(nodePressure, pair.Key.NodeId, pair.Key.TrafficType, PressureCauseKind.DemandBacklog, pair.Value.DemandBacklog, pressureEvents, nextPeriod);
        }

        // Bolt: Replaced LINQ chains (.Where, .Select, .ToHashSet, .Where) with manual loops to avoid delegate allocations.
        var allocationTargets = new HashSet<string>(Comparer);
        foreach (var allocation in plannedAllocations)
        {
            if (allocation.PathNodeIds.Count > 0)
            {
                allocationTargets.Add(allocation.PathNodeIds[^1]);
            }
        }

        foreach (var pair in nodeStates)
        {
            if (pair.Value.DemandBacklog <= Epsilon)
            {
                continue;
            }

            if (allocationTargets.Contains(pair.Key.NodeId))
            {
                continue;
            }

            AddNodePressure(nodePressure, pair.Key.NodeId, pair.Key.TrafficType, PressureCauseKind.RouteUnavailable, pair.Value.DemandBacklog, pressureEvents, nextPeriod, weight: 1.2d);
        }

        // Scores can decrease between periods when adverse causes shrink in later steps.
        // Relief is currently modeled indirectly (fewer future causes), not via explicit negative deltas.
        // Bolt: Replaced LINQ ToDictionary() with manual loops to avoid enumerator and delegate allocations on hot path.
        var nodePressureSnapshot = new Dictionary<string, NodePressureSnapshot>(nodePressure.Count, Comparer);
        foreach (var pair in nodePressure)
        {
            nodePressureSnapshot[pair.Key] = pair.Value.ToNodeSnapshot();
        }

        var edgePressureSnapshot = new Dictionary<string, EdgePressureSnapshot>(edgePressure.Count, Comparer);
        foreach (var pair in edgePressure)
        {
            edgePressureSnapshot[pair.Key] = pair.Value.ToEdgeSnapshot();
        }

        state.CurrentPeriod = nextPeriod;
        if (copyStateBeforeAdvance)
        {
            state.NodeStates.Clear();
            foreach (var pair in nodeStates)
            {
                state.NodeStates[pair.Key] = pair.Value;
            }

            state.InFlightMovements.Clear();
            state.InFlightMovements.AddRange(movements);
            ReplacePositiveEntries(state.OccupiedEdgeCapacity, occupiedEdgeCapacity);
            ReplacePositiveEntries(state.OccupiedEdgeTrafficCapacity, occupiedEdgeTrafficCapacity);
            ReplacePositiveEntries(state.OccupiedTranshipmentCapacity, occupiedTranshipmentCapacity);
        }
        else
        {
            RemoveNonPositiveEntries(occupiedEdgeCapacity);
            RemoveNonPositiveEntries(occupiedEdgeTrafficCapacity);
            RemoveNonPositiveEntries(occupiedTranshipmentCapacity);
        }

        // Bolt: Replaced LINQ ToDictionary() with manual loops to avoid enumerator and delegate allocations on hot path.
        var nodeSnapshots = new Dictionary<TemporalNodeTrafficKey, TemporalNodeStateSnapshot>(nodeStates.Count, TemporalNodeTrafficKey.Comparer);
        foreach (var pair in nodeStates)
        {
            nodeSnapshots[pair.Key] = new TemporalNodeStateSnapshot(pair.Value.AvailableSupply, pair.Value.DemandBacklog, pair.Value.StoreInventory);
        }

        clock.Advance(options.DeltaTime);
        var settledAllocations = SettleAllocations(effectiveNetwork, plannedAllocations);

        return new TemporalSimulationStepResult(
            nextPeriod,
            settledAllocations,
            edgeFlowById,
            nodeFlowById,
            nodeSnapshots,
            edgeOccupancySnapshot,
            transhipmentOccupancySnapshot,
            effectivePeriod,
            movements.Count,
            nodePressureSnapshot,
            edgePressureSnapshot,
            pressureEvents);
    }

    private IReadOnlyList<RouteAllocation> SettleAllocations(NetworkModel network, IReadOnlyList<RouteAllocation> allocations)
    {
        if (allocations.Count == 0)
        {
            return allocations;
        }

        var groups = new Dictionary<string, List<RouteAllocation>>(Comparer);
        var orderedKeys = new List<string>();
        foreach (var allocation in allocations)
        {
            if (!groups.TryGetValue(allocation.TrafficType, out var list))
            {
                list = new List<RouteAllocation>();
                groups.Add(allocation.TrafficType, list);
                orderedKeys.Add(allocation.TrafficType);
            }
            list.Add(allocation);
        }

        var outcomes = new List<TrafficSimulationOutcome>(groups.Count);
        foreach (var key in orderedKeys)
        {
            var trafficAllocations = groups[key];
            double totalDelivered = 0d;
            foreach (var allocation in trafficAllocations)
            {
                totalDelivered += allocation.Quantity;
            }

            TrafficTypeDefinition? definition = null;
            foreach (var candidate in network.TrafficTypes)
            {
                if (Comparer.Equals(candidate.Name, key))
                {
                    definition = candidate;
                    break;
                }
            }

            outcomes.Add(new TrafficSimulationOutcome
            {
                TrafficType = key,
                RoutingPreference = definition?.RoutingPreference ?? trafficAllocations[0].RoutingPreference,
                AllocationMode = definition?.AllocationMode ?? trafficAllocations[0].AllocationMode,
                TotalDelivered = totalDelivered,
                Allocations = trafficAllocations
            });
        }

        var settledOutcomes = settlementService.Settle(network, outcomes).Outcomes;
        var settledAllocations = new List<RouteAllocation>();
        foreach (var outcome in settledOutcomes)
        {
            settledAllocations.AddRange(outcome.Allocations);
        }
        return settledAllocations;
    }

    private static void ExpireNodeTraffic(
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates,
        IDictionary<string, PressureAccumulator> nodePressure,
        ICollection<PressureEvent> pressureEvents,
        int period)
    {
        foreach (var pair in nodeStates)
        {
            var delta = pair.Value.AdvancePerishability();
            if (delta.ExpiredAvailableSupply + delta.ExpiredStoreInventory <= Epsilon)
            {
                continue;
            }

            AddNodePressure(
                nodePressure,
                pair.Key.NodeId,
                pair.Key.TrafficType,
                PressureCauseKind.PerishedInNodeInventory,
                delta.ExpiredAvailableSupply + delta.ExpiredStoreInventory,
                pressureEvents,
                period,
                weight: 1.4d);
        }
    }

    private static void ExpireInFlightMovements(
        IList<TemporalInFlightMovement> movements,
        IDictionary<string, double> occupiedEdgeCapacity,
        IDictionary<EdgeTrafficResourceKey, double> occupiedEdgeTrafficCapacity,
        IDictionary<string, double> occupiedTranshipmentCapacity,
        IDictionary<string, PressureAccumulator> edgePressure,
        IDictionary<string, PressureAccumulator> nodePressure,
        ICollection<PressureEvent> pressureEvents,
        int period)
    {
        for (var index = movements.Count - 1; index >= 0; index--)
        {
            var movement = movements[index];
            if (!movement.RemainingShelfLifePeriods.HasValue)
            {
                continue;
            }

            movement.RemainingShelfLifePeriods -= 1;
            if (movement.RemainingShelfLifePeriods.Value > 0)
            {
                continue;
            }

            if (!movement.IsWaitingBetweenEdges)
            {
                ReleaseCurrentMovementResources(movement, occupiedEdgeCapacity, occupiedEdgeTrafficCapacity, occupiedTranshipmentCapacity);
                if (movement.CurrentEdgeIndex >= 0 && movement.CurrentEdgeIndex < movement.PathEdgeIds.Count)
                {
                    AddEdgePressure(
                        edgePressure,
                        movement.PathEdgeIds[movement.CurrentEdgeIndex],
                        movement.TrafficType,
                        PressureCauseKind.PerishedInTransit,
                        movement.Quantity,
                        pressureEvents,
                        period,
                        weight: 1.6d);
                }
            }
            else if (movement.CurrentEdgeIndex + 1 < movement.PathNodeIds.Count)
            {
                AddNodePressure(
                    nodePressure,
                    movement.PathNodeIds[movement.CurrentEdgeIndex + 1],
                    movement.TrafficType,
                    PressureCauseKind.PerishedInTransit,
                    movement.Quantity,
                    pressureEvents,
                    period,
                    weight: 1.6d);
            }

            movements.RemoveAt(index);
        }
    }

    private static void AddScheduledNodeChanges(
        NetworkModel network,
        IReadOnlyDictionary<string, TrafficTypeDefinition> definitionsByTraffic,
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
                    var production = CalculateAndConsumeProductionInputs(
                        node.Id,
                        profile,
                        definitionsByTraffic.GetValueOrDefault(profile.TrafficType),
                        nodeStates,
                        profilesByNodeAndTraffic.GetValueOrDefault(node.Id) ?? new Dictionary<string, NodeTrafficProfile>(Comparer));

                    state.BlendAvailableSupply(
                        production.OutputQuantity,
                        production.InheritedUnitCost,
                        GetPerishabilityPeriods(definitionsByTraffic, profile.TrafficType));
                }

                if (profile.Consumption > Epsilon && IsConsumptionActive(profile, period))
                {
                    if (profile.IsStore)
                    {
                        var consumedFromStore = Math.Min(state.StoreInventory, profile.Consumption);
                        state.ConsumeStoreInventory(consumedFromStore);
                        var unmetConsumption = profile.Consumption - consumedFromStore;
                        if (unmetConsumption > Epsilon)
                        {
                            state.DemandBacklog += unmetConsumption;
                        }
                    }
                    else
                    {
                        state.DemandBacklog += profile.Consumption;
                    }
                }
            }
        }
    }

    private static NetworkModel ApplyTimelineEventOverlay(NetworkModel network, int effectivePeriod)
    {
        // Bolt: Replaced LINQ .Where(...).ToList() with manual loop to avoid enumerator and delegate allocations.
        var activeEvents = new List<TimelineEventModel>();
        foreach (var timelineEvent in network.TimelineEvents)
        {
            if (IsTimelineEventActive(timelineEvent, effectivePeriod))
            {
                activeEvents.Add(timelineEvent);
            }
        }

        if (activeEvents.Count == 0)
        {
            return network;
        }

        var overlay = CloneNetworkForTimelineEvents(network);
        foreach (var timelineEvent in activeEvents)
        {
            foreach (var effect in timelineEvent.Effects)
            {
                ApplyTimelineEventEffect(overlay, effect);
            }
        }

        return overlay;
    }

    private static bool IsTimelineEventActive(TimelineEventModel timelineEvent, int effectivePeriod)
    {
        if (timelineEvent.StartPeriod.HasValue && effectivePeriod < timelineEvent.StartPeriod.Value)
        {
            return false;
        }

        if (timelineEvent.EndPeriod.HasValue && effectivePeriod > timelineEvent.EndPeriod.Value)
        {
            return false;
        }

        return true;
    }

    private static void ApplyTimelineEventEffect(NetworkModel network, TimelineEventEffectModel effect)
    {
        switch (effect.EffectType)
        {
            case TimelineEventEffectType.ProductionMultiplier:
                ApplyNodeTrafficMultiplier(network, effect, profile => profile.Production *= effect.Multiplier);
                break;

            case TimelineEventEffectType.ConsumptionMultiplier:
                ApplyNodeTrafficMultiplier(network, effect, profile => profile.Consumption *= effect.Multiplier);
                break;

            case TimelineEventEffectType.RouteCostMultiplier:
                ApplyEdgeMultiplier(network, effect);
                break;
        }
    }

    private static void ApplyNodeTrafficMultiplier(
        NetworkModel network,
        TimelineEventEffectModel effect,
        Action<NodeTrafficProfile> apply)
    {
        if (effect.NodeId is null || effect.TrafficType is null)
        {
            return;
        }

        var node = network.Nodes.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, effect.NodeId));
        var profile = node?.TrafficProfiles.FirstOrDefault(candidate => Comparer.Equals(candidate.TrafficType, effect.TrafficType));
        if (profile is not null)
        {
            apply(profile);
        }
    }

    private static void ApplyEdgeMultiplier(NetworkModel network, TimelineEventEffectModel effect)
    {
        if (effect.EdgeId is null)
        {
            return;
        }

        var edge = network.Edges.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, effect.EdgeId));
        if (edge is not null)
        {
            edge.Cost *= effect.Multiplier;
        }
    }

    private static double GetRequiredInputPerOutputUnit(
        string nodeId,
        string outputTrafficType,
        ProductionInputRequirement requirement)
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
                $"Node '{nodeId}' has an invalid recipe ratio for output traffic '{outputTrafficType}' and input traffic '{requirement.TrafficType}'.");
        }

        return inputQuantity / outputQuantity;
    }

    private static NetworkModel CloneNetworkForTimelineEvents(NetworkModel network)
    {
        return new NetworkModel
        {
            Name = network.Name,
            Description = network.Description,
            TimelineLoopLength = network.TimelineLoopLength,
            DefaultAllocationMode = network.DefaultAllocationMode,
            SimulationSeed = network.SimulationSeed,
            TrafficTypes = network.TrafficTypes.Select(CloneTrafficTypeDefinition).ToList(),
            TimelineEvents = network.TimelineEvents,
            RouteTaxRules = network.RouteTaxRules.Select(CloneRouteTaxRule).ToList(),
            Nodes = network.Nodes.Select(CloneNode).ToList(),
            Edges = network.Edges.Select(CloneEdge).ToList()
        };
    }

    private static RouteTaxRule CloneRouteTaxRule(RouteTaxRule rule)
    {
        return new RouteTaxRule
        {
            EdgeId = rule.EdgeId,
            TrafficType = rule.TrafficType,
            TaxRate = rule.TaxRate,
            TaxAuthorityActorId = rule.TaxAuthorityActorId,
            IsActive = rule.IsActive
        };
    }

    private static TrafficTypeDefinition CloneTrafficTypeDefinition(TrafficTypeDefinition definition)
    {
        return new TrafficTypeDefinition
        {
            Name = definition.Name,
            Description = definition.Description,
            RoutingPreference = definition.RoutingPreference,
            AllocationMode = definition.AllocationMode,
            RouteChoiceModel = definition.RouteChoiceModel,
            FlowSplitPolicy = definition.FlowSplitPolicy,
            RouteChoiceSettings = new RouteChoiceSettings
            {
                MaxCandidateRoutes = definition.RouteChoiceSettings.MaxCandidateRoutes,
                Priority = definition.RouteChoiceSettings.Priority,
                InformationAccuracy = definition.RouteChoiceSettings.InformationAccuracy,
                RouteDiversity = definition.RouteChoiceSettings.RouteDiversity,
                CongestionSensitivity = definition.RouteChoiceSettings.CongestionSensitivity,
                RerouteThreshold = definition.RouteChoiceSettings.RerouteThreshold,
                Stickiness = definition.RouteChoiceSettings.Stickiness,
                IterationCount = definition.RouteChoiceSettings.IterationCount,
                InternalizeCongestion = definition.RouteChoiceSettings.InternalizeCongestion
            },
            CapacityBidPerUnit = definition.CapacityBidPerUnit,
            DefaultUnitSalePrice = definition.DefaultUnitSalePrice,
            DefaultUnitProductionCost = definition.DefaultUnitProductionCost,
            SalesTaxRate = definition.SalesTaxRate,
            RouteTaxRate = definition.RouteTaxRate,
            PerishabilityPeriods = definition.PerishabilityPeriods
        };
    }

    private static NodeModel CloneNode(NodeModel node)
    {
        return new NodeModel
        {
            Id = node.Id,
            Name = node.Name,
            Shape = node.Shape,
            X = node.X,
            Y = node.Y,
            TranshipmentCapacity = node.TranshipmentCapacity,
            PlaceType = node.PlaceType,
            LoreDescription = node.LoreDescription,
            Tags = node.Tags.ToList(),
            TemplateId = node.TemplateId,
            TrafficProfiles = node.TrafficProfiles.Select(CloneProfile).ToList()
        };
    }

    private static NodeTrafficProfile CloneProfile(NodeTrafficProfile profile)
    {
        return new NodeTrafficProfile
        {
            TrafficType = profile.TrafficType,
            Production = profile.Production,
            Consumption = profile.Consumption,
            ConsumerPremiumPerUnit = profile.ConsumerPremiumPerUnit,
            CanTransship = profile.CanTransship,
            ProductionStartPeriod = profile.ProductionStartPeriod,
            ProductionEndPeriod = profile.ProductionEndPeriod,
            ConsumptionStartPeriod = profile.ConsumptionStartPeriod,
            ConsumptionEndPeriod = profile.ConsumptionEndPeriod,
            ProductionWindows = profile.ProductionWindows.Select(CloneWindow).ToList(),
            ConsumptionWindows = profile.ConsumptionWindows.Select(CloneWindow).ToList(),
            InputRequirements = profile.InputRequirements.Select(CloneInputRequirement).ToList(),
            IsStore = profile.IsStore,
            StoreCapacity = profile.StoreCapacity,
            Inventory = profile.Inventory,
            UnitPrice = profile.UnitPrice,
            ProductionCostPerUnit = profile.ProductionCostPerUnit,
            SalesTaxRate = profile.SalesTaxRate,
            HoldingCostPerTime = profile.HoldingCostPerTime,
            Revenue = profile.Revenue,
            Profit = profile.Profit,
            ShortagePenalty = profile.ShortagePenalty
        };
    }

    private static PeriodWindow CloneWindow(PeriodWindow window)
    {
        return new PeriodWindow
        {
            StartPeriod = window.StartPeriod,
            EndPeriod = window.EndPeriod
        };
    }

    private static ProductionInputRequirement CloneInputRequirement(ProductionInputRequirement requirement)
    {
        return new ProductionInputRequirement
        {
            TrafficType = requirement.TrafficType,
            InputQuantity = requirement.InputQuantity,
            OutputQuantity = requirement.OutputQuantity,
            QuantityPerOutputUnit = requirement.QuantityPerOutputUnit
        };
    }

    private static EdgeModel CloneEdge(EdgeModel edge)
    {
        return new EdgeModel
        {
            Id = edge.Id,
            FromNodeId = edge.FromNodeId,
            ToNodeId = edge.ToNodeId,
            Time = edge.Time,
            Cost = edge.Cost,
            Capacity = edge.Capacity,
            IsBidirectional = edge.IsBidirectional,
            RouteType = edge.RouteType,
            AccessNotes = edge.AccessNotes,
            SeasonalRisk = edge.SeasonalRisk,
            TollNotes = edge.TollNotes,
            SecurityNotes = edge.SecurityNotes
        };
    }

    private static List<RouteAllocation> PlanNewAllocations(
        CompiledNetworkSimulationContext compiledContext,
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates,
        int period,
        int effectivePeriod,
        IReadOnlyDictionary<string, double> availableCapacityByEdgeId,
        IReadOnlyDictionary<string, double> availableTranshipmentCapacityByNodeId,
        IReadOnlyDictionary<EdgeTrafficResourceKey, double> occupiedEdgeTrafficCapacity)
    {
        var network = compiledContext.EffectiveNetwork;
        var definitionsByTraffic = compiledContext.TrafficDefinitionsByName;
        // Bolt: Replaced LINQ ToDictionary() with manual loops to avoid enumerator and delegate allocations on hot path.
        var remainingCapacityByEdgeId = new Dictionary<string, double>(availableCapacityByEdgeId.Count, Comparer);
        foreach (var pair in availableCapacityByEdgeId)
        {
            remainingCapacityByEdgeId[pair.Key] = pair.Value;
        }

        var remainingTranshipmentCapacityByNodeId = new Dictionary<string, double>(availableTranshipmentCapacityByNodeId.Count, Comparer);
        foreach (var pair in availableTranshipmentCapacityByNodeId)
        {
            remainingTranshipmentCapacityByNodeId[pair.Key] = pair.Value;
        }

        var contexts = new List<TemporalTrafficContext>(compiledContext.OrderedTrafficNames.Length);
        for (var index = 0; index < compiledContext.OrderedTrafficNames.Length; index++)
        {
            var trafficType = compiledContext.OrderedTrafficNames[index];
            definitionsByTraffic.TryGetValue(trafficType, out var definition);
            contexts.Add(BuildTemporalContext(
                compiledContext,
                definition ?? new TrafficTypeDefinition { Name = trafficType, RoutingPreference = RoutingPreference.TotalCost },
                nodeStates,
                period,
                effectivePeriod,
                network.SimulationSeed + (index * 997)));
        }

        foreach (var context in contexts)
        {
            ApplyLocalAllocations(context, compiledContext, nodeStates);
        }

        var routingContexts = new List<RoutingTrafficContext>(contexts.Count);
        for (var index = 0; index < contexts.Count; index++)
        {
            routingContexts.Add(ToRoutingContext(contexts[index]));
        }

        MixedRoutingAllocator.Allocate(
            network,
            routingContexts,
            remainingCapacityByEdgeId,
            remainingTranshipmentCapacityByNodeId,
            occupiedEdgeTrafficByKey: occupiedEdgeTrafficCapacity,
            period: period,
            compiledContext: compiledContext);
        for (var index = 0; index < contexts.Count; index++)
        {
            contexts[index].Allocations.AddRange(routingContexts[index].Allocations);
            CopyCommittedQuantities(routingContexts[index].CommittedSupply, contexts[index].CommittedSupply);
            CopyCommittedQuantities(routingContexts[index].CommittedDemand, contexts[index].CommittedDemand);
        }

        foreach (var context in contexts)
        {
            ApplyCommittedState(context, nodeStates);
        }

        return contexts.SelectMany(context => context.Allocations).ToList();
    }

    private static TemporalTrafficContext BuildTemporalContext(
        CompiledNetworkSimulationContext compiledContext,
        TrafficTypeDefinition definition,
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates,
        int period,
        int effectivePeriod,
        int seed)
    {
        var network = compiledContext.EffectiveNetwork;
        var profilesByNodeId = compiledContext.NodeProfilesByTrafficType.TryGetValue(definition.Name, out var profiles)
            ? profiles
            : FrozenDictionary<string, NodeTrafficProfile?>.Empty;
        var nodesById = compiledContext.NodesById;
        var supply = new Dictionary<string, double>(Comparer);
        var supplyUnitCosts = new Dictionary<string, double>(Comparer);
        var demand = new Dictionary<string, double>(Comparer);
        var committedSupply = new Dictionary<string, double>(Comparer);
        var committedDemand = new Dictionary<string, double>(Comparer);
        var storeSupplyNodes = new HashSet<string>(Comparer);
        var storeDemandNodes = new HashSet<string>(Comparer);
        var recipeInputDemandNodes = new HashSet<string>(Comparer);

        var permittedSellerNodeIds = compiledContext.PermittedSellerNodeIdsByTrafficType.TryGetValue(definition.Name, out var permittedSellers)
            ? permittedSellers
            : FrozenSet<string>.Empty;
        var enforceSellLocal = LocalTrafficPermissionResolver.IsEnforced(network);

        foreach (var node in compiledContext.NodesByIndex)
        {
            profilesByNodeId.TryGetValue(node.Id, out var profile);
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

            if (availableSupply > Epsilon && (!enforceSellLocal || permittedSellerNodeIds.Contains(node.Id)))
            {
                supply[node.Id] = availableSupply;
                var supplyUnitCost = nodeState.AvailableSupplyUnitCostPerUnit;
                if (profile?.IsStore == true &&
                    profile.Production > Epsilon &&
                    IsProductionActive(profile, effectivePeriod))
                {
                    var storeQuantity = Math.Min(nodeState.StoreInventory, profile.Production);
                    supplyUnitCost = CalculateBlendedUnitCost(
                        nodeState.AvailableSupply,
                        nodeState.AvailableSupplyUnitCostPerUnit,
                        storeQuantity,
                        nodeState.StoreInventoryUnitCostPerUnit);
                }

                supplyUnitCosts[node.Id] = supplyUnitCost;
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
                    var targetDemand = profile.Consumption + nodeState.DemandBacklog;
                    availableDemand = Math.Min(targetDemand, spareCapacity);
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
            definition.AllocationMode,
            definition.RouteChoiceModel,
            definition.FlowSplitPolicy,
            definition.RouteChoiceSettings,
            GetCapacityBidPerUnit(definition),
            seed,
            nodesById,
            profilesByNodeId,
            compiledContext.MeetingDemandEligibleNodeIdsByTrafficType.TryGetValue(definition.Name, out var eligibleNodeIds)
                ? eligibleNodeIds
                : FrozenSet<string>.Empty,
            supply,
            supplyUnitCosts,
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
        CompiledNetworkSimulationContext compiledContext,
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates)
    {
        foreach (var nodeId in context.Supply.Keys.Intersect(context.Demand.Keys, Comparer).ToList())
        {
            if (!context.MeetingDemandEligibleNodeIds.Contains(nodeId))
            {
                continue;
            }

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
            state.ConsumeAvailableSupply(quantity);
            state.DemandBacklog = Math.Max(0d, state.DemandBacklog - quantity);
        }
    }

    private static RoutingTrafficContext ToRoutingContext(TemporalTrafficContext context)
    {
        return new RoutingTrafficContext
        {
            TrafficType = context.TrafficType,
            RoutingPreference = context.RoutingPreference,
            AllocationMode = context.AllocationMode,
            RouteChoiceModel = context.RouteChoiceModel,
            FlowSplitPolicy = context.FlowSplitPolicy,
            RouteChoiceSettings = context.RouteChoiceSettings,
            CapacityBidPerUnit = context.CapacityBidPerUnit,
            Seed = context.Seed,
            NodesById = context.NodesById,
            ProfilesByNodeId = context.ProfilesByNodeId,
            Supply = context.Supply.ToDictionary(pair => pair.Key, pair => pair.Value, Comparer),
            SupplyUnitCosts = context.SupplyUnitCosts.ToDictionary(pair => pair.Key, pair => pair.Value, Comparer),
            Demand = context.Demand.ToDictionary(pair => pair.Key, pair => pair.Value, Comparer),
            MeetingDemandEligibleNodeIds = context.MeetingDemandEligibleNodeIds
        };
    }

    private static void CopyCommittedQuantities(
        IReadOnlyDictionary<string, double> source,
        IDictionary<string, double> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = (target.TryGetValue(pair.Key, out var existing) ? existing : 0d) + pair.Value;
        }
    }

    private static void ReplacePositiveEntries<TKey>(
        IDictionary<TKey, double> target,
        IReadOnlyDictionary<TKey, double> source)
        where TKey : notnull
    {
        target.Clear();
        foreach (var pair in source)
        {
            if (pair.Value > Epsilon)
            {
                target[pair.Key] = pair.Value;
            }
        }
    }

    private static void RemoveNonPositiveEntries<TKey>(IDictionary<TKey, double> values)
        where TKey : notnull
    {
        var keysToRemove = new List<TKey>();
        foreach (var pair in values)
        {
            if (pair.Value <= Epsilon)
            {
                keysToRemove.Add(pair.Key);
            }
        }

        for (var index = 0; index < keysToRemove.Count; index++)
        {
            values.Remove(keysToRemove[index]);
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
            if (context.StoreSupplyNodes.Contains(pair.Key))
            {
                ConsumeLocalSupply(state, pair.Value, includeStoreInventory: true);
            }
            else
            {
                ConsumeLocalSupply(state, pair.Value, includeStoreInventory: false);
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
                state.DemandBacklog = Math.Max(0d, state.DemandBacklog - pair.Value);
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
        IReadOnlyDictionary<string, TrafficTypeDefinition> definitionsByTraffic,
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
            nodeState.BlendStoreInventory(
                movement.Quantity,
                movement.LandedUnitCostPerUnit,
                movement.RemainingShelfLifePeriods);
            nodeState.ReservedStoreReceipts = Math.Max(0d, nodeState.ReservedStoreReceipts - movement.Quantity);
            return;
        }

        if (IsRecipeInputTraffic(nodeLookup[finalNodeId], movement.TrafficType))
        {
            nodeState.BlendAvailableSupply(
                movement.Quantity,
                movement.LandedUnitCostPerUnit,
                movement.RemainingShelfLifePeriods);
        }
    }

    private static bool IsRecipeInputTraffic(NodeModel node, string trafficType)
    {
        // Bolt: Replaced LINQ .Where, .SelectMany, and .Any with standard foreach loops
        // to prevent delegate allocations and enumerator overhead in this frequently called method.
        foreach (var profile in node.TrafficProfiles)
        {
            if (profile.Production > Epsilon)
            {
                foreach (var requirement in profile.InputRequirements)
                {
                    if (Comparer.Equals(requirement.TrafficType, trafficType))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static ProductionResult CalculateAndConsumeProductionInputs(
        string nodeId,
        NodeTrafficProfile outputProfile,
        TrafficTypeDefinition? definition,
        IDictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> nodeStates,
        IReadOnlyDictionary<string, NodeTrafficProfile> profilesByTrafficType)
    {
        var outputQuantity = outputProfile.Production;
        var baseProductionCost = ResolveBaseProductionCost(outputProfile, definition);
        if (outputProfile.InputRequirements.Count == 0)
        {
            return new ProductionResult(outputQuantity, baseProductionCost);
        }

        foreach (var requirement in outputProfile.InputRequirements)
        {
            var inputPerOutputUnit = GetRequiredInputPerOutputUnit(nodeId, outputProfile.TrafficType, requirement);
            var availableInput = GetLocalInputQuantity(nodeId, requirement.TrafficType, nodeStates, profilesByTrafficType);
            outputQuantity = Math.Min(outputQuantity, availableInput / inputPerOutputUnit);
        }

        if (outputQuantity < Epsilon)
        {
            return new ProductionResult(0d, 0d);
        }

        var inheritedUnitCost = 0d;
        foreach (var requirement in outputProfile.InputRequirements)
        {
            var inputPerOutputUnit = GetRequiredInputPerOutputUnit(nodeId, outputProfile.TrafficType, requirement);
            var consumedUnitCost = ConsumeLocalInputQuantity(
                nodeId,
                requirement.TrafficType,
                outputQuantity * inputPerOutputUnit,
                nodeStates,
                profilesByTrafficType);

            inheritedUnitCost += consumedUnitCost * inputPerOutputUnit;
        }

        return new ProductionResult(outputQuantity, inheritedUnitCost + baseProductionCost);
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
            var inputPerOutputUnit = GetRequiredInputPerOutputUnit(nodeId, outputProfile.TrafficType, requirement);
            var requiredInput = outputProfile.Production * inputPerOutputUnit;
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

    private static void ConsumeLocalSupply(
        TemporalNodeTrafficState state,
        double quantity,
        bool includeStoreInventory)
    {
        var remaining = quantity;
        var supplyConsumed = Math.Min(state.AvailableSupply, remaining);
        state.ConsumeAvailableSupply(supplyConsumed);
        remaining -= supplyConsumed;

        if (includeStoreInventory && remaining > Epsilon)
        {
            var storeConsumed = Math.Min(state.StoreInventory, remaining);
            state.ConsumeStoreInventory(storeConsumed);
            remaining -= storeConsumed;
        }

        if (remaining > Epsilon)
        {
            throw new InvalidOperationException("Temporal supply commitment exceeded local supply.");
        }
    }

    private static double CalculateBlendedUnitCost(
        double firstQuantity,
        double firstUnitCost,
        double secondQuantity,
        double secondUnitCost)
    {
        var totalQuantity = Math.Max(0d, firstQuantity) + Math.Max(0d, secondQuantity);
        if (totalQuantity <= Epsilon)
        {
            return 0d;
        }

        return ((Math.Max(0d, firstQuantity) * firstUnitCost) + (Math.Max(0d, secondQuantity) * secondUnitCost)) / totalQuantity;
    }

    private static double ConsumeLocalInputQuantity(
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
        var totalCost = supplyConsumed * state.ConsumeAvailableSupply(supplyConsumed);
        remaining -= supplyConsumed;

        if (remaining > Epsilon && profilesByTrafficType.TryGetValue(trafficType, out var profile) && profile.IsStore)
        {
            var storeConsumed = Math.Min(state.StoreInventory, remaining);
            totalCost += storeConsumed * state.ConsumeStoreInventory(storeConsumed);
            remaining -= storeConsumed;
        }

        if (remaining > Epsilon)
        {
            throw new InvalidOperationException($"Node '{nodeId}' would over-consume precursor traffic '{trafficType}'.");
        }

        return quantity > Epsilon ? totalCost / quantity : 0d;
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

        // Bolt: Replaced LINQ ToDictionary() with manual loops to avoid enumerator and delegate allocations on hot path.
        var edgeCapacityDict = new Dictionary<string, double>(network.Edges.Count, Comparer);
        foreach (var edge in network.Edges)
        {
            edgeCapacityDict[edge.Id] = GetAvailableCapacity(edge.Capacity, edge.Id, occupiedEdgeCapacity, pendingEdgeClaims);
        }

        var nodeCapacityDict = new Dictionary<string, double>(network.Nodes.Count, Comparer);
        foreach (var node in network.Nodes)
        {
            nodeCapacityDict[node.Id] = GetAvailableCapacity(node.TranshipmentCapacity, node.Id, occupiedTranshipmentCapacity, pendingTranshipmentClaims);
        }

        return new AvailableResourceCapacity(edgeCapacityDict, nodeCapacityDict);
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
        NetworkModel network,
        IReadOnlyDictionary<string, EdgeModel> edgeLookup,
        IReadOnlyDictionary<string, NodeModel> nodeLookup,
        TemporalInFlightMovement movement,
        IDictionary<string, double> occupiedEdgeCapacity,
        IDictionary<EdgeTrafficResourceKey, double> occupiedEdgeTrafficCapacity,
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
            network,
            edgeLookup,
            nodeLookup,
            movement,
            nextEdgeIndex,
            occupiedEdgeCapacity,
            occupiedEdgeTrafficCapacity,
            occupiedTranshipmentCapacity,
            releasedEdgeId,
            releasedTranshipmentNodeId))
        {
            if (!movement.IsWaitingBetweenEdges)
            {
                ReleaseCurrentMovementResources(movement, occupiedEdgeCapacity, occupiedEdgeTrafficCapacity, occupiedTranshipmentCapacity);
                movement.IsWaitingBetweenEdges = true;
                movement.RemainingPeriodsOnCurrentEdge = 0;
            }

            return false;
        }

        if (!movement.IsWaitingBetweenEdges)
        {
            ReleaseCurrentMovementResources(movement, occupiedEdgeCapacity, occupiedEdgeTrafficCapacity, occupiedTranshipmentCapacity);
        }

        movement.CurrentEdgeIndex = nextEdgeIndex;
        movement.RemainingPeriodsOnCurrentEdge = GetEdgePeriods(edgeLookup[movement.PathEdgeIds[movement.CurrentEdgeIndex]]);
        movement.IsWaitingBetweenEdges = false;
        ClaimCurrentMovementResources(network, edgeLookup, nodeLookup, movement, occupiedEdgeCapacity, occupiedEdgeTrafficCapacity, occupiedTranshipmentCapacity);
        return true;
    }

    private static void ClaimCurrentMovementResources(
        NetworkModel network,
        IReadOnlyDictionary<string, EdgeModel> edgeLookup,
        IReadOnlyDictionary<string, NodeModel> nodeLookup,
        TemporalInFlightMovement movement,
        IDictionary<string, double> occupiedEdgeCapacity,
        IDictionary<EdgeTrafficResourceKey, double> occupiedEdgeTrafficCapacity,
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
            network,
            edgeLookup,
            nodeLookup,
            movement,
            movement.CurrentEdgeIndex,
            occupiedEdgeCapacity,
            occupiedEdgeTrafficCapacity,
            occupiedTranshipmentCapacity))
        {
            throw new InvalidOperationException("Movement cannot claim current resources without exceeding capacity.");
        }

        AddResourceQuantity(occupiedEdgeCapacity, edgeId, movement.Quantity);
        AddResourceQuantity(occupiedEdgeTrafficCapacity, new EdgeTrafficResourceKey(edgeId, movement.TrafficType), movement.Quantity);

        var transhipmentNodeId = GetCurrentTranshipmentNodeId(movement);
        if (transhipmentNodeId is not null)
        {
            AddResourceQuantity(occupiedTranshipmentCapacity, transhipmentNodeId, movement.Quantity);
        }
    }

    private static bool CanClaimMovementResourcesForEdgeIndex(
        NetworkModel network,
        IReadOnlyDictionary<string, EdgeModel> edgeLookup,
        IReadOnlyDictionary<string, NodeModel> nodeLookup,
        TemporalInFlightMovement movement,
        int edgeIndex,
        IDictionary<string, double> occupiedEdgeCapacity,
        IDictionary<EdgeTrafficResourceKey, double> occupiedEdgeTrafficCapacity,
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

        if (!CanClaimResource(
                GetEdgeTrafficCapacity(network, edge, movement.TrafficType),
                new EdgeTrafficResourceKey(edgeId, movement.TrafficType),
                movement.Quantity,
                occupiedEdgeTrafficCapacity,
                releasedEdgeQuantity))
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

    private static bool CanClaimResource<TKey>(
        double? nominalCapacity,
        TKey resourceId,
        double quantity,
        IDictionary<TKey, double> occupiedCapacity,
        double releasedQuantity = 0d)
        where TKey : notnull
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
        IDictionary<EdgeTrafficResourceKey, double> occupiedEdgeTrafficCapacity,
        IDictionary<string, double> occupiedTranshipmentCapacity)
    {
        if (movement.CurrentEdgeIndex < 0 || movement.CurrentEdgeIndex >= movement.PathEdgeIds.Count)
        {
            return;
        }

        ReleaseResourceQuantity(occupiedEdgeCapacity, movement.PathEdgeIds[movement.CurrentEdgeIndex], movement.Quantity);
        ReleaseResourceQuantity(
            occupiedEdgeTrafficCapacity,
            new EdgeTrafficResourceKey(movement.PathEdgeIds[movement.CurrentEdgeIndex], movement.TrafficType),
            movement.Quantity);

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

    private static void AddResourceQuantity<TKey>(IDictionary<TKey, double> occupiedCapacity, TKey resourceId, double quantity)
        where TKey : notnull
    {
        occupiedCapacity[resourceId] = (occupiedCapacity.TryGetValue(resourceId, out var existing) ? existing : 0d) + quantity;
    }

    private static void ReleaseResourceQuantity<TKey>(IDictionary<TKey, double> occupiedCapacity, TKey resourceId, double quantity)
        where TKey : notnull
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

    private static double? GetEdgeTrafficCapacity(NetworkModel network, EdgeModel edge, string trafficType)
    {
        var resolver = new EdgeTrafficPermissionResolver();
        var effective = resolver.Resolve(network, edge, trafficType);
        var allowed = resolver.GetAllowedCapacity(edge, effective);
        return double.IsPositiveInfinity(allowed) ? null : allowed;
    }

    private static IReadOnlyDictionary<string, double> SnapshotResourceOccupancy(IReadOnlyDictionary<string, double> occupiedCapacity)
    {
        // Bolt: Replaced LINQ .Where(...).ToDictionary(...) with manual loop to avoid enumerator and delegate allocations.
        var snapshot = new Dictionary<string, double>(Comparer);
        foreach (var pair in occupiedCapacity)
        {
            if (pair.Value > Epsilon)
            {
                snapshot[pair.Key] = pair.Value;
            }
        }

        return snapshot;
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
        NetworkModel network,
        IReadOnlyDictionary<string, EdgeModel> edgeLookup,
        IReadOnlyList<TemporalInFlightMovement> movements,
        IReadOnlyDictionary<string, double> occupiedEdgeCapacity,
        IReadOnlyDictionary<EdgeTrafficResourceKey, double> occupiedEdgeTrafficCapacity,
        IReadOnlyDictionary<string, double> occupiedTranshipmentCapacity)
    {
        var expectedEdgeCapacity = new Dictionary<string, double>(Comparer);
        var expectedEdgeTrafficCapacity = new Dictionary<EdgeTrafficResourceKey, double>(EdgeTrafficResourceKey.Comparer);
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
            AddResourceQuantity(
                expectedEdgeTrafficCapacity,
                new EdgeTrafficResourceKey(movement.PathEdgeIds[movement.CurrentEdgeIndex], movement.TrafficType),
                movement.Quantity);

            var transhipmentNodeId = GetCurrentTranshipmentNodeId(movement);
            if (transhipmentNodeId is not null)
            {
                AddResourceQuantity(expectedTranshipmentCapacity, transhipmentNodeId, movement.Quantity);
            }
        }

        ValidateExpectedOccupancy(expectedEdgeCapacity, occupiedEdgeCapacity, "edge");
        ValidateExpectedOccupancy(expectedEdgeTrafficCapacity, occupiedEdgeTrafficCapacity, "edge traffic");
        ValidateExpectedOccupancy(expectedTranshipmentCapacity, occupiedTranshipmentCapacity, "transhipment node");
        ValidateEdgeTrafficOccupancy(network, edgeLookup, occupiedEdgeTrafficCapacity);
    }

    private static void ValidateExpectedOccupancy<TKey>(
        IReadOnlyDictionary<TKey, double> expectedCapacity,
        IReadOnlyDictionary<TKey, double> actualCapacity,
        string resourceKind)
        where TKey : notnull
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

    private static void ValidateEdgeTrafficOccupancy(
        NetworkModel network,
        IReadOnlyDictionary<string, EdgeModel> edgeLookup,
        IReadOnlyDictionary<EdgeTrafficResourceKey, double> occupiedEdgeTrafficCapacity)
    {
        foreach (var pair in occupiedEdgeTrafficCapacity)
        {
            if (!edgeLookup.TryGetValue(pair.Key.EdgeId, out var edge))
            {
                throw new InvalidOperationException($"Occupied edge traffic resource '{pair.Key.EdgeId}/{pair.Key.TrafficType}' does not exist.");
            }

            var allowed = GetEdgeTrafficCapacity(network, edge, pair.Key.TrafficType);
            if (allowed.HasValue && pair.Value > allowed.Value + Epsilon)
            {
                throw new InvalidOperationException(
                    $"Occupied edge traffic resource '{pair.Key.EdgeId}/{pair.Key.TrafficType}' exceeds limit {allowed.Value} with occupancy {pair.Value}.");
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

    private static void ApplyCapacityPressure(
        NetworkModel network,
        IReadOnlyDictionary<string, double> edgeOccupancy,
        IReadOnlyDictionary<string, double> transhipmentOccupancy,
        IDictionary<string, PressureAccumulator> edgePressure,
        IDictionary<string, PressureAccumulator> nodePressure,
        ICollection<PressureEvent> pressureEvents,
        int period)
    {
        foreach (var edge in network.Edges)
        {
            if (!edge.Capacity.HasValue || edge.Capacity.Value <= Epsilon)
            {
                continue;
            }

            var occupancy = edgeOccupancy.GetValueOrDefault(edge.Id, 0d);
            var utilization = occupancy / edge.Capacity.Value;
            if (utilization < 0.95d || occupancy <= Epsilon)
            {
                continue;
            }

            AddEdgePressure(
                edgePressure,
                edge.Id,
                string.Empty,
                PressureCauseKind.EdgeCapacitySaturation,
                occupancy,
                pressureEvents,
                period);
        }

        foreach (var node in network.Nodes)
        {
            if (!node.TranshipmentCapacity.HasValue || node.TranshipmentCapacity.Value <= Epsilon)
            {
                continue;
            }

            var occupancy = transhipmentOccupancy.GetValueOrDefault(node.Id, 0d);
            var utilization = occupancy / node.TranshipmentCapacity.Value;
            if (utilization < 0.95d || occupancy <= Epsilon)
            {
                continue;
            }

            AddNodePressure(
                nodePressure,
                node.Id,
                string.Empty,
                PressureCauseKind.TranshipmentCapacitySaturation,
                occupancy,
                pressureEvents,
                period);
        }
    }

    private static void AddNodePressure(
        IDictionary<string, PressureAccumulator> pressureByNodeId,
        string nodeId,
        string trafficType,
        PressureCauseKind cause,
        double quantity,
        ICollection<PressureEvent> pressureEvents,
        int period,
        double weight = 1d)
    {
        if (quantity <= Epsilon || string.IsNullOrWhiteSpace(nodeId))
        {
            return;
        }

        if (!pressureByNodeId.TryGetValue(nodeId, out var accumulator))
        {
            accumulator = new PressureAccumulator();
            pressureByNodeId[nodeId] = accumulator;
        }

        accumulator.Add(cause, quantity, weight);
        pressureEvents.Add(new PressureEvent(period, nodeId, IsEdge: false, trafficType, cause, quantity, quantity * weight, string.Empty));
    }

    private static void AddEdgePressure(
        IDictionary<string, PressureAccumulator> pressureByEdgeId,
        string edgeId,
        string trafficType,
        PressureCauseKind cause,
        double quantity,
        ICollection<PressureEvent> pressureEvents,
        int period,
        double weight = 1d)
    {
        if (quantity <= Epsilon || string.IsNullOrWhiteSpace(edgeId))
        {
            return;
        }

        if (!pressureByEdgeId.TryGetValue(edgeId, out var accumulator))
        {
            accumulator = new PressureAccumulator();
            pressureByEdgeId[edgeId] = accumulator;
        }

        accumulator.Add(cause, quantity, weight);
        pressureEvents.Add(new PressureEvent(period, edgeId, IsEdge: true, trafficType, cause, quantity, quantity * weight, string.Empty));
    }
    /// <summary>
    /// Retrieves the effective period based on the provided parameters.
    /// </summary>

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
            // Check if there are any valid consumers left (excluding the producer itself).
            if (activeConsumers.Count == 0 || (activeConsumers.Count == 1 && activeConsumers.Contains(producerNodeId)))
            {
                continue;
            }

            var routesToConsumers = FindBestRoutes(
                context,
                producerNodeId,
                activeConsumers,
                adjacency,
                remainingCapacityByEdgeId,
                remainingTranshipmentCapacityByNodeId);

            routes.AddRange(routesToConsumers.Values);
        }

        return routes;
    }

    private static IReadOnlyDictionary<string, RouteCandidate> FindBestRoutes(
        TemporalTrafficContext context,
        string producerNodeId,
        IReadOnlySet<string> consumerNodeIds,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        // A batched Dijkstra pass finds the best currently-feasible routes to all targeted consumers.
        var distances = new Dictionary<string, double>(Comparer) { [producerNodeId] = 0d };
        var previous = new Dictionary<string, PreviousStep>(Comparer);
        var queue = new PriorityQueue<string, double>();
        queue.Enqueue(producerNodeId, 0d);

        var consumersToFind = consumerNodeIds.Count;
        if (consumerNodeIds.Contains(producerNodeId))
        {
            consumersToFind--;
        }

        while (queue.TryDequeue(out var currentNodeId, out var currentDistance))
        {
            if (currentDistance > distances[currentNodeId] + Epsilon)
            {
                continue;
            }

            if (consumerNodeIds.Contains(currentNodeId) && !Comparer.Equals(currentNodeId, producerNodeId))
            {
                consumersToFind--;
            }

            if (consumersToFind <= 0)
            {
                break;
            }

            if (!adjacency.TryGetValue(currentNodeId, out var arcs))
            {
                continue;
            }

            // Before expanding out of the current node (except the origin), check if transshipment is allowed.
            if (!Comparer.Equals(currentNodeId, producerNodeId))
            {
                if (!context.ProfilesByNodeId.TryGetValue(currentNodeId, out var profile) || profile?.CanTransship != true)
                {
                    continue;
                }

                if (remainingTranshipmentCapacityByNodeId.TryGetValue(currentNodeId, out var transhipmentCapacity) && transhipmentCapacity <= Epsilon)
                {
                    continue;
                }
            }

            foreach (var arc in arcs)
            {
                if (!remainingCapacityByEdgeId.TryGetValue(arc.EdgeId, out var remainingCapacity) || remainingCapacity <= Epsilon)
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

        var results = new Dictionary<string, RouteCandidate>(Comparer);

        foreach (var consumerNodeId in consumerNodeIds)
        {
            if (Comparer.Equals(consumerNodeId, producerNodeId) || !distances.ContainsKey(consumerNodeId))
            {
                continue;
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

            var pathEdgeIds = new List<string>(pathArcs.Count);
            var totalTime = 0d;
            var totalCost = 0d;
            var totalScore = 0d;
            foreach (var arc in pathArcs)
            {
                pathEdgeIds.Add(arc.EdgeId);
                totalTime += arc.Time;
                totalCost += arc.Cost;
                totalScore += Score(arc.Time, arc.Cost, context.RoutingPreference);
            }

            results[consumerNodeId] = new RouteCandidate(
                context,
                producerNodeId,
                consumerNodeId,
                pathNodeIds,
                pathEdgeIds,
                pathTranshipmentNodeIds,
                totalTime,
                totalCost,
                totalScore,
                GetCapacityBidPerUnit(context, consumerNodeId));
        }

        return results;
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

        double minCapacity = double.PositiveInfinity;
        // Bolt: Replaced LINQ Select/Min with a for loop to avoid delegate and enumerator allocations in hot path
        for (int i = 0; i < pathResourceIds.Count; i++)
        {
            var remainingCapacity = remainingCapacityById.TryGetValue(pathResourceIds[i], out var capacity) ? capacity : 0d;
            if (remainingCapacity < minCapacity)
            {
                minCapacity = remainingCapacity;
            }
        }

        return minCapacity == double.PositiveInfinity ? 0d : minCapacity;
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
        int count = 0;
        // Bolt: Replaced LINQ Count with foreach to avoid delegate allocations and closure overhead
        foreach (var resourceId in pathResourceIds)
        {
            if (remainingCapacityById.TryGetValue(resourceId, out var remainingCapacity) &&
                !double.IsPositiveInfinity(remainingCapacity) &&
                remainingCapacity <= routeCapacity + Epsilon)
            {
                count++;
            }
        }
        return count;
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
        // Bolt: Replaced LINQ queries (.Select, .Where, .GroupBy, .SelectMany, .Concat)
        // with standard loops and list sorting to avoid significant enumerator and delegate allocations
        var definitionsList = new List<(TrafficTypeDefinition Definition, int Index)>();
        var definitionsSeen = new HashSet<string>(Comparer);

        for (int i = 0; i < network.TrafficTypes.Count; i++)
        {
            var definition = network.TrafficTypes[i];
            if (!string.IsNullOrWhiteSpace(definition.Name) && definitionsSeen.Add(definition.Name))
            {
                definitionsList.Add((definition, i));
            }
        }

        definitionsList.Sort((a, b) =>
        {
            var aHasPerishability = a.Definition.PerishabilityPeriods.HasValue;
            var bHasPerishability = b.Definition.PerishabilityPeriods.HasValue;
            if (aHasPerishability != bHasPerishability)
                return aHasPerishability ? -1 : 1;

            var perishCmp = (a.Definition.PerishabilityPeriods ?? int.MaxValue).CompareTo(b.Definition.PerishabilityPeriods ?? int.MaxValue);
            if (perishCmp != 0) return perishCmp;

            var aPriority = a.Definition.RouteChoiceSettings?.Priority ?? 0d;
            var bPriority = b.Definition.RouteChoiceSettings?.Priority ?? 0d;
            var priorityCmp = bPriority.CompareTo(aPriority);
            if (priorityCmp != 0) return priorityCmp;

            return a.Index.CompareTo(b.Index);
        });

        var result = new List<string>(definitionsList.Count);
        var resultSeen = new HashSet<string>(Comparer);
        foreach (var item in definitionsList)
        {
            result.Add(item.Definition.Name);
            resultSeen.Add(item.Definition.Name);
        }

        var undeclaredNames = new HashSet<string>(Comparer);
        foreach (var node in network.Nodes)
        {
            foreach (var profile in node.TrafficProfiles)
            {
                if (!string.IsNullOrWhiteSpace(profile.TrafficType) && !resultSeen.Contains(profile.TrafficType))
                {
                    undeclaredNames.Add(profile.TrafficType);
                }

                foreach (var req in profile.InputRequirements)
                {
                    if (!string.IsNullOrWhiteSpace(req.TrafficType) && !resultSeen.Contains(req.TrafficType))
                    {
                        undeclaredNames.Add(req.TrafficType);
                    }
                }
            }
        }

        if (undeclaredNames.Count > 0)
        {
            var undeclaredList = undeclaredNames.ToList();
            undeclaredList.Sort(Comparer);
            result.AddRange(undeclaredList);
        }

        return result;
    }

    private static int? GetPerishabilityPeriods(
        IReadOnlyDictionary<string, TrafficTypeDefinition> definitionsByTraffic,
        string trafficType)
    {
        if (!definitionsByTraffic.TryGetValue(trafficType, out var definition))
        {
            return null;
        }

        if (!definition.PerishabilityPeriods.HasValue || definition.PerishabilityPeriods.Value <= 0)
        {
            return null;
        }

        return definition.PerishabilityPeriods.Value;
    }

    private static double GetCapacityBidPerUnit(TrafficTypeDefinition definition)
    {
        var baseBid = definition.CapacityBidPerUnit.HasValue
            ? Math.Max(0d, definition.CapacityBidPerUnit.Value)
            : definition.RoutingPreference == RoutingPreference.Speed
                ? 1d
                : 0d;

        var perishabilityBonus = definition.PerishabilityPeriods.HasValue && definition.PerishabilityPeriods.Value > 0
            ? PerishabilityPriorityBidFactor / definition.PerishabilityPeriods.Value
            : 0d;

        return baseBid + perishabilityBonus;
    }

    private static double ResolveBaseProductionCost(NodeTrafficProfile? profile, TrafficTypeDefinition? definition)
    {
        if (profile?.ProductionCostPerUnit is { } profileCost)
        {
            return Math.Max(0d, profileCost);
        }

        return Math.Max(0d, definition?.DefaultUnitProductionCost ?? 0d);
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
    /// <summary>
    /// Represents the temporal simulation state component.
    /// </summary>

    public sealed class TemporalSimulationState
    {
        /// <summary>
        /// Gets or sets the current period.
        /// </summary>
        public int CurrentPeriod { get; set; }
        /// <summary>
        /// Gets or sets the node states.
        /// </summary>

        public Dictionary<TemporalNodeTrafficKey, TemporalNodeTrafficState> NodeStates { get; } = new(TemporalNodeTrafficKey.Comparer);
        /// <summary>
        /// Gets the collection of in flight movements associated with this entity.
        /// </summary>

        public List<TemporalInFlightMovement> InFlightMovements { get; } = [];
        /// <summary>
        /// Gets or sets the occupied edge capacity.
        /// </summary>

        public Dictionary<string, double> OccupiedEdgeCapacity { get; } = new(Comparer);
        /// <summary>
        /// Gets or sets the occupied edge traffic capacity.
        /// </summary>

        public Dictionary<EdgeTrafficResourceKey, double> OccupiedEdgeTrafficCapacity { get; } = new(EdgeTrafficResourceKey.Comparer);
        /// <summary>
        /// Gets or sets the occupied transhipment capacity.
        /// </summary>

        public Dictionary<string, double> OccupiedTranshipmentCapacity { get; } = new(Comparer);
        /// <summary>
        /// Retrieves the or create node traffic state based on the provided parameters.
        /// </summary>

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
    /// <summary>
    /// Represents the temporal simulation step result component.
    /// </summary>

    public sealed record TemporalSimulationStepResult(
        int Period,
        IReadOnlyList<RouteAllocation> Allocations,
        IReadOnlyDictionary<string, EdgeFlowVisualSummary> EdgeFlows,
        IReadOnlyDictionary<string, NodeFlowVisualSummary> NodeFlows,
        IReadOnlyDictionary<TemporalNodeTrafficKey, TemporalNodeStateSnapshot> NodeStates,
        IReadOnlyDictionary<string, double> EdgeOccupancy,
        IReadOnlyDictionary<string, double> TranshipmentOccupancy,
        int EffectivePeriod,
        int InFlightMovementCount,
        IReadOnlyDictionary<string, NodePressureSnapshot> NodePressureById,
        IReadOnlyDictionary<string, EdgePressureSnapshot> EdgePressureById,
        IReadOnlyList<PressureEvent> PressureEvents);
    /// <summary>
    /// Represents the temporal node state snapshot component.
    /// </summary>

    public readonly record struct TemporalNodeStateSnapshot(double AvailableSupply, double DemandBacklog, double StoreInventory);
    /// <summary>
    /// Represents the temporal node traffic key component.
    /// </summary>

    public readonly record struct TemporalNodeTrafficKey(string NodeId, string TrafficType)
    {
        /// <summary>
        /// Gets or sets the comparer.
        /// </summary>
        public static IEqualityComparer<TemporalNodeTrafficKey> Comparer { get; } = new TemporalNodeTrafficKeyComparer();
        /// <summary>
        /// Represents the temporal node traffic key comparer component.
        /// </summary>

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
    /// <summary>
    /// Represents the temporal node traffic state component.
    /// </summary>

    public sealed class TemporalNodeTrafficState
    {
        private readonly List<TemporalQuantityBatch> availableSupplyBatches = [];
        private readonly List<TemporalQuantityBatch> storeInventoryBatches = [];
        /// <summary>
        /// Gets or sets the available supply.
        /// </summary>

        public double AvailableSupply
        {
            get
            {
                double sum = 0d;
                foreach (var batch in availableSupplyBatches)
                {
                    sum += batch.Quantity;
                }
                return sum;
            }
        }
        /// <summary>
        /// Gets or sets the available supply unit cost per unit.
        /// </summary>

        public double AvailableSupplyUnitCostPerUnit => GetWeightedUnitCost(availableSupplyBatches);
        /// <summary>
        /// Gets or sets the demand backlog.
        /// </summary>

        public double DemandBacklog { get; set; }
        /// <summary>
        /// Gets or sets the store inventory.
        /// </summary>

        public double StoreInventory
        {
            get
            {
                double sum = 0d;
                foreach (var batch in storeInventoryBatches)
                {
                    sum += batch.Quantity;
                }
                return sum;
            }
        }
        /// <summary>
        /// Gets or sets the store inventory unit cost per unit.
        /// </summary>

        public double StoreInventoryUnitCostPerUnit => GetWeightedUnitCost(storeInventoryBatches);
        /// <summary>
        /// Gets or sets the reserved store receipts.
        /// </summary>

        public double ReservedStoreReceipts { get; set; }
        /// <summary>
        /// Executes the blend available supply operation.
        /// </summary>

        public void BlendAvailableSupply(double quantity, double unitCost, int? remainingLifePeriods = null)
        {
            AddBatch(availableSupplyBatches, quantity, unitCost, remainingLifePeriods);
        }
        /// <summary>
        /// Executes the blend store inventory operation.
        /// </summary>

        public void BlendStoreInventory(double quantity, double unitCost, int? remainingLifePeriods = null)
        {
            AddBatch(storeInventoryBatches, quantity, unitCost, remainingLifePeriods);
        }
        /// <summary>
        /// Executes the consume available supply operation.
        /// </summary>

        public double ConsumeAvailableSupply(double quantity)
        {
            return ConsumeFromBatches(availableSupplyBatches, quantity);
        }
        /// <summary>
        /// Executes the consume store inventory operation.
        /// </summary>

        public double ConsumeStoreInventory(double quantity)
        {
            return ConsumeFromBatches(storeInventoryBatches, quantity);
        }
        /// <summary>
        /// Executes the advance perishability operation.
        /// </summary>

        public TemporalPerishabilityDelta AdvancePerishability()
        {
            var expiredAvailable = AdvancePerishability(availableSupplyBatches);
            var expiredStore = AdvancePerishability(storeInventoryBatches);
            ReservedStoreReceipts = Math.Max(0d, Math.Min(ReservedStoreReceipts, StoreInventory));
            return new TemporalPerishabilityDelta(expiredAvailable, expiredStore);
        }
        /// <summary>
        /// Executes the clone operation.
        /// </summary>

        public TemporalNodeTrafficState Clone()
        {
            var clone = new TemporalNodeTrafficState
            {
                DemandBacklog = DemandBacklog,
                ReservedStoreReceipts = ReservedStoreReceipts
            };

            // Bolt: Replaced LINQ .Select() and .AddRange() with pre-sized loops to prevent multiple enumerator and delegate allocations on cloning.
            clone.availableSupplyBatches.Capacity = availableSupplyBatches.Count;
            foreach (var batch in availableSupplyBatches)
            {
                clone.availableSupplyBatches.Add(batch.Clone());
            }

            clone.storeInventoryBatches.Capacity = storeInventoryBatches.Count;
            foreach (var batch in storeInventoryBatches)
            {
                clone.storeInventoryBatches.Add(batch.Clone());
            }
            return clone;
        }

        private static void AddBatch(
            ICollection<TemporalQuantityBatch> batches,
            double quantity,
            double unitCost,
            int? remainingLifePeriods)
        {
            if (quantity <= Epsilon)
            {
                return;
            }

            if (remainingLifePeriods.HasValue && remainingLifePeriods.Value <= 0)
            {
                return;
            }

            batches.Add(new TemporalQuantityBatch
            {
                Quantity = quantity,
                UnitCost = unitCost,
                RemainingLifePeriods = remainingLifePeriods
            });
        }

        private static double ConsumeFromBatches(List<TemporalQuantityBatch> batches, double quantity)
        {
            if (quantity <= Epsilon)
            {
                return GetWeightedUnitCost(batches);
            }

            // Bolt: Replaced O(N log N) LINQ multiple sorting and enumerator allocations with in-place List allocation and List.Sort().
            // Sequence acts as a stable sort tie-breaker as it's generated via Interlocked.Increment.
            var ordered = new List<TemporalQuantityBatch>(batches.Count);
            ordered.AddRange(batches);
            ordered.Sort((a, b) =>
            {
                int cmp = (a.RemainingLifePeriods ?? int.MaxValue).CompareTo(b.RemainingLifePeriods ?? int.MaxValue);
                if (cmp != 0) return cmp;
                return a.Sequence.CompareTo(b.Sequence);
            });

            var remaining = quantity;
            var totalCost = 0d;

            foreach (var batch in ordered)
            {
                if (remaining <= Epsilon)
                {
                    break;
                }

                var consumed = Math.Min(batch.Quantity, remaining);
                if (consumed <= Epsilon)
                {
                    continue;
                }

                batch.Quantity -= consumed;
                remaining -= consumed;
                totalCost += consumed * batch.UnitCost;
            }

            batches.RemoveAll(batch => batch.Quantity <= Epsilon);

            if (remaining > Epsilon)
            {
                throw new InvalidOperationException("Attempted to consume more traffic than was available.");
            }

            return quantity > Epsilon ? totalCost / quantity : 0d;
        }

        private static double AdvancePerishability(List<TemporalQuantityBatch> batches)
        {
            var expired = 0d;
            foreach (var batch in batches)
            {
                if (batch.RemainingLifePeriods.HasValue)
                {
                    batch.RemainingLifePeriods -= 1;
                    if (batch.RemainingLifePeriods.Value <= 0 && batch.Quantity > Epsilon)
                    {
                        expired += batch.Quantity;
                    }
                }
            }

            batches.RemoveAll(batch =>
                batch.Quantity <= Epsilon ||
                (batch.RemainingLifePeriods.HasValue && batch.RemainingLifePeriods.Value <= 0));
            return expired;
        }

        // Bolt: Changed parameter type from IEnumerable<T> to List<T> to avoid boxing the List<T> enumerator on the heap during the foreach loop.
        private static double GetWeightedUnitCost(List<TemporalQuantityBatch> batches)
        {
            var quantity = 0d;
            var cost = 0d;

            foreach (var batch in batches)
            {
                if (batch.Quantity <= Epsilon)
                {
                    continue;
                }

                quantity += batch.Quantity;
                cost += batch.Quantity * batch.UnitCost;
            }

            return quantity <= Epsilon ? 0d : cost / quantity;
        }
    }
    /// <summary>
    /// Represents the temporal quantity batch component.
    /// </summary>

    private sealed class TemporalQuantityBatch
    {
        private static long nextSequence;

        public TemporalQuantityBatch()
        {
            Sequence = Interlocked.Increment(ref nextSequence);
        }
        /// <summary>
        /// Gets or sets the sequence.
        /// </summary>

        public long Sequence { get; }
        /// <summary>
        /// Gets or sets the quantity.
        /// </summary>

        public double Quantity { get; set; }
        /// <summary>
        /// Gets or sets the unit cost.
        /// </summary>

        public double UnitCost { get; set; }
        /// <summary>
        /// Gets or sets the remaining life periods.
        /// </summary>

        public int? RemainingLifePeriods { get; set; }
        /// <summary>
        /// Executes the clone operation.
        /// </summary>

        public TemporalQuantityBatch Clone()
        {
            return new TemporalQuantityBatch
            {
                Quantity = Quantity,
                UnitCost = UnitCost,
                RemainingLifePeriods = RemainingLifePeriods
            };
        }
    }
    /// <summary>
    /// Represents the temporal in flight movement component.
    /// </summary>

    public sealed class TemporalInFlightMovement
    {
        /// <summary>
        /// Gets or sets the traffic type.
        /// </summary>
        public string TrafficType { get; init; } = string.Empty;
        /// <summary>
        /// Gets or sets the quantity.
        /// </summary>

        public double Quantity { get; init; }
        /// <summary>
        /// Gets or sets the source unit cost per unit.
        /// </summary>

        public double SourceUnitCostPerUnit { get; init; }
        /// <summary>
        /// Gets or sets the landed unit cost per unit.
        /// </summary>

        public double LandedUnitCostPerUnit { get; init; }
        /// <summary>
        /// Gets the collection of path node ids associated with this entity.
        /// </summary>

        public List<string> PathNodeIds { get; init; } = [];
        /// <summary>
        /// Gets the collection of path node names associated with this entity.
        /// </summary>

        public List<string> PathNodeNames { get; init; } = [];
        /// <summary>
        /// Gets the collection of path edge ids associated with this entity.
        /// </summary>

        public List<string> PathEdgeIds { get; init; } = [];
        /// <summary>
        /// Gets or sets the current edge index.
        /// </summary>

        public int CurrentEdgeIndex { get; set; }
        /// <summary>
        /// Gets or sets the remaining periods on current edge.
        /// </summary>

        public int RemainingPeriodsOnCurrentEdge { get; set; }
        /// <summary>
        /// Gets a value indicating whether is waiting between edges is enabled or active.
        /// </summary>

        public bool IsWaitingBetweenEdges { get; set; }
        /// <summary>
        /// Gets or sets the remaining shelf life periods.
        /// </summary>

        public int? RemainingShelfLifePeriods { get; set; }
        /// <summary>
        /// Executes the clone operation.
        /// </summary>

        public TemporalInFlightMovement Clone()
        {
            return new TemporalInFlightMovement
            {
                TrafficType = TrafficType,
                Quantity = Quantity,
                SourceUnitCostPerUnit = SourceUnitCostPerUnit,
                LandedUnitCostPerUnit = LandedUnitCostPerUnit,
                PathNodeIds = PathNodeIds.ToList(),
                PathNodeNames = PathNodeNames.ToList(),
                PathEdgeIds = PathEdgeIds.ToList(),
                CurrentEdgeIndex = CurrentEdgeIndex,
                RemainingPeriodsOnCurrentEdge = RemainingPeriodsOnCurrentEdge,
                IsWaitingBetweenEdges = IsWaitingBetweenEdges,
                RemainingShelfLifePeriods = RemainingShelfLifePeriods
            };
        }
    }
    /// <summary>
    /// Represents the edge flow visual summary component.
    /// </summary>

    public readonly record struct EdgeFlowVisualSummary(double ForwardQuantity, double ReverseQuantity)
    {
        /// <summary>
        /// Gets or sets the empty.
        /// </summary>
        public static EdgeFlowVisualSummary Empty => new(0d, 0d);
    }
    /// <summary>
    /// Represents the node flow visual summary component.
    /// </summary>

    public readonly record struct NodeFlowVisualSummary(double OutboundQuantity, double InboundQuantity)
    {
        /// <summary>
        /// Gets or sets the empty.
        /// </summary>
        public static NodeFlowVisualSummary Empty => new(0d, 0d);
    }
    /// <summary>
    /// Specifies the pressure cause kind.
    /// </summary>

    public enum PressureCauseKind
    {
        DemandBacklog,
        InputShortage,
        StoreCapacitySaturation,
        EdgeCapacitySaturation,
        TranshipmentCapacitySaturation,
        RouteUnavailable,
        PerishedInNodeInventory,
        PerishedInTransit,
        TimelineShock
    }
    /// <summary>
    /// Represents the pressure event component.
    /// </summary>

    public readonly record struct PressureEvent(
        int Period,
        string EntityId,
        bool IsEdge,
        string TrafficType,
        PressureCauseKind Cause,
        double Quantity,
        double WeightedImpact,
        string Detail);
    /// <summary>
    /// Represents the node pressure snapshot component.
    /// </summary>

    public readonly record struct NodePressureSnapshot(
        double Score,
        double BacklogQuantity,
        double ExpiredQuantity,
        IReadOnlyDictionary<PressureCauseKind, double> CauseWeights,
        string TopCause);
    /// <summary>
    /// Represents the edge pressure snapshot component.
    /// </summary>

    public readonly record struct EdgePressureSnapshot(
        double Score,
        double BlockedQuantity,
        double ExpiredInTransitQuantity,
        double Utilization,
        IReadOnlyDictionary<PressureCauseKind, double> CauseWeights,
        string TopCause);
    /// <summary>
    /// Represents the temporal perishability delta component.
    /// </summary>

    public readonly record struct TemporalPerishabilityDelta(double ExpiredAvailableSupply, double ExpiredStoreInventory);
    /// <summary>
    /// Represents the pressure accumulator component.
    /// </summary>

    private sealed class PressureAccumulator
    {
        private readonly Dictionary<PressureCauseKind, double> weightedByCause = [];
        /// <summary>
        /// Executes the add operation.
        /// </summary>

        public void Add(PressureCauseKind cause, double quantity, double weight)
        {
            if (quantity <= Epsilon || weight <= 0d)
            {
                return;
            }

            weightedByCause[cause] = weightedByCause.GetValueOrDefault(cause, 0d) + (quantity * weight);
        }
        /// <summary>
        /// Executes the to node snapshot operation.
        /// </summary>

        public NodePressureSnapshot ToNodeSnapshot()
        {
            var score = weightedByCause.Sum(pair => pair.Value);
            var backlogQuantity = weightedByCause.GetValueOrDefault(PressureCauseKind.DemandBacklog, 0d);
            var expiredQuantity =
                weightedByCause.GetValueOrDefault(PressureCauseKind.PerishedInNodeInventory, 0d) +
                weightedByCause.GetValueOrDefault(PressureCauseKind.PerishedInTransit, 0d);
            var topCause = weightedByCause.Count == 0
                ? string.Empty
                : weightedByCause.MaxBy(pair => pair.Value).Key.ToString();
            return new NodePressureSnapshot(score, backlogQuantity, expiredQuantity, weightedByCause, topCause);
        }
        /// <summary>
        /// Executes the to edge snapshot operation.
        /// </summary>

        public EdgePressureSnapshot ToEdgeSnapshot()
        {
            var score = weightedByCause.Sum(pair => pair.Value);
            var blockedQuantity = weightedByCause.GetValueOrDefault(PressureCauseKind.EdgeCapacitySaturation, 0d);
            var expiredInTransitQuantity = weightedByCause.GetValueOrDefault(PressureCauseKind.PerishedInTransit, 0d);
            var topCause = weightedByCause.Count == 0
                ? string.Empty
                : weightedByCause.MaxBy(pair => pair.Value).Key.ToString();
            return new EdgePressureSnapshot(score, blockedQuantity, expiredInTransitQuantity, Utilization: 0d, weightedByCause, topCause);
        }
    }
    /// <summary>
    /// Represents the graph arc component.
    /// </summary>

    private sealed record GraphArc(string EdgeId, string FromNodeId, string ToNodeId, double Time, double Cost);
    /// <summary>
    /// Represents the previous step component.
    /// </summary>

    private sealed record PreviousStep(string PreviousNodeId, GraphArc Arc);
    /// <summary>
    /// Represents the available resource capacity component.
    /// </summary>

    private sealed record AvailableResourceCapacity(
        IReadOnlyDictionary<string, double> EdgeCapacityById,
        IReadOnlyDictionary<string, double> TranshipmentCapacityByNodeId);
    /// <summary>
    /// Represents the production result component.
    /// </summary>

    private readonly record struct ProductionResult(double OutputQuantity, double InheritedUnitCost);
    /// <summary>
    /// Represents the route candidate component.
    /// </summary>

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
    /// <summary>
    /// Represents the temporal traffic context component.
    /// </summary>

    private sealed record TemporalTrafficContext(
        string TrafficType,
        RoutingPreference RoutingPreference,
        AllocationMode AllocationMode,
        RouteChoiceModel RouteChoiceModel,
        FlowSplitPolicy FlowSplitPolicy,
        RouteChoiceSettings RouteChoiceSettings,
        double CapacityBidPerUnit,
        int Seed,
        IReadOnlyDictionary<string, NodeModel> NodesById,
        IReadOnlyDictionary<string, NodeTrafficProfile?> ProfilesByNodeId,
        IReadOnlySet<string> MeetingDemandEligibleNodeIds,
        IDictionary<string, double> Supply,
        IDictionary<string, double> SupplyUnitCosts,
        IDictionary<string, double> Demand,
        IDictionary<string, double> CommittedSupply,
        IDictionary<string, double> CommittedDemand,
        ISet<string> StoreSupplyNodes,
        ISet<string> StoreDemandNodes,
        ISet<string> RecipeInputDemandNodes,
        List<RouteAllocation> Allocations);
}

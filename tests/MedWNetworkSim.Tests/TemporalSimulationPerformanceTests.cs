using System.Diagnostics;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using Xunit;
using Xunit.Abstractions;

namespace MedWNetworkSim.Tests;

public sealed class TemporalSimulationPerformanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(6, 2, 3, 25)]
    [InlineData(16, 3, 6, 16)]
    [InlineData(32, 4, 9, 8)]
    public void Advance_OptimizedPath_MatchesLegacyLikeResults_AndImprovesHotPath(
        int nodeCount,
        int trafficTypeCount,
        int seededMovements,
        int iterations)
    {
        var network = BuildSyntheticNetwork(nodeCount, trafficTypeCount);
        var baselineOptions = new SimulationRunOptions
        {
            CopyStateBeforeAdvance = true,
            EnableInvariantValidation = true
        };
        var optimizedOptions = new SimulationRunOptions();
        var baselineSeedState = BuildSeededState(network, seededMovements);
        var optimizedSeedState = CloneState(baselineSeedState);

        var baselineEngine = new TemporalNetworkSimulationEngine();
        var optimizedEngine = new TemporalNetworkSimulationEngine();
        var baselineResult = baselineEngine.Advance(network, baselineSeedState, baselineOptions);
        var optimizedResult = optimizedEngine.Advance(network, optimizedSeedState, optimizedOptions);

        AssertEquivalent(baselineResult, optimizedResult);

        var baselineMeasurement = MeasureAdvance(network, baselineSeedState, baselineOptions, iterations);
        var optimizedMeasurement = MeasureAdvance(network, baselineSeedState, optimizedOptions, iterations);

        output.WriteLine(
            $"nodes={nodeCount}, traffic={trafficTypeCount}, inFlight={seededMovements}, iterations={iterations}, " +
            $"legacyLike={baselineMeasurement.ElapsedMilliseconds}ms/{baselineMeasurement.AllocatedBytes}B, " +
            $"optimized={optimizedMeasurement.ElapsedMilliseconds}ms/{optimizedMeasurement.AllocatedBytes}B");

        Assert.True(
            optimizedMeasurement.AllocatedBytes <= baselineMeasurement.AllocatedBytes ||
            optimizedMeasurement.ElapsedMilliseconds <= baselineMeasurement.ElapsedMilliseconds,
            "The default one-tick path should improve either allocations or elapsed time versus the legacy-like clone+validation path.");
    }


    [Fact]
    public void Allocate_CompiledIndexedRouting_ReducesHotPathCostVersusStringFallback()
    {
        var network = BuildSyntheticNetwork(nodeCount: 28, trafficTypeCount: 3);
        var compiledContext = CompiledNetworkSimulationContext.Create(network, network, revision: 20260507, effectivePeriod: 0, activeTimelineEventSignature: 0);
        var iterations = 5;

        var stringMeasurement = MeasureAllocation(network, compiledContext: null, iterations);
        var indexedMeasurement = MeasureAllocation(network, compiledContext, iterations);

        output.WriteLine(
            $"allocation indexed-routing benchmark: stringFallback={stringMeasurement.ElapsedMilliseconds}ms/{stringMeasurement.AllocatedBytes}B, " +
            $"indexed={indexedMeasurement.ElapsedMilliseconds}ms/{indexedMeasurement.AllocatedBytes}B");

        Assert.True(
            indexedMeasurement.AllocatedBytes <= stringMeasurement.AllocatedBytes ||
            indexedMeasurement.ElapsedMilliseconds <= stringMeasurement.ElapsedMilliseconds,
            "The compiled indexed allocator should reduce either allocations or elapsed time versus the string adjacency fallback.");
    }

    [Fact]
    public void Advance_CopyStateBeforeAdvanceFalse_PreservesSnapshotIsolation()
    {
        var network = BuildSyntheticNetwork(nodeCount: 8, trafficTypeCount: 2);
        var engine = new TemporalNetworkSimulationEngine();
        var state = BuildSeededState(network, seededMovements: 2);

        var first = engine.Advance(network, state, new SimulationRunOptions());
        var snapshot = first.NodeStates.ToDictionary(pair => pair.Key, pair => pair.Value, TemporalNetworkSimulationEngine.TemporalNodeTrafficKey.Comparer);

        var second = engine.Advance(network, state, new SimulationRunOptions());

        Assert.NotEmpty(first.NodeStates);
        Assert.NotEmpty(second.NodeStates);
        Assert.Equal(snapshot, first.NodeStates);
    }


    private static Measurement MeasureAllocation(
        NetworkModel network,
        CompiledNetworkSimulationContext? compiledContext,
        int iterations)
    {
        RunAllocation(network, compiledContext);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            RunAllocation(network, compiledContext);
        }

        stopwatch.Stop();
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        return new Measurement(stopwatch.ElapsedMilliseconds, allocatedAfter - allocatedBefore);
    }

    private static void RunAllocation(NetworkModel network, CompiledNetworkSimulationContext? compiledContext)
    {
        var contexts = MixedRoutingAllocator.BuildStaticContexts(network);
        var remainingCapacityByEdgeId = network.Edges.ToDictionary(edge => edge.Id, edge => edge.Capacity ?? double.PositiveInfinity, StringComparer.OrdinalIgnoreCase);
        var remainingTranshipmentCapacityByNodeId = network.Nodes.ToDictionary(node => node.Id, node => node.TranshipmentCapacity ?? double.PositiveInfinity, StringComparer.OrdinalIgnoreCase);
        MixedRoutingAllocator.Allocate(
            network,
            contexts,
            remainingCapacityByEdgeId,
            remainingTranshipmentCapacityByNodeId,
            compiledContext: compiledContext);
    }

    private static Measurement MeasureAdvance(
        NetworkModel network,
        TemporalNetworkSimulationEngine.TemporalSimulationState seedState,
        SimulationRunOptions options,
        int iterations)
    {
        var engine = new TemporalNetworkSimulationEngine();
        engine.Advance(network, CloneState(seedState), options);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var state = CloneState(seedState);
            engine.Advance(network, state, options);
        }

        stopwatch.Stop();
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        return new Measurement(stopwatch.ElapsedMilliseconds, allocatedAfter - allocatedBefore);
    }

    private static TemporalNetworkSimulationEngine.TemporalSimulationState BuildSeededState(NetworkModel network, int seededMovements)
    {
        var engine = new TemporalNetworkSimulationEngine();
        var state = engine.Initialize(network);
        var definitionsByTraffic = network.TrafficTypes.ToDictionary(definition => definition.Name, definition => definition, StringComparer.OrdinalIgnoreCase);

        for (var trafficIndex = 0; trafficIndex < network.TrafficTypes.Count; trafficIndex++)
        {
            var trafficType = network.TrafficTypes[trafficIndex].Name;
            for (var nodeIndex = 0; nodeIndex < network.Nodes.Count; nodeIndex++)
            {
                var node = network.Nodes[nodeIndex];
                var profile = node.TrafficProfiles.FirstOrDefault(candidate => string.Equals(candidate.TrafficType, trafficType, StringComparison.OrdinalIgnoreCase));
                if (profile?.Production > 0d)
                {
                    var nodeState = state.GetOrCreateNodeTrafficState(node.Id, trafficType);
                    nodeState.BlendAvailableSupply(profile.Production * 2d, Math.Max(0d, profile.ProductionCostPerUnit ?? 1d), definitionsByTraffic[trafficType].PerishabilityPeriods);
                }

                if (profile?.Consumption > 0d)
                {
                    state.GetOrCreateNodeTrafficState(node.Id, trafficType).DemandBacklog += profile.Consumption * 2d;
                }
            }
        }

        for (var index = 0; index < seededMovements && index < network.Edges.Count; index++)
        {
            var edge = network.Edges[index];
            var trafficType = network.TrafficTypes[index % network.TrafficTypes.Count].Name;
            state.InFlightMovements.Add(new TemporalNetworkSimulationEngine.TemporalInFlightMovement
            {
                TrafficType = trafficType,
                Quantity = 1d + (index % 3),
                PathNodeIds = [edge.FromNodeId, edge.ToNodeId],
                PathNodeNames = [edge.FromNodeId, edge.ToNodeId],
                PathEdgeIds = [edge.Id],
                CurrentEdgeIndex = 0,
                RemainingPeriodsOnCurrentEdge = Math.Max(1, (int)Math.Ceiling(edge.Time)),
                RemainingShelfLifePeriods = 3
            });
            state.OccupiedEdgeCapacity[edge.Id] = state.OccupiedEdgeCapacity.GetValueOrDefault(edge.Id) + (1d + (index % 3));
            state.OccupiedEdgeTrafficCapacity[new EdgeTrafficResourceKey(edge.Id, trafficType)] =
                state.OccupiedEdgeTrafficCapacity.GetValueOrDefault(new EdgeTrafficResourceKey(edge.Id, trafficType)) + (1d + (index % 3));
        }

        return state;
    }

    private static TemporalNetworkSimulationEngine.TemporalSimulationState CloneState(
        TemporalNetworkSimulationEngine.TemporalSimulationState source)
    {
        var clone = new TemporalNetworkSimulationEngine.TemporalSimulationState
        {
            CurrentPeriod = source.CurrentPeriod
        };

        foreach (var pair in source.NodeStates)
        {
            clone.NodeStates[pair.Key] = pair.Value.Clone();
        }

        foreach (var movement in source.InFlightMovements)
        {
            clone.InFlightMovements.Add(movement.Clone());
        }

        foreach (var pair in source.OccupiedEdgeCapacity)
        {
            clone.OccupiedEdgeCapacity[pair.Key] = pair.Value;
        }

        foreach (var pair in source.OccupiedEdgeTrafficCapacity)
        {
            clone.OccupiedEdgeTrafficCapacity[pair.Key] = pair.Value;
        }

        foreach (var pair in source.OccupiedTranshipmentCapacity)
        {
            clone.OccupiedTranshipmentCapacity[pair.Key] = pair.Value;
        }

        return clone;
    }

    private static void AssertEquivalent(
        TemporalNetworkSimulationEngine.TemporalSimulationStepResult expected,
        TemporalNetworkSimulationEngine.TemporalSimulationStepResult actual)
    {
        Assert.Equal(expected.Period, actual.Period);
        Assert.Equal(expected.EffectivePeriod, actual.EffectivePeriod);
        Assert.Equal(expected.InFlightMovementCount, actual.InFlightMovementCount);
        Assert.Equal(expected.Allocations.Count, actual.Allocations.Count);
        Assert.Equal(expected.PressureEvents.Count, actual.PressureEvents.Count);
        Assert.Equal(expected.NodeStates, actual.NodeStates);
        Assert.Equal(expected.EdgeOccupancy, actual.EdgeOccupancy);
        Assert.Equal(expected.TranshipmentOccupancy, actual.TranshipmentOccupancy);

        for (var index = 0; index < expected.Allocations.Count; index++)
        {
            var left = expected.Allocations[index];
            var right = actual.Allocations[index];
            Assert.Equal(left.TrafficType, right.TrafficType);
            Assert.Equal(left.ProducerNodeId, right.ProducerNodeId);
            Assert.Equal(left.ConsumerNodeId, right.ConsumerNodeId);
            Assert.Equal(left.Quantity, right.Quantity, precision: 6);
            Assert.Equal(left.PathNodeIds, right.PathNodeIds);
            Assert.Equal(left.PathEdgeIds, right.PathEdgeIds);
            Assert.Equal(left.PathNodeNames, right.PathNodeNames);
        }
    }

    private static NetworkModel BuildSyntheticNetwork(int nodeCount, int trafficTypeCount)
    {
        var layerId = Guid.NewGuid();
        var network = new NetworkModel
        {
            Name = $"Synthetic-{nodeCount}-{trafficTypeCount}",
            SimulationSeed = 12345,
            TimelineLoopLength = 6,
            Layers = [new NetworkLayerModel { Id = layerId, Name = "Physical", Type = NetworkLayerType.Physical, Order = 0 }]
        };

        for (var trafficIndex = 0; trafficIndex < trafficTypeCount; trafficIndex++)
        {
            network.TrafficTypes.Add(new TrafficTypeDefinition
            {
                Name = $"Traffic-{trafficIndex}",
                RoutingPreference = trafficIndex % 2 == 0 ? RoutingPreference.TotalCost : RoutingPreference.Speed,
                AllocationMode = AllocationMode.GreedyBestRoute,
                RouteChoiceModel = trafficIndex % 2 == 0 ? RouteChoiceModel.StochasticUserResponsive : RouteChoiceModel.SystemOptimal,
                FlowSplitPolicy = FlowSplitPolicy.MultiPath,
                PerishabilityPeriods = 4 + trafficIndex,
                CapacityBidPerUnit = 0.25d * (trafficIndex + 1),
                RouteChoiceSettings = new RouteChoiceSettings
                {
                    MaxCandidateRoutes = 3,
                    IterationCount = 3,
                    Priority = 1d + trafficIndex,
                    CongestionSensitivity = 0.6d,
                    Stickiness = 0.2d
                }
            });
        }

        for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            var node = new NodeModel
            {
                Id = $"N{nodeIndex}",
                Name = $"Node {nodeIndex}",
                LayerId = layerId,
                X = nodeIndex,
                Y = nodeIndex % 5,
                TranshipmentCapacity = 12d + (nodeIndex % 4)
            };

            for (var trafficIndex = 0; trafficIndex < trafficTypeCount; trafficIndex++)
            {
                node.TrafficProfiles.Add(new NodeTrafficProfile
                {
                    TrafficType = network.TrafficTypes[trafficIndex].Name,
                    Production = nodeIndex % 7 == trafficIndex % 3 ? 4d + trafficIndex : 0d,
                    Consumption = (nodeIndex + trafficIndex) % 6 == 0 ? 3d + trafficIndex : 0d,
                    CanTransship = nodeIndex % 5 != 0 || nodeIndex == 0 || nodeIndex == nodeCount - 1,
                    ProductionCostPerUnit = 1d + trafficIndex,
                    ConsumerPremiumPerUnit = 0.2d * trafficIndex,
                    IsStore = nodeIndex % 9 == 0,
                    StoreCapacity = nodeIndex % 9 == 0 ? 25d : null
                });
            }

            network.Nodes.Add(node);
        }

        var edgeCounter = 0;
        for (var nodeIndex = 0; nodeIndex < nodeCount - 1; nodeIndex++)
        {
            AddEdge(network, layerId, ref edgeCounter, $"N{nodeIndex}", $"N{nodeIndex + 1}", 1d + (nodeIndex % 3), 0.75d + (nodeIndex % 4), 6d + (nodeIndex % 5), isBidirectional: true);
            if (nodeIndex + 2 < nodeCount)
            {
                AddEdge(network, layerId, ref edgeCounter, $"N{nodeIndex}", $"N{nodeIndex + 2}", 1.5d + (nodeIndex % 2), 1.25d + (nodeIndex % 3), 5d + (nodeIndex % 4), isBidirectional: false);
            }
        }

        network.TimelineEvents.Add(new TimelineEventModel
        {
            Id = "storm",
            Name = "Storm",
            StartPeriod = 1,
            EndPeriod = 2,
            Effects =
            [
                new TimelineEventEffectModel
                {
                    EffectType = TimelineEventEffectType.RouteCostMultiplier,
                    EdgeId = network.Edges[Math.Min(2, network.Edges.Count - 1)].Id,
                    Multiplier = 2d
                },
                new TimelineEventEffectModel
                {
                    EffectType = TimelineEventEffectType.ConsumptionMultiplier,
                    NodeId = network.Nodes[Math.Min(3, network.Nodes.Count - 1)].Id,
                    TrafficType = network.TrafficTypes[0].Name,
                    Multiplier = 1.5d
                }
            ]
        });

        return network;
    }

    private static void AddEdge(
        NetworkModel network,
        Guid layerId,
        ref int edgeCounter,
        string fromNodeId,
        string toNodeId,
        double time,
        double cost,
        double capacity,
        bool isBidirectional)
    {
        network.Edges.Add(new EdgeModel
        {
            Id = $"E{edgeCounter++}",
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            LayerId = layerId,
            Time = time,
            Cost = cost,
            Capacity = capacity,
            IsBidirectional = isBidirectional
        });
    }

    private readonly record struct Measurement(long ElapsedMilliseconds, long AllocatedBytes);
}

using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class MixedRoutingIndexedEquivalenceTests
{
    [Theory]
    [InlineData(RouteChoiceModel.SystemOptimal, FlowSplitPolicy.SinglePath)]
    [InlineData(RouteChoiceModel.StochasticUserResponsive, FlowSplitPolicy.MultiPath)]
    public void Allocate_CompiledIndexedPath_MatchesStringFallback_ForConstrainedMixedRouting(
        RouteChoiceModel routeChoiceModel,
        FlowSplitPolicy flowSplitPolicy)
    {
        var network = BuildConstrainedRoutingNetwork(routeChoiceModel, flowSplitPolicy);
        var stringContexts = MixedRoutingAllocator.BuildStaticContexts(network, applyLocalAllocations: true);
        var indexedContexts = MixedRoutingAllocator.BuildStaticContexts(network, applyLocalAllocations: true);
        var stringEdgeCapacity = BuildEdgeCapacity(network);
        var indexedEdgeCapacity = BuildEdgeCapacity(network);
        var stringNodeCapacity = BuildNodeCapacity(network);
        var indexedNodeCapacity = BuildNodeCapacity(network);
        var compiledContext = CompiledNetworkSimulationContext.Create(network, network, revision: 112233, effectivePeriod: 0, activeTimelineEventSignature: 0);

        var stringAllocations = MixedRoutingAllocator.Allocate(network, stringContexts, stringEdgeCapacity, stringNodeCapacity, compiledContext: null);
        var indexedAllocations = MixedRoutingAllocator.Allocate(network, indexedContexts, indexedEdgeCapacity, indexedNodeCapacity, compiledContext: compiledContext);

        AssertAllocationsEquivalent(stringAllocations, indexedAllocations);
        AssertDictionariesEquivalent(stringEdgeCapacity, indexedEdgeCapacity);
        AssertDictionariesEquivalent(stringNodeCapacity, indexedNodeCapacity);
    }

    [Fact]
    public void Allocate_CompiledIndexedPath_RespectsTimelineOverlayAndTrafficPermissions()
    {
        var network = BuildConstrainedRoutingNetwork(RouteChoiceModel.SystemOptimal, FlowSplitPolicy.SinglePath);
        network.TimelineEvents.Add(new TimelineEventModel
        {
            Name = "make-detour-cheap",
            StartPeriod = 2,
            EndPeriod = 2,
            Effects =
            [
                new TimelineEventEffectModel { EffectType = TimelineEventEffectType.RouteCostMultiplier, EdgeId = "producer-detour", Multiplier = 0.1d },
                new TimelineEventEffectModel { EffectType = TimelineEventEffectType.RouteCostMultiplier, EdgeId = "detour-consumer", Multiplier = 0.1d }
            ]
        });
        var effectiveNetwork = BuildConstrainedRoutingNetwork(RouteChoiceModel.SystemOptimal, FlowSplitPolicy.SinglePath);
        effectiveNetwork.Edges.Single(edge => edge.Id == "producer-detour").Cost *= 0.1d;
        effectiveNetwork.Edges.Single(edge => edge.Id == "detour-consumer").Cost *= 0.1d;
        effectiveNetwork.TimelineEvents = network.TimelineEvents;
        var compiledContext = CompiledNetworkSimulationContext.Create(
            network,
            effectiveNetwork,
            revision: 445566,
            effectivePeriod: 2,
            activeTimelineEventSignature: 778899);
        var stringContexts = MixedRoutingAllocator.BuildStaticContexts(effectiveNetwork, applyLocalAllocations: true);
        var indexedContexts = MixedRoutingAllocator.BuildStaticContexts(effectiveNetwork, applyLocalAllocations: true);
        var stringEdgeCapacity = BuildEdgeCapacity(effectiveNetwork);
        var indexedEdgeCapacity = BuildEdgeCapacity(effectiveNetwork);
        var stringNodeCapacity = BuildNodeCapacity(effectiveNetwork);
        var indexedNodeCapacity = BuildNodeCapacity(effectiveNetwork);

        var stringAllocations = MixedRoutingAllocator.Allocate(effectiveNetwork, stringContexts, stringEdgeCapacity, stringNodeCapacity, compiledContext: null);
        var indexedAllocations = MixedRoutingAllocator.Allocate(effectiveNetwork, indexedContexts, indexedEdgeCapacity, indexedNodeCapacity, compiledContext: compiledContext);

        AssertAllocationsEquivalent(stringAllocations, indexedAllocations);
        Assert.All(indexedAllocations, allocation => Assert.DoesNotContain("blocked", allocation.PathEdgeIds));
    }

    private static NetworkModel BuildConstrainedRoutingNetwork(RouteChoiceModel routeChoiceModel, FlowSplitPolicy flowSplitPolicy)
    {
        return new NetworkModel
        {
            Name = "indexed equivalence",
            SimulationSeed = 17,
            TrafficTypes =
            [
                new TrafficTypeDefinition
                {
                    Name = "Food",
                    RoutingPreference = RoutingPreference.TotalCost,
                    RouteChoiceModel = routeChoiceModel,
                    FlowSplitPolicy = flowSplitPolicy,
                    RouteChoiceSettings = new RouteChoiceSettings
                    {
                        MaxCandidateRoutes = 4,
                        IterationCount = 2,
                        InformationAccuracy = 1d,
                        RouteDiversity = 0.2d,
                        CongestionSensitivity = 0d,
                        Priority = 2d
                    }
                }
            ],
            Nodes =
            [
                Node("producer", "Producer", production: 12d, consumption: 0d, canTransship: false),
                Node("hub", "Hub", production: 0d, consumption: 0d, canTransship: true, transhipmentCapacity: 8d),
                Node("detour", "Detour", production: 0d, consumption: 0d, canTransship: true, transhipmentCapacity: 10d),
                Node("consumer", "Consumer", production: 0d, consumption: 12d, canTransship: false)
            ],
            Edges =
            [
                Edge("producer-hub", "producer", "hub", time: 1d, cost: 1d, capacity: 8d, bidirectional: false),
                Edge("hub-consumer", "hub", "consumer", time: 1d, cost: 1d, capacity: 8d, bidirectional: false),
                Edge("consumer-hub-return", "consumer", "hub", time: 1d, cost: 1d, capacity: 8d, bidirectional: false),
                Edge("producer-detour", "producer", "detour", time: 3d, cost: 3d, capacity: 10d, bidirectional: true),
                Edge("detour-consumer", "detour", "consumer", time: 3d, cost: 3d, capacity: 10d, bidirectional: true),
                Edge("blocked", "producer", "consumer", time: 0.1d, cost: 0.1d, capacity: 20d, bidirectional: false, blockedForFood: true)
            ]
        };
    }

    private static NodeModel Node(string id, string name, double production, double consumption, bool canTransship, double? transhipmentCapacity = null)
    {
        return new NodeModel
        {
            Id = id,
            Name = name,
            TranshipmentCapacity = transhipmentCapacity,
            TrafficProfiles =
            [
                new NodeTrafficProfile
                {
                    TrafficType = "Food",
                    Production = production,
                    Consumption = consumption,
                    CanTransship = canTransship
                }
            ]
        };
    }

    private static EdgeModel Edge(
        string id,
        string from,
        string to,
        double time,
        double cost,
        double capacity,
        bool bidirectional,
        bool blockedForFood = false)
    {
        var edge = new EdgeModel
        {
            Id = id,
            FromNodeId = from,
            ToNodeId = to,
            Time = time,
            Cost = cost,
            Capacity = capacity,
            IsBidirectional = bidirectional
        };
        if (blockedForFood)
        {
            edge.TrafficPermissions.Add(new EdgeTrafficPermissionRule
            {
                TrafficType = "Food",
                Mode = EdgeTrafficPermissionMode.Blocked,
                IsActive = true
            });
        }

        return edge;
    }

    private static Dictionary<string, double> BuildEdgeCapacity(NetworkModel network)
    {
        return network.Edges.ToDictionary(edge => edge.Id, edge => edge.Capacity ?? double.PositiveInfinity, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, double> BuildNodeCapacity(NetworkModel network)
    {
        return network.Nodes.ToDictionary(node => node.Id, node => node.TranshipmentCapacity ?? double.PositiveInfinity, StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertAllocationsEquivalent(IReadOnlyList<RouteAllocation> expected, IReadOnlyList<RouteAllocation> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].TrafficType, actual[index].TrafficType);
            Assert.Equal(expected[index].ProducerNodeId, actual[index].ProducerNodeId);
            Assert.Equal(expected[index].ConsumerNodeId, actual[index].ConsumerNodeId);
            Assert.Equal(expected[index].PathNodeIds, actual[index].PathNodeIds);
            Assert.Equal(expected[index].PathEdgeIds, actual[index].PathEdgeIds);
            Assert.Equal(expected[index].Quantity, actual[index].Quantity, precision: 6);
            Assert.Equal(expected[index].TotalCost, actual[index].TotalCost, precision: 6);
            Assert.Equal(expected[index].TotalTime, actual[index].TotalTime, precision: 6);
        }
    }

    private static void AssertDictionariesEquivalent(IReadOnlyDictionary<string, double> expected, IReadOnlyDictionary<string, double> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected)
        {
            Assert.True(actual.TryGetValue(pair.Key, out var actualValue), $"Missing key {pair.Key}");
            Assert.Equal(pair.Value, actualValue, precision: 6);
        }
    }
}

using MedWNetworkSim.App.Agents;
using MedWNetworkSim.App.Models;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class BoundedMarketSemanticsTests
{
    [Fact]
    public void BuyTraffic_DoesNotIncreaseConsumptionWhenAppliedDirectly()
    {
        var network = BuildSupplyDemandNetwork(production: 0d, consumption: 5d, capacity: 100d);
        var buyer = new SimulationActorState
        {
            Id = "buyer",
            Kind = SimulationActorKind.Firm,
            ControlledNodeIds = ["consumer"],
            Cash = 100d,
            Capability = SimulationActorCapabilityCatalog.ForKind("buyer", SimulationActorKind.Firm)
        };

        var (updated, outcomes) = new SimulationActorActionApplier().Apply(
            network,
            [new SimulationActorAction
            {
                Id = "buy",
                ActorId = "buyer",
                Kind = SimulationActorActionKind.BuyTraffic,
                TargetNodeId = "consumer",
                TrafficType = "Food",
                DeltaValue = 100d
            }],
            new Dictionary<string, SimulationActorState>(StringComparer.OrdinalIgnoreCase) { [buyer.Id] = buyer },
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));

        Assert.False(outcomes.Single().Applied);
        Assert.Contains("does not mutate consumption", outcomes.Single().Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5d, updated.Nodes.Single(node => node.Id == "consumer").TrafficProfiles.Single().Consumption);
        Assert.Equal(100d, buyer.Cash);
    }

    [Fact]
    public void Logistics_DoesNotExpandBeyondSupplyLimitedNeed()
    {
        var network = BuildSupplyDemandNetwork(production: 50d, consumption: 5000d, capacity: 100d);
        var planner = BuildPlanner(cash: 1000d);

        var decision = new SimulationActorCoordinator().PreviewActorActions(network, [planner]).Single();

        Assert.Contains(decision.Actions, action => action.Kind == SimulationActorActionKind.NoOp && action.Reason.Contains("supply-limited", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(decision.Actions, action => action.Kind == SimulationActorActionKind.AdjustEdgeCapacity);
    }

    [Fact]
    public void Logistics_CapacityDeltaIsRightSized()
    {
        var network = BuildSupplyDemandNetwork(production: 50d, consumption: 5000d, capacity: 20d);
        var planner = BuildPlanner(cash: 1000d);

        var decision = new SimulationActorCoordinator().PreviewActorActions(network, [planner]).Single();
        var action = Assert.Single(decision.Actions, action => action.Kind == SimulationActorActionKind.AdjustEdgeCapacity);

        Assert.InRange(action.DeltaValue, 34.999d, 35.001d);
        Assert.InRange(action.Cost, 34.999d, 35.001d);
        Assert.Contains("available supply=50", action.Reason);
    }

    [Fact]
    public void Logistics_CapacityCostIsProportionalAndCashBounded()
    {
        var network = BuildSupplyDemandNetwork(production: 200d, consumption: 200d, capacity: 0d);
        var actor = BuildPlanner(cash: 50d);
        var action = new SimulationActorAction
        {
            Id = "cap",
            ActorId = actor.Id,
            Kind = SimulationActorActionKind.AdjustEdgeCapacity,
            TargetEdgeId = "edge",
            TrafficType = "Food",
            DeltaValue = 100d,
            Cost = 200d
        };

        var (updated, outcomes) = new SimulationActorActionApplier().Apply(
            network,
            [action],
            new Dictionary<string, SimulationActorState>(StringComparer.OrdinalIgnoreCase) { [actor.Id] = actor },
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));

        Assert.True(outcomes.Single().Applied);
        Assert.Equal(25d, updated.Edges.Single().Capacity);
        Assert.Equal(50d, action.Cost);
        Assert.Contains("scaled", outcomes.Single().Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Logistics_CapacityReduction_AppliesWhenAboveCurrentRoutedFlow()
    {
        var network = BuildSupplyDemandNetwork(production: 200d, consumption: 200d, capacity: 100d);
        var actor = BuildPlanner(cash: 50d);
        var action = new SimulationActorAction
        {
            Id = "cap-reduce",
            ActorId = actor.Id,
            Kind = SimulationActorActionKind.AdjustEdgeCapacity,
            TargetEdgeId = "edge",
            TrafficType = "Food",
            AbsoluteValue = 75d
        };

        var (updated, outcomes) = new SimulationActorActionApplier().Apply(
            network,
            [action],
            new Dictionary<string, SimulationActorState>(StringComparer.OrdinalIgnoreCase) { [actor.Id] = actor },
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["edge"] = 60d });

        Assert.True(outcomes.Single().Applied);
        Assert.Equal(75d, updated.Edges.Single().Capacity);
        Assert.Equal(0d, action.Cost);
        Assert.Contains("reduced", outcomes.Single().Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Logistics_CapacityReduction_DoesNotDropBelowCurrentRoutedFlow()
    {
        var network = BuildSupplyDemandNetwork(production: 200d, consumption: 200d, capacity: 100d);
        var actor = BuildPlanner(cash: 50d);
        var action = new SimulationActorAction
        {
            Id = "cap-over-reduce",
            ActorId = actor.Id,
            Kind = SimulationActorActionKind.AdjustEdgeCapacity,
            TargetEdgeId = "edge",
            TrafficType = "Food",
            AbsoluteValue = 50d
        };

        var (updated, outcomes) = new SimulationActorActionApplier().Apply(
            network,
            [action],
            new Dictionary<string, SimulationActorState>(StringComparer.OrdinalIgnoreCase) { [actor.Id] = actor },
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["edge"] = 60d });

        Assert.False(outcomes.Single().Applied);
        Assert.Equal(100d, updated.Edges.Single().Capacity);
        Assert.Contains("routed flow", outcomes.Single().Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuyTraffic_RouteSelectionMinimisesProducerPricePlusTransportCost()
    {
        var network = BuildLandedCostRouteNetwork();
        var method = typeof(SimulationActorCoordinator).GetMethod(
            "FindCheapestRouteWithResidualCapacity",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var remainingCapacityByEdgeId = network.Edges.ToDictionary(edge => edge.Id, edge => edge.Capacity ?? double.PositiveInfinity, StringComparer.OrdinalIgnoreCase);
        var route = ((double ResidualCapacity, double RouteCost, double LandedUnitPrice, IReadOnlyList<string> PathEdgeIds))method.Invoke(
            null,
            [network, "consumer", "Food", remainingCapacityByEdgeId])!;

        Assert.Equal(["expensive-direct"], route.PathEdgeIds);
        Assert.Equal(0d, route.RouteCost);
        Assert.Equal(10d, route.LandedUnitPrice);
    }

    private static SimulationActorState BuildPlanner(double cash) => new()
    {
        Id = "planner",
        Kind = SimulationActorKind.LogisticsPlanner,
        Objective = SimulationActorObjective.MinimiseUnmetDemand,
        Cash = cash,
        Capability = new SimulationActorCapability
        {
            ActorId = "planner",
            AllowedActionKinds = [SimulationActorActionKind.AdjustEdgeCapacity],
            Permissions =
            [
                new SimulationActorPermission { ActionKind = SimulationActorActionKind.AdjustEdgeCapacity, EdgeId = "edge", TrafficType = "Food", IsAllowed = true }
            ]
        }
    };

    private static NetworkModel BuildLandedCostRouteNetwork()
    {
        var layerId = Guid.NewGuid();
        return new NetworkModel
        {
            Layers = [new NetworkLayerModel { Id = layerId, Name = "Layer", Type = NetworkLayerType.Physical }],
            TrafficTypes = [new TrafficTypeDefinition { Name = "Food", RoutingPreference = RoutingPreference.Cost, AllocationMode = AllocationMode.GreedyBestRoute, DefaultUnitSalePrice = 5d }],
            Nodes =
            [
                new NodeModel { Id = "cheap-producer", Name = "Cheap Producer", LayerId = layerId, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Production = 100d, UnitPrice = 1d }] },
                new NodeModel { Id = "expensive-producer", Name = "Expensive Producer", LayerId = layerId, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Production = 100d, UnitPrice = 10d }] },
                new NodeModel { Id = "consumer", Name = "Consumer", LayerId = layerId, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Consumption = 100d }] }
            ],
            Edges =
            [
                new EdgeModel { Id = "cheap-long-haul", FromNodeId = "cheap-producer", ToNodeId = "consumer", LayerId = layerId, Capacity = 100d, Cost = 100d, Time = 1d },
                new EdgeModel { Id = "expensive-direct", FromNodeId = "expensive-producer", ToNodeId = "consumer", LayerId = layerId, Capacity = 100d, Cost = 0d, Time = 1d }
            ]
        };
    }

    private static NetworkModel BuildSupplyDemandNetwork(double production, double consumption, double capacity)
    {
        var layerId = Guid.NewGuid();
        return new NetworkModel
        {
            Layers = [new NetworkLayerModel { Id = layerId, Name = "Layer", Type = NetworkLayerType.Physical }],
            TrafficTypes = [new TrafficTypeDefinition { Name = "Food", RoutingPreference = RoutingPreference.Cost, AllocationMode = AllocationMode.GreedyBestRoute, DefaultUnitSalePrice = 5d }],
            Nodes =
            [
                new NodeModel { Id = "producer", Name = "Producer", LayerId = layerId, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Production = production, UnitPrice = 5d }] },
                new NodeModel { Id = "consumer", Name = "Consumer", LayerId = layerId, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Consumption = consumption }] }
            ],
            Edges = [new EdgeModel { Id = "edge", FromNodeId = "producer", ToNodeId = "consumer", LayerId = layerId, Capacity = capacity, Cost = 0d, Time = 1d }]
        };
    }
}

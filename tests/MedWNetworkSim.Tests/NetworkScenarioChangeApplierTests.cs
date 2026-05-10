using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class NetworkScenarioChangeApplierTests
{
    [Fact]
    public void Apply_DoesNotMutateOriginalNetwork()
    {
        var network = CreateNetwork();
        var applier = new NetworkScenarioChangeApplier();

        var (updated, outcomes) = applier.Apply(
            network,
            [new NetworkScenarioChange { Kind = NetworkChangeKind.AdjustProduction, TargetNodeId = "producer", TrafficType = "Food", AbsoluteValue = 25d }]);

        Assert.Single(outcomes);
        Assert.Equal(10d, network.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single().Production);
        Assert.Equal(25d, updated.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single().Production);
    }

    [Fact]
    public void Apply_AdjustsProductionConsumptionAndPrice()
    {
        var applier = new NetworkScenarioChangeApplier();
        var (updated, outcomes) = applier.Apply(
            CreateNetwork(),
            [
                new NetworkScenarioChange { Kind = NetworkChangeKind.AdjustProduction, TargetNodeId = "producer", TrafficType = "Food", AbsoluteValue = 20d },
                new NetworkScenarioChange { Kind = NetworkChangeKind.AdjustConsumption, TargetNodeId = "consumer", TrafficType = "Food", AbsoluteValue = 15d },
                new NetworkScenarioChange { Kind = NetworkChangeKind.AdjustTrafficPrice, TargetNodeId = "producer", TrafficType = "Food", AbsoluteValue = 7d }
            ]);

        Assert.All(outcomes, outcome => Assert.True(outcome.Applied, outcome.Reason));
        Assert.Equal(20d, updated.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single().Production);
        Assert.Equal(15d, updated.Nodes.Single(node => node.Id == "consumer").TrafficProfiles.Single().Consumption);
        Assert.Equal(7d, updated.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single().UnitPrice);
    }

    [Fact]
    public void Apply_AdjustsEdgeCapacityAndCost()
    {
        var applier = new NetworkScenarioChangeApplier();
        var (updated, outcomes) = applier.Apply(
            CreateNetwork(),
            [
                new NetworkScenarioChange { Kind = NetworkChangeKind.AdjustEdgeCapacity, TargetEdgeId = "edge", AbsoluteValue = 30d },
                new NetworkScenarioChange { Kind = NetworkChangeKind.AdjustEdgeCost, TargetEdgeId = "edge", AbsoluteValue = 4d }
            ]);

        Assert.All(outcomes, outcome => Assert.True(outcome.Applied, outcome.Reason));
        var edge = Assert.Single(updated.Edges);
        Assert.Equal(30d, edge.Capacity);
        Assert.Equal(4d, edge.Cost);
    }

    [Fact]
    public void Apply_RejectsInvalidTargetsCleanly()
    {
        var applier = new NetworkScenarioChangeApplier();
        var (_, outcomes) = applier.Apply(
            CreateNetwork(),
            [
                new NetworkScenarioChange { Kind = NetworkChangeKind.AdjustProduction, TargetNodeId = "missing", TrafficType = "Food", AbsoluteValue = 20d },
                new NetworkScenarioChange { Kind = NetworkChangeKind.AdjustEdgeCost, TargetEdgeId = "missing", AbsoluteValue = 4d }
            ]);

        Assert.All(outcomes, outcome => Assert.False(outcome.Applied));
        Assert.Contains("missing", outcomes[0].Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing", outcomes[1].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_RejectsCapacityReductionBelowCurrentFlowUnlessAllowed()
    {
        var applier = new NetworkScenarioChangeApplier();
        var network = CreateNetwork();

        var (_, rejectedOutcomes) = applier.Apply(
            network,
            [new NetworkScenarioChange { Kind = NetworkChangeKind.AdjustEdgeCapacity, TargetEdgeId = "edge", AbsoluteValue = 5d }],
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["edge"] = 8d });

        Assert.False(rejectedOutcomes.Single().Applied);

        var (updated, acceptedOutcomes) = applier.Apply(
            network,
            [new NetworkScenarioChange { Kind = NetworkChangeKind.AdjustEdgeCapacity, TargetEdgeId = "edge", AbsoluteValue = 5d, AllowReduceBelowCurrentFlow = true }],
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["edge"] = 8d });

        Assert.True(acceptedOutcomes.Single().Applied, acceptedOutcomes.Single().Reason);
        Assert.Equal(5d, updated.Edges.Single().Capacity);
    }

    [Fact]
    public void Apply_BlocksAndRelaxesTrafficPermissions()
    {
        var applier = new NetworkScenarioChangeApplier();
        var blocked = applier.Apply(
            CreateNetwork(),
            [new NetworkScenarioChange { Kind = NetworkChangeKind.SetTrafficPermission, TargetEdgeId = "edge", TrafficType = "Food" }]).Network;

        var permission = Assert.Single(blocked.Edges.Single().TrafficPermissions);
        Assert.Equal(EdgeTrafficPermissionMode.Blocked, permission.Mode);

        var relaxed = applier.Apply(
            blocked,
            [new NetworkScenarioChange { Kind = NetworkChangeKind.ClearTrafficRestrictions, TargetEdgeId = "edge", TrafficType = "Food" }]).Network;

        Assert.Equal(EdgeTrafficPermissionMode.Permitted, relaxed.Edges.Single().TrafficPermissions.Single().Mode);
    }

    private static NetworkModel CreateNetwork() => new()
    {
        Name = "Scenario Test",
        TrafficTypes = [new TrafficTypeDefinition { Name = "Food" }],
        Nodes =
        [
            new NodeModel
            {
                Id = "producer",
                Name = "Producer",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Production = 10d, UnitPrice = 2d }]
            },
            new NodeModel
            {
                Id = "consumer",
                Name = "Consumer",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Consumption = 12d }]
            }
        ],
        Edges =
        [
            new EdgeModel
            {
                Id = "edge",
                FromNodeId = "producer",
                ToNodeId = "consumer",
                Time = 1d,
                Cost = 2d,
                Capacity = 20d
            }
        ]
    };
}

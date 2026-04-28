using MedWNetworkSim.App.Agents;
using MedWNetworkSim.App.Models;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class SimulationActorsTests
{
    [Fact]
    public void Firm_IncreasesProduction_WhenProfitable()
    {
        var network = BuildNetwork();
        var coordinator = new SimulationActorCoordinator();
        var actor = new SimulationActorState
        {
            Id = "firm-1",
            Name = "Firm",
            Kind = SimulationActorKind.Firm,
            Objective = SimulationActorObjective.MaximiseProfit,
            ControlledNodeIds = ["producer"],
            Cash = 100,
            Budget = 100
        };

        var preview = coordinator.PreviewActorActions(network, [actor]);

        Assert.Contains(preview.Single().Actions, action => action.Kind == SimulationActorActionKind.AdjustProduction && action.DeltaValue > 0d);
    }

    [Fact]
    public void Government_Overrides_Firm_WhenPolicyConflicts()
    {
        var network = BuildNetwork();
        var coordinator = new SimulationActorCoordinator();
        var government = new SimulationActorState
        {
            Id = "gov",
            Name = "Gov",
            Kind = SimulationActorKind.Government,
            Objective = SimulationActorObjective.EnforcePolicy,
            ControlledEdgeIds = ["edge-a"],
            Cash = 100,
            Budget = 100
        };
        var firm = new SimulationActorState
        {
            Id = "firm",
            Name = "Firm",
            Kind = SimulationActorKind.Firm,
            Objective = SimulationActorObjective.MaximiseProfit,
            ControlledEdgeIds = ["edge-a"],
            Cash = 100,
            Budget = 100
        };

        var step = coordinator.StepActorsOnce(network, [firm, government], policySettings: new SimulationActorPolicySettings { ConstrainedTrafficTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Food" } });

        Assert.Contains(step.ActionOutcomes, outcome => outcome.Applied && outcome.Action.Kind == SimulationActorActionKind.BanTrafficOnEdge);
        var rule = step.NetworkAfterStep.Edges.Single(edge => edge.Id == "edge-a").TrafficPermissions.SingleOrDefault(permission => permission.TrafficType == "Food");
        Assert.NotNull(rule);
        Assert.Equal(EdgeTrafficPermissionMode.Blocked, rule!.Mode);
    }

    [Fact]
    public void LogisticsPlanner_ReducesUnmetDemand_WhenCapacityCanGrow()
    {
        var network = BuildNetwork();
        var coordinator = new SimulationActorCoordinator();
        var actor = new SimulationActorState
        {
            Id = "log-1",
            Name = "Planner",
            Kind = SimulationActorKind.LogisticsPlanner,
            Objective = SimulationActorObjective.MinimiseUnmetDemand,
            Cash = 100,
            Budget = 100
        };

        var before = new MedWNetworkSim.App.Services.NetworkSimulationEngine().Simulate(network).Sum(outcome => outcome.UnmetDemand);
        var step = coordinator.StepActorsOnce(network, [actor]);
        var after = new MedWNetworkSim.App.Services.NetworkSimulationEngine().Simulate(step.NetworkAfterStep).Sum(outcome => outcome.UnmetDemand);

        Assert.True(after <= before);
    }

    [Fact]
    public void ConflictResolver_IsDeterministic()
    {
        var network = BuildNetwork();
        var coordinator = new SimulationActorCoordinator();
        var actors = new List<SimulationActorState>
        {
            new()
            {
                Id = "gov",
                Name = "Gov",
                Kind = SimulationActorKind.Government,
                Objective = SimulationActorObjective.EnforcePolicy,
                ControlledEdgeIds = ["edge-a"],
                Cash = 100,
                Budget = 100
            },
            new()
            {
                Id = "firm",
                Name = "Firm",
                Kind = SimulationActorKind.Firm,
                Objective = SimulationActorObjective.MaximiseProfit,
                ControlledNodeIds = ["producer"],
                Cash = 100,
                Budget = 100
            }
        };

        var first = coordinator.PreviewActorActions(network, actors, policySettings: new SimulationActorPolicySettings { ConstrainedTrafficTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Food" } });
        var second = coordinator.PreviewActorActions(network, actors, policySettings: new SimulationActorPolicySettings { ConstrainedTrafficTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Food" } });

        Assert.Equal(string.Join('|', first.SelectMany(decision => decision.Actions).Select(action => action.Id)), string.Join('|', second.SelectMany(decision => decision.Actions).Select(action => action.Id)));
    }

    [Fact]
    public void ActionApplier_DoesNotMutateOriginalNetwork()
    {
        var source = BuildNetwork();
        var action = new SimulationActorAction
        {
            Id = "a1",
            ActorId = "firm",
            Kind = SimulationActorActionKind.AdjustEdgeCapacity,
            TargetEdgeId = "edge-a",
            DeltaValue = 10,
            Cost = 0
        };

        var applier = new SimulationActorActionApplier();
        var (result, outcomes) = applier.Apply(source, [action], new Dictionary<string, SimulationActorState>(StringComparer.OrdinalIgnoreCase)
        {
            ["firm"] = new SimulationActorState { Id = "firm", IsEnabled = true, Cash = 100 }
        }, new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));

        Assert.True(outcomes.Single().Applied);
        Assert.Equal(20, result.Edges.Single(edge => edge.Id == "edge-a").Capacity);
        Assert.Equal(10, source.Edges.Single(edge => edge.Id == "edge-a").Capacity);
    }

    [Fact]
    public void DisabledActors_DoNothing()
    {
        var network = BuildNetwork();
        var coordinator = new SimulationActorCoordinator();
        var disabled = new SimulationActorState
        {
            Id = "d1",
            Name = "Disabled",
            Kind = SimulationActorKind.Firm,
            Objective = SimulationActorObjective.MaximiseProfit,
            ControlledNodeIds = ["producer"],
            IsEnabled = false
        };

        var preview = coordinator.PreviewActorActions(network, [disabled]);

        Assert.Empty(preview);
    }

    [Fact]
    public void InvalidTargets_AreRejectedCleanly()
    {
        var applier = new SimulationActorActionApplier();
        var source = BuildNetwork();
        var action = new SimulationActorAction
        {
            Id = "bad",
            ActorId = "firm",
            Kind = SimulationActorActionKind.AdjustEdgeCapacity,
            TargetEdgeId = "missing-edge"
        };

        var (_, outcomes) = applier.Apply(source, [action], new Dictionary<string, SimulationActorState>(StringComparer.OrdinalIgnoreCase)
        {
            ["firm"] = new SimulationActorState { Id = "firm", IsEnabled = true, Cash = 10 }
        }, new Dictionary<string, double>());

        Assert.False(outcomes.Single().Applied);
        Assert.Contains("not found", outcomes.Single().Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NetworkSerialization_PreservesActorSettings()
    {
        var service = new MedWNetworkSim.App.Services.NetworkFileService();
        var network = BuildNetwork();
        network.Actors.Add(new SimulationActorState
        {
            Id = "gov",
            Name = "Government",
            Kind = SimulationActorKind.Government,
            Objective = SimulationActorObjective.EnforcePolicy,
            Budget = 100,
            Cash = 45,
            CooperationWeight = 0.75d
        });

        var json = System.Text.Json.JsonSerializer.Serialize(network, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        var loaded = service.LoadJson(json);

        Assert.Single(loaded.Actors);
        Assert.Equal("gov", loaded.Actors[0].Id);
        Assert.Equal(SimulationActorKind.Government, loaded.Actors[0].Kind);
        Assert.Equal(0.75d, loaded.Actors[0].CooperationWeight);
    }

    private static NetworkModel BuildNetwork()
    {
        var layerId = Guid.NewGuid();
        return new NetworkModel
        {
            Name = "Actors Test",
            Layers = [new NetworkLayerModel { Id = layerId, Name = "Physical", Type = NetworkLayerType.Physical, Order = 0 }],
            TrafficTypes = [new TrafficTypeDefinition { Name = "Food", RoutingPreference = RoutingPreference.LowestCost, AllocationMode = AllocationMode.GreedyBestRoute }],
            Nodes =
            [
                new NodeModel
                {
                    Id = "producer",
                    Name = "Producer",
                    LayerId = layerId,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Production = 120, UnitPrice = 1d }]
                },
                new NodeModel
                {
                    Id = "consumer",
                    Name = "Consumer",
                    LayerId = layerId,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Consumption = 100, UnitPrice = 2d }]
                }
            ],
            Edges =
            [
                new EdgeModel
                {
                    Id = "edge-a",
                    FromNodeId = "producer",
                    ToNodeId = "consumer",
                    Capacity = 10,
                    Cost = 1,
                    Time = 1,
                    LayerId = layerId,
                    IsBidirectional = false
                }
            ]
        };
    }
}

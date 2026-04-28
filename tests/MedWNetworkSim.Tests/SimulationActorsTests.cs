using MedWNetworkSim.App.Agents;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.Presentation;
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

    [Fact]
    public void Firm_Cannot_SetPolicyActions()
    {
        var applier = new SimulationActorActionApplier();
        var source = BuildNetwork();
        var action = new SimulationActorAction
        {
            Id = "policy",
            ActorId = "firm",
            Kind = SimulationActorActionKind.SetEdgePolicy,
            TargetEdgeId = "edge-a"
        };

        var (_, outcomes) = applier.Apply(source, [action], new Dictionary<string, SimulationActorState>(StringComparer.OrdinalIgnoreCase)
        {
            ["firm"] = new SimulationActorState { Id = "firm", Kind = SimulationActorKind.Firm, IsEnabled = true, Capability = SimulationActorCapabilityCatalog.ForKind("firm", SimulationActorKind.Firm) }
        }, new Dictionary<string, double>());

        Assert.False(outcomes.Single().Applied);
        Assert.Contains("capability", outcomes.Single().Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Government_Cannot_BuyTraffic()
    {
        var applier = new SimulationActorActionApplier();
        var source = BuildNetwork();
        var action = new SimulationActorAction { Id = "buy", ActorId = "gov", Kind = SimulationActorActionKind.BuyTraffic, TrafficType = "Food" };
        var (_, outcomes) = applier.Apply(source, [action], new Dictionary<string, SimulationActorState>(StringComparer.OrdinalIgnoreCase)
        {
            ["gov"] = new SimulationActorState { Id = "gov", Kind = SimulationActorKind.Government, IsEnabled = true, Capability = SimulationActorCapabilityCatalog.ForKind("gov", SimulationActorKind.Government) }
        }, new Dictionary<string, double>());
        Assert.False(outcomes.Single().Applied);
    }

    [Fact]
    public void TrafficTypeCapabilityRestriction_IsEnforced()
    {
        var applier = new SimulationActorActionApplier();
        var source = BuildNetwork();
        source.TrafficTypes.Add(new TrafficTypeDefinition { Name = "Wool" });
        var capability = SimulationActorCapabilityCatalog.ForKind("firm", SimulationActorKind.Firm);
        capability.AllowAllTrafficTypes = false;
        capability.AllowedTrafficTypes = ["Food"];
        var action = new SimulationActorAction { Id = "a", ActorId = "firm", Kind = SimulationActorActionKind.AdjustProduction, TargetNodeId = "producer", TrafficType = "Wool" };
        var (_, outcomes) = applier.Apply(source, [action], new Dictionary<string, SimulationActorState>(StringComparer.OrdinalIgnoreCase)
        {
            ["firm"] = new SimulationActorState { Id = "firm", Kind = SimulationActorKind.Firm, IsEnabled = true, Capability = capability }
        }, new Dictionary<string, double>());
        Assert.False(outcomes.Single().Applied);
        Assert.Contains("traffic type", outcomes.Single().Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectedNodeAssignment_TogglesControlledNodeIds()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        vm.AddFirmActorCommand.Execute(null);
        var actor = Assert.IsType<SimulationActorState>(vm.SelectedSimulationActor);

        vm.Scene.Selection.SelectedNodeIds.Add("producer");
        vm.Scene.Selection.SelectedNodeIds.Add("consumer");
        vm.AssignSelectedNodeToActorCommand.Execute(null);
        Assert.Contains("producer", actor.ControlledNodeIds);
        Assert.Contains("consumer", actor.ControlledNodeIds);

        vm.AssignSelectedNodeToActorCommand.Execute(null);
        Assert.DoesNotContain("producer", actor.ControlledNodeIds);
        Assert.DoesNotContain("consumer", actor.ControlledNodeIds);
    }

    [Fact]
    public void SelectedEdgeAssignment_TogglesControlledEdgeIds()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        vm.AddFirmActorCommand.Execute(null);
        var actor = Assert.IsType<SimulationActorState>(vm.SelectedSimulationActor);

        vm.Scene.Selection.SelectedEdgeIds.Add("edge-a");
        vm.AssignSelectedEdgeToActorCommand.Execute(null);
        Assert.Contains("edge-a", actor.ControlledEdgeIds);

        vm.AssignSelectedEdgeToActorCommand.Execute(null);
        Assert.DoesNotContain("edge-a", actor.ControlledEdgeIds);
    }

    [Fact]
    public void BudgetEditsPersist()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        vm.AddFirmActorCommand.Execute(null);
        vm.ActorBudgetText = "420";
        vm.ActorCashText = "111";
        vm.ActorRiskToleranceText = "0.7";
        vm.ActorCooperationWeightText = "0.4";
        vm.ApplySelectedActorCommand.Execute(null);

        var path = Path.GetTempFileName();
        try
        {
            vm.SaveNetwork(path);
            var loaded = new MedWNetworkSim.App.Services.NetworkFileService().Load(path);
            Assert.Single(loaded.Actors);
            Assert.Equal(420d, loaded.Actors[0].Budget);
            Assert.Equal(111d, loaded.Actors[0].Cash);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AllowedTrafficTypesPersist()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        vm.AddFirmActorCommand.Execute(null);
        vm.ActorAllowAllTrafficTypes = false;
        var row = Assert.Single(vm.ActorTrafficTypeRows);
        row.IsAllowed = true;
        vm.ApplySelectedActorCommand.Execute(null);

        var path = Path.GetTempFileName();
        try
        {
            vm.SaveNetwork(path);
            var loaded = new MedWNetworkSim.App.Services.NetworkFileService().Load(path);
            Assert.Single(loaded.Actors);
            Assert.False(loaded.Actors[0].Capability.AllowAllTrafficTypes);
            Assert.Contains("Food", loaded.Actors[0].Capability.AllowedTrafficTypes);
        }
        finally
        {
            File.Delete(path);
        }
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

    private static WorkspaceViewModel BuildWorkspaceViewModelWithNetwork()
    {
        var vm = new WorkspaceViewModel();
        var path = Path.GetTempFileName();
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(BuildNetwork(), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
        vm.OpenNetwork(path);
        File.Delete(path);
        return vm;
    }
}

using MedWNetworkSim.App.Agents;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.Presentation;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class SimulationActorsTests
{
    [Fact]
    public void Firm_ProposesPermittedProfitAction()
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

        Assert.Contains(preview.Single().Actions, action => action.Kind != SimulationActorActionKind.NoOp);
    }

    [Fact]
    public void Firm_AutomaticSupplyIncrease_UsesSellTrafficWhenPermitted()
    {
        var network = BuildHighThroughputNetwork();
        network.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single().UnitPrice = 3d;
        var coordinator = new SimulationActorCoordinator();
        var actor = new SimulationActorState
        {
            Id = "firm-seller",
            Name = "Seller",
            Kind = SimulationActorKind.Firm,
            Objective = SimulationActorObjective.MaximiseProfit,
            ControlledNodeIds = ["producer"],
            Cash = 100d,
            Capability = new SimulationActorCapability
            {
                ActorId = "firm-seller",
                AllowedActionKinds = [SimulationActorActionKind.SellTraffic],
                Permissions =
                [
                    new SimulationActorPermission
                    {
                        ActionKind = SimulationActorActionKind.SellTraffic,
                        TrafficType = "Food",
                        NodeId = "producer",
                        IsAllowed = true
                    }
                ]
            }
        };

        var decision = coordinator.PreviewActorActions(network, [actor]).Single();

        var action = Assert.Single(decision.Actions);
        Assert.Equal(SimulationActorActionKind.SellTraffic, action.Kind);
        Assert.Equal("Offer additional traffic for sale because delivered demand and unit margin are positive.", action.Reason);
    }

    [Fact]
    public void Firm_AutomaticSupplyIncrease_DoesNotExpandLossMakingProduction()
    {
        var network = BuildHighThroughputNetwork();
        var producerProfile = network.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single();
        producerProfile.UnitPrice = 0.5d;
        producerProfile.ProductionCostPerUnit = 2d;
        var coordinator = new SimulationActorCoordinator();
        var actor = new SimulationActorState
        {
            Id = "firm-seller",
            Name = "Seller",
            Kind = SimulationActorKind.Firm,
            Objective = SimulationActorObjective.MaximiseProfit,
            ControlledNodeIds = ["producer"],
            Cash = 100d,
            Capability = new SimulationActorCapability
            {
                ActorId = "firm-seller",
                AllowedActionKinds = [SimulationActorActionKind.SellTraffic],
                Permissions =
                [
                    new SimulationActorPermission
                    {
                        ActionKind = SimulationActorActionKind.SellTraffic,
                        TrafficType = "Food",
                        NodeId = "producer",
                        IsAllowed = true
                    }
                ]
            }
        };

        var decision = coordinator.PreviewActorActions(network, [actor]).Single();

        Assert.DoesNotContain(decision.Actions, action => action.Kind == SimulationActorActionKind.SellTraffic);
    }

    [Fact]
    public void Firm_DoesNotCutProduction_WhenAllProductionDeliveredAndUnmetDemandRemains()
    {
        var network = BuildProductionPressureNetwork(production: 10d, consumption: 400d);
        var coordinator = new SimulationActorCoordinator();
        var actor = BuildProductionOnlyFirm();

        var outcome = new MedWNetworkSim.App.Services.NetworkSimulationEngine().Simulate(network).Single();
        Assert.Equal(10d, outcome.TotalDelivered);
        Assert.Equal(390d, outcome.UnmetDemand);

        var decision = coordinator.PreviewActorActions(network, [actor]).Single();

        Assert.DoesNotContain(decision.Actions, action =>
            action.Kind == SimulationActorActionKind.AdjustProduction &&
            action.DeltaValue < 0d);
    }

    [Fact]
    public void Firm_IncreasesOrHoldsProduction_WhenDeliveredProductionHasUnmetDemand()
    {
        var network = BuildProductionPressureNetwork(production: 10d, consumption: 400d);
        var coordinator = new SimulationActorCoordinator();
        var actor = BuildProductionOnlyFirm();

        var decision = coordinator.PreviewActorActions(network, [actor]).Single();
        var productionActions = decision.Actions
            .Where(action => action.Kind == SimulationActorActionKind.AdjustProduction)
            .ToList();

        Assert.DoesNotContain(productionActions, action => action.DeltaValue < 0d);
        Assert.True(
            productionActions.Any(action => action.DeltaValue > 0d) ||
            decision.Actions.Any(action => action.Kind == SimulationActorActionKind.NoOp));
    }

    [Fact]
    public void Firm_CutsProduction_WhenProductionExceedsDeliveredDemandAndUnmetDemandIsZero()
    {
        var network = BuildProductionPressureNetwork(production: 100d, consumption: 10d);
        var actor = BuildProductionOnlyFirm();
        var firm = new FirmSimulationActor(actor);

        var decision = firm.Decide(new SimulationActorContext
        {
            CurrentNetwork = network,
            CurrentSnapshot = new MedWNetworkSim.App.VisualAnalytics.VisualAnalyticsSnapshot
            {
                Network = network,
                TrafficOutcomes =
                [
                    new TrafficSimulationOutcome
                    {
                        TrafficType = "Food",
                        TotalProduction = 100d,
                        TotalConsumption = 10d,
                        TotalDelivered = 10d,
                        UnusedSupply = 90d,
                        UnmetDemand = 0d,
                        Allocations =
                        [
                            new RouteAllocation
                            {
                                TrafficType = "Food",
                                ProducerNodeId = "producer",
                                ConsumerNodeId = "consumer",
                                Quantity = 10d
                            }
                        ]
                    }
                ],
                ConsumerCosts = [],
                Period = 0
            },
            CurrentInsights = [],
            Tick = 0,
            PreviousDecisions = [],
            PolicySettings = new SimulationActorPolicySettings()
        });

        Assert.Contains(decision.Actions, action =>
            action.Kind == SimulationActorActionKind.AdjustProduction &&
            action.DeltaValue < 0d);
    }

    [Fact]
    public void Firm_AutomaticDemandIncrease_UsesBuyTrafficWhenPermitted()
    {
        var network = BuildNetwork();
        var coordinator = new SimulationActorCoordinator();
        var actor = new SimulationActorState
        {
            Id = "firm-buyer",
            Name = "Buyer",
            Kind = SimulationActorKind.Firm,
            Objective = SimulationActorObjective.MaximiseProfit,
            ControlledNodeIds = ["consumer"],
            Cash = 100d,
            Capability = new SimulationActorCapability
            {
                ActorId = "firm-buyer",
                AllowedActionKinds = [SimulationActorActionKind.BuyTraffic],
                Permissions =
                [
                    new SimulationActorPermission
                    {
                        ActionKind = SimulationActorActionKind.BuyTraffic,
                        TrafficType = "Food",
                        NodeId = "consumer",
                        IsAllowed = true
                    }
                ]
            }
        };

        var decision = coordinator.PreviewActorActions(network, [actor]).Single();

        var action = Assert.Single(decision.Actions);
        Assert.Equal(SimulationActorActionKind.BuyTraffic, action.Kind);
        Assert.Equal(10d, action.DeltaValue);
        Assert.Equal("Buy/input traffic because it is required for profitable downstream production or unmet demand exists.", action.Reason);
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
    public void LogisticsPlanner_Intervenes_WhenHighUnmetDemandHasLowUtilisation()
    {
        var network = BuildLowUtilisationUnmetDemandNetwork();
        var coordinator = new SimulationActorCoordinator();
        var actor = new SimulationActorState
        {
            Id = "log-low-util",
            Name = "Planner",
            Kind = SimulationActorKind.LogisticsPlanner,
            Objective = SimulationActorObjective.MinimiseUnmetDemand,
            Cash = 100,
            Budget = 100
        };

        var beforeOutcomes = new MedWNetworkSim.App.Services.NetworkSimulationEngine().Simulate(network);
        Assert.Equal(100d, beforeOutcomes.Sum(outcome => outcome.UnmetDemand));
        Assert.Empty(beforeOutcomes.SelectMany(outcome => outcome.Allocations));

        var decision = coordinator.PreviewActorActions(network, [actor]).Single();

        Assert.Contains(decision.Actions, action => action.Kind == SimulationActorActionKind.AdjustEdgeCapacity);
        Assert.DoesNotContain(decision.Actions, action => action.Kind == SimulationActorActionKind.NoOp);
        Assert.Contains("unmet", decision.ReasonSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LogisticsPlanner_Intervention_DecreasesUnmetDemand()
    {
        var network = BuildLowUtilisationUnmetDemandNetwork();
        var coordinator = new SimulationActorCoordinator();
        var actor = new SimulationActorState
        {
            Id = "log-lower-unmet",
            Name = "Planner",
            Kind = SimulationActorKind.LogisticsPlanner,
            Objective = SimulationActorObjective.MinimiseUnmetDemand,
            Cash = 100,
            Budget = 100
        };

        var before = new MedWNetworkSim.App.Services.NetworkSimulationEngine().Simulate(network).Sum(outcome => outcome.UnmetDemand);
        var step = coordinator.StepActorsOnce(network, [actor]);
        var after = new MedWNetworkSim.App.Services.NetworkSimulationEngine().Simulate(step.NetworkAfterStep).Sum(outcome => outcome.UnmetDemand);

        Assert.Contains(step.ActionOutcomes, outcome =>
            outcome.Applied &&
            outcome.Action.Kind == SimulationActorActionKind.AdjustEdgeCapacity &&
            outcome.Action.TargetEdgeId == "blocked-capacity");
        Assert.True(after < before);
    }

    [Fact]
    public void InactiveEdgeTrafficPermission_FallsBackToPermittedDefault()
    {
        var network = BuildNetwork();
        network.EdgeTrafficPermissionDefaults =
        [
            new EdgeTrafficPermissionRule
            {
                TrafficType = "Food",
                Mode = EdgeTrafficPermissionMode.Permitted,
                IsActive = true
            }
        ];
        network.Edges.Single(edge => edge.Id == "edge-a").TrafficPermissions =
        [
            new EdgeTrafficPermissionRule
            {
                TrafficType = "Food",
                Mode = EdgeTrafficPermissionMode.Blocked,
                IsActive = false
            }
        ];

        var outcome = new MedWNetworkSim.App.Services.NetworkSimulationEngine().Simulate(network).Single();

        Assert.True(outcome.TotalDelivered > 0d);
        Assert.Equal(0d, outcome.NoPermittedPathDemand);
    }

    [Fact]
    public void TimelineCsv_IncludesAgentActions_WithReadableAgentNames()
    {
        var network = BuildNetwork();
        network.Actors =
        [
            new SimulationActorState { Id = "long-unique-firm-id", Name = "Local Mill", Kind = SimulationActorKind.Firm },
            new SimulationActorState { Id = "duplicate-id-a", Name = "Planner", Kind = SimulationActorKind.LogisticsPlanner },
            new SimulationActorState { Id = "duplicate-id-b", Name = "Planner", Kind = SimulationActorKind.LogisticsPlanner }
        ];
        network.AgentActionLogs =
        [
            new AgentActionLogEntry
            {
                AgentId = Guid.NewGuid(),
                ActorId = "long-unique-firm-id",
                AgentName = "Local Mill",
                SimulationTick = 0,
                ActionType = "AdjustProduction",
                TargetId = "producer",
                DecisionSummary = "Increase output",
                Outcome = "More Food",
                UtilityScore = 1.25d
            },
            new AgentActionLogEntry
            {
                AgentId = Guid.NewGuid(),
                ActorId = "duplicate-id-a",
                AgentName = "Planner",
                SimulationTick = 1,
                ActionType = "PreferRoute",
                TargetId = "edge-a",
                DecisionSummary = "Use route",
                Outcome = "Lower delay"
            }
        ];
        var engine = new TemporalNetworkSimulationEngine();
        var state = engine.Initialize(network);
        var results = new[] { engine.Advance(network, state) };
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");

        try
        {
            new ReportExportService().SaveTimelineReport(network, results, path, ReportExportFormat.Csv);
            var csv = File.ReadAllText(path);

            Assert.Contains("Agent Actions", csv);
            Assert.Contains("Local Mill", csv);
            Assert.DoesNotContain("long-unique-firm-id,AdjustProduction", csv);
            Assert.Contains("duplicate-id-a", csv);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
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
            CooperationWeight = 0.75d,
            Capability = new SimulationActorCapability
            {
                ActorId = "gov",
                AllowedActionKinds = [SimulationActorActionKind.AdjustRoutePermission],
                Permissions =
                [
                    new SimulationActorPermission
                    {
                        ActionKind = SimulationActorActionKind.BanTrafficOnEdge,
                        TrafficType = "Food",
                        EdgeId = "edge-a",
                        IsAllowed = false
                    }
                ]
            }
        });

        var json = System.Text.Json.JsonSerializer.Serialize(network, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        var loaded = service.LoadJson(json);

        Assert.Single(loaded.Actors);
        Assert.Equal("gov", loaded.Actors[0].Id);
        Assert.Equal(SimulationActorKind.Government, loaded.Actors[0].Kind);
        Assert.Equal(0.75d, loaded.Actors[0].CooperationWeight);
        var permission = Assert.Single(loaded.Actors[0].Capability.Permissions);
        Assert.Equal(SimulationActorActionKind.BanTrafficOnEdge, permission.ActionKind);
        Assert.Equal("Food", permission.TrafficType);
        Assert.Equal("edge-a", permission.EdgeId);
        Assert.False(permission.IsAllowed);
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
    public void Permission_NodeSpecificRestriction_DeniesMatchingAction()
    {
        var source = BuildNetwork();
        var actor = new SimulationActorState
        {
            Id = "firm",
            Kind = SimulationActorKind.Firm,
            IsEnabled = true,
            Cash = 100,
            Capability = SimulationActorCapabilityCatalog.ForKind("firm", SimulationActorKind.Firm)
        };
        actor.Capability.Permissions.Add(new SimulationActorPermission
        {
            ActionKind = SimulationActorActionKind.AdjustProduction,
            NodeId = "producer",
            IsAllowed = false
        });

        var (_, outcomes) = new SimulationActorActionApplier().Apply(source, [
            new SimulationActorAction { Id = "deny-node", ActorId = "firm", Kind = SimulationActorActionKind.AdjustProduction, TargetNodeId = "producer", TrafficType = "Food", DeltaValue = 10 }
        ], BuildActorMap(actor), new Dictionary<string, double>());

        Assert.False(outcomes.Single().Applied);
        Assert.Equal("Permission explicitly denied.", outcomes.Single().Reason);
    }

    [Fact]
    public void Permission_EdgeSpecificRestriction_DeniesMatchingAction()
    {
        var source = BuildNetwork();
        var actor = new SimulationActorState
        {
            Id = "firm",
            Kind = SimulationActorKind.Firm,
            IsEnabled = true,
            Cash = 100,
            ControlledEdgeIds = ["edge-a"],
            Capability = SimulationActorCapabilityCatalog.ForKind("firm", SimulationActorKind.Firm)
        };
        actor.Capability.Permissions.Add(new SimulationActorPermission
        {
            ActionKind = SimulationActorActionKind.AdjustEdgeCapacity,
            EdgeId = "edge-a",
            IsAllowed = false
        });

        var (_, outcomes) = new SimulationActorActionApplier().Apply(source, [
            new SimulationActorAction { Id = "deny-edge", ActorId = "firm", Kind = SimulationActorActionKind.AdjustEdgeCapacity, TargetEdgeId = "edge-a", DeltaValue = 10 }
        ], BuildActorMap(actor), new Dictionary<string, double>());

        Assert.False(outcomes.Single().Applied);
        Assert.Equal("Permission explicitly denied.", outcomes.Single().Reason);
    }

    [Fact]
    public void Permission_TrafficTypeAndNodeRestriction_OnlyDeniesCombinedMatch()
    {
        var source = BuildNetwork();
        source.TrafficTypes.Add(new TrafficTypeDefinition { Name = "Wool" });
        source.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Add(new NodeTrafficProfile { TrafficType = "Wool", Production = 20 });
        var actor = new SimulationActorState
        {
            Id = "firm",
            Kind = SimulationActorKind.Firm,
            IsEnabled = true,
            Cash = 100,
            Capability = SimulationActorCapabilityCatalog.ForKind("firm", SimulationActorKind.Firm)
        };
        actor.Capability.Permissions.Add(new SimulationActorPermission
        {
            ActionKind = SimulationActorActionKind.AdjustProduction,
            TrafficType = "Wool",
            NodeId = "producer",
            IsAllowed = false
        });

        var (_, outcomes) = new SimulationActorActionApplier().Apply(source, [
            new SimulationActorAction { Id = "deny-wool", ActorId = "firm", Kind = SimulationActorActionKind.AdjustProduction, TargetNodeId = "producer", TrafficType = "Wool", DeltaValue = 10 },
            new SimulationActorAction { Id = "allow-food", ActorId = "firm", Kind = SimulationActorActionKind.AdjustProduction, TargetNodeId = "producer", TrafficType = "Food", DeltaValue = 10 }
        ], BuildActorMap(actor), new Dictionary<string, double>());

        Assert.False(outcomes[0].Applied);
        Assert.True(outcomes[1].Applied);
    }

    [Fact]
    public void Permission_ExplicitAllow_TakesPrecedenceOverLegacyCapabilityRestrictions()
    {
        var source = BuildNetwork();
        var actor = new SimulationActorState
        {
            Id = "gov",
            Kind = SimulationActorKind.Government,
            IsEnabled = true,
            Cash = 100,
            Capability = SimulationActorCapabilityCatalog.ForKind("gov", SimulationActorKind.Government)
        };
        actor.Capability.Permissions.Add(new SimulationActorPermission
        {
            ActionKind = SimulationActorActionKind.AdjustProduction,
            TrafficType = "Food",
            NodeId = "producer",
            IsAllowed = true
        });

        var (result, outcomes) = new SimulationActorActionApplier().Apply(source, [
            new SimulationActorAction { Id = "allow-override", ActorId = "gov", Kind = SimulationActorActionKind.AdjustProduction, TargetNodeId = "producer", TrafficType = "Food", DeltaValue = 10 }
        ], BuildActorMap(actor), new Dictionary<string, double>());

        Assert.True(outcomes.Single().Applied);
        Assert.Equal(130d, result.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single(profile => profile.TrafficType == "Food").Production);
    }

    [Fact]
    public void Permission_ExplicitAllow_DoesNotPermitOtherActions()
    {
        var source = BuildNetwork();
        var actor = new SimulationActorState
        {
            Id = "firm",
            Kind = SimulationActorKind.Firm,
            IsEnabled = true,
            Cash = 100,
            Capability = SimulationActorCapabilityCatalog.ForKind("firm", SimulationActorKind.Firm)
        };
        actor.Capability.Permissions.Add(new SimulationActorPermission
        {
            ActionKind = SimulationActorActionKind.AdjustProduction,
            TrafficType = "Food",
            NodeId = "producer",
            IsAllowed = true
        });

        var (_, outcomes) = new SimulationActorActionApplier().Apply(source, [
            new SimulationActorAction { Id = "deny-edge", ActorId = "firm", Kind = SimulationActorActionKind.AdjustEdgeCapacity, TargetEdgeId = "edge-a", DeltaValue = 10 }
        ], BuildActorMap(actor), new Dictionary<string, double>());

        Assert.False(outcomes.Single().Applied);
        Assert.Equal("Permission is not explicitly allowed.", outcomes.Single().Reason);
    }

    [Fact]
    public void Permission_ExplicitAllow_LimitsAutomaticActionsToMatchingScope()
    {
        var network = BuildNetwork();
        var coordinator = new SimulationActorCoordinator();
        var actor = new SimulationActorState
        {
            Id = "firm",
            Name = "Firm",
            Kind = SimulationActorKind.Firm,
            Objective = SimulationActorObjective.MaximiseProfit,
            Cash = 100,
            Budget = 100,
            Capability = SimulationActorCapabilityCatalog.ForKind("firm", SimulationActorKind.Firm)
        };
        actor.Capability.Permissions.Add(new SimulationActorPermission
        {
            ActionKind = SimulationActorActionKind.AdjustProduction,
            TrafficType = "Food",
            NodeId = "producer",
            IsAllowed = true
        });

        var preview = coordinator.PreviewActorActions(network, [actor]);

        Assert.DoesNotContain(preview.Single().Actions, action => action.Kind == SimulationActorActionKind.AdjustEdgeCapacity);
    }

    [Fact]
    public void Permission_NoRules_FallsBackToLegacyCapabilityBehavior()
    {
        var source = BuildNetwork();
        var capability = SimulationActorCapabilityCatalog.ForKind("firm", SimulationActorKind.Firm);
        capability.AllowAllTrafficTypes = false;
        capability.AllowedTrafficTypes = ["Food"];
        var actor = new SimulationActorState
        {
            Id = "firm",
            Kind = SimulationActorKind.Firm,
            IsEnabled = true,
            Cash = 100,
            Capability = capability
        };

        var (_, outcomes) = new SimulationActorActionApplier().Apply(source, [
            new SimulationActorAction { Id = "legacy-deny", ActorId = "firm", Kind = SimulationActorActionKind.AdjustProduction, TargetNodeId = "producer", TrafficType = "Wool", DeltaValue = 10 }
        ], BuildActorMap(actor), new Dictionary<string, double>());

        Assert.False(outcomes.Single().Applied);
        Assert.Contains("traffic type", outcomes.Single().Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BudgetedActor_CanAdjustProductionAcrossMultipleTicks()
    {
        var network = BuildHighThroughputNetwork();
        var coordinator = new SimulationActorCoordinator();
        var actor = new SimulationActorState
        {
            Id = "firm",
            Name = "Firm",
            Kind = SimulationActorKind.Firm,
            Objective = SimulationActorObjective.MaximiseProfit,
            Budget = 100,
            Cash = 4,
            Capability = new SimulationActorCapability
            {
                ActorId = "firm",
                AllowedActionKinds = [SimulationActorActionKind.AdjustProduction],
                Permissions =
                [
                    new SimulationActorPermission
                    {
                        ActionKind = SimulationActorActionKind.AdjustProduction,
                        TrafficType = "Food",
                        NodeId = "producer",
                        IsAllowed = true
                    }
                ]
            }
        };

        for (var tick = 0; tick < 3; tick++)
        {
            var step = coordinator.StepActorsOnce(network, [actor], tick);
            Assert.Contains(step.ActionOutcomes, outcome =>
                outcome.Applied &&
                outcome.Action.Kind == SimulationActorActionKind.AdjustProduction &&
                outcome.Action.TargetNodeId == "producer");
            network = step.NetworkAfterStep;
        }

        var production = network.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single(profile => profile.TrafficType == "Food").Production;
        Assert.True(production > 130d);
    }

    [Fact]
    public void BudgetedActor_CanApplyOtherCostedActionsAcrossMultipleTicks()
    {
        var network = BuildDraftNetwork();
        var coordinator = new SimulationActorCoordinator();
        var actor = new SimulationActorState
        {
            Id = "gov",
            Name = "Government",
            Kind = SimulationActorKind.Government,
            Objective = SimulationActorObjective.StabiliseNetwork,
            Budget = 2,
            Cash = 2,
            Capability = SimulationActorCapabilityCatalog.ForKind("gov", SimulationActorKind.Government)
        };

        for (var tick = 0; tick < 3; tick++)
        {
            var step = coordinator.StepActorsOnce(network, [actor], tick);
            Assert.Contains(step.ActionOutcomes, outcome =>
                outcome.Applied &&
                outcome.Action.Kind == SimulationActorActionKind.SubsidiseCapacity &&
                outcome.Action.TargetEdgeId == "draft-edge");
            network = step.NetworkAfterStep;
        }

        Assert.True(network.Edges.Single(edge => edge.Id == "draft-edge").Capacity > 34d);
    }

    [Fact]
    public void AddPermissionRule_AddsGranularNodeRule()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        vm.AddFirmActorCommand.Execute(null);
        var actor = Assert.IsType<SimulationActorState>(vm.SelectedSimulationActor);

        vm.AddPermissionRuleCommand.Execute(null);
        var row = Assert.Single(vm.ActorPermissionRows);
        row.ActionKind = SimulationActorActionKind.AdjustProduction;
        row.Scope = ActorPermissionScope.Node;
        row.TargetId = "producer";

        var permission = Assert.Single(actor.Capability.Permissions);
        Assert.Equal(SimulationActorActionKind.AdjustProduction, permission.ActionKind);
        Assert.Equal("producer", permission.NodeId);
        Assert.Null(permission.EdgeId);
    }

    [Fact]
    public void RemovePermissionRule_RemovesGranularRule()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        vm.AddFirmActorCommand.Execute(null);
        var actor = Assert.IsType<SimulationActorState>(vm.SelectedSimulationActor);

        vm.AddPermissionRuleCommand.Execute(null);
        var row = Assert.Single(vm.ActorPermissionRows);

        vm.RemovePermissionRuleCommand.Execute(row);

        Assert.Empty(actor.Capability.Permissions);
        Assert.Empty(vm.ActorPermissionRows);
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

    [Fact]
    public void AgentTools_AreOffByDefault_AndCanBeEnabled()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        Assert.False(vm.ShowAgentTools);
        vm.ToggleAgentToolsCommand.Execute(null);
        Assert.True(vm.ShowAgentTools);
    }

    [Fact]
    public void AgentSearch_FiltersActorListByNameOrKind()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        vm.AddFirmActorCommand.Execute(null);
        vm.AddGovernmentActorCommand.Execute(null);

        vm.AgentSearchText = "government";
        Assert.Single(vm.FilteredSimulationActors);
        Assert.Equal(SimulationActorKind.Government, vm.FilteredSimulationActors[0].Kind);

        vm.AgentSearchText = string.Empty;
        Assert.Equal(vm.SimulationActors.Count, vm.FilteredSimulationActors.Count);
    }

    [Fact]
    public void RunningActorStep_AppliesActionsAndRecordsOutcomes()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        vm.AddFirmActorCommand.Execute(null);

        vm.RunActorStepCommand.Execute(null);

        Assert.NotEmpty(vm.ActorDecisions);
        Assert.Contains(vm.ActorActionOutcomes, outcome => outcome.AppliedState == "Applied");
        Assert.NotEmpty(vm.AgentLog.Entries);

        var path = Path.GetTempFileName();
        try
        {
            vm.SaveNetwork(path);
            var loaded = new MedWNetworkSim.App.Services.NetworkFileService().Load(path);
            var producerProfile = loaded.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single(profile => profile.TrafficType == "Food");
            Assert.True(producerProfile.Production > 120d || producerProfile.UnitPrice > 1d);
            Assert.NotEmpty(loaded.AgentActionLogs);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MainTimelineStep_RunsAgentsBeforeAdvancingPeriod()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        vm.AddFirmActorCommand.Execute(null);

        vm.StepCommand.Execute(null);

        Assert.Equal(1, vm.ActorTick);
        Assert.NotEmpty(vm.ActorDecisions);
        Assert.Contains(vm.ActorActionOutcomes, outcome => outcome.AppliedState == "Applied");
        Assert.NotEmpty(vm.AgentLog.Entries);

        var path = Path.GetTempFileName();
        try
        {
            vm.SaveNetwork(path);
            var loaded = new MedWNetworkSim.App.Services.NetworkFileService().Load(path);
            var producerProfile = loaded.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single(profile => profile.TrafficType == "Food");
            Assert.True(producerProfile.UnitPrice > 1d);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResetTimeline_RevertsAgentMutationsToPreAgentNetwork()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        vm.AddFirmActorCommand.Execute(null);

        vm.StepCommand.Execute(null);
        Assert.Equal(1, vm.ActorTick);

        vm.ResetTimelineCommand.Execute(null);

        Assert.Equal(0, vm.ActorTick);
        Assert.Empty(vm.ActorDecisions);
        Assert.Empty(vm.ActorActionOutcomes);
        Assert.Empty(vm.AgentLog.Entries);

        var path = Path.GetTempFileName();
        try
        {
            vm.SaveNetwork(path);
            var loaded = new MedWNetworkSim.App.Services.NetworkFileService().Load(path);
            Assert.Single(loaded.Actors);
            var producerProfile = loaded.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single(profile => profile.TrafficType == "Food");
            Assert.Equal(120d, producerProfile.Production);
            Assert.Equal(1d, producerProfile.UnitPrice);
            Assert.Empty(loaded.AgentActionLogs);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResetTimeline_AfterReload_RevertsPersistedAgentMutationsToPreAgentNetwork()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        vm.AddFirmActorCommand.Execute(null);
        vm.StepCommand.Execute(null);

        var path = Path.GetTempFileName();
        var resetPath = Path.GetTempFileName();
        try
        {
            vm.SaveNetwork(path);
            var saved = new MedWNetworkSim.App.Services.NetworkFileService().Load(path);
            Assert.NotNull(saved.PreAgentMutationNetwork);
            var mutatedProducerProfile = saved.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single(profile => profile.TrafficType == "Food");
            Assert.True(mutatedProducerProfile.UnitPrice > 1d);
            var baselineProducerProfile = saved.PreAgentMutationNetwork!.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single(profile => profile.TrafficType == "Food");
            Assert.Equal(1d, baselineProducerProfile.UnitPrice);

            var reloadedVm = new WorkspaceViewModel();
            reloadedVm.OpenNetwork(path);
            reloadedVm.ResetTimelineCommand.Execute(null);
            reloadedVm.SaveNetwork(resetPath);
            var reset = new MedWNetworkSim.App.Services.NetworkFileService().Load(resetPath);
            var resetProducerProfile = reset.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single(profile => profile.TrafficType == "Food");

            Assert.Equal(1d, resetProducerProfile.UnitPrice);
            Assert.Null(reset.PreAgentMutationNetwork);
            Assert.Empty(reset.AgentActionLogs);
            Assert.Equal(0, reset.ActorTick);
        }
        finally
        {
            File.Delete(path);
            File.Delete(resetPath);
        }
    }

    [Fact]
    public void TrafficReports_ShowProductionAndConsumptionPrices()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();

        vm.SimulateCommand.Execute(null);

        var food = Assert.Single(vm.TrafficReports, row => row.TrafficType == "Food");
        Assert.Equal("0:1", food.PriceSummary);
    }

    [Fact]
    public void Actors_MakeVisibleChanges_OnPermittedAssets()
    {
        var coordinator = new SimulationActorCoordinator();

        var firmStep = coordinator.StepActorsOnce(BuildDraftNetwork(), [
            new SimulationActorState
            {
                Id = "firm",
                Name = "Firm",
                Kind = SimulationActorKind.Firm,
                Objective = SimulationActorObjective.MaximiseProfit,
                Cash = 100,
                Budget = 100,
                Capability = SimulationActorCapabilityCatalog.ForKind("firm", SimulationActorKind.Firm)
            }
        ]);
        Assert.True(firmStep.NetworkAfterStep.Nodes.Single(node => node.Id == "draft-a").TrafficProfiles.Single().Production > 0d);

        var governmentStep = coordinator.StepActorsOnce(BuildDraftNetwork(), [
            new SimulationActorState
            {
                Id = "gov",
                Name = "Government",
                Kind = SimulationActorKind.Government,
                Objective = SimulationActorObjective.StabiliseNetwork,
                Cash = 100,
                Budget = 100,
                Capability = SimulationActorCapabilityCatalog.ForKind("gov", SimulationActorKind.Government)
            }
        ]);
        Assert.True(governmentStep.NetworkAfterStep.Edges.Single(edge => edge.Id == "draft-edge").Capacity > 30d);

        var logisticsStep = coordinator.StepActorsOnce(BuildDraftNetwork(), [
            new SimulationActorState
            {
                Id = "logistics",
                Name = "Logistics",
                Kind = SimulationActorKind.LogisticsPlanner,
                Objective = SimulationActorObjective.MinimiseMovementCost,
                Cash = 100,
                Budget = 100,
                Capability = SimulationActorCapabilityCatalog.ForKind("logistics", SimulationActorKind.LogisticsPlanner)
            }
        ]);
        Assert.True(logisticsStep.NetworkAfterStep.Edges.Single(edge => edge.Id == "draft-edge").Cost < 1d);
    }

    [Fact]
    public void DeletingNode_RemovesActorPermissionNodeReference()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        vm.AddFirmActorCommand.Execute(null);
        var actor = Assert.IsType<SimulationActorState>(vm.SelectedSimulationActor);
        actor.Capability.Permissions.Add(new SimulationActorPermission
        {
            ActionKind = SimulationActorActionKind.AdjustProduction,
            NodeId = "producer"
        });

        vm.DeleteNodeById("producer");

        Assert.DoesNotContain(actor.Capability.Permissions, permission => permission.NodeId == "producer");
    }

    [Fact]
    public void DeletingEdge_RemovesActorPermissionEdgeReference()
    {
        var vm = BuildWorkspaceViewModelWithNetwork();
        vm.AddFirmActorCommand.Execute(null);
        var actor = Assert.IsType<SimulationActorState>(vm.SelectedSimulationActor);
        actor.Capability.Permissions.Add(new SimulationActorPermission
        {
            ActionKind = SimulationActorActionKind.AdjustEdgeCapacity,
            EdgeId = "edge-a"
        });

        vm.DeleteRouteById("edge-a");

        Assert.DoesNotContain(actor.Capability.Permissions, permission => permission.EdgeId == "edge-a");
    }

    private static NetworkModel BuildNetwork()
    {
        var layerId = Guid.NewGuid();
        return new NetworkModel
        {
            Name = "Actors Test",
            Layers = [new NetworkLayerModel { Id = layerId, Name = "Physical", Type = NetworkLayerType.Physical, Order = 0 }],
            TrafficTypes = [new TrafficTypeDefinition { Name = "Food", RoutingPreference = RoutingPreference.Cost, AllocationMode = AllocationMode.GreedyBestRoute }],
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

    private static NetworkModel BuildDraftNetwork()
    {
        var layerId = Guid.NewGuid();
        return new NetworkModel
        {
            Name = "Draft Actors Test",
            Layers = [new NetworkLayerModel { Id = layerId, Name = "Physical", Type = NetworkLayerType.Physical, Order = 0 }],
            TrafficTypes = [new TrafficTypeDefinition { Name = "Food", RoutingPreference = RoutingPreference.Cost, AllocationMode = AllocationMode.GreedyBestRoute }],
            Nodes =
            [
                new NodeModel
                {
                    Id = "draft-a",
                    Name = "Draft A",
                    LayerId = layerId,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", CanTransship = true }]
                },
                new NodeModel
                {
                    Id = "draft-b",
                    Name = "Draft B",
                    LayerId = layerId,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", CanTransship = true }]
                }
            ],
            Edges =
            [
                new EdgeModel
                {
                    Id = "draft-edge",
                    FromNodeId = "draft-a",
                    ToNodeId = "draft-b",
                    Capacity = 30,
                    Cost = 1,
                    Time = 1,
                    LayerId = layerId
                }
            ]
        };
    }

    private static NetworkModel BuildLowUtilisationUnmetDemandNetwork()
    {
        var network = BuildNetwork();
        network.Edges.Single(edge => edge.Id == "edge-a").Id = "blocked-capacity";
        network.Edges.Single(edge => edge.Id == "blocked-capacity").Capacity = 0d;
        return network;
    }

    private static NetworkModel BuildHighThroughputNetwork()
    {
        var network = BuildNetwork();
        network.Edges.Single(edge => edge.Id == "edge-a").Capacity = 1000;
        return network;
    }

    private static NetworkModel BuildProductionPressureNetwork(double production, double consumption)
    {
        var network = BuildHighThroughputNetwork();
        network.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single().Production = production;
        network.Nodes.Single(node => node.Id == "consumer").TrafficProfiles.Single().Consumption = consumption;
        return network;
    }

    private static SimulationActorState BuildProductionOnlyFirm() => new()
    {
        Id = "firm-producer",
        Name = "Producer",
        Kind = SimulationActorKind.Firm,
        Objective = SimulationActorObjective.MaximiseProfit,
        ControlledNodeIds = ["producer"],
        Cash = 100d,
        Budget = 100d,
        Capability = new SimulationActorCapability
        {
            ActorId = "firm-producer",
            AllowedActionKinds = [SimulationActorActionKind.AdjustProduction],
            Permissions =
            [
                new SimulationActorPermission
                {
                    ActionKind = SimulationActorActionKind.AdjustProduction,
                    TrafficType = "Food",
                    NodeId = "producer",
                    IsAllowed = true
                }
            ]
        }
    };

    private static Dictionary<string, SimulationActorState> BuildActorMap(params SimulationActorState[] actors) =>
        actors.ToDictionary(actor => actor.Id, StringComparer.OrdinalIgnoreCase);

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

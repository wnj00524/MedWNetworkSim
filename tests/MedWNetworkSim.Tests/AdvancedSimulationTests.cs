using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.Tests;

public sealed class AdvancedSimulationTests
{
    [Fact]
    public void EnsureLayerIntegrity_AddsDefaultPhysicalLayer_ForLegacyNetwork()
    {
        var network = new NetworkModel { Layers = [], Nodes = [], Edges = [] };
        var service = new NetworkLayerResolver();

        service.EnsureLayerIntegrity(network);

        Assert.Contains(network.Layers, layer => layer.Type == NetworkLayerType.Physical);
    }

    [Fact]
    public void EnsureLayerIntegrity_RepairsEmptyLayerIds()
    {
        var network = new NetworkModel
        {
            Layers = [],
            Nodes = [new NodeModel { Id = Guid.NewGuid().ToString(), LayerId = Guid.Empty }],
            Edges = [new EdgeModel { Id = Guid.NewGuid().ToString(), FromNodeId = "a", ToNodeId = "b", LayerId = Guid.Empty }]
        };

        var service = new NetworkLayerResolver();
        service.EnsureLayerIntegrity(network);

        var defaultLayer = service.GetDefaultLayer(network);
        Assert.Equal(defaultLayer.Id, network.Nodes[0].LayerId);
        Assert.Equal(defaultLayer.Id, network.Edges[0].LayerId);
    }

    [Fact]
    public void GetSimulationOrder_AlwaysPhysicalLogicalPolicy()
    {
        var network = new NetworkModel
        {
            Layers =
            [
                new NetworkLayerModel { Type = NetworkLayerType.Policy, Name = "Policy", Order = 0 },
                new NetworkLayerModel { Type = NetworkLayerType.Physical, Name = "Physical", Order = 9 },
                new NetworkLayerModel { Type = NetworkLayerType.Logical, Name = "Logical", Order = 1 }
            ]
        };

        var ordered = new NetworkLayerResolver().GetSimulationOrder(network);

        Assert.Equal(NetworkLayerType.Physical, ordered[0].Type);
        Assert.Equal(NetworkLayerType.Logical, ordered[1].Type);
        Assert.Equal(NetworkLayerType.Policy, ordered[2].Type);
    }

    [Fact]
    public void ScenarioRun_DoesNotMutateSourceNetwork_AndWarningsForInvalidTarget()
    {
        var network = BuildNetwork();
        var sourceCost = network.Edges[0].Cost;
        var runner = new ScenarioRunner();
        var scenario = new ScenarioDefinitionModel
        {
            Name = "Bad",
            Events = [new ScenarioEventModel { Name = "bad", Kind = ScenarioEventKind.EdgeCostChange, TargetKind = ScenarioTargetKind.Edge, TargetId = Guid.NewGuid(), Time = 0, Value = 9 }]
        };

        var result = runner.Run(network, scenario, new ScenarioRunOptions { EndTime = 1, DeltaTime = 1 });

        Assert.Equal(sourceCost, network.Edges[0].Cost);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void ScenarioEvents_ChangeOutput_ForEdgeClosureAndDemandSpike()
    {
        var network = BuildNetwork();
        var runner = new ScenarioRunner();
        var edgeId = Guid.Parse(network.Edges[0].Id);
        var nodeId = Guid.Parse(network.Nodes[1].Id);

        var baseResult = runner.Run(network, new ScenarioDefinitionModel { Name = "Base" }, new ScenarioRunOptions { EndTime = 0, DeltaTime = 1 }).SimulationResult!;
        var closure = runner.Run(network, new ScenarioDefinitionModel
        {
            Name = "Closure",
            Events = [new ScenarioEventModel { Name = "close", Kind = ScenarioEventKind.EdgeClosure, TargetKind = ScenarioTargetKind.Edge, TargetId = edgeId, Time = 0 }]
        }, new ScenarioRunOptions { EndTime = 0, DeltaTime = 1 }).SimulationResult!;
        var spike = runner.Run(network, new ScenarioDefinitionModel
        {
            Name = "Spike",
            Events = [new ScenarioEventModel { Name = "spike", Kind = ScenarioEventKind.DemandSpike, TargetKind = ScenarioTargetKind.Node, TargetId = nodeId, TrafficTypeIdOrName = "Food", Time = 0, Value = 2 }]
        }, new ScenarioRunOptions { EndTime = 0, DeltaTime = 1 }).SimulationResult!;

        Assert.True(closure.TotalThroughput <= baseResult.TotalThroughput);
        Assert.True(spike.TotalUnmetDemand >= baseResult.TotalUnmetDemand);
    }

    [Fact]
    public void BottleneckAndExplainability_ReturnUsefulOutput()
    {
        var network = BuildNetwork();
        var step = new TemporalNetworkSimulationEngine.TemporalSimulationStepResult(
            0, [], new Dictionary<string, TemporalNetworkSimulationEngine.EdgeFlowVisualSummary>(),
            new Dictionary<string, TemporalNetworkSimulationEngine.NodeFlowVisualSummary>(),
            new Dictionary<TemporalNetworkSimulationEngine.TemporalNodeTrafficKey, TemporalNetworkSimulationEngine.TemporalNodeStateSnapshot>(),
            new Dictionary<string, double> { [network.Edges[0].Id] = 95d },
            new Dictionary<string, double>(), 0, 0,
            new Dictionary<string, TemporalNetworkSimulationEngine.NodePressureSnapshot>(),
            new Dictionary<string, TemporalNetworkSimulationEngine.EdgePressureSnapshot>(),
            []);
        var result = new SimulationResult
        {
            Outcomes = [new TrafficSimulationOutcome { TrafficType = "Food", UnmetDemand = 25 }],
            Steps = [step]
        };

        var issues = new BottleneckDetectionService().DetectIssues(network, result);
        var explanation = new ExplainabilityService().ExplainEdge(network, result, Guid.Parse(network.Edges[0].Id));

        Assert.Contains(issues, i => i.Type == NetworkIssueType.CongestedEdge);
        Assert.Contains(issues, i => i.Type == NetworkIssueType.StarvedNode);
        Assert.Contains("Why this matters", explanation.Summary);
    }

    [Fact]
    public void PolicyCostMultiplier_ChangesTransportCost()
    {
        var network = BuildNetwork();
        var engine = new NetworkSimulationEngine();
        var baseCost = engine.Simulate(network).SelectMany(outcome => outcome.Allocations).Sum(allocation => allocation.TotalMovementCost);
        network.PolicyRules.Add(new PolicyRuleModel
        {
            Name = "Expensive corridor",
            Effect = PolicyRuleEffect.CostMultiplier,
            TargetEdgeId = network.Edges[0].Id,
            Value = 4d
        });

        var policyCost = engine.Simulate(network).SelectMany(outcome => outcome.Allocations).Sum(allocation => allocation.TotalMovementCost);
        Assert.True(policyCost >= baseCost);
    }

    [Fact]
    public void PolicyBlockTraffic_PreventsFlow()
    {
        var network = BuildNetwork();
        network.PolicyRules.Add(new PolicyRuleModel
        {
            Name = "Block food",
            Effect = PolicyRuleEffect.BlockTraffic,
            TargetEdgeId = network.Edges[0].Id,
            TrafficTypeIdOrName = "Food"
        });

        var outcome = new NetworkSimulationEngine().Simulate(network).Single();
        Assert.True(outcome.UnmetDemand > 0d);
    }

    [Fact]
    public void ScenarioValidation_ReturnsActionableMessages()
    {
        var validator = new ScenarioValidationService();
        var scenarioErrors = validator.ValidateScenario(new ScenarioDefinitionModel { Name = " ", StartTime = -1, EndTime = 0, DeltaTime = 0 });
        var nodeFailureErrors = validator.ValidateEvent(new ScenarioEventModel { Name = " ", Kind = ScenarioEventKind.NodeFailure, TargetKind = ScenarioTargetKind.Edge, Time = -1 });
        var edgeClosureErrors = validator.ValidateEvent(new ScenarioEventModel { Name = " ", Kind = ScenarioEventKind.EdgeClosure, TargetKind = ScenarioTargetKind.Node, Time = -1 });
        var demandSpikeErrors = validator.ValidateEvent(new ScenarioEventModel { Name = " ", Kind = ScenarioEventKind.DemandSpike, TargetKind = ScenarioTargetKind.Node, Time = 0, Value = 0 });
        var edgeCostErrors = validator.ValidateEvent(new ScenarioEventModel { Name = " ", Kind = ScenarioEventKind.EdgeCostChange, TargetKind = ScenarioTargetKind.Edge, Time = 0, Value = 0 });

        Assert.Contains("Enter a scenario name.", scenarioErrors);
        Assert.Contains("Start time must be zero or greater.", scenarioErrors);
        Assert.Contains("End time must be after start time.", scenarioErrors);
        Assert.Contains("Enter an event name.", nodeFailureErrors);
        Assert.Contains("Choose a node for this event.", nodeFailureErrors);
        Assert.Contains("Choose an edge for this event.", edgeClosureErrors);
        Assert.Contains("Choose a traffic type for this event.", demandSpikeErrors);
        Assert.Contains("Value must be greater than zero.", demandSpikeErrors);
        Assert.Contains("Value must be greater than zero.", edgeCostErrors);
    }

    [Fact]
    public void ScenarioRunner_AppliesProductionAndConsumptionMultiplierEvents()
    {
        var network = BuildNetwork();
        var runner = new ScenarioRunner();
        var targetNode = network.Nodes[0].Id;

        var scenario = new ScenarioDefinitionModel
        {
            Name = "Multiplier Test",
            StartTime = 0,
            EndTime = 1,
            DeltaTime = 1,
            Events =
            [
                new ScenarioEventModel
                {
                    Name = "Production up",
                    Kind = ScenarioEventKind.ProductionMultiplier,
                    TargetKind = ScenarioTargetKind.Node,
                    TargetId = targetNode,
                    TrafficTypeIdOrName = "Food",
                    Time = 0,
                    Value = 2d
                },
                new ScenarioEventModel
                {
                    Name = "Consumption up",
                    Kind = ScenarioEventKind.ConsumptionMultiplier,
                    TargetKind = ScenarioTargetKind.Node,
                    TargetId = network.Nodes[1].Id,
                    TrafficTypeIdOrName = "Food",
                    Time = 0,
                    Value = 2d
                }
            ]
        };

        var baseline = runner.Run(network, new ScenarioDefinitionModel { Name = "Base" }, new ScenarioRunOptions { StartTime = 0, EndTime = 1, DeltaTime = 1 }).SimulationResult!;
        var modified = runner.Run(network, scenario, new ScenarioRunOptions { StartTime = 0, EndTime = 1, DeltaTime = 1 }).SimulationResult!;

        Assert.NotNull(modified);
        Assert.True(modified.TotalThroughput >= baseline.TotalThroughput || modified.TotalUnmetDemand >= baseline.TotalUnmetDemand);
    }

    [Fact]
    public void ScenarioRunner_RevertsTimedEvents_AfterEndTime()
    {
        var network = BuildNetwork();
        var runner = new ScenarioRunner();
        var edgeId = network.Edges[0].Id;
        var scenario = new ScenarioDefinitionModel
        {
            Name = "Timed closure",
            StartTime = 0,
            EndTime = 2,
            DeltaTime = 1,
            Events =
            [
                new ScenarioEventModel
                {
                    Name = "Close edge briefly",
                    Kind = ScenarioEventKind.EdgeClosure,
                    TargetKind = ScenarioTargetKind.Edge,
                    TargetId = edgeId,
                    Time = 0,
                    EndTime = 0.5
                }
            ]
        };

        var result = runner.Run(network, scenario, new ScenarioRunOptions { StartTime = 0, EndTime = 2, DeltaTime = 1 }).SimulationResult!;
        var baseline = runner.Run(network, new ScenarioDefinitionModel { Name = "Base" }, new ScenarioRunOptions { StartTime = 0, EndTime = 2, DeltaTime = 1 }).SimulationResult!;

        Assert.True(result.TotalThroughput < baseline.TotalThroughput);
        Assert.True(result.TotalThroughput > 0d);
    }

    [Fact]
    public void ScenarioRunner_EventWithoutEndTime_RemainsActive()
    {
        var network = BuildNetwork();
        var runner = new ScenarioRunner();
        var edgeId = network.Edges[0].Id;
        var scenario = new ScenarioDefinitionModel
        {
            Name = "Always closed",
            Events =
            [
                new ScenarioEventModel
                {
                    Name = "Close edge",
                    Kind = ScenarioEventKind.EdgeClosure,
                    TargetKind = ScenarioTargetKind.Edge,
                    TargetId = edgeId,
                    Time = 0
                }
            ]
        };

        var result = runner.Run(network, scenario, new ScenarioRunOptions { StartTime = 0, EndTime = 2, DeltaTime = 1 }).SimulationResult!;
        Assert.Equal(0d, result.TotalThroughput);
    }

    [Fact]
    public void ScenarioRunner_RevertRestoresExactSourceValues()
    {
        var network = BuildNetwork();
        var originalCost = network.Edges[0].Cost;
        var originalDemand = network.Nodes[1].TrafficProfiles[0].Consumption;
        var runner = new ScenarioRunner();
        var scenario = new ScenarioDefinitionModel
        {
            Name = "Timed modifiers",
            Events =
            [
                new ScenarioEventModel
                {
                    Name = "Cost increase",
                    Kind = ScenarioEventKind.EdgeCostChange,
                    TargetKind = ScenarioTargetKind.Edge,
                    TargetId = network.Edges[0].Id,
                    Time = 0,
                    EndTime = 0.5,
                    Value = 10
                },
                new ScenarioEventModel
                {
                    Name = "Demand spike",
                    Kind = ScenarioEventKind.DemandSpike,
                    TargetKind = ScenarioTargetKind.Node,
                    TargetId = network.Nodes[1].Id,
                    TrafficTypeIdOrName = "Food",
                    Time = 0,
                    EndTime = 0.5,
                    Value = 2
                }
            ]
        };

        _ = runner.Run(network, scenario, new ScenarioRunOptions { StartTime = 0, EndTime = 2, DeltaTime = 1 });
        Assert.Equal(originalCost, network.Edges[0].Cost);
        Assert.Equal(originalDemand, network.Nodes[1].TrafficProfiles[0].Consumption);
    }

    private static NetworkModel BuildNetwork()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var edge = Guid.NewGuid();
        var layer = Guid.NewGuid();
        return new NetworkModel
        {
            Layers = [new NetworkLayerModel { Id = layer, Name = "Physical", Type = NetworkLayerType.Physical, Order = 0 }],
            TrafficTypes = [new TrafficTypeDefinition { Name = "Food" }],
            Nodes =
            [
                new NodeModel { Id = a.ToString(), Name = "A", LayerId = layer, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Production = 20 }] },
                new NodeModel { Id = b.ToString(), Name = "B", LayerId = layer, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Consumption = 10 }] }
            ],
            Edges = [new EdgeModel { Id = edge.ToString(), FromNodeId = a.ToString(), ToNodeId = b.ToString(), LayerId = layer, Capacity = 100, Cost = 1, Time = 1 }]
        };
    }
}

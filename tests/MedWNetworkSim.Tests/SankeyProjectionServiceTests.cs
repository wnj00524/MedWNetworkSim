using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.App.VisualAnalytics;
using MedWNetworkSim.App.VisualAnalytics.Sankey;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class SankeyProjectionServiceTests
{
    [Fact]
    public void Build_GroupsFlowsBySourceDestinationAndTrafficType()
    {
        var network = new NetworkModel
        {
            Nodes = [new NodeModel { Id = "a", Name = "A" }, new NodeModel { Id = "b", Name = "B" }]
        };
        var outcome = new TrafficSimulationOutcome
        {
            TrafficType = "Med",
            Allocations =
            [
                new RouteAllocation { ProducerNodeId = "a", ConsumerNodeId = "b", Quantity = 3, TrafficType = "Med" },
                new RouteAllocation { ProducerNodeId = "a", ConsumerNodeId = "b", Quantity = 5, TrafficType = "Med" }
            ]
        };

        var model = new SankeyProjectionService().Build(new VisualAnalyticsSnapshot { Network = network, TrafficOutcomes = [outcome], ConsumerCosts = [], Period = 0 });

        var link = Assert.Single(model.Links.Where(l => !l.IsUnmetDemand));
        Assert.Equal(8d, link.Value, 6);
    }

    [Fact]
    public void Build_AppliesTrafficTypeFilter()
    {
        var snapshot = new VisualAnalyticsSnapshot
        {
            Network = new NetworkModel { Nodes = [new NodeModel { Id = "a", Name = "A" }, new NodeModel { Id = "b", Name = "B" }] },
            TrafficOutcomes =
            [
                new TrafficSimulationOutcome { TrafficType = "Food", Allocations = [new RouteAllocation { ProducerNodeId = "a", ConsumerNodeId = "b", Quantity = 2, TrafficType = "Food" }] },
                new TrafficSimulationOutcome { TrafficType = "Water", Allocations = [new RouteAllocation { ProducerNodeId = "a", ConsumerNodeId = "b", Quantity = 7, TrafficType = "Water" }] }
            ],
            ConsumerCosts = [],
            Period = 0
        };

        var model = new SankeyProjectionService().Build(snapshot, new SankeyProjectionOptions { TrafficTypeFilter = "Water" });

        Assert.All(model.Links.Where(l => !l.IsUnmetDemand), link => Assert.Equal("Water", link.TrafficType));
    }

    [Fact]
    public void Build_AddsUnmetDemandSink_WhenEnabled()
    {
        var model = new SankeyProjectionService().Build(new VisualAnalyticsSnapshot
        {
            Network = new NetworkModel { Nodes = [new NodeModel { Id = "a", Name = "A" }] },
            TrafficOutcomes = [new TrafficSimulationOutcome { TrafficType = "Food", UnmetDemand = 4, Allocations = [new RouteAllocation { ProducerNodeId = "a", ConsumerNodeId = "a", Quantity = 1, TrafficType = "Food" }] }],
            ConsumerCosts = [],
            Period = 0
        });

        Assert.Contains(model.Nodes, node => node.Kind == SankeyNodeKind.UnmetDemandSink);
        Assert.Contains(model.Links, link => link.IsUnmetDemand);
    }

    [Fact]
    public void Build_CollapsesMinorFlows_WhenConfigured()
    {
        var snapshot = new VisualAnalyticsSnapshot
        {
            Network = new NetworkModel { Nodes = [new NodeModel { Id = "a", Name = "A" }, new NodeModel { Id = "b", Name = "B" }, new NodeModel { Id = "c", Name = "C" }] },
            TrafficOutcomes = [new TrafficSimulationOutcome { TrafficType = "Food", Allocations = [new RouteAllocation { ProducerNodeId = "a", ConsumerNodeId = "b", Quantity = 100, TrafficType = "Food" }, new RouteAllocation { ProducerNodeId = "a", ConsumerNodeId = "c", Quantity = 1, TrafficType = "Food" }] }],
            ConsumerCosts = [],
            Period = 0
        };

        var model = new SankeyProjectionService().Build(snapshot, new SankeyProjectionOptions { MinorFlowThresholdRatio = 0.05d, CollapseMinorFlows = true });

        Assert.Contains(model.Nodes, node => node.Kind == SankeyNodeKind.CollapsedOther);
    }
}

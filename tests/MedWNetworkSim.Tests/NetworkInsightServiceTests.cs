using MedWNetworkSim.App.Insights;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.App.VisualAnalytics;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class NetworkInsightServiceTests
{
    [Fact]
    public void Generate_DetectsUnmetDemand()
    {
        var insights = CreateService().Generate(CreateSnapshot(outcomes: [new TrafficSimulationOutcome { TrafficType = "Food", UnmetDemand = 5 }]));
        Assert.Contains(insights, insight => insight.Category == InsightCategory.UnmetDemand);
    }

    [Fact]
    public void Generate_DetectsCapacityBottleneck()
    {
        var edge = new EdgeModel { Id = "e1", FromNodeId = "n1", ToNodeId = "n2", Capacity = 10 };
        var insights = CreateService().Generate(CreateSnapshot(
            edges: [edge],
            outcomes: [new TrafficSimulationOutcome { TrafficType = "Food", Allocations = [new RouteAllocation { ProducerNodeId = "n1", ConsumerNodeId = "n2", Quantity = 10, PathEdgeIds = ["e1"] }] }]));
        Assert.Contains(insights, insight => insight.Category == InsightCategory.Capacity && insight.TargetEdgeId == "e1");
    }

    [Fact]
    public void Generate_DetectsRouteRestriction()
    {
        var insights = CreateService().Generate(CreateSnapshot(outcomes: [new TrafficSimulationOutcome { TrafficType = "Med", NoPermittedPathDemand = 3 }]));
        Assert.Contains(insights, insight => insight.Category == InsightCategory.Restriction);
    }

    [Fact]
    public void Generate_DetectsDisconnectedComponents()
    {
        var nodes = [new NodeModel { Id = "n1", Name = "N1" }, new NodeModel { Id = "n2", Name = "N2" }];
        var insights = CreateService().Generate(CreateSnapshot(nodes: nodes));
        Assert.Contains(insights, insight => insight.Category == InsightCategory.Connectivity);
    }

    private static NetworkInsightService CreateService() => new();

    private static VisualAnalyticsSnapshot CreateSnapshot(
        IReadOnlyList<NodeModel>? nodes = null,
        IReadOnlyList<EdgeModel>? edges = null,
        IReadOnlyList<TrafficSimulationOutcome>? outcomes = null)
    {
        return new VisualAnalyticsSnapshot
        {
            Network = new NetworkModel
            {
                Nodes = nodes?.ToList() ?? [new NodeModel { Id = "n1", Name = "N1" }, new NodeModel { Id = "n2", Name = "N2" }],
                Edges = edges?.ToList() ?? [new EdgeModel { Id = "e1", FromNodeId = "n1", ToNodeId = "n2" }]
            },
            TrafficOutcomes = outcomes ?? [],
            ConsumerCosts = [],
            Period = 0
        };
    }
}

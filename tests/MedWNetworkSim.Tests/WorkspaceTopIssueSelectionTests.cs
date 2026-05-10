using System.Reflection;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.Presentation;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class WorkspaceTopIssueSelectionTests
{
    [Fact]
    public void SelectTopIssue_Node_UsesStableNodeId_NotDisplayBreadcrumbText()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, BuildNetworkModel());

        var issue = new TopIssueViewModel
        {
            Title = "Node issue",
            Detail = "detail",
            TargetKind = TopIssueTargetKind.Node,
            NodeId = "node-1",
            NodeDisplayName = "Emergency Department",
            Breadcrumb = "Issue -> Node Emergency Department"
        };

        workspace.SelectTopIssueCommand.Execute(issue);

        Assert.Contains("node-1", workspace.Scene.Selection.SelectedNodeIds, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("node-1", workspace.InspectorNodeTargetId);
    }

    [Fact]
    public void SelectTopIssue_Edge_UsesStableEdgeId_NotBreadcrumbText()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, BuildNetworkModel());

        var issue = new TopIssueViewModel
        {
            Title = "Edge issue",
            Detail = "detail",
            TargetKind = TopIssueTargetKind.Edge,
            EdgeId = "edge-1",
            FromNodeName = "Ward A",
            ToNodeName = "Pharmacy",
            Breadcrumb = "Issue -> Ward A -> Pharmacy"
        };

        workspace.SelectTopIssueCommand.Execute(issue);

        Assert.Contains("edge-1", workspace.Scene.Selection.SelectedEdgeIds, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(WorkspaceMode.EdgeEditor, workspace.CurrentWorkspaceMode);
    }

    [Fact]
    public void Simulate_PopulatesPressureReports()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, BuildNodeIssueNetworkModel());

        workspace.StepCommand.Execute(null);

        Assert.NotNull(workspace.NodePressureReports);
        Assert.NotNull(workspace.RouteReports);
    }

    [Fact]
    public void Simulate_TimelineMetrics_UsesLatestTicks()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, BuildNodeIssueNetworkModel());

        var networkField = typeof(WorkspaceViewModel).GetField("network", BindingFlags.Instance | BindingFlags.NonPublic);
        var network = (NetworkModel)networkField!.GetValue(workspace)!;

        network.ActorMetrics = Enumerable.Range(1, 250).Select(i => new MedWNetworkSim.App.Agents.SimulationActorMetrics { Tick = i }).ToList();

        var refreshMethod = typeof(WorkspaceViewModel).GetMethod("RefreshDashboardSummaries", BindingFlags.Instance | BindingFlags.NonPublic);
        refreshMethod!.Invoke(workspace, null);

        Assert.Equal(240, workspace.TimelineMetrics.Count);
        Assert.Equal(250, workspace.TimelineMetrics.Last().Period);
        Assert.Equal(11, workspace.TimelineMetrics.First().Period);
    }

    private static NetworkModel BuildNetworkModel() => new()
    {
        Name = "Top issue test",
        Nodes =
        [
            new NodeModel { Id = "node-1", Name = "Emergency Department", X = 100d, Y = 120d },
            new NodeModel { Id = "node-2", Name = "Pharmacy", X = 400d, Y = 120d }
        ],
        Edges =
        [
            new EdgeModel { Id = "edge-1", FromNodeId = "node-1", ToNodeId = "node-2", Time = 1d, Cost = 1d, Capacity = 10d }
        ]
    };

    private static NetworkModel BuildNodeIssueNetworkModel() => new()
    {
        Name = "Node issue network",
        TrafficTypes = [new TrafficTypeDefinition { Name = "Supply" }],
        Nodes =
        [
            new NodeModel
            {
                Id = "producer",
                Name = "Producer",
                X = 100d,
                Y = 120d,
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Supply", Production = 5d }]
            },
            new NodeModel
            {
                Id = "consumer",
                Name = "Consumer",
                X = 400d,
                Y = 120d,
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Supply", Consumption = 30d }]
            }
        ],
        Edges =
        [
            new EdgeModel { Id = "supply-route", FromNodeId = "producer", ToNodeId = "consumer", Time = 1d, Cost = 1d, Capacity = 100d }
        ]
    };

    private static void LoadNetwork(WorkspaceViewModel workspace, NetworkModel model)
    {
        var loadMethod = typeof(WorkspaceViewModel).GetMethod("LoadNetwork", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(loadMethod);
        loadMethod!.Invoke(workspace, [model, "Loaded test network", null]);
    }
}

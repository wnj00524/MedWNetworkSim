using System;
using System.Linq;
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
            Breadcrumb = "Issue → Node Emergency Department"
        };

        workspace.SelectTopIssueCommand.Execute(issue);

        Assert.Contains("node-1", workspace.Scene.Selection.SelectedNodeIds, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("node-1", workspace.InspectorNodeTargetId);
        Assert.Equal("Issue → Node Emergency Department", workspace.SelectedIssueBreadcrumb);
        Assert.DoesNotContain("unavailable", workspace.StatusText, StringComparison.OrdinalIgnoreCase);
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
            Breadcrumb = "Issue → Ward A → Pharmacy"
        };

        workspace.SelectTopIssueCommand.Execute(issue);

        Assert.Contains("edge-1", workspace.Scene.Selection.SelectedEdgeIds, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(WorkspaceMode.EdgeEditor, workspace.CurrentWorkspaceMode);
        Assert.Equal("Issue → Ward A → Pharmacy", workspace.SelectedIssueBreadcrumb);
        Assert.DoesNotContain("unavailable", workspace.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Simulate_NodeIssue_AssignsNodeTargetMetadata()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, BuildNodeIssueNetworkModel());

        workspace.SimulateCommand.Execute(null);

        var nodeIssue = workspace.TopIssues.FirstOrDefault(issue => issue.TargetKind == TopIssueTargetKind.Node);
        Assert.NotNull(nodeIssue);
        Assert.False(string.IsNullOrWhiteSpace(nodeIssue!.NodeId));
        Assert.False(string.IsNullOrWhiteSpace(nodeIssue.NodeDisplayName));
        Assert.StartsWith("Issue → Node ", nodeIssue.Breadcrumb, StringComparison.Ordinal);
    }

    [Fact]
    public void Simulate_EdgeIssue_AssignsEdgeTargetMetadata()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, BuildEdgeIssueNetworkModel());

        workspace.SimulateCommand.Execute(null);

        var edgeIssue = workspace.TopIssues.FirstOrDefault(issue => issue.TargetKind == TopIssueTargetKind.Edge);
        Assert.NotNull(edgeIssue);
        Assert.False(string.IsNullOrWhiteSpace(edgeIssue!.EdgeId));
        Assert.False(string.IsNullOrWhiteSpace(edgeIssue.FromNodeName));
        Assert.False(string.IsNullOrWhiteSpace(edgeIssue.ToNodeName));
        Assert.StartsWith("Issue → Route ", edgeIssue.Breadcrumb, StringComparison.Ordinal);
    }

    [Fact]
    public void TopIssues_ClickableList_DoesNotContainUnspecifiedTargets()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, BuildNodeIssueNetworkModel());

        workspace.SimulateCommand.Execute(null);

        Assert.All(workspace.TopIssues, issue => Assert.NotEqual(TopIssueTargetKind.None, issue.TargetKind));
    }

    [Fact]
    public void Timeline_NodePressureReport_WithActionablePressure_PopulatesTopIssues()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, BuildNodeIssueNetworkModel());

        workspace.StepCommand.Execute(null);

        Assert.Contains(workspace.NodePressureReports, row => !string.Equals(row.PressureScore, "None", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(workspace.TopIssues);
    }

    [Fact]
    public void Timeline_RoutePressureReport_WithActionablePressure_PopulatesTopIssues()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, BuildEdgeIssueNetworkModel());

        workspace.StepCommand.Execute(null);

        Assert.Contains(workspace.RouteReports, row => !string.Equals(row.Pressure, "None", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(workspace.TopIssues);
    }

    private static NetworkModel BuildNetworkModel()
    {
        return new NetworkModel
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
    }

    private static NetworkModel BuildNodeIssueNetworkModel()
    {
        return new NetworkModel
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
    }

    private static NetworkModel BuildEdgeIssueNetworkModel()
    {
        return new NetworkModel
        {
            Name = "Edge issue network",
            TrafficTypes = [new TrafficTypeDefinition { Name = "Supply" }],
            Nodes =
            [
                new NodeModel
                {
                    Id = "ward-a",
                    Name = "Ward A",
                    X = 100d,
                    Y = 120d,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Supply", Production = 30d }]
                },
                new NodeModel
                {
                    Id = "pharmacy",
                    Name = "Pharmacy",
                    X = 400d,
                    Y = 120d,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Supply", Consumption = 30d }]
                }
            ],
            Edges =
            [
                new EdgeModel { Id = "congested-route", FromNodeId = "ward-a", ToNodeId = "pharmacy", Time = 1d, Cost = 1d, Capacity = 5d }
            ]
        };
    }

    private static void LoadNetwork(WorkspaceViewModel workspace, NetworkModel model)
    {
        var loadMethod = typeof(WorkspaceViewModel).GetMethod("LoadNetwork", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(loadMethod);
        loadMethod!.Invoke(workspace, [model, "Loaded test network", null]);
    }
}

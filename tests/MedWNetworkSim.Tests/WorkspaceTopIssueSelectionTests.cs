using System;
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

    private static void LoadNetwork(WorkspaceViewModel workspace, NetworkModel model)
    {
        var loadMethod = typeof(WorkspaceViewModel).GetMethod("LoadNetwork", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(loadMethod);
        loadMethod!.Invoke(workspace, [model, "Loaded test network", null]);
    }
}

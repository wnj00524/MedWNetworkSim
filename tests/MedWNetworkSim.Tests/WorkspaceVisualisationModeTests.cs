using System.Reflection;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.VisualAnalytics;
using MedWNetworkSim.Presentation;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class WorkspaceVisualisationModeTests
{
    [Fact]
    public void GraphMode_IsDefault()
    {
        var workspace = new WorkspaceViewModel();
        Assert.Equal(VisualisationMode.Graph, workspace.VisualisationState.ActiveMode);
    }

    [Fact]
    public void SwitchingModes_DoesNotClearSelection()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, new NetworkModel
        {
            Nodes = [new NodeModel { Id = "a", Name = "A", X = 10, Y = 10 }, new NodeModel { Id = "b", Name = "B", X = 50, Y = 10 }],
            Edges = [new EdgeModel { Id = "e1", FromNodeId = "a", ToNodeId = "b", Time = 1, Cost = 1 }]
        });

        workspace.SelectNode("a");
        workspace.SetSankeyVisualisationCommand.Execute(null);
        workspace.SetMapVisualisationCommand.Execute(null);
        workspace.SetGraphVisualisationCommand.Execute(null);

        Assert.Contains("a", workspace.Scene.Selection.SelectedNodeIds, StringComparer.OrdinalIgnoreCase);
    }

    private static void LoadNetwork(WorkspaceViewModel workspace, NetworkModel model)
    {
        var loadMethod = typeof(WorkspaceViewModel).GetMethod("LoadNetwork", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(loadMethod);
        loadMethod!.Invoke(workspace, [model, "Loaded test network", null]);
    }
}

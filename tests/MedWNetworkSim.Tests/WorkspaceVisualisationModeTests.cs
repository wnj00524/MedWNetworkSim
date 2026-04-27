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
        workspace.ShowSankeyModeCommand.Execute(null);
        workspace.ShowMapModeCommand.Execute(null);
        workspace.ShowGraphModeCommand.Execute(null);

        Assert.Contains("a", workspace.Scene.Selection.SelectedNodeIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShowSankeyModeCommand_SetsActiveMode()
    {
        var workspace = new WorkspaceViewModel();
        workspace.ShowSankeyModeCommand.Execute(null);
        Assert.Equal(VisualisationMode.Sankey, workspace.VisualisationState.ActiveMode);
    }

    [Fact]
    public void ShowMapModeCommand_SetsActiveMode()
    {
        var workspace = new WorkspaceViewModel();
        workspace.ShowMapModeCommand.Execute(null);
        Assert.Equal(VisualisationMode.Map, workspace.VisualisationState.ActiveMode);
    }

    [Fact]
    public void ChangingSankeyFilter_InvalidatesSankeyVersion()
    {
        var workspace = new WorkspaceViewModel();
        var baseline = workspace.SankeyVersion;
        workspace.VisualisationState.ActiveTrafficTypeFilter = "Food";
        Assert.True(workspace.SankeyVersion > baseline);
    }

    [Fact]
    public void Simulate_GeneratesInsights_WhenOutcomesExist()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, new NetworkModel
        {
            TrafficTypes = [new TrafficTypeDefinition { Name = "Supply" }],
            Nodes =
            [
                new NodeModel { Id = "p", Name = "Producer", X = 10, Y = 10, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Supply", Production = 10 }] },
                new NodeModel { Id = "c", Name = "Consumer", X = 60, Y = 20, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Supply", Consumption = 20 }] }
            ],
            Edges = [new EdgeModel { Id = "e1", FromNodeId = "p", ToNodeId = "c", Capacity = 5, Time = 1, Cost = 1 }]
        });

        workspace.SimulateCommand.Execute(null);

        Assert.NotNull(workspace.NetworkInsights);
        Assert.NotEmpty(workspace.NetworkInsights);
    }

    private static void LoadNetwork(WorkspaceViewModel workspace, NetworkModel model)
    {
        var loadMethod = typeof(WorkspaceViewModel).GetMethod("LoadNetwork", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(loadMethod);
        loadMethod!.Invoke(workspace, [model, "Loaded test network", null]);
    }
}

using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.Presentation;
using MedWNetworkSim.UI;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class AvaloniaRedesignTests
{
    [Fact]
    public void IconOnlyCommandButton_HasTooltipAndAutomationName()
    {
        var button = IconButtonFactory.Create(
            IconGeometryCatalog.Save,
            "Save network",
            new RelayCommand(() => { }),
            shortcutText: "Ctrl+S");

        Assert.IsType<PathIcon>(button.Content);
        Assert.Equal("Save network", AutomationProperties.GetName(button));
        Assert.Equal("Save network (Ctrl+S)", ToolTip.GetTip(button));
    }

    [Fact]
    public void SwitchingWorkspaces_DoesNotClearGraphSelection()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, new NetworkModel
        {
            Nodes = [new NodeModel { Id = "a", Name = "A", X = 10, Y = 10 }],
            Edges = []
        });

        workspace.SelectNode("a");
        workspace.SetAnalyticsViewCommand.Execute(null);
        workspace.SetAgentsViewCommand.Execute(null);
        workspace.SetReportsViewCommand.Execute(null);
        workspace.SetFacilitiesViewCommand.Execute(null);
        workspace.SetNetworkViewCommand.Execute(null);

        Assert.Contains("a", workspace.Scene.Selection.SelectedNodeIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalyticsFilters_UpdateViewStateAndSankeyVersion()
    {
        var workspace = new WorkspaceViewModel();
        var baseline = workspace.SankeyVersion;

        workspace.VisualisationState.ShowUnmetDemand = !workspace.VisualisationState.ShowUnmetDemand;
        Assert.True(workspace.SankeyVersion > baseline);

        baseline = workspace.SankeyVersion;
        workspace.VisualisationState.CollapseMinorFlows = !workspace.VisualisationState.CollapseMinorFlows;
        Assert.True(workspace.SankeyVersion > baseline);
    }

    [Fact]
    public void FacilityPlanningScreenValues_BindToCoverageState()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, new NetworkModel
        {
            Nodes =
            [
                new NodeModel { Id = "A", Name = "A", TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }] },
                new NodeModel { Id = "B", Name = "B", TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }] },
                new NodeModel { Id = "C", Name = "C", TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }] }
            ],
            Edges =
            [
                new EdgeModel { Id = "A-B", FromNodeId = "A", ToNodeId = "B", Time = 1d, Cost = 1d, IsBidirectional = true },
                new EdgeModel { Id = "B-C", FromNodeId = "B", ToNodeId = "C", Time = 1d, Cost = 1d, IsBidirectional = true }
            ]
        });

        workspace.SetFacilityPlanningMode(true);
        workspace.ToggleFacilityOriginById("A", 1.5d);
        workspace.RunMultiOriginIsochrone();

        Assert.Equal("2", workspace.ReachableNodeCountText);
        Assert.Equal("1", workspace.UncoveredNodeCountText);
        Assert.Equal("0", workspace.OverlapNodeCountText);
        Assert.NotEmpty(workspace.UncoveredPlanningItems);
    }

    [Fact]
    public void NetworkWorkspace_DefaultStateSeparatesWorkflowPanels()
    {
        var workspace = new WorkspaceViewModel();

        Assert.True(workspace.IsNetworkView);
        Assert.False(workspace.IsAnalyticsView);
        Assert.False(workspace.IsAgentsView);
        Assert.False(workspace.IsOsmImportView);
        Assert.False(workspace.IsFacilitiesView);
        Assert.False(workspace.IsReportsView);
    }

    [Fact]
    public void MainShell_AgentModeGroupContainsMeetingDemandLimitCheckBox()
    {
        var workspace = new WorkspaceViewModel
        {
            LimitMeetingNodeDemandBySellLocalPermission = true
        };

        var buildMethod = typeof(ShellWindow).GetMethod("BuildAgentModeSelector", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(buildMethod);

        var control = Assert.IsAssignableFrom<Control>(buildMethod!.Invoke(null, [workspace]));
        control.DataContext = workspace;

        var checkBox = FindCheckBox(control, "Limit meeting-node demand");

        Assert.NotNull(checkBox);
        Assert.True(checkBox!.IsEnabled);
        Assert.Equal("Limit meeting-node demand", checkBox.Content);
        Assert.Equal("Limit meeting-node demand by Sell local permission.", ToolTip.GetTip(checkBox));
        Assert.True(checkBox.IsChecked);

        checkBox.IsChecked = false;

        Assert.False(workspace.LimitMeetingNodeDemandBySellLocalPermission);

        workspace.LimitMeetingNodeDemandBySellLocalPermission = true;

        Assert.True(checkBox.IsChecked);
    }

    private static void LoadNetwork(WorkspaceViewModel workspace, NetworkModel model)
    {
        var loadMethod = typeof(WorkspaceViewModel).GetMethod("LoadNetwork", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(loadMethod);
        loadMethod!.Invoke(workspace, [model, "Loaded test network", null]);
    }

    private static CheckBox? FindCheckBox(ILogical root, string content)
    {
        if (root is CheckBox checkBox && string.Equals(checkBox.Content as string, content, StringComparison.Ordinal))
        {
            return checkBox;
        }

        foreach (var child in root.LogicalChildren)
        {
            var match = FindCheckBox(child, content);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}

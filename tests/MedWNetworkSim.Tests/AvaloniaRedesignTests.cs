using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using MedWNetworkSim.App.Agents;
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
    public void MeetingDemandLimitCheckBox_UsesExpectedLabelAndTooltip()
    {
        var buildMethod = typeof(ShellWindow).GetMethod("BuildMeetingDemandLimitCheckBox", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(buildMethod);

        var checkBox = Assert.IsType<CheckBox>(buildMethod!.Invoke(null, null));
        Assert.Equal("Limit meeting-node demand", checkBox.Content);
        Assert.Equal("Limit meeting-node demand by Sell local permission.", ToolTip.GetTip(checkBox));
    }

    [Fact]
    public void ApplyNetworkDetails_UpdatesMeetingDemandLimitSetting()
    {
        var workspace = new WorkspaceViewModel
        {
            LimitMeetingNodeDemandBySellLocalPermission = false
        };

        workspace.ApplyNetworkDetails("Test network", "Notes", loops: true, loopLength: 4, limitMeetingNodeDemandBySellLocalPermission: true);

        Assert.True(workspace.LimitMeetingNodeDemandBySellLocalPermission);
        Assert.Equal("Test network", workspace.NetworkNameText);
        Assert.Equal("Notes", workspace.NetworkDescriptionText);
        Assert.Equal("4", workspace.NetworkTimelineLoopLengthText);
    }


    [Fact]
    public void AnalyticsCockpit_ExposesBoundSankeyTrafficTypeSelector()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, new NetworkModel
        {
            TrafficTypes = [new TrafficTypeDefinition { Name = "Food" }, new TrafficTypeDefinition { Name = "Water" }]
        });
        var shell = new ShellWindow(workspace);
        var buildMethod = typeof(ShellWindow).GetMethod("BuildAnalyticsView", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(buildMethod);

        var analyticsView = Assert.IsType<AnalyticsWorkspaceView>(buildMethod!.Invoke(shell, [workspace]));
        analyticsView.DataContext = workspace;
        var selector = FindControls<ComboBox>(analyticsView)
            .Single(comboBox => AutomationProperties.GetName(comboBox) == "Traffic type");

        selector.DataContext = workspace;
        var selectorOptions = Assert.IsAssignableFrom<IEnumerable<string>>(selector.ItemsSource);
        Assert.Contains(WorkspaceViewModel.AllTrafficTypesFilterLabel, selectorOptions);
        Assert.Contains("Food", selectorOptions);
        selector.SelectedItem = "Food";

        Assert.Equal("Food", workspace.SankeyTrafficTypeFilterSelection);
        Assert.Equal("Food", workspace.VisualisationState.ActiveTrafficTypeFilter);
    }

    [Fact]
    public void AgentProfitReportTable_RendersHeadersAndRowValues()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, CreateEconomicActorNetwork());
        workspace.StepCommand.Execute(null);

        var buildMethod = typeof(ShellWindow).GetMethod("BuildAgentProfitReportTable", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(buildMethod);

        var table = Assert.IsAssignableFrom<Control>(buildMethod!.Invoke(null, null));
        var headers = FindControls<TextBlock>(table)
            .Select(text => text.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        Assert.Contains("Agent Name", headers);
        Assert.Contains("Agent Cash", headers);
        Assert.Contains("Agent Budget", headers);
        Assert.Contains("Agent Tick Revenue", headers);
        Assert.Contains("Agent Tick Costs", headers);
        Assert.Contains("Agent Tick Profit", headers);

        var rows = FindControls<ItemsControl>(table).Single();
        Assert.NotNull(rows.ItemTemplate);

        var firstRow = Assert.IsAssignableFrom<Control>(rows.ItemTemplate!.Build(workspace.AgentProfitReportRows[0]));
        firstRow.DataContext = workspace.AgentProfitReportRows[0];
        var rowValues = FindControls<TextBlock>(firstRow)
            .Select(text => text.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        Assert.Contains(workspace.AgentProfitReportRows[0].AgentName, rowValues);
        Assert.Contains(workspace.AgentProfitReportRows[0].AgentCash, rowValues);
        Assert.Contains(workspace.AgentProfitReportRows[0].AgentBudget, rowValues);
        Assert.Contains(workspace.AgentProfitReportRows[0].AgentTickRevenue, rowValues);
        Assert.Contains(workspace.AgentProfitReportRows[0].AgentTickCosts, rowValues);
        Assert.Contains(workspace.AgentProfitReportRows[0].AgentTickProfit, rowValues);
    }

    private static IEnumerable<T> FindControls<T>(object? root)
        where T : Control
    {
        if (root is null)
        {
            yield break;
        }

        if (root is T match)
        {
            yield return match;
        }

        switch (root)
        {
            case Panel panel:
                foreach (var child in panel.Children.SelectMany(FindControls<T>))
                {
                    yield return child;
                }
                break;
            case Decorator decorator:
                foreach (var child in FindControls<T>(decorator.Child))
                {
                    yield return child;
                }
                break;
            case ContentControl contentControl:
                foreach (var child in FindControls<T>(contentControl.Content))
                {
                    yield return child;
                }
                break;
        }
    }

    private static void LoadNetwork(WorkspaceViewModel workspace, NetworkModel model)
    {
        var loadMethod = typeof(WorkspaceViewModel).GetMethod("LoadNetwork", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(loadMethod);
        loadMethod!.Invoke(workspace, [model, "Loaded test network", null]);
    }

    private static NetworkModel CreateEconomicActorNetwork()
    {
        var layerId = Guid.NewGuid();
        return new NetworkModel
        {
            Name = "Economic actor network",
            Layers = [new NetworkLayerModel { Id = layerId, Name = "Physical", Type = NetworkLayerType.Physical, Order = 0 }],
            TrafficTypes =
            [
                new TrafficTypeDefinition
                {
                    Name = "Food",
                    RoutingPreference = RoutingPreference.Cost,
                    AllocationMode = AllocationMode.GreedyBestRoute,
                    DefaultUnitSalePrice = 10d,
                    DefaultUnitProductionCost = 2d
                }
            ],
            Nodes =
            [
                new NodeModel
                {
                    Id = "producer",
                    Name = "Producer",
                    LayerId = layerId,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Production = 3d, UnitPrice = 10d, ProductionCostPerUnit = 2d }]
                },
                new NodeModel
                {
                    Id = "consumer",
                    Name = "Consumer",
                    LayerId = layerId,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Consumption = 3d }]
                }
            ],
            Edges = [new EdgeModel { Id = "edge", FromNodeId = "producer", ToNodeId = "consumer", LayerId = layerId, Capacity = 10d, Time = 1d, Cost = 1d }],
            Actors =
            [
                new SimulationActorState
                {
                    Id = "producer-actor",
                    Name = "Producer Firm",
                    Kind = SimulationActorKind.Firm,
                    Objective = SimulationActorObjective.MaximiseProfit,
                    IsEnabled = true,
                    Cash = 100d,
                    Budget = 100d,
                    ControlledNodeIds = ["producer"]
                }
            ]
        };
    }
}

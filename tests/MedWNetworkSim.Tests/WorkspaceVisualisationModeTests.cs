using System.Reflection;
using MedWNetworkSim.App.Agents;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.VisualAnalytics;
using MedWNetworkSim.Presentation;
using MedWNetworkSim.Rendering;
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
    public void SankeyTrafficTypeFilterSelection_RebuildsForNewTrafficType()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, CreateTwoTrafficNetwork());
        workspace.SimulateCommand.Execute(null);

        workspace.SankeyTrafficTypeFilterSelection = "Food";
        var food = workspace.CurrentSankey;
        workspace.SankeyTrafficTypeFilterSelection = "Water";
        var water = workspace.CurrentSankey;

        Assert.NotSame(food, water);
        Assert.NotEmpty(food.Links);
        Assert.NotEmpty(water.Links);
        Assert.All(food.Links.Where(link => !link.IsUnmetDemand), link => Assert.Equal("Food", link.TrafficType));
        Assert.All(water.Links.Where(link => !link.IsUnmetDemand), link => Assert.Equal("Water", link.TrafficType));
    }

    [Fact]
    public void StepCommand_PopulatesSankeyFromTimelineAllocations()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, CreateTwoTrafficNetwork());

        workspace.StepCommand.Execute(null);

        Assert.Contains(workspace.CurrentSankey.Links, link => !link.IsUnmetDemand);
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

    [Fact]
    public void GraphViewport_ScreenWorldMapping_RoundTrips()
    {
        var viewport = new GraphViewport();
        var viewportSize = new GraphSize(1280d, 720d);
        viewport.Pan(new GraphVector(-220d, 140d));
        viewport.ZoomAt(new GraphPoint(640d, 360d), viewportSize, 1.8d);

        var screen = new GraphPoint(930d, 510d);
        var world = viewport.ScreenToWorld(screen, viewportSize);
        var roundTrip = viewport.WorldToScreen(world, viewportSize);

        Assert.InRange(Math.Abs(roundTrip.X - screen.X), 0d, 0.0001d);
        Assert.InRange(Math.Abs(roundTrip.Y - screen.Y), 0d, 0.0001d);
    }

    [Fact]
    public void LockLayoutToMap_SkipsGeoNodePositionSync()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, new NetworkModel
        {
            LockLayoutToMap = true,
            Nodes =
            [
                new NodeModel { Id = "geo", Name = "Geo", X = 40d, Y = 50d, Latitude = 51.5d, Longitude = -0.12d },
                new NodeModel { Id = "plain", Name = "Plain", X = 80d, Y = 90d }
            ]
        });

        var geoSceneNode = workspace.Scene.Nodes.First(node => node.Id == "geo");
        geoSceneNode.Bounds = geoSceneNode.Bounds with { X = 900d, Y = 700d };

        workspace.NotifyVisualChanged();
        var network = GetNetwork(workspace);
        var geoNode = network.Nodes.First(node => node.Id == "geo");

        Assert.Equal(40d, geoNode.X);
        Assert.Equal(50d, geoNode.Y);
    }

    [Fact]
    public void TryBulkApplyTrafficRole_ApplyToAllNodes_TargetsEveryNode()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, new NetworkModel
        {
            TrafficTypes = [new TrafficTypeDefinition { Name = "general" }],
            Nodes =
            [
                new NodeModel { Id = "n1", Name = "A" },
                new NodeModel { Id = "n2", Name = "B" },
                new NodeModel { Id = "n3", Name = "C" }
            ]
        });

        var applied = workspace.TryBulkApplyTrafficRole("Producer", "general", applyToAllNodes: true, out var _);
        var network = GetNetwork(workspace);

        Assert.True(applied);
        Assert.All(network.Nodes, node =>
        {
            var profile = Assert.Single(node.TrafficProfiles);
            Assert.Equal("general", profile.TrafficType);
            Assert.True(profile.Production > 0d);
        });
    }

    [Fact]
    public void UndeclaredProfileTraffic_PopulatesSankeyOptionsAndReports()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, new NetworkModel
        {
            Nodes =
            [
                new NodeModel
                {
                    Id = "p",
                    Name = "Producer",
                    X = 10,
                    Y = 10,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Medicine", Production = 12 }]
                },
                new NodeModel
                {
                    Id = "c",
                    Name = "Consumer",
                    X = 60,
                    Y = 20,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Medicine", Consumption = 7 }]
                }
            ],
            Edges = [new EdgeModel { Id = "e1", FromNodeId = "p", ToNodeId = "c", Capacity = 20, Time = 1, Cost = 1 }]
        });

        workspace.SimulateCommand.Execute(null);

        Assert.Contains("Medicine", workspace.SankeyTrafficTypeNameOptions);
        Assert.Contains(workspace.TrafficReports, row => row.TrafficType == "Medicine" && row.DeliveredQuantity != "0");
        workspace.SankeyTrafficTypeFilterSelection = "Medicine";
        Assert.All(workspace.CurrentSankey.Links.Where(link => !link.IsUnmetDemand), link => Assert.Equal("Medicine", link.TrafficType));
    }


    [Fact]
    public void SimulateCommand_PopulatesSettledEconomicsAgentReportAndRevenueCostSeries()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, CreateEconomicActorNetwork());

        workspace.SimulateCommand.Execute(null);

        var outcomes = GetLastOutcomes(workspace);
        var food = Assert.Single(outcomes, outcome => outcome.TrafficType == "Food");
        Assert.True(food.TotalSalesRevenue > 0d);
        Assert.True(food.TotalProductionCost > 0d || food.TotalTransportCost > 0d);
        Assert.Equal(21d, food.TotalProfit, 6);
        Assert.NotEmpty(workspace.AgentProfitReportRows);
        Assert.Contains(workspace.AgentProfitReportRows, row => row.AgentName == "Producer Firm" && row.AgentTickRevenue != "0" && row.AgentTickProfit != "0");
        var producerSeries = Assert.Single(workspace.AgentProfitSeries, series => series.AgentName == "Producer Firm");
        Assert.Contains(producerSeries.Points, point => point.Revenue > 0d && point.Costs > 0d);

        var network = GetNetwork(workspace);
        var latestMetric = Assert.Single(network.ActorMetrics);
        Assert.Equal(30d, latestMetric.ActorSalesRevenueById["producer-actor"]);
        Assert.Equal(21d, latestMetric.ActorProfitById["producer-actor"], 6);
    }

    [Fact]
    public void StepCommand_PopulatesTemporalSettledEconomicsAndRevenueCostSeries()
    {
        var workspace = new WorkspaceViewModel();
        LoadNetwork(workspace, CreateEconomicActorNetwork());

        workspace.StepCommand.Execute(null);

        var outcomes = GetLastOutcomes(workspace);
        Assert.Contains(outcomes, outcome => outcome.TrafficType == "Food" && outcome.TotalSalesRevenue > 0d && outcome.TotalProfit != 0d);
        Assert.NotEmpty(workspace.AgentProfitReportRows);
        Assert.Contains(workspace.AgentProfitSeries, series => series.AgentName == "Producer Firm" && series.Points.Any(point => point.Revenue > 0d && point.Costs > 0d));
    }

    private static NetworkModel CreateTwoTrafficNetwork() => new()
    {
        TrafficTypes =
        [
            new TrafficTypeDefinition { Name = "Food" },
            new TrafficTypeDefinition { Name = "Water" }
        ],
        Nodes =
        [
            new NodeModel
            {
                Id = "p",
                Name = "Producer",
                X = 10,
                Y = 10,
                TrafficProfiles =
                [
                    new NodeTrafficProfile { TrafficType = "Food", Production = 10 },
                    new NodeTrafficProfile { TrafficType = "Water", Production = 8 }
                ]
            },
            new NodeModel
            {
                Id = "c",
                Name = "Consumer",
                X = 60,
                Y = 20,
                TrafficProfiles =
                [
                    new NodeTrafficProfile { TrafficType = "Food", Consumption = 5 },
                    new NodeTrafficProfile { TrafficType = "Water", Consumption = 4 }
                ]
            }
        ],
        Edges = [new EdgeModel { Id = "e1", FromNodeId = "p", ToNodeId = "c", Capacity = 20, Time = 1, Cost = 1 }]
    };


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

    private static void LoadNetwork(WorkspaceViewModel workspace, NetworkModel model)
    {
        var loadMethod = typeof(WorkspaceViewModel).GetMethod("LoadNetwork", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(loadMethod);
        loadMethod!.Invoke(workspace, [model, "Loaded test network", null]);
    }

    private static IReadOnlyList<MedWNetworkSim.App.Services.TrafficSimulationOutcome> GetLastOutcomes(WorkspaceViewModel workspace)
    {
        var field = typeof(WorkspaceViewModel).GetField("lastOutcomes", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsAssignableFrom<IReadOnlyList<MedWNetworkSim.App.Services.TrafficSimulationOutcome>>(field!.GetValue(workspace));
    }

    private static NetworkModel GetNetwork(WorkspaceViewModel workspace)
    {
        var field = typeof(WorkspaceViewModel).GetField("network", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<NetworkModel>(field!.GetValue(workspace));
    }
}

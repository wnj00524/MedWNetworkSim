using System.Reflection;
using System.Text.Json;
using MedWNetworkSim.App.Agents;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.Presentation;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class MeetingDemandSellLocalLimitTests
{
    [Fact]
    public void ToggleOff_AllowsSameNodeDemandWithoutSellLocalPermission()
    {
        var network = BuildLocalMeetingNetwork(limitMeetingDemand: false);

        var outcome = new NetworkSimulationEngine().Simulate(network).Single();

        Assert.Equal(10d, outcome.TotalDelivered);
        Assert.Equal(0d, outcome.UnmetDemand);
        Assert.Contains(outcome.Allocations, allocation => allocation.IsLocalSupply && allocation.Quantity == 10d);
    }

    [Fact]
    public void ToggleOn_BlocksSameNodeDemandWithoutSellLocalPermission()
    {
        var network = BuildLocalMeetingNetwork(limitMeetingDemand: true);

        var outcome = new NetworkSimulationEngine().Simulate(network).Single();

        Assert.Equal(0d, outcome.TotalDelivered);
        Assert.Equal(10d, outcome.UnmetDemand);
        Assert.Contains(outcome.Notes, note => note.Contains("Sell local meeting-demand limit is active", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ToggleOn_UsesPermittedExternalSellerWhenSameNodeSupplyIsBlocked()
    {
        var network = BuildLocalMeetingNetwork(limitMeetingDemand: true);
        network.Nodes.Add(new NodeModel
        {
            Id = "seller",
            Name = "Permitted Seller",
            TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Production = 10d }]
        });
        network.Edges.Add(new EdgeModel { Id = "seller-to-meeting", FromNodeId = "seller", ToNodeId = "meeting", IsBidirectional = true });
        network.Actors.Add(BuildSellLocalActor("seller-actor", "seller"));

        var outcome = new NetworkSimulationEngine().Simulate(network).Single();

        Assert.Equal(0d, outcome.TotalDelivered);
        Assert.Equal(10d, outcome.UnmetDemand);
        Assert.DoesNotContain(outcome.Allocations, allocation => allocation.ConsumerNodeId == "meeting");
    }

    [Fact]
    public void ToggleOn_AllowsSameNodeDemandWithExplicitSellLocalPermission()
    {
        var network = BuildLocalMeetingNetwork(limitMeetingDemand: true);
        network.Actors.Add(BuildSellLocalActor("meeting-actor", "meeting"));

        var outcome = new NetworkSimulationEngine().Simulate(network).Single();

        Assert.Equal(10d, outcome.TotalDelivered);
        Assert.Equal(0d, outcome.UnmetDemand);
        Assert.Contains(outcome.Allocations, allocation => allocation.IsLocalSupply && allocation.ProducerNodeId == "meeting");
    }

    [Fact]
    public void ToggleOn_AllowsExternalDeliveryWhenConsumerNodeHasExplicitSellLocalPermission()
    {
        var network = new NetworkModel
        {
            LimitMeetingNodeDemandBySellLocalPermission = true,
            TrafficTypes = [new TrafficTypeDefinition { Name = "Food" }],
            Nodes =
            [
                new NodeModel
                {
                    Id = "meeting",
                    Name = "Meeting Node",
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Consumption = 10d }]
                },
                new NodeModel
                {
                    Id = "seller",
                    Name = "Permitted Seller",
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Production = 10d }]
                }
            ],
            Edges = [new EdgeModel { Id = "seller-to-meeting", FromNodeId = "seller", ToNodeId = "meeting", IsBidirectional = true }]
        };
        network.Actors.Add(BuildSellLocalActor("meeting-actor", "meeting"));
        network.Actors.Add(BuildSellLocalActor("seller-actor", "seller"));

        var outcome = new NetworkSimulationEngine().Simulate(network).Single();

        Assert.Equal(10d, outcome.TotalDelivered);
        Assert.Equal(0d, outcome.UnmetDemand);
        Assert.Contains(outcome.Allocations, allocation => allocation.ProducerNodeId == "seller" && allocation.ConsumerNodeId == "meeting");
    }

    [Fact]
    public void TemporalToggleOn_BlocksExternalDeliveryWithoutControllingActorOnConsumerNode()
    {
        var network = BuildTemporalMeetingNetwork(limitMeetingDemand: true);
        network.Nodes.Add(new NodeModel
        {
            Id = "seller",
            Name = "Permitted Seller",
            TrafficProfiles =
            [
                new NodeTrafficProfile
                {
                    TrafficType = "Food",
                    Production = 10d,
                    ProductionStartPeriod = 1
                }
            ]
        });
        network.Edges.Add(new EdgeModel { Id = "seller-to-meeting", FromNodeId = "seller", ToNodeId = "meeting", IsBidirectional = true });
        network.Actors.Add(BuildSellLocalActor("seller-actor", "seller"));

        var engine = new TemporalNetworkSimulationEngine();
        var result = engine.Advance(network, engine.Initialize(network));

        Assert.Empty(result.Allocations);
        Assert.Contains(result.NodeStates, pair => pair.Key.NodeId == "meeting" && pair.Value.DemandBacklog >= 10d);
    }

    [Fact]
    public void LegacySellLocalJsonDefaultsMeetingDemandLimitOn()
    {
        var json = """
        {
          "name": "Legacy SellLocal",
          "agentMode": "sellLocal",
          "trafficTypes": [{ "name": "Food" }],
          "nodes": [],
          "edges": []
        }
        """;

        var loaded = new NetworkFileService().LoadJson(json);

        Assert.True(loaded.LimitMeetingNodeDemandBySellLocalPermission);
    }

    [Fact]
    public void SaveLoad_PreservesMeetingDemandLimit()
    {
        var service = new NetworkFileService();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            service.Save(BuildLocalMeetingNetwork(limitMeetingDemand: true), path);

            var loaded = service.Load(path);

            Assert.True(loaded.LimitMeetingNodeDemandBySellLocalPermission);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ReportsIncludeMeetingDemandLimitState()
    {
        var network = BuildLocalMeetingNetwork(limitMeetingDemand: true);
        var engine = new NetworkSimulationEngine();
        var outcomes = engine.Simulate(network);
        var consumerCosts = engine.SummarizeConsumerCosts(outcomes.SelectMany(outcome => outcome.Allocations));
        var exporter = new ReportExportService();

        var html = SaveCurrentReport(exporter, network, outcomes, consumerCosts, ReportExportFormat.Html);
        var csv = SaveCurrentReport(exporter, network, outcomes, consumerCosts, ReportExportFormat.Csv);
        var json = SaveCurrentReport(exporter, network, outcomes, consumerCosts, ReportExportFormat.Json);

        Assert.Contains("Sell local meeting-demand limit", html);
        Assert.Contains("On", html);
        Assert.Contains("Sell local meeting-demand limit", csv);
        Assert.Contains("On", csv);
        Assert.True(JsonDocument.Parse(json).RootElement.GetProperty("network").GetProperty("limitMeetingNodeDemandBySellLocalPermission").GetBoolean());
    }

    [Fact]
    public void TimelineReportsIncludeMeetingDemandLimitState()
    {
        var network = BuildTemporalMeetingNetwork(limitMeetingDemand: true);
        var engine = new TemporalNetworkSimulationEngine();
        var state = engine.Initialize(network);
        var results = new[] { engine.Advance(network, state) };
        var exporter = new ReportExportService();

        var html = SaveTimelineReport(exporter, network, results, ReportExportFormat.Html);
        var csv = SaveTimelineReport(exporter, network, results, ReportExportFormat.Csv);
        var json = SaveTimelineReport(exporter, network, results, ReportExportFormat.Json);

        Assert.Contains("Sell local meeting-demand limit", html);
        Assert.Contains("On", html);
        Assert.Contains("Sell local meeting-demand limit", csv);
        Assert.Contains("On", csv);
        Assert.True(JsonDocument.Parse(json).RootElement.GetProperty("network").GetProperty("limitMeetingNodeDemandBySellLocalPermission").GetBoolean());
    }

    [Fact]
    public void ExportOverrideUsesSnapshotWithoutMutatingWorkspaceSetting()
    {
        var viewModel = new WorkspaceViewModel();
        LoadNetwork(viewModel, BuildLocalMeetingNetwork(limitMeetingDemand: false));
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            viewModel.ExportCurrentReport(path, ReportExportFormat.Json, applySellLocalMeetingDemandLimit: true);
            var json = File.ReadAllText(path);

            Assert.True(JsonDocument.Parse(json).RootElement.GetProperty("network").GetProperty("limitMeetingNodeDemandBySellLocalPermission").GetBoolean());
            Assert.False(viewModel.LimitMeetingNodeDemandBySellLocalPermission);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void LoadNetwork(WorkspaceViewModel viewModel, NetworkModel network)
    {
        var loadMethod = typeof(WorkspaceViewModel).GetMethod("LoadNetwork", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(loadMethod);
        loadMethod!.Invoke(viewModel, [network, "Loaded test network", null]);
    }

    private static NetworkModel BuildLocalMeetingNetwork(bool limitMeetingDemand) => new()
    {
        LimitMeetingNodeDemandBySellLocalPermission = limitMeetingDemand,
        TrafficTypes = [new TrafficTypeDefinition { Name = "Food" }],
        Nodes =
        [
            new NodeModel
            {
                Id = "meeting",
                Name = "Meeting Node",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Production = 10d, Consumption = 10d }]
            }
        ]
    };

    private static NetworkModel BuildTemporalMeetingNetwork(bool limitMeetingDemand) => new()
    {
        LimitMeetingNodeDemandBySellLocalPermission = limitMeetingDemand,
        TrafficTypes = [new TrafficTypeDefinition { Name = "Food" }],
        Nodes =
        [
            new NodeModel
            {
                Id = "meeting",
                Name = "Meeting Node",
                TrafficProfiles =
                [
                    new NodeTrafficProfile
                    {
                        TrafficType = "Food",
                        Production = 10d,
                        Consumption = 10d,
                        ProductionStartPeriod = 1,
                        ConsumptionStartPeriod = 1
                    }
                ]
            }
        ]
    };

    private static SimulationActorState BuildSellLocalActor(string actorId, string nodeId) => new()
    {
        Id = actorId,
        Name = actorId,
        Kind = SimulationActorKind.Firm,
        IsEnabled = true,
        ControlledNodeIds = [nodeId],
        Capability = new SimulationActorCapability
        {
            ActorId = actorId,
            Permissions =
            [
                new SimulationActorPermission
                {
                    ActionKind = SimulationActorActionKind.SellLocal,
                    TrafficType = "Food",
                    NodeId = nodeId,
                    IsAllowed = true
                }
            ]
        }
    };

    private static string SaveCurrentReport(
        ReportExportService exporter,
        NetworkModel network,
        IReadOnlyList<TrafficSimulationOutcome> outcomes,
        IReadOnlyList<ConsumerCostSummary> consumerCosts,
        ReportExportFormat format)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.{format.ToString().ToLowerInvariant()}");
        try
        {
            exporter.SaveCurrentReport(network, outcomes, consumerCosts, path, format);
            return File.ReadAllText(path);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string SaveTimelineReport(
        ReportExportService exporter,
        NetworkModel network,
        IReadOnlyList<TemporalNetworkSimulationEngine.TemporalSimulationStepResult> results,
        ReportExportFormat format)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.{format.ToString().ToLowerInvariant()}");
        try
        {
            exporter.SaveTimelineReport(network, results, path, format);
            return File.ReadAllText(path);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

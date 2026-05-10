using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class NetworkReportAndSerializationTests
{
    [Fact]
    public void TimelineReports_DoNotContainAgentActions()
    {
        var network = CreateNetwork();
        var exporter = new ReportExportService();
        var results = new[]
        {
            new TemporalNetworkSimulationEngine.TemporalSimulationStepResult(
                1,
                [],
                new Dictionary<string, TemporalNetworkSimulationEngine.EdgeFlowVisualSummary>(),
                new Dictionary<string, TemporalNetworkSimulationEngine.NodeFlowVisualSummary>(),
                new Dictionary<TemporalNetworkSimulationEngine.TemporalNodeTrafficKey, TemporalNetworkSimulationEngine.TemporalNodeStateSnapshot>(),
                new Dictionary<string, double>(),
                new Dictionary<string, double>(),
                1,
                0,
                new Dictionary<string, TemporalNetworkSimulationEngine.NodePressureSnapshot>(),
                new Dictionary<string, TemporalNetworkSimulationEngine.EdgePressureSnapshot>(),
                [])
        };

        var html = SaveTimelineReport(exporter, network, results, ReportExportFormat.Html);
        var csv = SaveTimelineReport(exporter, network, results, ReportExportFormat.Csv);
        var json = SaveTimelineReport(exporter, network, results, ReportExportFormat.Json);

        Assert.DoesNotContain("Agent Actions", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Agent Actions", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("agentActions", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadAndSave_IgnoresLegacyAgentFields()
    {
        const string legacyJson = """
        {
          "name": "Legacy Network",
          "trafficTypes": [{ "name": "Food" }],
          "nodes": [
            { "id": "producer", "trafficProfiles": [{ "trafficType": "Food", "production": 5 }] },
            { "id": "consumer", "trafficProfiles": [{ "trafficType": "Food", "consumption": 5 }] }
          ],
          "edges": [{ "id": "edge", "fromNodeId": "producer", "toNodeId": "consumer", "time": 1, "cost": 1 }],
          "actors": [{ "id": "actor-1", "name": "Legacy Actor" }],
          "actorDecisions": [{ "tick": 1 }],
          "actorMetrics": [{ "tick": 1 }],
          "actorActionOutcomes": [{ "applied": true }],
          "agentActionLogs": [{ "actionType": "AdjustProduction" }],
          "actorTick": 9,
          "preAgentMutationNetwork": { "name": "Old snapshot", "nodes": [], "edges": [] }
        }
        """;

        var service = new NetworkFileService();
        var loaded = service.LoadJson(legacyJson);

        Assert.Empty(loaded.Actors);
        Assert.Empty(loaded.ActorDecisions);
        Assert.Empty(loaded.ActorMetrics);
        Assert.Empty(loaded.ActorActionOutcomes);
        Assert.Empty(loaded.AgentActionLogs);
        Assert.Null(loaded.PreAgentMutationNetwork);
        Assert.Equal(0, loaded.ActorTick);

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            service.Save(loaded, path);
            var savedJson = File.ReadAllText(path);

            Assert.DoesNotContain("actors", savedJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("actorDecisions", savedJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("actorMetrics", savedJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("actorActionOutcomes", savedJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("agentActionLogs", savedJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preAgentMutationNetwork", savedJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static NetworkModel CreateNetwork() => new()
    {
        Name = "Report Test",
        Nodes =
        [
            new NodeModel { Id = "a", Name = "A" },
            new NodeModel { Id = "b", Name = "B" }
        ],
        Edges =
        [
            new EdgeModel { Id = "ab", FromNodeId = "a", ToNodeId = "b", Time = 1d, Cost = 1d }
        ],
        TrafficTypes = [new TrafficTypeDefinition { Name = "Food" }]
    };

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

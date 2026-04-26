using System.Globalization;
using System.Text;
using System.Text.Json;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public interface INetworkLayerResolver
{
    IReadOnlyList<NetworkLayer> GetSimulationOrder(NetworkModel network);

    NetworkLayer GetDefaultLayer(NetworkModel network);
}

public sealed class NetworkLayerResolver : INetworkLayerResolver
{
    public IReadOnlyList<NetworkLayer> GetSimulationOrder(NetworkModel network)
    {
        ArgumentNullException.ThrowIfNull(network);
        RepairLayers(network);
        return network.Layers
            .OrderBy(layer => layer.Type == NetworkLayerType.Physical ? 0 : layer.Type == NetworkLayerType.Logical ? 1 : 2)
            .ThenBy(layer => layer.Order)
            .ThenBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public NetworkLayer GetDefaultLayer(NetworkModel network)
    {
        RepairLayers(network);
        return network.Layers.First(layer => layer.Type == NetworkLayerType.Physical);
    }

    public void RepairLayers(NetworkModel network)
    {
        network.Layers ??= [];
        if (network.Layers.Count == 0)
        {
            network.Layers.Add(new NetworkLayer { Name = "Physical", Type = NetworkLayerType.Physical, Order = 0 });
        }

        foreach (var type in Enum.GetValues<NetworkLayerType>())
        {
            if (network.Layers.Any(layer => layer.Type == type))
            {
                continue;
            }

            network.Layers.Add(new NetworkLayer { Name = type.ToString(), Type = type, Order = type == NetworkLayerType.Physical ? 0 : type == NetworkLayerType.Logical ? 1 : 2 });
        }

        var order = 0;
        foreach (var layer in network.Layers
                     .DistinctBy(layer => layer.Id)
                     .OrderBy(layer => layer.Type == NetworkLayerType.Physical ? 0 : layer.Type == NetworkLayerType.Logical ? 1 : 2)
                     .ThenBy(layer => layer.Order))
        {
            layer.Order = order++;
            layer.Name = string.IsNullOrWhiteSpace(layer.Name) ? layer.Type.ToString() : layer.Name.Trim();
        }
    }
}

public interface ISimulationScheduledEvent
{
    double Time { get; }

    string Name { get; }

    void Execute(SimulationContext context);
}

public interface ISimulationEventQueue
{
    void Enqueue(ISimulationScheduledEvent simulationEvent);

    IReadOnlyList<ISimulationScheduledEvent> DequeueDueEvents(double currentTime);

    void Clear();
}

public sealed class SimulationEventQueue : ISimulationEventQueue
{
    private readonly PriorityQueue<ISimulationScheduledEvent, double> queue = new();

    public void Enqueue(ISimulationScheduledEvent simulationEvent)
    {
        ArgumentNullException.ThrowIfNull(simulationEvent);
        queue.Enqueue(simulationEvent, simulationEvent.Time);
    }

    public IReadOnlyList<ISimulationScheduledEvent> DequeueDueEvents(double currentTime)
    {
        var due = new List<ISimulationScheduledEvent>();
        while (queue.TryPeek(out _, out var time) && time <= currentTime)
        {
            due.Add(queue.Dequeue());
        }

        return due;
    }

    public void Clear()
    {
        queue.Clear();
    }
}

public interface IAdaptiveRoutingMemory
{
    AdaptiveEdgeState GetOrCreate(Guid edgeId);

    void RecordObservation(Guid edgeId, double observedDelay, double utilisation);

    double GetAdaptivePenalty(Guid edgeId);

    void Reset();
}

public sealed class AdaptiveRoutingMemory : IAdaptiveRoutingMemory
{
    private readonly Dictionary<Guid, AdaptiveEdgeState> state = [];

    public AdaptiveEdgeState GetOrCreate(Guid edgeId)
    {
        if (!state.TryGetValue(edgeId, out var item))
        {
            item = new AdaptiveEdgeState { EdgeId = edgeId };
            state[edgeId] = item;
        }

        return item;
    }

    public void RecordObservation(Guid edgeId, double observedDelay, double utilisation)
    {
        var item = GetOrCreate(edgeId);
        item.HistoricalDelay = item.HistoricalDelay <= 0d ? observedDelay : (0.8d * item.HistoricalDelay) + (0.2d * observedDelay);
        item.LastObservedUtilisation = Math.Clamp(utilisation, 0d, 5d);
        item.ReinforcementScore = Math.Max(0d, item.ReinforcementScore * 0.92d);
        if (item.LastObservedUtilisation > 0.85d || observedDelay > item.HistoricalDelay * 1.05d)
        {
            item.ReinforcementScore += (item.LastObservedUtilisation * 0.5d) + (observedDelay * 0.1d);
        }
    }

    public double GetAdaptivePenalty(Guid edgeId)
    {
        var item = GetOrCreate(edgeId);
        return Math.Max(0d, item.HistoricalDelay * 0.05d + item.ReinforcementScore * 0.1d);
    }

    public void Reset() => state.Clear();
}

public interface IEconomicCalculator
{
    EconomicSummary Calculate(NetworkModel network, SimulationResult result);
}

public sealed class EconomicCalculator : IEconomicCalculator
{
    public EconomicSummary Calculate(NetworkModel network, SimulationResult result)
    {
        var revenue = 0d;
        var holding = 0d;
        var shortage = 0d;

        foreach (var profile in network.Nodes.SelectMany(node => node.TrafficProfiles))
        {
            revenue += profile.Revenue;
            holding += profile.Inventory * Math.Max(0d, profile.HoldingCostPerTime);
            shortage += Math.Max(0d, profile.Consumption - profile.Inventory) * Math.Max(0d, profile.ShortagePenalty);
        }

        var transport = result.Outcomes.Sum(outcome => outcome.Allocations.Sum(allocation => allocation.TotalMovementCost));
        return new EconomicSummary
        {
            TotalRevenue = revenue,
            TotalTransportCost = transport,
            TotalHoldingCost = holding,
            TotalShortagePenalty = shortage,
            TotalProfit = revenue - transport - holding - shortage
        };
    }
}

public interface IBottleneckDetectionService
{
    IReadOnlyList<NetworkIssue> DetectIssues(NetworkModel network, SimulationResult result, int maxIssues = 10);
}

public sealed class BottleneckDetectionService : IBottleneckDetectionService
{
    public IReadOnlyList<NetworkIssue> DetectIssues(NetworkModel network, SimulationResult result, int maxIssues = 10)
    {
        var issues = new List<NetworkIssue>();
        var final = result.Steps.LastOrDefault();
        if (final is not null)
        {
            foreach (var edge in network.Edges)
            {
                var capacity = edge.Capacity.GetValueOrDefault();
                if (capacity <= 0d)
                {
                    continue;
                }

                var util = final.EdgeOccupancy.GetValueOrDefault(edge.Id) / capacity;
                if (util >= 0.9d)
                {
                    issues.Add(new NetworkIssue
                    {
                        Type = NetworkIssueType.CongestedEdge,
                        Severity = util >= 1d ? NetworkIssueSeverity.Critical : NetworkIssueSeverity.Warning,
                        Title = $"Congested edge: {edge.Id}",
                        Explanation = $"Utilisation is {util:P0}, near or above the configured capacity.",
                        SuggestedAction = "Increase capacity or add an alternate path.",
                        Score = util
                    });
                }
            }
        }

        foreach (var outcome in result.Outcomes.Where(outcome => outcome.UnmetDemand > 0.01d))
        {
            issues.Add(new NetworkIssue
            {
                Type = NetworkIssueType.StarvedNode,
                Severity = NetworkIssueSeverity.Warning,
                Title = $"Unmet demand for {outcome.TrafficType}",
                Explanation = $"Unmet demand remains at {outcome.UnmetDemand:0.##}.",
                SuggestedAction = "Add supply, capacity, or lower demand.",
                Score = outcome.UnmetDemand
            });

            if (outcome.NoPermittedPathDemand > 0.01d)
            {
                issues.Add(new NetworkIssue
                {
                    Type = NetworkIssueType.PolicyBlockedFlow,
                    Severity = NetworkIssueSeverity.Critical,
                    Title = $"Policy blocked flow for {outcome.TrafficType}",
                    Explanation = $"{outcome.NoPermittedPathDemand:0.##} demand had no permitted route.",
                    SuggestedAction = "Review edge traffic permissions and policy constraints.",
                    Score = outcome.NoPermittedPathDemand
                });
            }
        }

        return issues.OrderByDescending(issue => issue.Score).Take(Math.Max(1, maxIssues)).ToList();
    }
}

public interface IExplainabilityService
{
    NodeExplanation ExplainNode(Guid nodeId, NetworkModel network, SimulationResult result);

    EdgeExplanation ExplainEdge(Guid edgeId, NetworkModel network, SimulationResult result);
}

public sealed class ExplainabilityService : IExplainabilityService
{
    public NodeExplanation ExplainNode(Guid nodeId, NetworkModel network, SimulationResult result)
    {
        var node = network.Nodes.FirstOrDefault(item => Guid.TryParse(item.Id, out var id) && id == nodeId);
        var explanation = new NodeExplanation { NodeId = nodeId };
        if (node is null)
        {
            explanation.Summary = "Node not found in this network.";
            return explanation;
        }

        explanation.Summary = $"Node '{node.Name}' currently shows mixed supply and demand pressure.";
        foreach (var profile in node.TrafficProfiles)
        {
            var unmet = Math.Max(0d, profile.Consumption - profile.Inventory);
            if (unmet <= 0d)
            {
                continue;
            }

            explanation.UnmetDemandByTrafficType[profile.TrafficType] = unmet;
            explanation.Causes.Add($"{profile.TrafficType} has unmet demand of {unmet:0.##}.");
        }

        if (explanation.Causes.Count == 0)
        {
            explanation.Causes.Add("No significant unmet demand was detected.");
        }

        explanation.SuggestedActions.Add("Check upstream capacity, policy permissions, and alternate routes.");
        return explanation;
    }

    public EdgeExplanation ExplainEdge(Guid edgeId, NetworkModel network, SimulationResult result)
    {
        var edge = network.Edges.FirstOrDefault(item => Guid.TryParse(item.Id, out var id) && id == edgeId);
        var explanation = new EdgeExplanation { EdgeId = edgeId };
        if (edge is null)
        {
            explanation.Summary = "Edge not found in this network.";
            return explanation;
        }

        explanation.Summary = $"Edge '{edge.Id}' movement performance summary.";
        var latest = result.Steps.LastOrDefault();
        if (latest is not null && edge.Capacity.HasValue && edge.Capacity.Value > 0d)
        {
            var util = latest.EdgeOccupancy.GetValueOrDefault(edge.Id) / edge.Capacity.Value;
            explanation.Causes.Add(util >= 0.9d
                ? $"Utilisation is high at {util:P0}."
                : $"Utilisation is moderate at {util:P0}.");
        }
        else
        {
            explanation.Causes.Add("No occupancy snapshot is available.");
        }

        explanation.SuggestedActions.Add("Increase capacity or redistribute demand across parallel routes.");
        return explanation;
    }
}

public interface IScenarioRunner
{
    SimulationResult RunScenario(NetworkModel network, ScenarioDefinition scenario, SimulationRunOptions options);
}

public interface IScenarioComparisonService
{
    ScenarioComparisonResult Compare(NetworkModel network, ScenarioDefinition scenario, SimulationRunOptions options);
}

public sealed class ScenarioRunner : IScenarioRunner
{
    private readonly TemporalNetworkSimulationEngine temporalEngine = new();
    private readonly NetworkSimulationEngine staticEngine = new();

    public SimulationResult RunScenario(NetworkModel network, ScenarioDefinition scenario, SimulationRunOptions options)
    {
        var clonedNetwork = Clone(network);
        var state = temporalEngine.Initialize(clonedNetwork);
        var context = new SimulationContext
        {
            Network = clonedNetwork,
            TemporalState = state,
            Options = options
        };

        var applied = new List<IScenarioEvent>();
        var steps = Math.Max(1, options.Steps);
        var timeline = new List<TemporalNetworkSimulationEngine.TemporalSimulationStepResult>(steps);
        for (var period = 0; period < steps; period++)
        {
            var now = period * options.DeltaTime;
            foreach (var evt in scenario.Events.Where(item => item.Time <= now).Except(applied))
            {
                evt.Apply(context);
                applied.Add(evt);
            }

            timeline.Add(temporalEngine.Advance(clonedNetwork, state));
        }

        applied.Reverse();
        foreach (var evt in applied)
        {
            evt.Revert(context);
        }

        return new SimulationResult
        {
            Outcomes = staticEngine.Simulate(clonedNetwork),
            Steps = timeline
        };
    }

    private static NetworkModel Clone(NetworkModel network)
    {
        var json = JsonSerializer.Serialize(network);
        return JsonSerializer.Deserialize<NetworkModel>(json) ?? new NetworkModel();
    }
}

public sealed class ScenarioComparisonService(IScenarioRunner scenarioRunner, IEconomicCalculator economicCalculator) : IScenarioComparisonService
{
    public ScenarioComparisonResult Compare(NetworkModel network, ScenarioDefinition scenario, SimulationRunOptions options)
    {
        var baseline = scenarioRunner.RunScenario(network, new ScenarioDefinition { Name = "Baseline" }, options);
        var variant = scenarioRunner.RunScenario(network, scenario, options);
        var baseEconomics = economicCalculator.Calculate(network, baseline);
        var variantEconomics = economicCalculator.Calculate(network, variant);

        return new ScenarioComparisonResult
        {
            Baseline = baseline,
            Variant = variant,
            ThroughputDelta = variant.TotalThroughput - baseline.TotalThroughput,
            CostDelta = variant.TotalCost - baseline.TotalCost,
            UnmetDemandDelta = variant.TotalUnmetDemand - baseline.TotalUnmetDemand,
            EconomicDelta = new EconomicSummary
            {
                TotalRevenue = variantEconomics.TotalRevenue - baseEconomics.TotalRevenue,
                TotalTransportCost = variantEconomics.TotalTransportCost - baseEconomics.TotalTransportCost,
                TotalHoldingCost = variantEconomics.TotalHoldingCost - baseEconomics.TotalHoldingCost,
                TotalShortagePenalty = variantEconomics.TotalShortagePenalty - baseEconomics.TotalShortagePenalty,
                TotalProfit = variantEconomics.TotalProfit - baseEconomics.TotalProfit
            }
        };
    }
}

public sealed class NodeFailureScenarioEvent : IScenarioEvent
{
    private readonly Dictionary<NodeTrafficProfile, (double Production, double Consumption)> snapshot = [];

    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; init; } = "Node failure";

    public double Time { get; init; }

    public ScenarioTargetKind TargetKind => ScenarioTargetKind.Node;

    public Guid? TargetId { get; init; }

    public void Apply(SimulationContext context)
    {
        var node = context.Network.Nodes.FirstOrDefault(item => Guid.TryParse(item.Id, out var id) && id == TargetId);
        if (node is null)
        {
            return;
        }

        snapshot.Clear();
        foreach (var profile in node.TrafficProfiles)
        {
            snapshot[profile] = (profile.Production, profile.Consumption);
            profile.Production = 0d;
            profile.Consumption = 0d;
        }
    }

    public void Revert(SimulationContext context)
    {
        foreach (var pair in snapshot)
        {
            pair.Key.Production = pair.Value.Production;
            pair.Key.Consumption = pair.Value.Consumption;
        }
    }
}

public sealed class EdgeClosureScenarioEvent : IScenarioEvent
{
    private bool? previousBidirectional;
    private double? previousCapacity;

    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; init; } = "Edge closure";

    public double Time { get; init; }

    public ScenarioTargetKind TargetKind => ScenarioTargetKind.Edge;

    public Guid? TargetId { get; init; }

    public void Apply(SimulationContext context)
    {
        var edge = context.Network.Edges.FirstOrDefault(item => Guid.TryParse(item.Id, out var id) && id == TargetId);
        if (edge is null)
        {
            return;
        }

        previousBidirectional = edge.IsBidirectional;
        previousCapacity = edge.Capacity;
        edge.IsBidirectional = false;
        edge.Capacity = 0d;
    }

    public void Revert(SimulationContext context)
    {
        var edge = context.Network.Edges.FirstOrDefault(item => Guid.TryParse(item.Id, out var id) && id == TargetId);
        if (edge is null)
        {
            return;
        }

        edge.IsBidirectional = previousBidirectional ?? edge.IsBidirectional;
        edge.Capacity = previousCapacity;
    }
}

public sealed class DemandSpikeScenarioEvent : IScenarioEvent
{
    private readonly Dictionary<NodeTrafficProfile, double> previousDemand = [];

    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; init; } = "Demand spike";

    public double Time { get; init; }

    public ScenarioTargetKind TargetKind => ScenarioTargetKind.Node;

    public Guid? TargetId { get; init; }

    public string? TrafficType { get; init; }

    public double Multiplier { get; init; } = 1.25d;

    public void Apply(SimulationContext context)
    {
        var node = context.Network.Nodes.FirstOrDefault(item => Guid.TryParse(item.Id, out var id) && id == TargetId);
        if (node is null)
        {
            return;
        }

        foreach (var profile in node.TrafficProfiles.Where(profile => string.IsNullOrWhiteSpace(TrafficType) || string.Equals(profile.TrafficType, TrafficType, StringComparison.OrdinalIgnoreCase)))
        {
            previousDemand[profile] = profile.Consumption;
            profile.Consumption *= Math.Max(1d, Multiplier);
        }
    }

    public void Revert(SimulationContext context)
    {
        foreach (var pair in previousDemand)
        {
            pair.Key.Consumption = pair.Value;
        }
    }
}

public sealed class EdgeCostChangeScenarioEvent : IScenarioEvent
{
    private double? previousCost;

    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; init; } = "Edge cost change";

    public double Time { get; init; }

    public ScenarioTargetKind TargetKind => ScenarioTargetKind.Edge;

    public Guid? TargetId { get; init; }

    public double NewCost { get; init; }

    public void Apply(SimulationContext context)
    {
        var edge = context.Network.Edges.FirstOrDefault(item => Guid.TryParse(item.Id, out var id) && id == TargetId);
        if (edge is null)
        {
            return;
        }

        previousCost = edge.Cost;
        edge.Cost = Math.Max(0d, NewCost);
    }

    public void Revert(SimulationContext context)
    {
        var edge = context.Network.Edges.FirstOrDefault(item => Guid.TryParse(item.Id, out var id) && id == TargetId);
        if (edge is null || previousCost is null)
        {
            return;
        }

        edge.Cost = previousCost.Value;
    }
}

public interface IDemandTimeSeriesImporter
{
    IReadOnlyList<DemandTimeSeriesRow> ImportCsv(string csv, bool allowNegativeDemand = false);
}

public sealed record DemandTimeSeriesRow(double Time, string Node, string TrafficType, double Demand, double? Price, double? Priority, string? Scenario);

public sealed class DemandTimeSeriesImporter : IDemandTimeSeriesImporter
{
    public IReadOnlyList<DemandTimeSeriesRow> ImportCsv(string csv, bool allowNegativeDemand = false)
    {
        var lines = csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            return [];
        }

        var headers = lines[0].Split(',').Select(value => value.Trim()).ToList();
        var map = headers.Select((value, index) => new { value, index }).ToDictionary(item => item.value, item => item.index, StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { "time", "node", "trafficType", "demand" })
        {
            if (!map.ContainsKey(required))
            {
                throw new InvalidOperationException($"Missing required CSV column '{required}'.");
            }
        }

        var rows = new List<DemandTimeSeriesRow>();
        for (var rowIndex = 1; rowIndex < lines.Length; rowIndex++)
        {
            var cols = lines[rowIndex].Split(',');
            string Read(string key) => map.TryGetValue(key, out var index) && index < cols.Length ? cols[index].Trim() : string.Empty;

            if (!double.TryParse(Read("time"), NumberStyles.Float, CultureInfo.InvariantCulture, out var time))
            {
                throw new InvalidOperationException($"Row {rowIndex + 1}: invalid time column.");
            }

            var node = Read("node");
            var traffic = Read("trafficType");
            if (string.IsNullOrWhiteSpace(node) || string.IsNullOrWhiteSpace(traffic))
            {
                throw new InvalidOperationException($"Row {rowIndex + 1}: node and trafficType are required.");
            }

            if (!double.TryParse(Read("demand"), NumberStyles.Float, CultureInfo.InvariantCulture, out var demand))
            {
                throw new InvalidOperationException($"Row {rowIndex + 1}: invalid demand value.");
            }

            if (!allowNegativeDemand && demand < 0d)
            {
                throw new InvalidOperationException($"Row {rowIndex + 1}: negative demand is not allowed.");
            }

            rows.Add(new DemandTimeSeriesRow(
                time,
                node,
                traffic,
                demand,
                TryParseOptional(Read("price")),
                TryParseOptional(Read("priority")),
                Read("scenario")));
        }

        return rows;
    }

    private static double? TryParseOptional(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}

public interface ISimulationReplayExporter
{
    void ExportJson(string path, NetworkModel network, ScenarioDefinition scenario, SimulationResult result, IReadOnlyList<NetworkIssue> issues, EconomicSummary? economics = null);

    void ExportCsv(string path, SimulationResult result, IReadOnlyList<NetworkIssue> issues, EconomicSummary? economics = null);
}

public sealed class SimulationReplayExporter : ISimulationReplayExporter
{
    public void ExportJson(string path, NetworkModel network, ScenarioDefinition scenario, SimulationResult result, IReadOnlyList<NetworkIssue> issues, EconomicSummary? economics = null)
    {
        var payload = new
        {
            network.Name,
            scenario,
            result.TotalThroughput,
            result.TotalUnmetDemand,
            result.TotalCost,
            NodeSummaries = network.Nodes.Select(node => new { node.Id, node.Name, Traffic = node.TrafficProfiles.Count }),
            EdgeSummaries = network.Edges.Select(edge => new { edge.Id, edge.FromNodeId, edge.ToNodeId, edge.Capacity }),
            Issues = issues,
            Economics = economics
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void ExportCsv(string path, SimulationResult result, IReadOnlyList<NetworkIssue> issues, EconomicSummary? economics = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("metric,value");
        builder.AppendLine($"throughput,{result.TotalThroughput.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"unmetDemand,{result.TotalUnmetDemand.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"cost,{result.TotalCost.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"issueCount,{issues.Count}");
        if (economics is not null)
        {
            builder.AppendLine($"profit,{economics.TotalProfit.ToString(CultureInfo.InvariantCulture)}");
        }

        File.WriteAllText(path, builder.ToString());
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public interface INetworkLayerService
{
    void EnsureLayerIntegrity(NetworkModel network);

    NetworkLayerModel GetDefaultLayer(NetworkModel network);

    IReadOnlyList<NetworkLayerModel> GetSimulationOrder(NetworkModel network);

    IReadOnlyList<NodeModel> GetNodesInLayer(NetworkModel network, Guid layerId);

    IReadOnlyList<EdgeModel> GetEdgesInLayer(NetworkModel network, Guid layerId);
}

public interface INetworkLayerResolver
{
    IReadOnlyList<NetworkLayerModel> GetSimulationOrder(NetworkModel network);

    NetworkLayerModel GetDefaultLayer(NetworkModel network);
}

public sealed class NetworkLayerResolver : INetworkLayerResolver, INetworkLayerService
{
    public IReadOnlyList<NetworkLayerModel> GetSimulationOrder(NetworkModel network)
    {
        ArgumentNullException.ThrowIfNull(network);
        EnsureLayerIntegrity(network);
        return network.Layers
            .OrderBy(layer => layer.Type == NetworkLayerType.Physical ? 0 : layer.Type == NetworkLayerType.Logical ? 1 : 2)
            .ThenBy(layer => layer.Order)
            .ThenBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public NetworkLayerModel GetDefaultLayer(NetworkModel network)
    {
        EnsureLayerIntegrity(network);
        return network.Layers.First(layer => layer.Type == NetworkLayerType.Physical);
    }

    public IReadOnlyList<NodeModel> GetNodesInLayer(NetworkModel network, Guid layerId)
    {
        EnsureLayerIntegrity(network);
        return network.Nodes.Where(node => node.LayerId == layerId).ToList();
    }

    public IReadOnlyList<EdgeModel> GetEdgesInLayer(NetworkModel network, Guid layerId)
    {
        EnsureLayerIntegrity(network);
        return network.Edges.Where(edge => edge.LayerId == layerId).ToList();
    }

    public void EnsureLayerIntegrity(NetworkModel network)
    {
        network.Layers ??= [];
        if (network.Layers.Count == 0)
        {
            network.Layers.Add(new NetworkLayerModel { Name = "Physical", Type = NetworkLayerType.Physical, Order = 0, IsVisible = true });
        }

        foreach (var layer in network.Layers.Where(layer => layer is not null && layer.Id == Guid.Empty))
        {
            layer.Id = Guid.NewGuid();
        }

        var unique = network.Layers
            .Where(layer => layer is not null)
            .GroupBy(layer => layer.Id)
            .Select(group => group.First())
            .ToList();

        network.Layers = unique;

        foreach (var type in Enum.GetValues<NetworkLayerType>())
        {
            if (network.Layers.Any(layer => layer.Type == type))
            {
                continue;
            }

            network.Layers.Add(new NetworkLayerModel
            {
                Name = type.ToString(),
                Type = type,
                Order = type == NetworkLayerType.Physical ? 0 : type == NetworkLayerType.Logical ? 1 : 2,
                IsVisible = true
            });
        }

        var order = 0;
        foreach (var layer in network.Layers
                     .OrderBy(layer => layer.Type == NetworkLayerType.Physical ? 0 : layer.Type == NetworkLayerType.Logical ? 1 : 2)
                     .ThenBy(layer => layer.Order))
        {
            layer.Order = order++;
            layer.Name = string.IsNullOrWhiteSpace(layer.Name) ? layer.Type.ToString() : layer.Name.Trim();
        }

        var validIds = network.Layers.Select(layer => layer.Id).ToHashSet();
        var defaultLayerId = network.Layers.First(layer => layer.Type == NetworkLayerType.Physical).Id;
        foreach (var node in network.Nodes)
        {
            if (node.LayerId == Guid.Empty || !validIds.Contains(node.LayerId))
            {
                node.LayerId = defaultLayerId;
            }
        }

        foreach (var edge in network.Edges)
        {
            if (edge.LayerId == Guid.Empty || !validIds.Contains(edge.LayerId))
            {
                edge.LayerId = defaultLayerId;
            }
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
        var revenueByNodeTraffic = network.Nodes
            .SelectMany(node => node.TrafficProfiles.Select(profile => new { node.Id, profile.TrafficType, UnitPrice = Math.Max(0d, profile.UnitPrice) }))
            .ToDictionary(item => (item.Id, item.TrafficType), item => item.UnitPrice);
        var shortagePenaltyByTraffic = network.Nodes
            .SelectMany(node => node.TrafficProfiles.Select(profile => new { profile.TrafficType, Penalty = Math.Max(0d, profile.ShortagePenalty) }))
            .GroupBy(item => item.TrafficType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Max(item => item.Penalty), StringComparer.OrdinalIgnoreCase);

        var revenue = result.Outcomes
            .Sum(outcome => outcome.Allocations.Sum(allocation =>
            {
                var key = (allocation.ConsumerNodeId, allocation.TrafficType);
                var unitPrice = revenueByNodeTraffic.GetValueOrDefault(key, 0d);
                return allocation.Quantity * unitPrice;
            }));
        var holding = 0d;
        var shortage = result.Outcomes.Sum(outcome =>
            Math.Max(0d, outcome.UnmetDemand) * shortagePenaltyByTraffic.GetValueOrDefault(outcome.TrafficType, 0d));

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
                        TargetId = edge.Id,
                        TargetName = edge.Id,
                        Title = "Edge is near capacity",
                        Explanation = $"This route is using {util:P0} of its capacity, so delays may increase.",
                        SuggestedAction = "Increase capacity, add another route, or reduce demand through this edge.",
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
                TargetId = outcome.TrafficType,
                TargetName = outcome.TrafficType,
                Title = "Node demand is not met",
                Explanation = $"Unmet demand remains at {outcome.UnmetDemand:0.##} for this traffic type.",
                SuggestedAction = "Add supply, increase route capacity, or reduce demand at the node.",
                Score = outcome.UnmetDemand
            });

            if (outcome.NoPermittedPathDemand > 0.01d)
            {
                issues.Add(new NetworkIssue
                {
                    Type = NetworkIssueType.PolicyBlockedFlow,
                    Severity = NetworkIssueSeverity.Critical,
                    TargetId = outcome.TrafficType,
                    TargetName = outcome.TrafficType,
                    Title = "Flow blocked by policy",
                    Explanation = $"{outcome.NoPermittedPathDemand:0.##} demand had no permitted route.",
                    SuggestedAction = "Review policy permissions and allow at least one valid route for this traffic type.",
                    Score = outcome.NoPermittedPathDemand
                });
            }
        }

        return issues.OrderByDescending(issue => issue.Score).Take(Math.Max(1, maxIssues)).ToList();
    }
}

public interface IExplainabilityService
{
    NodeExplanation ExplainNode(NetworkModel network, SimulationResult result, string nodeId);

    EdgeExplanation ExplainEdge(NetworkModel network, SimulationResult result, string edgeId);
}

public sealed class ExplainabilityService : IExplainabilityService
{
    public NodeExplanation ExplainNode(NetworkModel network, SimulationResult result, string nodeId)
    {
        var node = network.Nodes.FirstOrDefault(item => string.Equals(item.Id, nodeId, StringComparison.OrdinalIgnoreCase));
        var explanation = new NodeExplanation { NodeId = nodeId, NodeName = node?.Name ?? string.Empty };
        if (node is null)
        {
            explanation.Summary = "Node not found in this network. Select a node from the map and rerun the simulation.";
            return explanation;
        }

        explanation.Summary = $"Why this matters: node '{node.Name}' has supply-demand pressure that can reduce service reliability.";
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
            explanation.Causes.Add("Run a simulation to see why this node is constrained.");
        }

        explanation.SuggestedActions.Add("Check upstream capacity, policy permissions, and alternate routes.");
        return explanation;
    }

    public EdgeExplanation ExplainEdge(NetworkModel network, SimulationResult result, string edgeId)
    {
        var edge = network.Edges.FirstOrDefault(item => string.Equals(item.Id, edgeId, StringComparison.OrdinalIgnoreCase));
        var explanation = new EdgeExplanation { EdgeId = edgeId, EdgeName = edge?.Id ?? string.Empty };
        if (edge is null)
        {
            explanation.Summary = "Edge not found in this network. Select a route on the map and rerun the simulation.";
            return explanation;
        }

        explanation.Summary = $"Why this matters: edge '{edge.Id}' can become a bottleneck and delay downstream demand.";
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
        foreach (var rule in network.PolicyRules.Where(rule => rule.IsEnabled && rule.Effect is PolicyRuleEffect.BlockTraffic or PolicyRuleEffect.AllowOnlyTraffic))
        {
            if (!string.IsNullOrWhiteSpace(rule.TargetEdgeId) && string.Equals(rule.TargetEdgeId, edgeId, StringComparison.OrdinalIgnoreCase))
            {
                explanation.Causes.Add($"This route is blocked by the policy rule '{rule.Name}'.");
            }
        }

        return explanation;
    }
}

public interface IScenarioRunner
{
    ScenarioRunResult Run(NetworkModel sourceNetwork, ScenarioDefinitionModel scenario, ScenarioRunOptions options);
}

public sealed class ScenarioRunner : IScenarioRunner
{
    private const double ComparisonTolerance = 0.000001d;

    private readonly NetworkSimulationEngine simulationEngine;
    private readonly INetworkLayerService networkLayerService;
    private readonly IBottleneckDetectionService bottleneckDetectionService;

    public ScenarioRunner(
        NetworkSimulationEngine? simulationEngine = null,
        INetworkLayerService? networkLayerService = null,
        IBottleneckDetectionService? bottleneckDetectionService = null)
    {
        this.simulationEngine = simulationEngine ?? new NetworkSimulationEngine();
        this.networkLayerService = networkLayerService ?? new NetworkLayerResolver();
        this.bottleneckDetectionService = bottleneckDetectionService ?? new BottleneckDetectionService();
    }

    public ScenarioRunResult Run(NetworkModel sourceNetwork, ScenarioDefinitionModel scenario, ScenarioRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(sourceNetwork);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(options);

        var warnings = new List<string>();
        var clonedNetwork = Clone(sourceNetwork);
        networkLayerService.EnsureLayerIntegrity(clonedNetwork);

        foreach (var evt in scenario.Events
                     .Where(evt => evt is not null && evt.IsEnabled)
                     .OrderBy(evt => evt.Time)
                     .ThenBy(evt => evt.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (evt.Time > options.EndTime + ComparisonTolerance)
            {
                continue;
            }

            if (evt.EndTime.HasValue && evt.EndTime.Value <= options.EndTime + ComparisonTolerance)
            {
                continue;
            }

            if (!TryApplyEvent(clonedNetwork, evt, warnings))
            {
                continue;
            }
        }

        var simulation = new SimulationResult { Outcomes = simulationEngine.Simulate(clonedNetwork) };
        var issues = bottleneckDetectionService.DetectIssues(clonedNetwork, simulation);
        return new ScenarioRunResult
        {
            ScenarioName = scenario.Name,
            SimulationResult = simulation,
            Issues = issues,
            Warnings = warnings
        };
    }

    private static bool TryApplyEvent(NetworkModel network, ScenarioEventModel evt, List<string> warnings)
    {
        switch (evt.Kind)
        {
            case ScenarioEventKind.NodeFailure:
                if (evt.TargetKind != ScenarioTargetKind.Node || evt.TargetId is null)
                {
                    warnings.Add("Choose a node for this node failure event.");
                    return false;
                }

                var node = network.Nodes.FirstOrDefault(item => string.Equals(item.Id, evt.TargetId, StringComparison.OrdinalIgnoreCase));
                if (node is null)
                {
                    warnings.Add($"Skipped event '{evt.Name}': target node was not found.");
                    return false;
                }

                foreach (var profile in node.TrafficProfiles)
                {
                    profile.Production = 0d;
                    profile.Consumption = 0d;
                    profile.CanTransship = false;
                }

                return true;
            case ScenarioEventKind.EdgeClosure:
                if (evt.TargetKind != ScenarioTargetKind.Edge || evt.TargetId is null)
                {
                    warnings.Add("Choose an edge for this edge closure.");
                    return false;
                }

                var edge = network.Edges.FirstOrDefault(item => string.Equals(item.Id, evt.TargetId, StringComparison.OrdinalIgnoreCase));
                if (edge is null)
                {
                    warnings.Add($"Skipped event '{evt.Name}': target edge was not found.");
                    return false;
                }

                edge.Capacity = 0d;
                return true;
            case ScenarioEventKind.DemandSpike:
                if (evt.TargetKind != ScenarioTargetKind.Node || evt.TargetId is null || string.IsNullOrWhiteSpace(evt.TrafficTypeIdOrName))
                {
                    warnings.Add("Choose a node for this demand spike.");
                    return false;
                }

                var spikeNode = network.Nodes.FirstOrDefault(item => string.Equals(item.Id, evt.TargetId, StringComparison.OrdinalIgnoreCase));
                if (spikeNode is null)
                {
                    warnings.Add($"Skipped event '{evt.Name}': target node was not found.");
                    return false;
                }

                foreach (var profile in spikeNode.TrafficProfiles.Where(p => string.Equals(p.TrafficType, evt.TrafficTypeIdOrName, StringComparison.OrdinalIgnoreCase)))
                {
                    profile.Consumption *= Math.Max(1d, evt.Value);
                }

                return true;
            case ScenarioEventKind.EdgeCostChange:
                if (evt.TargetKind != ScenarioTargetKind.Edge || evt.TargetId is null)
                {
                    warnings.Add("Choose an edge for this edge cost change.");
                    return false;
                }

                var targetEdge = network.Edges.FirstOrDefault(item => string.Equals(item.Id, evt.TargetId, StringComparison.OrdinalIgnoreCase));
                if (targetEdge is null)
                {
                    warnings.Add($"Skipped event '{evt.Name}': target edge was not found.");
                    return false;
                }

                targetEdge.Cost *= Math.Max(0d, evt.Value);
                return true;
            default:
                warnings.Add($"Skipped unsupported event type '{evt.Kind}'.");
                return false;
        }
    }

    private static NetworkModel Clone(NetworkModel network)
    {
        var json = JsonSerializer.Serialize(network);
        return JsonSerializer.Deserialize<NetworkModel>(json) ?? new NetworkModel();
    }
}

public sealed class NodeFailureScenarioEvent : IScenarioEvent
{
    private readonly Dictionary<NodeTrafficProfile, (double Production, double Consumption)> snapshot = [];

    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; init; } = "Node failure";

    public double Time { get; init; }

    public ScenarioTargetKind TargetKind => ScenarioTargetKind.Node;

    public string? TargetId { get; init; }

    public void Apply(SimulationContext context)
    {
        var node = context.Network.Nodes.FirstOrDefault(item => string.Equals(item.Id, TargetId, StringComparison.OrdinalIgnoreCase));
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

    public string? TargetId { get; init; }

    public void Apply(SimulationContext context)
    {
        var edge = context.Network.Edges.FirstOrDefault(item => string.Equals(item.Id, TargetId, StringComparison.OrdinalIgnoreCase));
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
        var edge = context.Network.Edges.FirstOrDefault(item => string.Equals(item.Id, TargetId, StringComparison.OrdinalIgnoreCase));
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

    public string? TargetId { get; init; }

    public string? TrafficType { get; init; }

    public double Multiplier { get; init; } = 1.25d;

    public void Apply(SimulationContext context)
    {
        var node = context.Network.Nodes.FirstOrDefault(item => string.Equals(item.Id, TargetId, StringComparison.OrdinalIgnoreCase));
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

    public string? TargetId { get; init; }

    public double NewCost { get; init; }

    public void Apply(SimulationContext context)
    {
        var edge = context.Network.Edges.FirstOrDefault(item => string.Equals(item.Id, TargetId, StringComparison.OrdinalIgnoreCase));
        if (edge is null)
        {
            return;
        }

        previousCost = edge.Cost;
        edge.Cost = Math.Max(0d, NewCost);
    }

    public void Revert(SimulationContext context)
    {
        var edge = context.Network.Edges.FirstOrDefault(item => string.Equals(item.Id, TargetId, StringComparison.OrdinalIgnoreCase));
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

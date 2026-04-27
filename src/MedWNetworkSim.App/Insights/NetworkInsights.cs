using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.Insights;

public enum InsightSeverity { Info, Warning, Critical }
public enum InsightCategory { UnmetDemand, Capacity, Restriction, Cost, Connectivity, LayerConflict, ScenarioRegression }
public enum InsightTarget { Network, Node, Edge, Route }

public sealed class InsightCause
{
    public required string Summary { get; init; }
    public required string Evidence { get; init; }
}

public sealed class InsightRecommendation
{
    public required string Action { get; init; }
    public string? TargetHint { get; init; }
}

public sealed class NetworkInsight
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required InsightSeverity Severity { get; init; }
    public required InsightCategory Category { get; init; }
    public required InsightTarget TargetType { get; init; }
    public string? TargetNodeId { get; init; }
    public string? TargetEdgeId { get; init; }
    public IReadOnlyList<InsightCause> Causes { get; init; } = [];
    public IReadOnlyList<InsightRecommendation> Recommendations { get; init; } = [];
}

public interface INetworkInsightService
{
    IReadOnlyList<NetworkInsight> Generate(VisualAnalytics.VisualAnalyticsSnapshot snapshot);
}

public sealed class NetworkInsightService : INetworkInsightService
{
    private readonly EdgeTrafficPermissionResolver permissionResolver = new();

    public IReadOnlyList<NetworkInsight> Generate(VisualAnalytics.VisualAnalyticsSnapshot snapshot)
    {
        var insights = new List<NetworkInsight>();
        var network = snapshot.Network;
        var routedByEdgeId = BuildRoutedQuantityByEdge(snapshot);

        foreach (var outcome in snapshot.TrafficOutcomes)
        {
            if (outcome.UnmetDemand > 0d)
            {
                insights.Add(new NetworkInsight
                {
                    Id = $"unmet:{outcome.TrafficType}",
                    Title = $"Unmet {outcome.TrafficType} demand detected",
                    Summary = $"{outcome.UnmetDemand:0.##} units were not delivered for {outcome.TrafficType}.",
                    Severity = InsightSeverity.Critical,
                    Category = InsightCategory.UnmetDemand,
                    TargetType = InsightTarget.Network,
                    Causes = [new InsightCause { Summary = "Demand exceeds delivered supply", Evidence = $"Delivered {outcome.TotalDelivered:0.##} of {outcome.TotalConsumption:0.##}." }],
                    Recommendations = [new InsightRecommendation { Action = "Increase supply, capacity, or alternate routing for this traffic type.", TargetHint = outcome.TrafficType }]
                });
            }

            if (outcome.NoPermittedPathDemand > 0d)
            {
                insights.Add(new NetworkInsight
                {
                    Id = $"restrict:{outcome.TrafficType}",
                    Title = $"Route restrictions are blocking {outcome.TrafficType}",
                    Summary = $"{outcome.NoPermittedPathDemand:0.##} units had no permitted path.",
                    Severity = InsightSeverity.Warning,
                    Category = InsightCategory.Restriction,
                    TargetType = InsightTarget.Network,
                    Causes = [new InsightCause { Summary = "Edge permissions prevented routing", Evidence = "NoPermittedPathDemand was greater than zero." }],
                    Recommendations = [new InsightRecommendation { Action = "Review traffic permissions on constrained edges." }]
                });
            }
        }

        foreach (var edge in network.Edges.Where(edge => edge.Capacity.HasValue && edge.Capacity.Value > 0d))
        {
            var routed = routedByEdgeId.TryGetValue(edge.Id, out var quantity) ? quantity : 0d;

            var ratio = routed / edge.Capacity!.Value;
            if (ratio >= 0.9d)
            {
                insights.Add(new NetworkInsight
                {
                    Id = $"cap:{edge.Id}",
                    Title = "Capacity bottleneck risk",
                    Summary = $"Route {edge.Id} is using {ratio:P0} of capacity.",
                    Severity = InsightSeverity.Warning,
                    Category = InsightCategory.Capacity,
                    TargetType = InsightTarget.Edge,
                    TargetEdgeId = edge.Id,
                    Causes = [new InsightCause { Summary = "Edge flow is near capacity", Evidence = $"Flow {routed:0.##} / Capacity {edge.Capacity.Value:0.##}." }],
                    Recommendations = [new InsightRecommendation { Action = "Increase capacity or add a parallel route.", TargetHint = edge.Id }]
                });
            }
        }

        var highCost = snapshot.ConsumerCosts.OrderByDescending(cost => cost.BlendedUnitCost).FirstOrDefault();
        if (highCost is not null && highCost.BlendedUnitCost > 0d)
        {
            insights.Add(new NetworkInsight
            {
                Id = $"cost:{highCost.ConsumerNodeId}:{highCost.TrafficType}",
                Title = "High delivered movement cost",
                Summary = $"{highCost.ConsumerName} has the highest blended unit cost ({highCost.BlendedUnitCost:0.##}).",
                Severity = InsightSeverity.Info,
                Category = InsightCategory.Cost,
                TargetType = InsightTarget.Node,
                TargetNodeId = highCost.ConsumerNodeId,
                Causes = [new InsightCause { Summary = "Long or expensive inbound routes", Evidence = $"Imported unit cost: {highCost.ImportedUnitCost:0.##}." }],
                Recommendations = [new InsightRecommendation { Action = "Consider nearer supply or cheaper routes.", TargetHint = highCost.ConsumerName }]
            });
        }

        if (CountConnectedComponents(network) > 1)
        {
            insights.Add(new NetworkInsight
            {
                Id = "connectivity:disconnected",
                Title = "Network has disconnected components",
                Summary = "Some nodes are unreachable from others.",
                Severity = InsightSeverity.Warning,
                Category = InsightCategory.Connectivity,
                TargetType = InsightTarget.Network,
                Causes = [new InsightCause { Summary = "Disconnected graph", Evidence = "At least two components were found in the current graph." }],
                Recommendations = [new InsightRecommendation { Action = "Add connecting routes between isolated node groups." }]
            });
        }

        var nodeIds = network.Nodes.Select(node => node.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (network.Edges.Any(edge => !nodeIds.Contains(edge.FromNodeId) || !nodeIds.Contains(edge.ToNodeId)))
        {
            insights.Add(new NetworkInsight
            {
                Id = "layer:conflict",
                Title = "Layer conflict or orphaned route",
                Summary = "At least one route endpoint does not map cleanly to known nodes/layers.",
                Severity = InsightSeverity.Warning,
                Category = InsightCategory.LayerConflict,
                TargetType = InsightTarget.Network,
                Causes = [new InsightCause { Summary = "Inconsistent layer wiring", Evidence = "An edge endpoint does not resolve to a node in the active network." }],
                Recommendations = [new InsightRecommendation { Action = "Reconcile edge endpoints and layer assignments." }]
            });
        }

        return insights.OrderByDescending(item => item.Severity).ThenBy(item => item.Title, StringComparer.Ordinal).ToArray();
    }

    private static int CountConnectedComponents(NetworkModel network)
    {
        var map = network.Nodes.ToDictionary(node => node.Id, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var edge in network.Edges)
        {
            if (map.TryGetValue(edge.FromNodeId, out var from))
            {
                from.Add(edge.ToNodeId);
            }
            if (map.TryGetValue(edge.ToNodeId, out var to))
            {
                to.Add(edge.FromNodeId);
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = 0;
        foreach (var nodeId in map.Keys)
        {
            if (!visited.Add(nodeId))
            {
                continue;
            }

            components++;
            var queue = new Queue<string>();
            queue.Enqueue(nodeId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in map[current])
                {
                    if (visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        return components;
    }

    private static Dictionary<string, double> BuildRoutedQuantityByEdge(VisualAnalytics.VisualAnalyticsSnapshot snapshot)
    {
        var routedByEdgeId = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var allocation in snapshot.TrafficOutcomes.SelectMany(outcome => outcome.Allocations))
        {
            if (allocation.Quantity <= 0d || allocation.PathEdgeIds is null)
            {
                continue;
            }

            foreach (var edgeId in allocation.PathEdgeIds)
            {
                if (string.IsNullOrWhiteSpace(edgeId))
                {
                    continue;
                }

                routedByEdgeId.TryGetValue(edgeId, out var current);
                routedByEdgeId[edgeId] = current + allocation.Quantity;
            }
        }

        return routedByEdgeId;
    }
}

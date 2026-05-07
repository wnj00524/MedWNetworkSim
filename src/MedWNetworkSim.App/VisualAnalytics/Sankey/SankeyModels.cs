using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.VisualAnalytics.Sankey;
/// <summary>
/// Specifies the sankey node kind.
/// </summary>

public enum SankeyNodeKind
{
    Source,
    Transit,
    Sink,
    UnmetDemandSink,
    CollapsedOther
}
/// <summary>
/// Represents the sankey node component.
/// </summary>

public sealed class SankeyNode
{
    /// <summary>
    /// Gets or sets the unique identifier for this instance.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or sets the label.
    /// </summary>
    public required string Label { get; init; }
    /// <summary>
    /// Gets or sets the kind.
    /// </summary>
    public required SankeyNodeKind Kind { get; init; }
    /// <summary>
    /// Gets or sets the graph node id.
    /// </summary>
    public string? GraphNodeId { get; init; }
    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    public double Value { get; set; }
}
/// <summary>
/// Represents the sankey link component.
/// </summary>

public sealed class SankeyLink
{
    /// <summary>
    /// Gets or sets the unique identifier for this instance.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or sets the source node id.
    /// </summary>
    public required string SourceNodeId { get; init; }
    /// <summary>
    /// Gets or sets the target node id.
    /// </summary>
    public required string TargetNodeId { get; init; }
    /// <summary>
    /// Gets or sets the traffic type.
    /// </summary>
    public required string TrafficType { get; init; }
    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    public required double Value { get; init; }
    /// <summary>
    /// Gets the collection of route edge ids associated with this entity.
    /// </summary>
    public IReadOnlyList<string> RouteEdgeIds { get; init; } = [];
    /// <summary>
    /// Gets or sets the route signature.
    /// </summary>
    public string? RouteSignature { get; init; }
    /// <summary>
    /// Gets a value indicating whether is unmet demand is enabled or active.
    /// </summary>
    public bool IsUnmetDemand { get; init; }
}
/// <summary>
/// Represents a data model for sankey diagram entities within the simulation.
/// </summary>

public sealed class SankeyDiagramModel
{
    /// <summary>
    /// Gets the collection of nodes associated with this entity.
    /// </summary>
    public IReadOnlyList<SankeyNode> Nodes { get; init; } = [];
    /// <summary>
    /// Gets the collection of links associated with this entity.
    /// </summary>
    public IReadOnlyList<SankeyLink> Links { get; init; } = [];
    /// <summary>
    /// Gets or sets the empty state message.
    /// </summary>
    public string EmptyStateMessage { get; init; } = string.Empty;
}
/// <summary>
/// Represents the sankey projection options component.
/// </summary>

public sealed class SankeyProjectionOptions
{
    /// <summary>
    /// Gets or sets the traffic type filter.
    /// </summary>
    public string? TrafficTypeFilter { get; init; }
    /// <summary>
    /// Gets a value indicating whether include unmet demand sink is enabled or active.
    /// </summary>
    public bool IncludeUnmetDemandSink { get; init; } = true;
    /// <summary>
    /// Gets or sets the minor flow threshold ratio.
    /// </summary>
    public double MinorFlowThresholdRatio { get; init; } = 0.02d;
    /// <summary>
    /// Gets a value indicating whether collapse minor flows is enabled or active.
    /// </summary>
    public bool CollapseMinorFlows { get; init; } = true;
}
/// <summary>
/// Provides business logic and operations related to isankey projection.
/// </summary>

public interface ISankeyProjectionService
{
    SankeyDiagramModel Build(VisualAnalyticsSnapshot snapshot, SankeyProjectionOptions? options = null);
}
/// <summary>
/// Provides business logic and operations related to sankey projection.
/// </summary>

public sealed class SankeyProjectionService : ISankeyProjectionService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    /// <summary>
    /// Executes the build operation.
    /// </summary>

    public SankeyDiagramModel Build(VisualAnalyticsSnapshot snapshot, SankeyProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options ??= new SankeyProjectionOptions();

        if (snapshot.TrafficOutcomes.Count == 0)
        {
            return new SankeyDiagramModel { EmptyStateMessage = "Run a simulation to build the Sankey view." };
        }

        var filteredOutcomes = string.IsNullOrWhiteSpace(options.TrafficTypeFilter)
            ? snapshot.TrafficOutcomes
            : snapshot.TrafficOutcomes.Where(o => Comparer.Equals(o.TrafficType, options.TrafficTypeFilter)).ToArray();

        var grouped = filteredOutcomes
            .SelectMany(outcome => outcome.Allocations.Select(a => (Outcome: outcome, Allocation: a)))
            .Where(tuple => tuple.Allocation.Quantity > 0d)
            .GroupBy(tuple => (tuple.Allocation.ProducerNodeId, tuple.Allocation.ConsumerNodeId, tuple.Outcome.TrafficType), tuple => tuple.Allocation)
            .Select(group => new
            {
                group.Key.ProducerNodeId,
                group.Key.ConsumerNodeId,
                group.Key.TrafficType,
                Quantity = group.Sum(item => item.Quantity),
                RouteEdgeIds = group.SelectMany(item => item.PathEdgeIds ?? []).Distinct(Comparer).ToArray(),
                RouteSignature = BuildDominantRouteSignature(group)
            })
            .Where(item => item.Quantity > 0d)
            .ToList();

        if (grouped.Count == 0)
        {
            return new SankeyDiagramModel { EmptyStateMessage = "Run a simulation to build the Sankey view." };
        }

        var totalFlow = grouped.Sum(item => item.Quantity);
        var collapseThreshold = totalFlow * Math.Max(0d, options.MinorFlowThresholdRatio);
        var includeCollapsed = options.CollapseMinorFlows && collapseThreshold > 0d;

        var nodes = new Dictionary<string, SankeyNode>(Comparer);
        var links = new List<SankeyLink>();

        foreach (var flow in grouped)
        {
            var source = snapshot.Network.Nodes.FirstOrDefault(n => Comparer.Equals(n.Id, flow.ProducerNodeId));
            var target = snapshot.Network.Nodes.FirstOrDefault(n => Comparer.Equals(n.Id, flow.ConsumerNodeId));

            var sourceNodeId = $"node:{flow.ProducerNodeId}";
            var targetNodeId = $"node:{flow.ConsumerNodeId}";

            EnsureNode(nodes, sourceNodeId, source?.Name ?? flow.ProducerNodeId, SankeyNodeKind.Source, flow.ProducerNodeId, flow.Quantity);
            EnsureNode(nodes, targetNodeId, target?.Name ?? flow.ConsumerNodeId, SankeyNodeKind.Sink, flow.ConsumerNodeId, flow.Quantity);

            if (includeCollapsed && flow.Quantity < collapseThreshold)
            {
                EnsureNode(nodes, "node:collapsed", "Other minor flows", SankeyNodeKind.CollapsedOther, null, flow.Quantity);
                links.Add(new SankeyLink
                {
                    Id = $"link:{flow.ProducerNodeId}:{flow.ConsumerNodeId}:{flow.TrafficType}:collapsed",
                    SourceNodeId = sourceNodeId,
                    TargetNodeId = "node:collapsed",
                    TrafficType = flow.TrafficType,
                    Value = flow.Quantity,
                    RouteEdgeIds = flow.RouteEdgeIds,
                    RouteSignature = flow.RouteSignature
                });

                links.Add(new SankeyLink
                {
                    Id = $"link:collapsed:{flow.ConsumerNodeId}:{flow.TrafficType}",
                    SourceNodeId = "node:collapsed",
                    TargetNodeId = targetNodeId,
                    TrafficType = flow.TrafficType,
                    Value = flow.Quantity,
                    RouteEdgeIds = flow.RouteEdgeIds,
                    RouteSignature = flow.RouteSignature
                });

                continue;
            }

            links.Add(new SankeyLink
            {
                Id = $"link:{flow.ProducerNodeId}:{flow.ConsumerNodeId}:{flow.TrafficType}",
                SourceNodeId = sourceNodeId,
                TargetNodeId = targetNodeId,
                TrafficType = flow.TrafficType,
                Value = flow.Quantity,
                RouteEdgeIds = flow.RouteEdgeIds,
                RouteSignature = flow.RouteSignature
            });
        }

        if (options.IncludeUnmetDemandSink)
        {
            foreach (var outcome in filteredOutcomes.Where(outcome => outcome.UnmetDemand > 0d))
            {
                EnsureNode(nodes, "node:unmet", "Unmet demand", SankeyNodeKind.UnmetDemandSink, null, outcome.UnmetDemand);
                links.Add(new SankeyLink
                {
                    Id = $"link:unmet:{outcome.TrafficType}",
                    SourceNodeId = "node:unmet",
                    TargetNodeId = "node:unmet",
                    TrafficType = outcome.TrafficType,
                    Value = outcome.UnmetDemand,
                    IsUnmetDemand = true
                });
            }
        }

        return new SankeyDiagramModel
        {
            Nodes = nodes.Values.OrderByDescending(n => n.Value).ToArray(),
            Links = links
        };
    }

    private static void EnsureNode(IDictionary<string, SankeyNode> nodes, string id, string label, SankeyNodeKind kind, string? graphNodeId, double value)
    {
        if (!nodes.TryGetValue(id, out var node))
        {
            node = new SankeyNode { Id = id, Label = label, Kind = kind, GraphNodeId = graphNodeId, Value = 0d };
            nodes[id] = node;
        }

        node.Value += value;
    }

    private static string? BuildDominantRouteSignature(IEnumerable<RouteAllocation> allocations)
    {
        var dominantRoute = allocations
            .Where(item => item.PathNodeIds is { Count: > 0 })
            .Select(item => new { item.Quantity, Signature = string.Join(" -> ", item.PathNodeIds!) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Signature))
            .GroupBy(item => item.Signature, item => item.Quantity, Comparer)
            .Select(group => new { Signature = group.Key, Quantity = group.Sum() })
            .OrderByDescending(item => item.Quantity)
            .ThenBy(item => item.Signature, Comparer)
            .FirstOrDefault();

        return dominantRoute?.Signature;
    }
}

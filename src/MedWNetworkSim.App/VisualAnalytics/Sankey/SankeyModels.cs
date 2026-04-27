using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.VisualAnalytics.Sankey;

public enum SankeyNodeKind
{
    Source,
    Transit,
    Sink,
    UnmetDemandSink,
    CollapsedOther
}

public sealed class SankeyNode
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required SankeyNodeKind Kind { get; init; }
    public string? GraphNodeId { get; init; }
    public double Value { get; set; }
}

public sealed class SankeyLink
{
    public required string Id { get; init; }
    public required string SourceNodeId { get; init; }
    public required string TargetNodeId { get; init; }
    public required string TrafficType { get; init; }
    public required double Value { get; init; }
    public IReadOnlyList<string> RouteEdgeIds { get; init; } = [];
    public string? RouteSignature { get; init; }
    public bool IsUnmetDemand { get; init; }
}

public sealed class SankeyDiagramModel
{
    public IReadOnlyList<SankeyNode> Nodes { get; init; } = [];
    public IReadOnlyList<SankeyLink> Links { get; init; } = [];
    public string EmptyStateMessage { get; init; } = string.Empty;
}

public sealed class SankeyProjectionOptions
{
    public string? TrafficTypeFilter { get; init; }
    public bool IncludeUnmetDemandSink { get; init; } = true;
    public double MinorFlowThresholdRatio { get; init; } = 0.02d;
    public bool CollapseMinorFlows { get; init; } = true;
}

public interface ISankeyProjectionService
{
    SankeyDiagramModel Build(VisualAnalyticsSnapshot snapshot, SankeyProjectionOptions? options = null);
}

public sealed class SankeyProjectionService : ISankeyProjectionService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

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
                RouteSignature = string.Join(" -> ", group.SelectMany(item => item.PathNodeIds ?? []).Distinct(Comparer))
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
}

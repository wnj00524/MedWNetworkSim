using System.Globalization;
using System.IO;
using System.Text;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

/// <summary>
/// Builds human-readable Markdown reports from the current network and simulation outputs.
/// </summary>
public sealed class ReportExportService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private readonly NetworkSimulationEngine networkSimulationEngine = new();
    private readonly TemporalNetworkSimulationEngine temporalNetworkSimulationEngine = new();

    public void SaveCurrentReport(NetworkModel network, string path, ReportExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var contents = format == ReportExportFormat.Csv
            ? BuildCurrentCsvReport(network)
            : BuildCurrentMarkdownReport(network);
        File.WriteAllText(path, contents, Encoding.UTF8);
    }

    public void SaveTimelineReport(NetworkModel network, string path, int periods, ReportExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (periods <= 0)
        {
            throw new InvalidOperationException("Timeline report periods must be greater than zero.");
        }

        var contents = format == ReportExportFormat.Csv
            ? BuildTimelineCsvReport(network, periods)
            : BuildTimelineMarkdownReport(network, periods);
        File.WriteAllText(path, contents, Encoding.UTF8);
    }

    private string BuildCurrentMarkdownReport(NetworkModel network)
    {
        var outcomes = networkSimulationEngine.Simulate(network);
        var consumerCosts = networkSimulationEngine.SummarizeConsumerCosts(outcomes);
        var allocations = outcomes.SelectMany(outcome => outcome.Allocations).ToList();
        var builder = CreateReportHeader("Current Network Report", network);

        builder.AppendLine("## Network Overview");
        AppendTable(
            builder,
            ["Measure", "Value"],
            [
                ["Nodes", network.Nodes.Count.ToString(CultureInfo.InvariantCulture)],
                ["Edges", network.Edges.Count.ToString(CultureInfo.InvariantCulture)],
                ["Traffic Types", network.TrafficTypes.Count.ToString(CultureInfo.InvariantCulture)],
                ["Total Routed Movements", allocations.Count.ToString(CultureInfo.InvariantCulture)],
                ["Total Delivered", FormatNumber(outcomes.Sum(outcome => outcome.TotalDelivered))]
            ]);

        builder.AppendLine("## Traffic Types");
        AppendTable(
            builder,
            ["Traffic Type", "Preference", "Capacity Bid / Unit", "Description"],
            network.TrafficTypes.Select(definition => new[]
            {
                definition.Name,
                FormatRoutingPreference(definition.RoutingPreference),
                FormatNumber(definition.CapacityBidPerUnit),
                definition.Description
            }));

        builder.AppendLine("## Nodes");
        AppendTable(
            builder,
            ["Node", "Shape", "Position", "Transhipment Cap", "Traffic Profiles"],
            network.Nodes.Select(node => new[]
            {
                $"{node.Name} ({node.Id})",
                node.Shape.ToString(),
                $"{FormatNumber(node.X)}, {FormatNumber(node.Y)}",
                FormatNumber(node.TranshipmentCapacity),
                string.Join("<br/>", node.TrafficProfiles.Select(FormatTrafficProfile))
            }));

        builder.AppendLine("## Edges");
        AppendTable(
            builder,
            ["Edge", "Route", "Time", "Cost", "Capacity", "Direction"],
            network.Edges.Select(edge => new[]
            {
                edge.Id,
                $"{edge.FromNodeId} -> {edge.ToNodeId}",
                FormatNumber(edge.Time),
                FormatNumber(edge.Cost),
                FormatNumber(edge.Capacity),
                edge.IsBidirectional ? "Bidirectional" : "One-way"
            }));

        builder.AppendLine("## Traffic Outcomes");
        AppendTable(
            builder,
            ["Traffic Type", "Preference", "Production", "Consumption", "Delivered", "Unused Supply", "Unmet Demand", "Notes"],
            outcomes.Select(outcome => new[]
            {
                outcome.TrafficType,
                FormatRoutingPreference(outcome.RoutingPreference),
                FormatNumber(outcome.TotalProduction),
                FormatNumber(outcome.TotalConsumption),
                FormatNumber(outcome.TotalDelivered),
                FormatNumber(outcome.UnusedSupply),
                FormatNumber(outcome.UnmetDemand),
                outcome.Notes.Count == 0 ? "None" : string.Join(" ", outcome.Notes)
            }));

        builder.AppendLine("## Consumer Costs");
        AppendTable(
            builder,
            ["Traffic Type", "Consumer", "Local Qty", "Imported Qty", "Blended Unit Cost", "Total Movement Cost"],
            consumerCosts.Select(summary => new[]
            {
                summary.TrafficType,
                $"{summary.ConsumerName} ({summary.ConsumerNodeId})",
                FormatNumber(summary.LocalQuantity),
                FormatNumber(summary.ImportedQuantity),
                FormatNumber(summary.BlendedUnitCost),
                FormatNumber(summary.TotalMovementCost)
            }));

        builder.AppendLine("## Routed Movements");
        AppendTable(
            builder,
            ["Traffic Type", "Producer", "Consumer", "Qty", "Path", "Time", "Transit Cost", "Bid Cost", "Delivered Cost"],
            allocations
                .OrderBy(allocation => allocation.TrafficType, Comparer)
                .ThenBy(allocation => allocation.ConsumerName, Comparer)
                .ThenBy(allocation => allocation.ProducerName, Comparer)
                .Select(allocation => new[]
                {
                    allocation.TrafficType,
                    allocation.ProducerName,
                    allocation.ConsumerName,
                    FormatNumber(allocation.Quantity),
                    string.Join(" -> ", allocation.PathNodeNames),
                    FormatNumber(allocation.TotalTime),
                    FormatNumber(allocation.TotalCost),
                    FormatNumber(allocation.BidCostPerUnit),
                    FormatNumber(allocation.DeliveredCostPerUnit)
                }));

        return builder.ToString();
    }

    private string BuildTimelineMarkdownReport(NetworkModel network, int periods)
    {
        var builder = CreateReportHeader($"Timeline Report ({periods} periods)", network);
        var state = temporalNetworkSimulationEngine.Initialize(network);
        var periodResults = new List<TemporalNetworkSimulationEngine.TemporalSimulationStepResult>(periods);

        for (var period = 0; period < periods; period++)
        {
            periodResults.Add(temporalNetworkSimulationEngine.Advance(network, state));
        }

        var allAllocations = periodResults.SelectMany(result => result.Allocations).ToList();
        var finalPeriodResult = periodResults[^1];
        builder.AppendLine("## Timeline Overview");
        AppendTable(
            builder,
            ["Measure", "Value"],
            [
                ["Periods Simulated", periods.ToString(CultureInfo.InvariantCulture)],
                ["Allocations Planned", allAllocations.Count.ToString(CultureInfo.InvariantCulture)],
                ["Total Delivered", FormatNumber(allAllocations.Sum(allocation => allocation.Quantity))],
                ["Periods With Movement", periodResults.Count(result => result.Allocations.Count > 0).ToString(CultureInfo.InvariantCulture)],
                ["Final In-Flight Movements", finalPeriodResult.InFlightMovementCount.ToString(CultureInfo.InvariantCulture)]
            ]);

        builder.AppendLine("## Timeline Outcomes By Traffic");
        AppendTable(
            builder,
            ["Traffic Type", "Delivered", "Movements", "Avg Delivered Cost / Unit"],
            allAllocations
                .GroupBy(allocation => allocation.TrafficType, Comparer)
                .OrderBy(group => group.Key, Comparer)
                .Select(group =>
                {
                    var totalQuantity = group.Sum(item => item.Quantity);
                    var totalMovementCost = group.Sum(item => item.TotalMovementCost);
                    return new[]
                    {
                        group.Key,
                        FormatNumber(totalQuantity),
                        group.Count().ToString(CultureInfo.InvariantCulture),
                        FormatNumber(totalQuantity > 0 ? totalMovementCost / totalQuantity : 0d)
                    };
                }));

        foreach (var stepResult in periodResults)
        {
            builder.AppendLine($"## Period {stepResult.Period}");

            AppendTable(
                builder,
                ["Measure", "Value"],
                [
                    ["Delivered This Period", FormatNumber(stepResult.Allocations.Sum(allocation => allocation.Quantity))],
                    ["Movements Planned", stepResult.Allocations.Count.ToString(CultureInfo.InvariantCulture)],
                    ["Edges Used", stepResult.EdgeFlows.Count(summary => TotalEdgeFlow(summary.Value) > 0).ToString(CultureInfo.InvariantCulture)],
                    ["Nodes Active", stepResult.NodeFlows.Count(summary => summary.Value.OutboundQuantity > 0 || summary.Value.InboundQuantity > 0).ToString(CultureInfo.InvariantCulture)],
                    ["In-Flight After Period", stepResult.InFlightMovementCount.ToString(CultureInfo.InvariantCulture)]
                ]);

            builder.AppendLine("### Routed Movements");
            if (stepResult.Allocations.Count == 0)
            {
                builder.AppendLine("No movements were planned in this period.");
                builder.AppendLine();
            }
            else
            {
                AppendTable(
                    builder,
                    ["Traffic Type", "Producer", "Consumer", "Qty", "Path", "Time", "Delivered Cost"],
                    stepResult.Allocations.Select(allocation => new[]
                    {
                        allocation.TrafficType,
                        allocation.ProducerName,
                        allocation.ConsumerName,
                        FormatNumber(allocation.Quantity),
                        string.Join(" -> ", allocation.PathNodeNames),
                        FormatNumber(allocation.TotalTime),
                        FormatNumber(allocation.DeliveredCostPerUnit)
                    }));
            }

            builder.AppendLine("### Edge Usage");
            AppendTable(
                builder,
                ["Edge", "Route", "Flow", "Capacity", "Utilisation"],
                network.Edges.Select(edge =>
                {
                    var summary = stepResult.EdgeFlows.GetValueOrDefault(edge.Id, TemporalNetworkSimulationEngine.EdgeFlowVisualSummary.Empty);
                    var totalFlow = TotalEdgeFlow(summary);
                    return new[]
                    {
                        edge.Id,
                        $"{edge.FromNodeId} -> {edge.ToNodeId}",
                        $"{FormatNumber(summary.ForwardQuantity)} / {FormatNumber(summary.ReverseQuantity)}",
                        FormatNumber(edge.Capacity),
                        FormatUtilisation(totalFlow, edge.Capacity)
                    };
                }));

            builder.AppendLine("### Node Activity");
            AppendTable(
                builder,
                ["Node", "Outbound", "Inbound", "Ready Supply", "Demand Backlog", "Store Inventory"],
                network.Nodes.Select(node =>
                {
                    var flow = stepResult.NodeFlows.GetValueOrDefault(node.Id, TemporalNetworkSimulationEngine.NodeFlowVisualSummary.Empty);
                    var states = stepResult.NodeStates
                        .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id))
                        .Select(pair => pair.Value)
                        .ToList();
                    return new[]
                    {
                        $"{node.Name} ({node.Id})",
                        FormatNumber(flow.OutboundQuantity),
                        FormatNumber(flow.InboundQuantity),
                        FormatNumber(states.Sum(item => item.AvailableSupply)),
                        FormatNumber(states.Sum(item => item.DemandBacklog)),
                        FormatNumber(states.Sum(item => item.StoreInventory))
                    };
                }));
        }

        return builder.ToString();
    }

    private string BuildCurrentCsvReport(NetworkModel network)
    {
        var outcomes = networkSimulationEngine.Simulate(network);
        var consumerCosts = networkSimulationEngine.SummarizeConsumerCosts(outcomes);
        var allocations = outcomes.SelectMany(outcome => outcome.Allocations).ToList();
        var builder = new StringBuilder();
        AppendCsvTitleBlock(builder, "Current Network Report", network);
        AppendCsvTable(
            builder,
            "Network Overview",
            ["Measure", "Value"],
            [
                ["Nodes", network.Nodes.Count.ToString(CultureInfo.InvariantCulture)],
                ["Edges", network.Edges.Count.ToString(CultureInfo.InvariantCulture)],
                ["Traffic Types", network.TrafficTypes.Count.ToString(CultureInfo.InvariantCulture)],
                ["Total Routed Movements", allocations.Count.ToString(CultureInfo.InvariantCulture)],
                ["Total Delivered", FormatNumber(outcomes.Sum(outcome => outcome.TotalDelivered))]
            ]);
        AppendCsvTable(
            builder,
            "Traffic Types",
            ["Traffic Type", "Preference", "Capacity Bid / Unit", "Description"],
            network.TrafficTypes.Select(definition => new[]
            {
                definition.Name,
                FormatRoutingPreference(definition.RoutingPreference),
                FormatNumber(definition.CapacityBidPerUnit),
                definition.Description
            }));
        AppendCsvTable(
            builder,
            "Nodes",
            ["Node", "Shape", "Position", "Transhipment Cap", "Traffic Profiles"],
            network.Nodes.Select(node => new[]
            {
                $"{node.Name} ({node.Id})",
                node.Shape.ToString(),
                $"{FormatNumber(node.X)}, {FormatNumber(node.Y)}",
                FormatNumber(node.TranshipmentCapacity),
                string.Join(" | ", node.TrafficProfiles.Select(FormatTrafficProfile))
            }));
        AppendCsvTable(
            builder,
            "Edges",
            ["Edge", "Route", "Time", "Cost", "Capacity", "Direction"],
            network.Edges.Select(edge => new[]
            {
                edge.Id,
                $"{edge.FromNodeId} -> {edge.ToNodeId}",
                FormatNumber(edge.Time),
                FormatNumber(edge.Cost),
                FormatNumber(edge.Capacity),
                edge.IsBidirectional ? "Bidirectional" : "One-way"
            }));
        AppendCsvTable(
            builder,
            "Traffic Outcomes",
            ["Traffic Type", "Preference", "Production", "Consumption", "Delivered", "Unused Supply", "Unmet Demand", "Notes"],
            outcomes.Select(outcome => new[]
            {
                outcome.TrafficType,
                FormatRoutingPreference(outcome.RoutingPreference),
                FormatNumber(outcome.TotalProduction),
                FormatNumber(outcome.TotalConsumption),
                FormatNumber(outcome.TotalDelivered),
                FormatNumber(outcome.UnusedSupply),
                FormatNumber(outcome.UnmetDemand),
                outcome.Notes.Count == 0 ? "None" : string.Join(" ", outcome.Notes)
            }));
        AppendCsvTable(
            builder,
            "Consumer Costs",
            ["Traffic Type", "Consumer", "Local Qty", "Imported Qty", "Blended Unit Cost", "Total Movement Cost"],
            consumerCosts.Select(summary => new[]
            {
                summary.TrafficType,
                $"{summary.ConsumerName} ({summary.ConsumerNodeId})",
                FormatNumber(summary.LocalQuantity),
                FormatNumber(summary.ImportedQuantity),
                FormatNumber(summary.BlendedUnitCost),
                FormatNumber(summary.TotalMovementCost)
            }));
        AppendCsvTable(
            builder,
            "Routed Movements",
            ["Traffic Type", "Producer", "Consumer", "Qty", "Path", "Time", "Transit Cost", "Bid Cost", "Delivered Cost"],
            allocations
                .OrderBy(allocation => allocation.TrafficType, Comparer)
                .ThenBy(allocation => allocation.ConsumerName, Comparer)
                .ThenBy(allocation => allocation.ProducerName, Comparer)
                .Select(allocation => new[]
                {
                    allocation.TrafficType,
                    allocation.ProducerName,
                    allocation.ConsumerName,
                    FormatNumber(allocation.Quantity),
                    string.Join(" -> ", allocation.PathNodeNames),
                    FormatNumber(allocation.TotalTime),
                    FormatNumber(allocation.TotalCost),
                    FormatNumber(allocation.BidCostPerUnit),
                    FormatNumber(allocation.DeliveredCostPerUnit)
                }));

        return builder.ToString();
    }

    private string BuildTimelineCsvReport(NetworkModel network, int periods)
    {
        var state = temporalNetworkSimulationEngine.Initialize(network);
        var results = new List<TemporalNetworkSimulationEngine.TemporalSimulationStepResult>(periods);
        for (var period = 0; period < periods; period++)
        {
            results.Add(temporalNetworkSimulationEngine.Advance(network, state));
        }

        var allAllocations = results.SelectMany(result => result.Allocations).ToList();
        var builder = new StringBuilder();
        AppendCsvTitleBlock(builder, $"Timeline Report ({periods} periods)", network);
        AppendCsvTable(
            builder,
            "Timeline Overview",
            ["Measure", "Value"],
            [
                ["Periods Simulated", periods.ToString(CultureInfo.InvariantCulture)],
                ["Allocations Planned", allAllocations.Count.ToString(CultureInfo.InvariantCulture)],
                ["Total Delivered", FormatNumber(allAllocations.Sum(item => item.Quantity))],
                ["Periods With Movement", results.Count(result => result.Allocations.Count > 0).ToString(CultureInfo.InvariantCulture)],
                ["Final In-Flight Movements", results[^1].InFlightMovementCount.ToString(CultureInfo.InvariantCulture)]
            ]);
        AppendCsvTable(
            builder,
            "Timeline Outcomes By Traffic",
            ["Traffic Type", "Delivered", "Movements", "Avg Delivered Cost / Unit"],
            allAllocations
                .GroupBy(allocation => allocation.TrafficType, Comparer)
                .OrderBy(group => group.Key, Comparer)
                .Select(group =>
                {
                    var totalQuantity = group.Sum(item => item.Quantity);
                    var totalMovementCost = group.Sum(item => item.TotalMovementCost);
                    return new[]
                    {
                        group.Key,
                        FormatNumber(totalQuantity),
                        group.Count().ToString(CultureInfo.InvariantCulture),
                        FormatNumber(totalQuantity > 0 ? totalMovementCost / totalQuantity : 0d)
                    };
                }));

        foreach (var result in results)
        {
            AppendCsvTable(
                builder,
                $"Period {result.Period} Summary",
                ["Measure", "Value"],
                [
                    ["Delivered This Period", FormatNumber(result.Allocations.Sum(item => item.Quantity))],
                    ["Movements Planned", result.Allocations.Count.ToString(CultureInfo.InvariantCulture)],
                    ["Edges Used", result.EdgeFlows.Count(item => TotalEdgeFlow(item.Value) > 0).ToString(CultureInfo.InvariantCulture)],
                    ["Nodes Active", result.NodeFlows.Count(item => item.Value.OutboundQuantity > 0 || item.Value.InboundQuantity > 0).ToString(CultureInfo.InvariantCulture)],
                    ["In-Flight After Period", result.InFlightMovementCount.ToString(CultureInfo.InvariantCulture)]
                ]);

            AppendCsvTable(
                builder,
                $"Period {result.Period} Routed Movements",
                ["Traffic Type", "Producer", "Consumer", "Qty", "Path", "Time", "Delivered Cost"],
                result.Allocations.Select(allocation => new[]
                {
                    allocation.TrafficType,
                    allocation.ProducerName,
                    allocation.ConsumerName,
                    FormatNumber(allocation.Quantity),
                    string.Join(" -> ", allocation.PathNodeNames),
                    FormatNumber(allocation.TotalTime),
                    FormatNumber(allocation.DeliveredCostPerUnit)
                }));

            AppendCsvTable(
                builder,
                $"Period {result.Period} Edge Usage",
                ["Edge", "Route", "Forward Flow", "Reverse Flow", "Capacity", "Utilisation"],
                network.Edges.Select(edge =>
                {
                    var summary = result.EdgeFlows.GetValueOrDefault(edge.Id, TemporalNetworkSimulationEngine.EdgeFlowVisualSummary.Empty);
                    return new[]
                    {
                        edge.Id,
                        $"{edge.FromNodeId} -> {edge.ToNodeId}",
                        FormatNumber(summary.ForwardQuantity),
                        FormatNumber(summary.ReverseQuantity),
                        FormatNumber(edge.Capacity),
                        FormatUtilisation(TotalEdgeFlow(summary), edge.Capacity)
                    };
                }));

            AppendCsvTable(
                builder,
                $"Period {result.Period} Node Activity",
                ["Node", "Outbound", "Inbound", "Ready Supply", "Demand Backlog", "Store Inventory"],
                network.Nodes.Select(node =>
                {
                    var flow = result.NodeFlows.GetValueOrDefault(node.Id, TemporalNetworkSimulationEngine.NodeFlowVisualSummary.Empty);
                    var states = result.NodeStates
                        .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id))
                        .Select(pair => pair.Value)
                        .ToList();
                    return new[]
                    {
                        $"{node.Name} ({node.Id})",
                        FormatNumber(flow.OutboundQuantity),
                        FormatNumber(flow.InboundQuantity),
                        FormatNumber(states.Sum(item => item.AvailableSupply)),
                        FormatNumber(states.Sum(item => item.DemandBacklog)),
                        FormatNumber(states.Sum(item => item.StoreInventory))
                    };
                }));
        }

        return builder.ToString();
    }

    private static StringBuilder CreateReportHeader(string title, NetworkModel network)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {EscapeCell(title)}");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Network: {EscapeCell(network.Name)}");

        if (!string.IsNullOrWhiteSpace(network.Description))
        {
            builder.AppendLine();
            builder.AppendLine(network.Description.Trim());
        }

        builder.AppendLine();
        return builder;
    }

    private static void AppendTable(StringBuilder builder, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var materializedRows = rows.ToList();
        builder.Append("| ");
        builder.Append(string.Join(" | ", headers.Select(EscapeCell)));
        builder.AppendLine(" |");
        builder.Append("| ");
        builder.Append(string.Join(" | ", headers.Select(_ => "---")));
        builder.AppendLine(" |");

        foreach (var row in materializedRows)
        {
            builder.Append("| ");
            builder.Append(string.Join(" | ", row.Select(EscapeCell)));
            builder.AppendLine(" |");
        }

        if (materializedRows.Count == 0)
        {
            builder.Append("| ");
            builder.Append(string.Join(" | ", headers.Select((_, index) => index == 0 ? "No data" : string.Empty)));
            builder.AppendLine(" |");
        }

        builder.AppendLine();
    }

    private static string EscapeCell(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("|", "\\|").Replace(Environment.NewLine, "<br/>");
    }

    private static string FormatTrafficProfile(NodeTrafficProfile profile)
    {
        var parts = new List<string>
        {
            profile.TrafficType
        };

        if (profile.Production > 0)
        {
            parts.Add($"P {FormatNumber(profile.Production)}");
        }

        if (profile.Consumption > 0)
        {
            parts.Add($"C {FormatNumber(profile.Consumption)}");
        }

        if (profile.ConsumerPremiumPerUnit > 0)
        {
            parts.Add($"Bid+ {FormatNumber(profile.ConsumerPremiumPerUnit)}");
        }

        if (profile.CanTransship)
        {
            parts.Add("T");
        }

        if (profile.IsStore)
        {
            parts.Add(profile.StoreCapacity.HasValue
                ? $"Store {FormatNumber(profile.StoreCapacity)}"
                : "Store");
        }

        parts.Add($"Prod {FormatSchedule(profile.ProductionStartPeriod, profile.ProductionEndPeriod)}");
        parts.Add($"Cons {FormatSchedule(profile.ConsumptionStartPeriod, profile.ConsumptionEndPeriod)}");
        return string.Join(", ", parts);
    }

    private static string FormatSchedule(int? start, int? end)
    {
        return $"{start?.ToString(CultureInfo.InvariantCulture) ?? "0"}-{end?.ToString(CultureInfo.InvariantCulture) ?? "inf"}";
    }

    private static string FormatRoutingPreference(RoutingPreference routingPreference)
    {
        return routingPreference switch
        {
            RoutingPreference.Speed => "Speed",
            RoutingPreference.Cost => "Cost",
            _ => "Total cost"
        };
    }

    private static string FormatNumber(double? value)
    {
        if (!value.HasValue)
        {
            return "Unlimited";
        }

        if (double.IsPositiveInfinity(value.Value))
        {
            return "Unlimited";
        }

        return value.Value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatUtilisation(double flow, double? capacity)
    {
        if (!capacity.HasValue || capacity.Value <= 0)
        {
            return capacity.HasValue && capacity.Value == 0d
                ? "No capacity"
                : "Unlimited";
        }

        return $"{(flow / capacity.Value):0%}";
    }

    private static double TotalEdgeFlow(TemporalNetworkSimulationEngine.EdgeFlowVisualSummary summary)
    {
        return summary.ForwardQuantity + summary.ReverseQuantity;
    }

    private static void AppendCsvTitleBlock(StringBuilder builder, string title, NetworkModel network)
    {
        builder.AppendLine(BuildCsvRow([title]));
        builder.AppendLine(BuildCsvRow(["Generated", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")]));
        builder.AppendLine(BuildCsvRow(["Network", network.Name]));
        if (!string.IsNullOrWhiteSpace(network.Description))
        {
            builder.AppendLine(BuildCsvRow(["Description", network.Description.Trim()]));
        }

        builder.AppendLine();
    }

    private static void AppendCsvTable(StringBuilder builder, string sectionTitle, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        builder.AppendLine(BuildCsvRow([sectionTitle]));
        builder.AppendLine(BuildCsvRow(headers));

        var materializedRows = rows.ToList();
        if (materializedRows.Count == 0)
        {
            builder.AppendLine(BuildCsvRow(["No data"]));
        }
        else
        {
            foreach (var row in materializedRows)
            {
                builder.AppendLine(BuildCsvRow(row));
            }
        }

        builder.AppendLine();
    }

    private static string BuildCsvRow(IEnumerable<string?> values)
    {
        return string.Join(",", values.Select(EscapeCsvCell));
    }

    private static string EscapeCsvCell(string? value)
    {
        var safe = value ?? string.Empty;
        if (safe.Contains('"'))
        {
            safe = safe.Replace("\"", "\"\"");
        }

        return safe.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{safe}\""
            : safe;
    }
}

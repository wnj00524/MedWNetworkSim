using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

/// <summary>
/// Builds human-readable reports from the current network and simulation outputs.
/// </summary>
public sealed class ReportExportService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public void SaveCurrentReport(
        NetworkModel network,
        IEnumerable<TrafficSimulationOutcome> outcomes,
        IEnumerable<ConsumerCostSummary> consumerCosts,
        string path,
        ReportExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(consumerCosts);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var materializedOutcomes = outcomes.ToList();
        var materializedConsumerCosts = consumerCosts.ToList();
        var contents = format == ReportExportFormat.Csv
            ? BuildCurrentCsvReport(network, materializedOutcomes, materializedConsumerCosts)
            : BuildCurrentHtmlReport(network, materializedOutcomes, materializedConsumerCosts);
        File.WriteAllText(path, contents, Encoding.UTF8);
    }

    public void SaveTimelineReport(
        NetworkModel network,
        IReadOnlyList<TemporalNetworkSimulationEngine.TemporalSimulationStepResult> periodResults,
        string path,
        ReportExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(periodResults);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (periodResults.Count <= 0)
        {
            throw new InvalidOperationException("Timeline report periods must be greater than zero.");
        }

        var contents = format == ReportExportFormat.Csv
            ? BuildTimelineCsvReport(network, periodResults)
            : BuildTimelineHtmlReport(network, periodResults);
        File.WriteAllText(path, contents, Encoding.UTF8);
    }

    private string BuildCurrentHtmlReport(
        NetworkModel network,
        IReadOnlyList<TrafficSimulationOutcome> outcomes,
        IReadOnlyList<ConsumerCostSummary> consumerCosts)
    {
        var allocations = outcomes.SelectMany(outcome => outcome.Allocations).ToList();
        var builder = CreateHtmlReportHeader("Current Network Report", network);

        builder.AppendLine("<h2>Network Overview</h2>");
        AppendHtmlTable(
            builder,
            ["Measure", "Value"],
            [
                ["Nodes", network.Nodes.Count.ToString(CultureInfo.InvariantCulture)],
                ["Edges", network.Edges.Count.ToString(CultureInfo.InvariantCulture)],
                ["Traffic Types", network.TrafficTypes.Count.ToString(CultureInfo.InvariantCulture)],
                ["Total Routed Movements", allocations.Count.ToString(CultureInfo.InvariantCulture)],
                ["Total Delivered", FormatNumber(outcomes.Sum(outcome => outcome.TotalDelivered))]
            ]);

        builder.AppendLine("<h2>Traffic Types</h2>");
        AppendHtmlTable(
            builder,
            ["Traffic Type", "Preference", "Capacity Bid / Unit", "Description"],
            network.TrafficTypes.Select(definition => new[]
            {
                definition.Name,
                FormatRoutingPreference(definition.RoutingPreference),
                FormatNumber(definition.CapacityBidPerUnit),
                definition.Description
            }));

        builder.AppendLine("<h2>Nodes</h2>");
        AppendHtmlTable(
            builder,
            ["Node", "Shape", "Position", "Transhipment Cap", "Traffic Profiles"],
            network.Nodes.Select(node => new[]
            {
                $"{node.Name} ({node.Id})",
                node.Shape.ToString(),
                $"{FormatNumber(node.X)}, {FormatNumber(node.Y)}",
                FormatNumber(node.TranshipmentCapacity),
                string.Join(Environment.NewLine, node.TrafficProfiles.Select(FormatTrafficProfile))
            }));

        builder.AppendLine("<h2>Edges</h2>");
        AppendHtmlTable(
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

        builder.AppendLine("<h2>Traffic Outcomes</h2>");
        AppendHtmlTable(
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

        builder.AppendLine("<h2>Consumer Costs</h2>");
        AppendHtmlTable(
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

        builder.AppendLine("<h2>Routed Movements</h2>");
        AppendHtmlTable(
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

        return FinishHtmlReport(builder);
    }

    private string BuildTimelineHtmlReport(
        NetworkModel network,
        IReadOnlyList<TemporalNetworkSimulationEngine.TemporalSimulationStepResult> periodResults)
    {
        var periods = periodResults.Count;
        var builder = CreateHtmlReportHeader($"Timeline Report ({periods} periods)", network);
        var allAllocations = periodResults.SelectMany(result => result.Allocations).ToList();
        var finalPeriodResult = periodResults[^1];
        builder.AppendLine("<h2>Timeline Overview</h2>");
        AppendHtmlTable(
            builder,
            ["Measure", "Value"],
            [
                ["Periods Simulated", periods.ToString(CultureInfo.InvariantCulture)],
                ["Loop Length", network.TimelineLoopLength.HasValue ? network.TimelineLoopLength.Value.ToString(CultureInfo.InvariantCulture) : "None"],
                ["Allocations Planned", allAllocations.Count.ToString(CultureInfo.InvariantCulture)],
                ["Total Delivered", FormatNumber(allAllocations.Sum(allocation => allocation.Quantity))],
                ["Periods With Movement", periodResults.Count(result => result.Allocations.Count > 0).ToString(CultureInfo.InvariantCulture)],
                ["Final In-Flight Movements", finalPeriodResult.InFlightMovementCount.ToString(CultureInfo.InvariantCulture)]
            ]);

        builder.AppendLine("<h2>Timeline Outcomes By Traffic</h2>");
        AppendHtmlTable(
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
            builder.AppendLine($"<h2>{HtmlEncode(FormatTimelinePeriodLabel(network, stepResult))}</h2>");

            AppendHtmlTable(
                builder,
                ["Measure", "Value"],
                [
                    ["Delivered This Period", FormatNumber(stepResult.Allocations.Sum(allocation => allocation.Quantity))],
                    ["Movements Planned", stepResult.Allocations.Count.ToString(CultureInfo.InvariantCulture)],
                    ["Edges Used", stepResult.EdgeFlows.Count(summary => TotalEdgeFlow(summary.Value) > 0).ToString(CultureInfo.InvariantCulture)],
                    ["Nodes Active", stepResult.NodeFlows.Count(summary => summary.Value.OutboundQuantity > 0 || summary.Value.InboundQuantity > 0).ToString(CultureInfo.InvariantCulture)],
                    ["In-Flight After Period", stepResult.InFlightMovementCount.ToString(CultureInfo.InvariantCulture)]
                ]);

            builder.AppendLine("<h3>Routed Movements</h3>");
            if (stepResult.Allocations.Count == 0)
            {
                builder.AppendLine("<p>No movements were planned in this period.</p>");
            }
            else
            {
                AppendHtmlTable(
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

            builder.AppendLine("<h3>Edge Usage</h3>");
            AppendHtmlTable(
                builder,
                ["Edge", "Route", "Flow", "Occupancy", "Capacity", "Utilisation"],
                network.Edges.Select(edge =>
                {
                    var summary = stepResult.EdgeFlows.GetValueOrDefault(edge.Id, TemporalNetworkSimulationEngine.EdgeFlowVisualSummary.Empty);
                    var occupancy = stepResult.EdgeOccupancy.GetValueOrDefault(edge.Id, 0d);
                    return new[]
                    {
                        edge.Id,
                        $"{edge.FromNodeId} -> {edge.ToNodeId}",
                        $"{FormatNumber(summary.ForwardQuantity)} / {FormatNumber(summary.ReverseQuantity)}",
                        FormatNumber(occupancy),
                        FormatNumber(edge.Capacity),
                        FormatUtilisation(occupancy, edge.Capacity)
                    };
                }));

            builder.AppendLine("<h3>Node Activity</h3>");
            AppendHtmlTable(
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

        return FinishHtmlReport(builder);
    }

    private string BuildCurrentCsvReport(
        NetworkModel network,
        IReadOnlyList<TrafficSimulationOutcome> outcomes,
        IReadOnlyList<ConsumerCostSummary> consumerCosts)
    {
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

    private string BuildTimelineCsvReport(
        NetworkModel network,
        IReadOnlyList<TemporalNetworkSimulationEngine.TemporalSimulationStepResult> results)
    {
        var periods = results.Count;
        var allAllocations = results.SelectMany(result => result.Allocations).ToList();
        var builder = new StringBuilder();
        AppendCsvTitleBlock(builder, $"Timeline Report ({periods} periods)", network);
        AppendCsvTable(
            builder,
            "Timeline Overview",
            ["Measure", "Value"],
            [
                ["Periods Simulated", periods.ToString(CultureInfo.InvariantCulture)],
                ["Loop Length", network.TimelineLoopLength.HasValue ? network.TimelineLoopLength.Value.ToString(CultureInfo.InvariantCulture) : "None"],
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
                $"{FormatTimelinePeriodLabel(network, result)} Summary",
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
                $"{FormatTimelinePeriodLabel(network, result)} Routed Movements",
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
                $"{FormatTimelinePeriodLabel(network, result)} Edge Usage",
                ["Edge", "Route", "Forward Flow", "Reverse Flow", "Occupancy", "Capacity", "Utilisation"],
                network.Edges.Select(edge =>
                {
                    var summary = result.EdgeFlows.GetValueOrDefault(edge.Id, TemporalNetworkSimulationEngine.EdgeFlowVisualSummary.Empty);
                    var occupancy = result.EdgeOccupancy.GetValueOrDefault(edge.Id, 0d);
                    return new[]
                    {
                        edge.Id,
                        $"{edge.FromNodeId} -> {edge.ToNodeId}",
                        FormatNumber(summary.ForwardQuantity),
                        FormatNumber(summary.ReverseQuantity),
                        FormatNumber(occupancy),
                        FormatNumber(edge.Capacity),
                        FormatUtilisation(occupancy, edge.Capacity)
                    };
                }));

            AppendCsvTable(
                builder,
                $"{FormatTimelinePeriodLabel(network, result)} Node Activity",
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

    private static StringBuilder CreateHtmlReportHeader(string title, NetworkModel network)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine($"  <title>{HtmlEncode(title)}</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    body { font-family: 'Segoe UI', Arial, sans-serif; margin: 28px; color: #2d231e; background: #fffdf9; }");
        builder.AppendLine("    h1, h2, h3 { color: #6f3e1b; margin-bottom: 0.35rem; }");
        builder.AppendLine("    h2 { margin-top: 2rem; border-bottom: 2px solid #e7dccb; padding-bottom: 0.25rem; }");
        builder.AppendLine("    p.meta { margin: 0.2rem 0; color: #6e5d51; }");
        builder.AppendLine("    table { width: 100%; border-collapse: collapse; margin: 0.85rem 0 1.35rem; font-size: 0.95rem; }");
        builder.AppendLine("    th, td { border: 1px solid #d7c7b1; padding: 8px 10px; vertical-align: top; text-align: left; }");
        builder.AppendLine("    th { background: #f0e6d6; }");
        builder.AppendLine("    tr:nth-child(even) td { background: #fffaf3; }");
        builder.AppendLine("    .description { margin-top: 1rem; padding: 12px 14px; background: #f8f1e7; border: 1px solid #d7c7b1; border-radius: 10px; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine($"<h1>{HtmlEncode(title)}</h1>");
        builder.AppendLine($"<p class=\"meta\"><strong>Generated:</strong> {HtmlEncode(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))}</p>");
        builder.AppendLine($"<p class=\"meta\"><strong>Network:</strong> {HtmlEncode(network.Name)}</p>");

        if (!string.IsNullOrWhiteSpace(network.Description))
        {
            builder.AppendLine($"<div class=\"description\">{HtmlEncode(network.Description.Trim())}</div>");
        }

        return builder;
    }

    private static string FinishHtmlReport(StringBuilder builder)
    {
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void AppendHtmlTable(StringBuilder builder, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var materializedRows = rows.ToList();
        builder.AppendLine("<table>");
        builder.AppendLine("  <thead>");
        builder.AppendLine("    <tr>");
        foreach (var header in headers)
        {
            builder.AppendLine($"      <th>{HtmlEncode(header)}</th>");
        }
        builder.AppendLine("    </tr>");
        builder.AppendLine("  </thead>");
        builder.AppendLine("  <tbody>");

        if (materializedRows.Count == 0)
        {
            builder.AppendLine($"    <tr><td colspan=\"{headers.Count}\">No data</td></tr>");
        }
        else
        {
            foreach (var row in materializedRows)
            {
                builder.AppendLine("    <tr>");
                foreach (var cell in row)
                {
                    builder.AppendLine($"      <td>{HtmlEncode(cell)}</td>");
                }
                builder.AppendLine("    </tr>");
            }
        }

        builder.AppendLine("  </tbody>");
        builder.AppendLine("</table>");
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

        if (profile.InputRequirements.Count > 0)
        {
            parts.Add("Inputs " + string.Join(" + ", profile.InputRequirements.Select(requirement =>
                $"{requirement.TrafficType}:{FormatNumber(requirement.QuantityPerOutputUnit)}")));
        }

        parts.Add($"Prod {FormatWindows(profile.ProductionWindows, profile.ProductionStartPeriod, profile.ProductionEndPeriod)}");
        parts.Add($"Cons {FormatWindows(profile.ConsumptionWindows, profile.ConsumptionStartPeriod, profile.ConsumptionEndPeriod)}");
        return string.Join(", ", parts);
    }

    private static string FormatWindows(IReadOnlyList<PeriodWindow> windows, int? legacyStart, int? legacyEnd)
    {
        return windows.Count > 0
            ? string.Join(" | ", windows.Select(window => FormatSchedule(window.StartPeriod, window.EndPeriod)))
            : FormatSchedule(legacyStart, legacyEnd);
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

    private static string FormatTimelinePeriodLabel(
        NetworkModel network,
        TemporalNetworkSimulationEngine.TemporalSimulationStepResult result)
    {
        if (network.TimelineLoopLength.HasValue && network.TimelineLoopLength.Value > 0)
        {
            return $"Period {result.Period} (cycle period {result.EffectivePeriod} of {network.TimelineLoopLength.Value})";
        }

        return $"Period {result.Period}";
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

    private static string HtmlEncode(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty).Replace(Environment.NewLine, "<br/>");
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

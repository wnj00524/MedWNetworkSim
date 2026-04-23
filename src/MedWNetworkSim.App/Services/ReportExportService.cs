using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
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
        var contents = format switch
        {
            ReportExportFormat.Csv => BuildCurrentCsvReport(network, materializedOutcomes, materializedConsumerCosts),
            ReportExportFormat.Json => BuildCurrentJsonReport(network, materializedOutcomes, materializedConsumerCosts),
            _ => BuildCurrentHtmlReport(network, materializedOutcomes, materializedConsumerCosts)
        };
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

        var contents = format switch
        {
            ReportExportFormat.Csv => BuildTimelineCsvReport(network, periodResults),
            ReportExportFormat.Json => BuildTimelineJsonReport(network, periodResults),
            _ => BuildTimelineHtmlReport(network, periodResults)
        };
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

        AppendWorldbuilderHtmlReport(builder, network, allocations, outcomes);

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
            ["Edge", "Route", "Time", "Cost", "Capacity", "Direction", "Traffic permissions"],
            network.Edges.Select(edge => new[]
            {
                edge.Id,
                $"{edge.FromNodeId} -> {edge.ToNodeId}",
                FormatNumber(edge.Time),
                FormatNumber(edge.Cost),
                FormatNumber(edge.Capacity),
                edge.IsBidirectional ? "Bidirectional" : "One-way",
                FormatEdgeTrafficPermissions(network, edge)
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
                ["Total Quantity Planned", FormatNumber(allAllocations.Sum(allocation => allocation.Quantity))],
                ["Periods With Movement", periodResults.Count(result => result.Allocations.Count > 0).ToString(CultureInfo.InvariantCulture)],
                ["Final In-Flight Movements", finalPeriodResult.InFlightMovementCount.ToString(CultureInfo.InvariantCulture)]
            ]);

        builder.AppendLine("<h2>Timeline Outcomes By Traffic</h2>");
        AppendHtmlTable(
            builder,
            ["Traffic Type", "Planned Quantity", "Movements", "Avg Planned Delivered Cost / Unit"],
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
                    ["New Quantity Started This Period", FormatNumber(stepResult.Allocations.Sum(allocation => allocation.Quantity))],
                    ["Movements Planned", stepResult.Allocations.Count.ToString(CultureInfo.InvariantCulture)],
                    ["Edges Used", stepResult.EdgeFlows.Count(summary => TotalEdgeFlow(summary.Value) > 0).ToString(CultureInfo.InvariantCulture)],
                    ["Nodes Active", stepResult.NodeFlows.Count(summary => summary.Value.OutboundQuantity > 0 || summary.Value.InboundQuantity > 0).ToString(CultureInfo.InvariantCulture)],
                    ["In-Flight After Period", stepResult.InFlightMovementCount.ToString(CultureInfo.InvariantCulture)],
                    ["Nodes With Pressure", stepResult.NodePressureById.Count(snapshot => snapshot.Value.Score > 0).ToString(CultureInfo.InvariantCulture)],
                    ["Edges With Pressure", stepResult.EdgePressureById.Count(snapshot => snapshot.Value.Score > 0).ToString(CultureInfo.InvariantCulture)]
                ]);

            builder.AppendLine("<h3>Movements Started This Period</h3>");
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
                ["Edge", "Route", "Flow", "Occupancy", "Capacity", "Utilisation", "Traffic permissions"],
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
                        FormatUtilisation(occupancy, edge.Capacity),
                        FormatEdgeTrafficPermissions(network, edge)
                    };
                }));

            builder.AppendLine("<h3>Node Activity</h3>");
            AppendHtmlTable(
                builder,
                ["Node", "Outbound", "Inbound", "Ready Supply", "Demand Backlog", "Backlog by Good", "Store Inventory"],
                network.Nodes.Select(node =>
                {
                    var flow = stepResult.NodeFlows.GetValueOrDefault(node.Id, TemporalNetworkSimulationEngine.NodeFlowVisualSummary.Empty);
                    var states = stepResult.NodeStates
                        .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id))
                        .Select(pair => pair.Value)
                        .ToList();
                    var backlogByGood = stepResult.NodeStates
                        .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id) && pair.Value.DemandBacklog > 0d)
                        .OrderBy(pair => pair.Key.TrafficType, Comparer)
                        .Select(pair => $"{pair.Key.TrafficType}:{FormatNumber(pair.Value.DemandBacklog)}");
                    return new[]
                    {
                        $"{node.Name} ({node.Id})",
                        FormatNumber(flow.OutboundQuantity),
                        FormatNumber(flow.InboundQuantity),
                        FormatNumber(states.Sum(item => item.AvailableSupply)),
                        FormatNumber(states.Sum(item => item.DemandBacklog)),
                        backlogByGood.Any() ? string.Join(", ", backlogByGood) : "None",
                        FormatNumber(states.Sum(item => item.StoreInventory))
                    };
                }));

            builder.AppendLine("<h3>Pressure Overview</h3>");
            AppendHtmlTable(
                builder,
                ["Entity Type", "Entity", "Score", "Top Cause", "Cause Breakdown"],
                network.Nodes.Select(node =>
                {
                    var snapshot = stepResult.NodePressureById.GetValueOrDefault(
                        node.Id,
                        new TemporalNetworkSimulationEngine.NodePressureSnapshot(
                            0d,
                            0d,
                            0d,
                            new Dictionary<TemporalNetworkSimulationEngine.PressureCauseKind, double>(),
                            string.Empty));
                    return new[]
                    {
                        "Node",
                        $"{node.Name} ({node.Id})",
                        FormatNumber(snapshot.Score),
                        string.IsNullOrWhiteSpace(snapshot.TopCause) ? "None" : snapshot.TopCause,
                        FormatCauseBreakdown(snapshot.CauseWeights)
                    };
                }).Concat(network.Edges.Select(edge =>
                {
                    var snapshot = stepResult.EdgePressureById.GetValueOrDefault(
                        edge.Id,
                        new TemporalNetworkSimulationEngine.EdgePressureSnapshot(
                            0d,
                            0d,
                            0d,
                            0d,
                            new Dictionary<TemporalNetworkSimulationEngine.PressureCauseKind, double>(),
                            string.Empty));
                    return new[]
                    {
                        "Edge",
                        edge.Id,
                        FormatNumber(snapshot.Score),
                        string.IsNullOrWhiteSpace(snapshot.TopCause) ? "None" : snapshot.TopCause,
                        FormatCauseBreakdown(snapshot.CauseWeights)
                    };
                })));
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
            ["Edge", "Route", "Time", "Cost", "Capacity", "Direction", "Traffic permissions"],
            network.Edges.Select(edge => new[]
            {
                edge.Id,
                $"{edge.FromNodeId} -> {edge.ToNodeId}",
                FormatNumber(edge.Time),
                FormatNumber(edge.Cost),
                FormatNumber(edge.Capacity),
                edge.IsBidirectional ? "Bidirectional" : "One-way",
                FormatEdgeTrafficPermissions(network, edge)
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

    private void AppendWorldbuilderHtmlReport(
        StringBuilder builder,
        NetworkModel network,
        IReadOnlyList<RouteAllocation> allocations,
        IReadOnlyList<TrafficSimulationOutcome> outcomes)
    {
        builder.AppendLine("<h2>Worldbuilder Summary</h2>");
        builder.AppendLine("<p class=\"meta\">These notes are generated from node metadata, configured roles, and the current simulation result.</p>");

        builder.AppendLine("<h3>Settlement Summary</h3>");
        AppendHtmlTable(
            builder,
            ["Place", "Type / Tags", "Model Role", "Why It Matters In-World"],
            network.Nodes.Select(node => new[]
            {
                $"{node.Name} ({node.Id})",
                FormatPlaceIdentity(node),
                FormatModelRoleSummary(node),
                FormatPlaceImportance(node, allocations)
            }));

        builder.AppendLine("<h3>Dependency Summary</h3>");
        AppendHtmlTable(
            builder,
            ["Place", "Critical Inbound Dependencies", "Important Outbound Functions", "Storage Pressure"],
            network.Nodes.Select(node => new[]
            {
                $"{node.Name} ({node.Id})",
                FormatInboundDependencies(node, allocations, outcomes),
                FormatOutboundFunctions(node, allocations),
                FormatStoragePressure(node)
            }));

        builder.AppendLine("<h3>Bottleneck Summary</h3>");
        AppendHtmlTable(
            builder,
            ["Route", "Observed Use", "Capacity", "Worldbuilder Reading"],
            network.Edges
                .Select(edge => FormatBottleneckRow(edge, network, allocations))
                .Where(row => row is not null)
                .Select(row => row!));
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
                ["Total Quantity Planned", FormatNumber(allAllocations.Sum(item => item.Quantity))],
                ["Periods With Movement", results.Count(result => result.Allocations.Count > 0).ToString(CultureInfo.InvariantCulture)],
                ["Final In-Flight Movements", results[^1].InFlightMovementCount.ToString(CultureInfo.InvariantCulture)]
            ]);
        AppendCsvTable(
            builder,
            "Timeline Outcomes By Traffic",
            ["Traffic Type", "Planned Quantity", "Movements", "Avg Planned Delivered Cost / Unit"],
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
            var trafficNames = network.TrafficTypes
                .Select(type => type.Name)
                .Distinct(Comparer)
                .OrderBy(name => name, Comparer)
                .ToList();

            AppendCsvTable(
                builder,
                $"{FormatTimelinePeriodLabel(network, result)} Summary",
                ["Measure", "Value"],
                [
                    ["New Quantity Started This Period", FormatNumber(result.Allocations.Sum(item => item.Quantity))],
                    ["Movements Planned", result.Allocations.Count.ToString(CultureInfo.InvariantCulture)],
                    ["Edges Used", result.EdgeFlows.Count(item => TotalEdgeFlow(item.Value) > 0).ToString(CultureInfo.InvariantCulture)],
                    ["Nodes Active", result.NodeFlows.Count(item => item.Value.OutboundQuantity > 0 || item.Value.InboundQuantity > 0).ToString(CultureInfo.InvariantCulture)],
                    ["In-Flight After Period", result.InFlightMovementCount.ToString(CultureInfo.InvariantCulture)],
                    ["Nodes With Pressure", result.NodePressureById.Count(snapshot => snapshot.Value.Score > 0).ToString(CultureInfo.InvariantCulture)],
                    ["Edges With Pressure", result.EdgePressureById.Count(snapshot => snapshot.Value.Score > 0).ToString(CultureInfo.InvariantCulture)]
                ]);

            AppendCsvTable(
                builder,
                $"{FormatTimelinePeriodLabel(network, result)} Movements Started This Period",
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
                ["Edge", "Route", "Forward Flow", "Reverse Flow", "Occupancy", "Capacity", "Utilisation", "Traffic permissions"],
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
                        FormatUtilisation(occupancy, edge.Capacity),
                        FormatEdgeTrafficPermissions(network, edge)
                    };
                }));

            AppendCsvTable(
                builder,
                $"{FormatTimelinePeriodLabel(network, result)} Node Activity",
                ["Node", "Outbound", "Inbound", "Ready Supply", "Demand Backlog", .. trafficNames.Select(name => $"{name}_Backlog"), "Store Inventory"],
                network.Nodes.Select(node =>
                {
                    var flow = result.NodeFlows.GetValueOrDefault(node.Id, TemporalNetworkSimulationEngine.NodeFlowVisualSummary.Empty);
                    var states = result.NodeStates
                        .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id))
                        .Select(pair => pair.Value)
                        .ToList();
                    var backlogByType = result.NodeStates
                        .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id))
                        .GroupBy(pair => pair.Key.TrafficType, pair => pair.Value.DemandBacklog, Comparer)
                        .ToDictionary(group => group.Key, group => group.Sum(), Comparer);
                    var row = new List<string>
                    {
                        $"{node.Name} ({node.Id})",
                        FormatNumber(flow.OutboundQuantity),
                        FormatNumber(flow.InboundQuantity),
                        FormatNumber(states.Sum(item => item.AvailableSupply)),
                        FormatNumber(states.Sum(item => item.DemandBacklog)),
                    };

                    row.AddRange(trafficNames.Select(name => FormatNumber(backlogByType.GetValueOrDefault(name, 0d))));
                    row.Add(FormatNumber(states.Sum(item => item.StoreInventory)));
                    return row.ToArray();
                }));

            AppendCsvTable(
                builder,
                $"{FormatTimelinePeriodLabel(network, result)} Pressure Overview",
                ["Entity Type", "Entity", "Score", "Top Cause", "Cause Breakdown"],
                network.Nodes.Select(node =>
                {
                    var snapshot = result.NodePressureById.GetValueOrDefault(
                        node.Id,
                        new TemporalNetworkSimulationEngine.NodePressureSnapshot(
                            0d,
                            0d,
                            0d,
                            new Dictionary<TemporalNetworkSimulationEngine.PressureCauseKind, double>(),
                            string.Empty));
                    return new[]
                    {
                        "Node",
                        $"{node.Name} ({node.Id})",
                        FormatNumber(snapshot.Score),
                        string.IsNullOrWhiteSpace(snapshot.TopCause) ? "None" : snapshot.TopCause,
                        FormatCauseBreakdown(snapshot.CauseWeights)
                    };
                }).Concat(network.Edges.Select(edge =>
                {
                    var snapshot = result.EdgePressureById.GetValueOrDefault(
                        edge.Id,
                        new TemporalNetworkSimulationEngine.EdgePressureSnapshot(
                            0d,
                            0d,
                            0d,
                            0d,
                            new Dictionary<TemporalNetworkSimulationEngine.PressureCauseKind, double>(),
                            string.Empty));
                    return new[]
                    {
                        "Edge",
                        edge.Id,
                        FormatNumber(snapshot.Score),
                        string.IsNullOrWhiteSpace(snapshot.TopCause) ? "None" : snapshot.TopCause,
                        FormatCauseBreakdown(snapshot.CauseWeights)
                    };
                })));
        }

        return builder.ToString();
    }

    private static string BuildCurrentJsonReport(
        NetworkModel network,
        IReadOnlyList<TrafficSimulationOutcome> outcomes,
        IReadOnlyList<ConsumerCostSummary> consumerCosts)
    {
        var report = new
        {
            reportType = "current",
            network = new { network.Name, network.Description, nodes = network.Nodes.Count, edges = network.Edges.Count },
            edges = network.Edges.Select(edge => new
            {
                edge_id = edge.Id,
                route = $"{edge.FromNodeId}->{edge.ToNodeId}",
                trafficPermissions = FormatEdgeTrafficPermissions(network, edge)
            }),
            trafficOutcomes = outcomes.Select(outcome => new
            {
                trafficType = outcome.TrafficType,
                delivered = outcome.TotalDelivered,
                unmetDemand = outcome.UnmetDemand,
                notes = outcome.Notes
            }),
            nodes = network.Nodes.Select(node => new
            {
                node_id = node.Id,
                node_name = node.Name,
                backlog = node.TrafficProfiles.ToDictionary(
                    profile => profile.TrafficType,
                    _ => 0d,
                    Comparer)
            }),
            consumerCosts = consumerCosts.Select(cost => new
            {
                trafficType = cost.TrafficType,
                consumer = cost.ConsumerNodeId,
                local = cost.LocalQuantity,
                imported = cost.ImportedQuantity
            })
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildTimelineJsonReport(
        NetworkModel network,
        IReadOnlyList<TemporalNetworkSimulationEngine.TemporalSimulationStepResult> results)
    {
        var report = new
        {
            reportType = "timeline",
            periods = results.Count,
            edges = network.Edges.Select(edge => new
            {
                edge_id = edge.Id,
                route = $"{edge.FromNodeId}->{edge.ToNodeId}",
                trafficPermissions = FormatEdgeTrafficPermissions(network, edge)
            }),
            nodes = results.Select(result => new
            {
                period = result.Period,
                nodes = network.Nodes.Select(node =>
                {
                    var states = result.NodeStates.Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id)).ToList();
                    var backlog = states
                        .GroupBy(pair => pair.Key.TrafficType, pair => pair.Value.DemandBacklog, Comparer)
                        .ToDictionary(group => group.Key, group => group.Sum(), Comparer);
                    return new
                    {
                        node_id = node.Id,
                        node_name = node.Name,
                        demand_backlog_total = states.Sum(pair => pair.Value.DemandBacklog),
                        backlog
                    };
                })
            })
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string FormatEdgeTrafficPermissions(NetworkModel network, EdgeModel edge)
    {
        var resolver = new EdgeTrafficPermissionResolver();
        var trafficTypes = network.TrafficTypes
            .Select(definition => definition.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(Comparer)
            .OrderBy(name => name, Comparer)
            .ToList();

        if (trafficTypes.Count == 0)
        {
            return "Implicitly permitted";
        }

        return string.Join(
            "; ",
            trafficTypes.Select(trafficType =>
            {
                var effective = resolver.Resolve(network, edge, trafficType);
                return $"{trafficType}: {effective.Summary.Replace("Effective: ", string.Empty, StringComparison.Ordinal)}";
            }));
    }

    private static string FormatPlaceIdentity(NodeModel node)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(node.PlaceType))
        {
            parts.Add(node.PlaceType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(node.ControllingActor))
        {
            parts.Add($"controlled by {node.ControllingActor.Trim()}");
        }

        if (node.Tags.Count > 0)
        {
            parts.Add("tags: " + string.Join(", ", node.Tags));
        }

        return parts.Count == 0 ? "No worldbuilder metadata" : string.Join("; ", parts);
    }

    private static string FormatModelRoleSummary(NodeModel node)
    {
        var parts = new List<string>();
        var producers = FormatProfileTrafficList(node.TrafficProfiles.Where(profile => profile.Production > 0), profile => profile.Production);
        var consumers = FormatProfileTrafficList(node.TrafficProfiles.Where(profile => profile.Consumption > 0), profile => profile.Consumption);
        var stores = FormatProfileTrafficList(node.TrafficProfiles.Where(profile => profile.IsStore), profile => profile.StoreCapacity);

        if (node.NodeKind == NodeKind.CompositeSubnetwork)
        {
            parts.Add($"composite subnetwork {node.ReferencedSubnetworkId ?? "(unassigned)"}");
        }

        if (node.IsExternalInterface)
        {
            parts.Add($"external interface {node.InterfaceName ?? node.Id}");
        }

        if (!string.IsNullOrWhiteSpace(producers))
        {
            parts.Add($"produces {producers}");
        }

        if (!string.IsNullOrWhiteSpace(consumers))
        {
            parts.Add($"needs {consumers}");
        }

        if (!string.IsNullOrWhiteSpace(stores))
        {
            parts.Add($"stores {stores}");
        }

        if (node.TrafficProfiles.Any(profile => profile.CanTransship))
        {
            parts.Add(node.TranshipmentCapacity.HasValue
                ? $"transships up to {FormatNumber(node.TranshipmentCapacity)}"
                : "can transship");
        }

        return parts.Count == 0 ? "No configured production, demand, storage, or transhipment role." : SentenceJoin(parts) + ".";
    }

    private static string FormatPlaceImportance(NodeModel node, IReadOnlyList<RouteAllocation> allocations)
    {
        var parts = new List<string>();
        var outbound = SummarizeTrafficQuantities(allocations
            .Where(allocation => !allocation.IsLocalSupply && Comparer.Equals(allocation.ProducerNodeId, node.Id))
            .GroupBy(allocation => allocation.TrafficType, Comparer)
            .Select(group => new TrafficQuantity(group.Key, group.Sum(allocation => allocation.Quantity))));
        var inbound = SummarizeTrafficQuantities(allocations
            .Where(allocation => !allocation.IsLocalSupply && Comparer.Equals(allocation.ConsumerNodeId, node.Id))
            .GroupBy(allocation => allocation.TrafficType, Comparer)
            .Select(group => new TrafficQuantity(group.Key, group.Sum(allocation => allocation.Quantity))));
        var transitQuantity = allocations
            .Where(allocation => allocation.PathNodeIds.Count > 2 &&
                allocation.PathNodeIds.Skip(1).Take(allocation.PathNodeIds.Count - 2).Any(nodeId => Comparer.Equals(nodeId, node.Id)))
            .Sum(allocation => allocation.Quantity);

        if (!string.IsNullOrWhiteSpace(outbound))
        {
            parts.Add($"sends {outbound} to other places");
        }

        if (!string.IsNullOrWhiteSpace(inbound))
        {
            parts.Add($"receives {inbound}");
        }

        if (transitQuantity > 0)
        {
            parts.Add($"carries {FormatNumber(transitQuantity)} units as an intermediate stop");
        }

        if (!string.IsNullOrWhiteSpace(node.LoreDescription))
        {
            parts.Add($"metadata note: {node.LoreDescription.Trim()}");
        }

        if (node.NodeKind == NodeKind.CompositeSubnetwork)
        {
            parts.Add($"it represents child network {node.ReferencedSubnetworkId ?? "(unassigned)"}");
        }

        if (node.IsExternalInterface)
        {
            parts.Add($"it is exposed to a parent network as {node.InterfaceName ?? node.Id}");
        }

        return parts.Count == 0
            ? "This place matters because of its configured roles, but the current simulation did not route external flow through it."
            : SentenceJoin(parts) + ".";
    }

    private static string FormatInboundDependencies(
        NodeModel node,
        IReadOnlyList<RouteAllocation> allocations,
        IReadOnlyList<TrafficSimulationOutcome> outcomes)
    {
        var parts = new List<string>();
        var imported = SummarizeTrafficQuantities(allocations
            .Where(allocation => !allocation.IsLocalSupply && Comparer.Equals(allocation.ConsumerNodeId, node.Id))
            .GroupBy(allocation => allocation.TrafficType, Comparer)
            .Select(group => new TrafficQuantity(group.Key, group.Sum(allocation => allocation.Quantity))));
        if (!string.IsNullOrWhiteSpace(imported))
        {
            parts.Add($"imports {imported}");
        }

        var unmet = node.TrafficProfiles
            .Where(profile => profile.Consumption > 0)
            .Select(profile =>
            {
                var delivered = allocations
                    .Where(allocation => Comparer.Equals(allocation.ConsumerNodeId, node.Id) &&
                        Comparer.Equals(allocation.TrafficType, profile.TrafficType))
                    .Sum(allocation => allocation.Quantity);
                return new TrafficQuantity(profile.TrafficType, Math.Max(0d, profile.Consumption - delivered));
            })
            .Where(item => item.Quantity > 0)
            .ToList();

        var outcomeUnmetTypes = outcomes
            .Where(outcome => outcome.UnmetDemand > 0 && unmet.Any(item => Comparer.Equals(item.TrafficType, outcome.TrafficType)))
            .Select(outcome => outcome.TrafficType)
            .Distinct(Comparer)
            .ToHashSet(Comparer);

        var unmetSummary = SummarizeTrafficQuantities(unmet.Where(item => outcomeUnmetTypes.Contains(item.TrafficType)));
        if (!string.IsNullOrWhiteSpace(unmetSummary))
        {
            parts.Add($"unmet demand remains for {unmetSummary}");
        }

        return parts.Count == 0 ? "No imported dependency or unmet demand in the current result." : SentenceJoin(parts) + ".";
    }

    private static string FormatOutboundFunctions(NodeModel node, IReadOnlyList<RouteAllocation> allocations)
    {
        var outbound = SummarizeTrafficQuantities(allocations
            .Where(allocation => !allocation.IsLocalSupply && Comparer.Equals(allocation.ProducerNodeId, node.Id))
            .GroupBy(allocation => allocation.TrafficType, Comparer)
            .Select(group => new TrafficQuantity(group.Key, group.Sum(allocation => allocation.Quantity))));
        if (!string.IsNullOrWhiteSpace(outbound))
        {
            return $"Sends {outbound}.";
        }

        var local = SummarizeTrafficQuantities(allocations
            .Where(allocation => allocation.IsLocalSupply && Comparer.Equals(allocation.ProducerNodeId, node.Id))
            .GroupBy(allocation => allocation.TrafficType, Comparer)
            .Select(group => new TrafficQuantity(group.Key, group.Sum(allocation => allocation.Quantity))));
        return string.IsNullOrWhiteSpace(local)
            ? "No outbound deliveries in the current result."
            : $"Satisfies local demand for {local}.";
    }

    private static string FormatStoragePressure(NodeModel node)
    {
        var stores = node.TrafficProfiles
            .Where(profile => profile.IsStore)
            .Select(profile =>
            {
                var movement = profile.Production + profile.Consumption;
                return profile.StoreCapacity.HasValue && profile.StoreCapacity.Value > 0d
                    ? $"{profile.TrafficType}: configured stockpile {FormatNumber(profile.StoreCapacity)}, modeled role volume {FormatNumber(movement)} ({FormatUtilisation(movement, profile.StoreCapacity)})"
                    : $"{profile.TrafficType}: stockpile enabled without a finite capacity";
            })
            .ToList();

        return stores.Count == 0 ? "No configured stockpile role." : string.Join(" ", stores);
    }

    private static string[]? FormatBottleneckRow(EdgeModel edge, NetworkModel network, IReadOnlyList<RouteAllocation> allocations)
    {
        var routed = allocations
            .Where(allocation => allocation.PathEdgeIds.Contains(edge.Id, Comparer))
            .ToList();
        var flow = routed.Sum(allocation => allocation.Quantity);
        if (flow <= 0d && !edge.Capacity.HasValue)
        {
            return null;
        }

        var fromName = network.Nodes.FirstOrDefault(node => Comparer.Equals(node.Id, edge.FromNodeId))?.Name ?? edge.FromNodeId;
        var toName = network.Nodes.FirstOrDefault(node => Comparer.Equals(node.Id, edge.ToNodeId))?.Name ?? edge.ToNodeId;
        var routeNotes = new List<string>();
        if (!string.IsNullOrWhiteSpace(edge.RouteType))
        {
            routeNotes.Add(edge.RouteType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(edge.SeasonalRisk))
        {
            routeNotes.Add($"seasonal risk: {edge.SeasonalRisk.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(edge.SecurityNotes))
        {
            routeNotes.Add($"security: {edge.SecurityNotes.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(edge.TollNotes))
        {
            routeNotes.Add($"toll: {edge.TollNotes.Trim()}");
        }

        var observedUse = flow > 0d
            ? $"{FormatNumber(flow)} units across {routed.Count} movement(s)"
            : "No routed movement in the current result";
        var reading = edge.Capacity.HasValue && edge.Capacity.Value > 0d
            ? $"Current result uses {FormatUtilisation(flow, edge.Capacity)} of configured capacity."
            : "No finite route capacity is configured.";

        if (routeNotes.Count > 0)
        {
            reading += " " + string.Join(" ", routeNotes) + ".";
        }

        return
        [
            $"{edge.Id}: {fromName} -> {toName}",
            observedUse,
            FormatNumber(edge.Capacity),
            reading
        ];
    }

    private static string FormatProfileTrafficList(IEnumerable<NodeTrafficProfile> profiles, Func<NodeTrafficProfile, double?> quantitySelector)
    {
        return SummarizeTrafficQuantities(profiles.Select(profile => new TrafficQuantity(profile.TrafficType, quantitySelector(profile) ?? 0d)));
    }

    private static string SummarizeTrafficQuantities(IEnumerable<TrafficQuantity> quantities)
    {
        var materialized = quantities
            .Where(item => item.Quantity > 0d)
            .OrderByDescending(item => item.Quantity)
            .ThenBy(item => item.TrafficType, Comparer)
            .Take(3)
            .Select(item => $"{FormatNumber(item.Quantity)} {item.TrafficType}")
            .ToList();

        return materialized.Count == 0 ? string.Empty : string.Join(", ", materialized);
    }

    private static string SentenceJoin(IReadOnlyList<string> parts)
    {
        if (parts.Count == 0)
        {
            return string.Empty;
        }

        if (parts.Count == 1)
        {
            return char.ToUpperInvariant(parts[0][0]) + parts[0][1..];
        }

        return char.ToUpperInvariant(parts[0][0]) + parts[0][1..] + "; " + string.Join("; ", parts.Skip(1));
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

    public static string FormatRoutingPreference(RoutingPreference routingPreference)
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

    public static string FormatNumber(double? value)
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

    public static string FormatCauseBreakdown(
        IReadOnlyDictionary<TemporalNetworkSimulationEngine.PressureCauseKind, double> causeWeights)
    {
        if (causeWeights.Count == 0)
        {
            return "None";
        }

        return string.Join(
            "; ",
            causeWeights
                .Where(pair => pair.Value > 0)
                .OrderByDescending(pair => pair.Value)
                .Select(pair => $"{FormatPressureCause(pair.Key.ToString())} {FormatNumber(pair.Value)}"));
    }

    public static string FormatUtilisation(double flow, double? capacity)
    {
        if (!capacity.HasValue || capacity.Value <= 0)
        {
            return capacity.HasValue && capacity.Value == 0d
                ? "No capacity"
                : "Unlimited";
        }

        return $"{(flow / capacity.Value):0%}";
    }

    public static string FormatPressureCause(string? cause)
    {
        if (string.IsNullOrWhiteSpace(cause))
        {
            return string.Empty;
        }

        var normalized = cause.Trim();
        return normalized switch
        {
            "DemandBacklog" => "unmet demand",
            "InputShortage" => "input shortage",
            "StoreCapacitySaturation" => "store capacity saturation",
            "EdgeCapacitySaturation" => "route capacity saturation",
            "TranshipmentCapacitySaturation" => "transhipment saturation",
            "RouteUnavailable" => "route unavailable",
            "PerishedInNodeInventory" => "goods perished in storage",
            "PerishedInTransit" => "goods perished in transit",
            "TimelineShock" => "timeline shock",
            _ => string.Concat(
                normalized.Select((ch, index) =>
                    index > 0 && char.IsUpper(ch) && !char.IsUpper(normalized[index - 1])
                        ? $" {char.ToLowerInvariant(ch)}"
                        : char.ToLowerInvariant(ch).ToString()))
        };
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

    private sealed record TrafficQuantity(string TrafficType, double Quantity);
}

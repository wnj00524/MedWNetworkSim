using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.Presentation;

public sealed class NetworkHealthSummary
{
    public static NetworkHealthSummary Empty { get; } = new();
    public double HealthScore { get; init; }
    public double TotalDemand { get; init; }
    public double TotalServed { get; init; }
    public double TotalUnmet { get; init; }
    public int CriticalIssueCount { get; init; }
    public int WarningIssueCount { get; init; }
}

public sealed class BottleneckSummary
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public double SeverityScore { get; init; }
    public string Badge { get; init; } = string.Empty;
}

public sealed class FlowKpiSummary
{
    public static FlowKpiSummary Empty { get; } = new();
    public double ServedDemandRatio { get; init; }
    public double UnmetDemandRatio { get; init; }
    public double AverageRouteCost { get; init; }
    public double AverageRouteTime { get; init; }
}

public sealed class ScenarioDeltaSummary
{
    public static ScenarioDeltaSummary Empty { get; } = new();
    public double DeliveredDelta { get; init; }
    public double UnmetDelta { get; init; }
    public double CostDelta { get; init; }
}

public sealed class TimelineMetricPoint
{
    public int Period { get; init; }
    public double ServedDemand { get; init; }
    public double UnmetDemand { get; init; }
}

public sealed class InsightCardModel
{
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
}

public static class DashboardSummaryCalculator
{
    public static NetworkHealthSummary ComputeHealthSummary(IReadOnlyList<TrafficSimulationOutcome>? outcomes, IReadOnlyList<NetworkIssue>? issues)
    {
        outcomes ??= [];
        issues ??= [];

        double demand = 0d;
        double served = 0d;
        double unmet = 0d;

        foreach (var outcome in outcomes)
        {
            demand += Math.Max(0d, outcome.TotalConsumption);
            served += Math.Max(0d, outcome.TotalDelivered);
            unmet += Math.Max(0d, outcome.UnmetDemand);
        }

        var score = demand <= 0d ? 0d : Math.Clamp((served / demand) * 100d, 0d, 100d);

        int critical = 0;
        int warning = 0;

        foreach (var issue in issues)
        {
            if (issue.Severity == NetworkIssueSeverity.Critical)
            {
                critical++;
            }
            else if (issue.Severity == NetworkIssueSeverity.Warning)
            {
                warning++;
            }
        }

        return new NetworkHealthSummary
        {
            HealthScore = score,
            TotalDemand = demand,
            TotalServed = served,
            TotalUnmet = unmet,
            CriticalIssueCount = critical,
            WarningIssueCount = warning
        };
    }

    public static FlowKpiSummary ComputeFlowKpiSummary(IReadOnlyList<TrafficSimulationOutcome>? outcomes)
    {
        outcomes ??= [];

        double totalDemand = 0d;
        double totalServed = 0d;
        double totalCost = 0d;
        double totalTime = 0d;

        foreach (var outcome in outcomes)
        {
            totalDemand += Math.Max(0d, outcome.TotalConsumption);
            totalServed += Math.Max(0d, outcome.TotalDelivered);

            if (outcome.Allocations != null)
            {
                foreach (var allocation in outcome.Allocations)
                {
                    totalCost += allocation.DeliveredCostPerUnit * allocation.Quantity;
                    totalTime += allocation.TotalTime * allocation.Quantity;
                }
            }
        }

        return new FlowKpiSummary
        {
            ServedDemandRatio = totalDemand <= 0d ? 0d : totalServed / totalDemand,
            UnmetDemandRatio = totalDemand <= 0d ? 0d : Math.Max(0d, totalDemand - totalServed) / totalDemand,
            AverageRouteCost = totalServed <= 0d ? 0d : totalCost / totalServed,
            AverageRouteTime = totalServed <= 0d ? 0d : totalTime / totalServed
        };
    }
}

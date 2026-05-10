using Xunit;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.Presentation;

namespace MedWNetworkSim.Tests;

public sealed class DashboardSummaryCalculatorTests
{
    [Fact]
    public void ComputeHealthSummary_ReturnsZeros_ForEmptyInputs()
    {
        var summary = DashboardSummaryCalculator.ComputeHealthSummary([], []);
        Assert.Equal(0d, summary.HealthScore);
        Assert.Equal(0d, summary.TotalDemand);
        Assert.Equal(0d, summary.TotalServed);
        Assert.Equal(0d, summary.TotalUnmet);
    }

    [Fact]
    public void ComputeFlowKpiSummary_ComputesRatios_AndAverages()
    {
        var outcomes = new[]
        {
            new TrafficSimulationOutcome
            {
                TrafficType = "General",
                TotalConsumption = 100,
                TotalDelivered = 75,
                UnmetDemand = 25,
                Allocations = new[] { new RouteAllocation { Quantity = 75, DeliveredCostPerUnit = 12, TotalTime = 4 } }
            }
        };

        var summary = DashboardSummaryCalculator.ComputeFlowKpiSummary(outcomes);
        Assert.Equal(0.75d, summary.ServedDemandRatio, 3);
        Assert.Equal(0.25d, summary.UnmetDemandRatio, 3);
        Assert.Equal(12d, summary.AverageRouteCost, 3);
        Assert.Equal(4d, summary.AverageRouteTime, 3);
    }
}

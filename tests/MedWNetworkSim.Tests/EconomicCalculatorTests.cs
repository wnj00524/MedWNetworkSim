using System.Collections.Generic;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class EconomicCalculatorTests
{
    [Fact]
    public void Calculate_WithEmptyOutcomes_ReturnsZeroes()
    {
        var calculator = new EconomicCalculator();
        var network = new NetworkModel();
        var result = new SimulationResult { Outcomes = [] };

        var summary = calculator.Calculate(network, result);

        Assert.Equal(0d, summary.TotalRevenue);
        Assert.Equal(0d, summary.TotalSalesRevenue);
        Assert.Equal(0d, summary.TotalTransportCost);
        Assert.Equal(0d, summary.TotalProductionCost);
        Assert.Equal(0d, summary.TotalTax);
        Assert.Equal(0d, summary.TotalHoldingCost);
        Assert.Equal(0d, summary.TotalShortagePenalty);
        Assert.Equal(0d, summary.TotalProfit);
    }

    [Fact]
    public void Calculate_WithBasicAllocations_SumsCorrectly()
    {
        var calculator = new EconomicCalculator();
        var network = new NetworkModel
        {
            Nodes =
            [
                new NodeModel
                {
                    Id = "node1",
                    TrafficProfiles =
                    [
                        new NodeTrafficProfile
                        {
                            TrafficType = "TypeA",
                            UnitPrice = 20d,
                            SalesTaxRate = 0.1d
                        }
                    ]
                }
            ]
        };

        var result = new SimulationResult
        {
            Outcomes =
            [
                new TrafficSimulationOutcome
                {
                    TrafficType = "TypeA",
                    Allocations =
                    [
                        new RouteAllocation
                        {
                            TrafficType = "TypeA",
                            ProducerNodeId = "node1",
                            Quantity = 10d,
                            TotalCost = 5d,
                            SourceUnitCostPerUnit = 3d
                        }
                    ]
                }
            ]
        };

        var summary = calculator.Calculate(network, result);

        Assert.Equal(200d, summary.TotalRevenue);
        Assert.Equal(200d, summary.TotalSalesRevenue);
        Assert.Equal(50d, summary.TotalTransportCost);
        Assert.Equal(30d, summary.TotalProductionCost);
        Assert.Equal(20d, summary.TotalTax);
        Assert.Equal(0d, summary.TotalHoldingCost);
        Assert.Equal(0d, summary.TotalShortagePenalty);
        Assert.Equal(100d, summary.TotalProfit);
    }

    [Fact]
    public void Calculate_WithUnmetDemand_AppliesShortagePenalty()
    {
        var calculator = new EconomicCalculator();
        var network = new NetworkModel
        {
            Nodes =
            [
                new NodeModel
                {
                    Id = "node1",
                    TrafficProfiles =
                    [
                        new NodeTrafficProfile
                        {
                            TrafficType = "TypeA",
                            ShortagePenalty = 5d
                        }
                    ]
                }
            ]
        };

        var result = new SimulationResult
        {
            Outcomes =
            [
                new TrafficSimulationOutcome
                {
                    TrafficType = "TypeA",
                    UnmetDemand = 10d,
                    Allocations = [] // No allocations, just unmet demand
                }
            ]
        };

        var summary = calculator.Calculate(network, result);

        Assert.Equal(0d, summary.TotalRevenue);
        Assert.Equal(0d, summary.TotalTransportCost);
        Assert.Equal(0d, summary.TotalProductionCost);
        Assert.Equal(0d, summary.TotalTax);
        Assert.Equal(50d, summary.TotalShortagePenalty); // 10 UnmetDemand * 5 Penalty
        Assert.Equal(-50d, summary.TotalProfit); // 0 Revenue - (0 Costs + 50 Penalty)
    }

    [Fact]
    public void Calculate_MixedScenario_AggregatesCorrectly()
    {
        var calculator = new EconomicCalculator();
        var network = new NetworkModel
        {
            Nodes =
            [
                new NodeModel
                {
                    Id = "node1",
                    TrafficProfiles =
                    [
                        new NodeTrafficProfile
                        {
                            TrafficType = "TypeA",
                            ShortagePenalty = 2d,
                            UnitPrice = 10d,
                            SalesTaxRate = 0.1d
                        },
                        new NodeTrafficProfile
                        {
                            TrafficType = "TypeB",
                            ShortagePenalty = 10d,
                            UnitPrice = 10d,
                            SalesTaxRate = 0.1d
                        }
                    ]
                }
            ]
        };

        var result = new SimulationResult
        {
            Outcomes =
            [
                new TrafficSimulationOutcome
                {
                    TrafficType = "TypeA",
                    UnmetDemand = 5d,
                    Allocations =
                    [
                        new RouteAllocation
                        {
                            TrafficType = "TypeA",
                            ProducerNodeId = "node1",
                            Quantity = 10d,
                            TotalCost = 1d,
                            SourceUnitCostPerUnit = 2d
                        }
                    ]
                },
                new TrafficSimulationOutcome
                {
                    TrafficType = "TypeB",
                    UnmetDemand = 2d,
                    Allocations =
                    [
                        new RouteAllocation
                        {
                            TrafficType = "TypeB",
                            ProducerNodeId = "node1",
                            Quantity = 5d,
                            TotalCost = 1d,
                            SourceUnitCostPerUnit = 1d
                        }
                    ]
                }
            ]
        };

        var summary = calculator.Calculate(network, result);

        Assert.Equal(150d, summary.TotalRevenue); // 100 + 50
        Assert.Equal(150d, summary.TotalSalesRevenue);
        Assert.Equal(15d, summary.TotalTransportCost); // 10 + 5
        Assert.Equal(25d, summary.TotalProductionCost); // 20 + 5
        Assert.Equal(15d, summary.TotalTax); // 10 + 5
        Assert.Equal(30d, summary.TotalShortagePenalty); // (5 * 2) + (2 * 10)
        Assert.Equal(65d, summary.TotalProfit); // 150 Revenue - (15 Transport + 25 Production + 15 Tax + 30 Penalty)
    }
}

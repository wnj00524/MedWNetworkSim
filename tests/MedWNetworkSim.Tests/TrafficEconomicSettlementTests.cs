using MedWNetworkSim.App.Agents;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class TrafficEconomicSettlementTests
{
    [Fact]
    public void BuyTraffic_And_SellTraffic_UpdateIntent_WithoutImmediateCashTransfer()
    {
        var network = BuildSimpleNetwork(production: 0d, consumption: 0d);
        var actor = new SimulationActorState { Id = "firm", Kind = SimulationActorKind.Firm, Cash = 100d, Budget = 100d };
        var applier = new SimulationActorActionApplier();

        var (updated, outcomes) = applier.Apply(
            network,
            [
                new SimulationActorAction
                {
                    Id = "sell",
                    ActorId = "firm",
                    Kind = SimulationActorActionKind.SellTraffic,
                    TargetNodeId = "producer",
                    TrafficType = "Food",
                    AbsoluteValue = 12d
                },
                new SimulationActorAction
                {
                    Id = "buy",
                    ActorId = "firm",
                    Kind = SimulationActorActionKind.BuyTraffic,
                    TargetNodeId = "consumer",
                    TrafficType = "Food",
                    DeltaValue = 7d
                }
            ],
            new Dictionary<string, SimulationActorState>(StringComparer.OrdinalIgnoreCase) { ["firm"] = actor },
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));

        Assert.All(outcomes, outcome => Assert.True(outcome.Applied));
        Assert.Equal(12d, updated.Nodes.Single(node => node.Id == "producer").TrafficProfiles.Single().Production);
        Assert.Equal(7d, updated.Nodes.Single(node => node.Id == "consumer").TrafficProfiles.Single().Consumption);
        Assert.Equal(100d, actor.Cash);
    }

    [Fact]
    public void SellerRevenue_UsesDeliveredQuantity_NotDeclaredProduction()
    {
        var network = BuildSimpleNetwork(production: 10d, consumption: 5d, salePrice: 10d, productionCost: 0d, edgeCost: 0d);
        var outcome = new NetworkSimulationEngine().Simulate(network).Single();

        Assert.Equal(5d, outcome.TotalDelivered);
        Assert.Equal(5d, outcome.UnusedSupply);
        Assert.Equal(50d, outcome.TotalSalesRevenue);
        Assert.Equal(50d, outcome.TotalProfit);
    }

    [Fact]
    public void UnsoldProduction_CreatesNoSaleRevenue()
    {
        var network = BuildSimpleNetwork(production: 10d, consumption: 0d, salePrice: 10d);
        var outcome = new NetworkSimulationEngine().Simulate(network).Single();

        Assert.Equal(0d, outcome.TotalDelivered);
        Assert.Equal(0d, outcome.TotalSalesRevenue);
        Assert.Equal(0d, outcome.TotalProfit);
    }

    [Fact]
    public void ProfitFormula_UsesRevenueTransportProductionAndTax()
    {
        var network = BuildSimpleNetwork(
            production: 5d,
            consumption: 5d,
            salePrice: 10d,
            productionCost: 3d,
            edgeCost: 2d,
            salesTaxRate: 0.1d);

        var allocation = new NetworkSimulationEngine().Simulate(network).Single().Allocations.Single();

        Assert.Equal(50d, allocation.SaleRevenue);
        Assert.Equal(10d, allocation.TotalTransportCost);
        Assert.Equal(15d, allocation.TotalProductionCost);
        Assert.Equal(5d, allocation.TotalTax);
        Assert.Equal(20d, allocation.Profit);
    }

    [Fact]
    public void RecipeInputCosts_AreIncludedInProductionCost()
    {
        var network = BuildRecipeNetwork();
        RouteAllocation? allocation = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var finished = new NetworkSimulationEngine().Simulate(network).Single(outcome => outcome.TrafficType == "Finished");
            allocation = finished.Allocations.Single();
            if (Math.Abs(allocation.ProductionCostPerUnit - 9d) < 0.000001d)
            {
                break;
            }
        }

        Assert.NotNull(allocation);
        Assert.Equal(9d, allocation!.ProductionCostPerUnit);
        Assert.Equal(45d, allocation.TotalProductionCost);
    }

    [Fact]
    public void DefaultUnitProductionCost_IsUsedWithoutRecipeInputs()
    {
        var network = BuildSimpleNetwork(production: 4d, consumption: 4d, salePrice: 10d, productionCost: 3d, edgeCost: 2d);
        var allocation = new NetworkSimulationEngine().Simulate(network).Single().Allocations.Single();

        Assert.Equal(3d, allocation.ProductionCostPerUnit);
        Assert.Equal(12d, allocation.TotalProductionCost);
    }

    [Fact]
    public void ActorCash_UpdatesFromSettlementAfterStep()
    {
        var network = BuildSimpleNetwork(production: 5d, consumption: 5d, salePrice: 10d, productionCost: 3d, edgeCost: 2d);
        var seller = new SimulationActorState
        {
            Id = "seller",
            Kind = SimulationActorKind.Firm,
            ControlledNodeIds = ["producer"],
            Cash = 100d,
            GenerateAutomaticDecisions = false
        };
        var buyer = new SimulationActorState
        {
            Id = "buyer",
            Kind = SimulationActorKind.Firm,
            ControlledNodeIds = ["consumer"],
            Cash = 100d,
            GenerateAutomaticDecisions = false
        };

        var result = new SimulationActorCoordinator().StepActorsOnce(network, [seller, buyer]);

        Assert.Equal(125d, seller.Cash);
        Assert.Equal(50d, buyer.Cash);
        Assert.Equal(25d, result.Metrics.ActorProfitById["seller"]);
        Assert.Equal(50d, result.Metrics.ActorSalesRevenueById["seller"]);
    }

    [Fact]
    public void GovernmentActor_ReceivesTaxRevenue()
    {
        var network = BuildSimpleNetwork(
            production: 5d,
            consumption: 5d,
            salePrice: 10d,
            productionCost: 3d,
            edgeCost: 2d,
            salesTaxRate: 0.1d,
            routeTaxRate: 0.2d);
        var seller = new SimulationActorState { Id = "seller", Kind = SimulationActorKind.Firm, ControlledNodeIds = ["producer"], Cash = 0d, GenerateAutomaticDecisions = false };
        var buyer = new SimulationActorState { Id = "buyer", Kind = SimulationActorKind.Firm, ControlledNodeIds = ["consumer"], Cash = 100d, GenerateAutomaticDecisions = false };
        var government = new SimulationActorState { Id = "gov", Kind = SimulationActorKind.Government, Cash = 0d, GenerateAutomaticDecisions = false };

        var result = new SimulationActorCoordinator().StepActorsOnce(network, [seller, buyer, government]);

        Assert.Equal(18d, seller.Cash);
        Assert.Equal(50d, buyer.Cash);
        Assert.Equal(7d, government.Cash);
        Assert.Equal(7d, result.Metrics.ActorTaxesPaidById["seller"]);
        Assert.Equal(0d, result.Metrics.ActorTaxesPaidById["buyer"]);
        Assert.Equal(7d, result.Metrics.ActorTaxesReceivedById["gov"]);
    }

    [Fact]
    public void TaxLedgerPolicy_SellerRemitsSalesAndRouteTax()
    {
        var network = BuildSimpleNetwork(
            production: 5d,
            consumption: 5d,
            salePrice: 10d,
            productionCost: 3d,
            edgeCost: 2d,
            salesTaxRate: 0.1d,
            routeTaxRate: 0.2d);
        var actors = new Dictionary<string, SimulationActorState>(StringComparer.OrdinalIgnoreCase)
        {
            ["seller"] = new() { Id = "seller", Kind = SimulationActorKind.Firm, ControlledNodeIds = ["producer"] },
            ["buyer"] = new() { Id = "buyer", Kind = SimulationActorKind.Firm, ControlledNodeIds = ["consumer"] },
            ["gov"] = new() { Id = "gov", Kind = SimulationActorKind.Government }
        };
        var outcomes = new NetworkSimulationEngine().Simulate(network);

        var settlement = new TrafficEconomicSettlementService().Settle(network, outcomes, actors);

        var seller = settlement.Ledgers["seller"];
        Assert.Equal(7d, seller.TaxesPaidBySeller);
        Assert.Equal(0d, seller.TaxesPaidByBuyer);
        Assert.Equal(7d, seller.TaxesPaid);
        Assert.Equal(18d, seller.Profit);
        Assert.Equal(18d, seller.CashDelta);

        var buyer = settlement.Ledgers["buyer"];
        Assert.Equal(50d, buyer.PurchaseCost);
        Assert.Equal(0d, buyer.TaxesPaidByBuyer);
        Assert.Equal(0d, buyer.TaxesPaid);
        Assert.Equal(-50d, buyer.CashDelta);

        var government = settlement.Ledgers["gov"];
        Assert.Equal(7d, government.TaxesReceivedByAuthority);
        Assert.Equal(7d, government.TaxesReceived);
        Assert.Equal(7d, government.CashDelta);
    }

    [Fact]
    public void ZeroCostZeroPriceNetwork_RetainsCurrentZeroEconomicBehaviour()
    {
        var network = BuildSimpleNetwork(production: 5d, consumption: 5d, salePrice: 0d, productionCost: 0d, edgeCost: 0d);
        var outcome = new NetworkSimulationEngine().Simulate(network).Single();

        Assert.Equal(5d, outcome.TotalDelivered);
        Assert.Equal(0d, outcome.TotalSalesRevenue);
        Assert.Equal(0d, outcome.TotalTransportCost);
        Assert.Equal(0d, outcome.TotalProductionCost);
        Assert.Equal(0d, outcome.TotalTax);
        Assert.Equal(0d, outcome.TotalProfit);
        Assert.Equal(0d, outcome.Allocations.Single().TotalMovementCost);
    }

    [Fact]
    public void TemporalOverlay_PreservesEconomicsForSettledAllocations()
    {
        var network = BuildSimpleNetwork(
            production: 5d,
            consumption: 5d,
            salePrice: 10d,
            productionCost: 3d,
            edgeCost: 2d,
            salesTaxRate: 0.1d);
        network.TimelineEvents.Add(new TimelineEventModel
        {
            Id = "fuel-shock",
            Name = "Fuel shock",
            StartPeriod = 1,
            EndPeriod = 1,
            Effects =
            [
                new TimelineEventEffectModel
                {
                    EffectType = TimelineEventEffectType.RouteCostMultiplier,
                    EdgeId = "edge",
                    Multiplier = 2d
                }
            ]
        });

        var engine = new TemporalNetworkSimulationEngine();
        var state = engine.Initialize(network);
        var allocation = engine.Advance(network, state).Allocations.Single();

        Assert.Equal(50d, allocation.SaleRevenue);
        Assert.Equal(20d, allocation.TotalTransportCost);
        Assert.Equal(15d, allocation.TotalProductionCost);
        Assert.Equal(5d, allocation.TotalTax);
        Assert.Equal(
            allocation.SaleRevenue - (allocation.TotalTransportCost + allocation.TotalProductionCost + allocation.TotalTax),
            allocation.Profit);
        Assert.True(allocation.Profit > 0d);
    }

    private static NetworkModel BuildSimpleNetwork(
        double production,
        double consumption,
        double salePrice = 0d,
        double productionCost = 0d,
        double edgeCost = 0d,
        double salesTaxRate = 0d,
        double routeTaxRate = 0d)
    {
        var layerId = Guid.NewGuid();
        return new NetworkModel
        {
            Name = "Economic Test",
            Layers = [new NetworkLayerModel { Id = layerId, Name = "Physical", Type = NetworkLayerType.Physical, Order = 0 }],
            TrafficTypes =
            [
                new TrafficTypeDefinition
                {
                    Name = "Food",
                    RoutingPreference = RoutingPreference.Cost,
                    AllocationMode = AllocationMode.GreedyBestRoute,
                    DefaultUnitSalePrice = salePrice,
                    DefaultUnitProductionCost = productionCost,
                    SalesTaxRate = salesTaxRate,
                    RouteTaxRate = routeTaxRate
                }
            ],
            Nodes =
            [
                new NodeModel
                {
                    Id = "producer",
                    Name = "Producer",
                    LayerId = layerId,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Production = production }]
                },
                new NodeModel
                {
                    Id = "consumer",
                    Name = "Consumer",
                    LayerId = layerId,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Food", Consumption = consumption }]
                }
            ],
            Edges =
            [
                new EdgeModel
                {
                    Id = "edge",
                    FromNodeId = "producer",
                    ToNodeId = "consumer",
                    LayerId = layerId,
                    Capacity = 100d,
                    Cost = edgeCost,
                    Time = 1d
                }
            ]
        };
    }

    private static NetworkModel BuildRecipeNetwork()
    {
        var layerId = Guid.NewGuid();
        return new NetworkModel
        {
            Name = "Recipe Test",
            Layers = [new NetworkLayerModel { Id = layerId, Name = "Physical", Type = NetworkLayerType.Physical, Order = 0 }],
            TrafficTypes =
            [
                new TrafficTypeDefinition { Name = "Raw", RoutingPreference = RoutingPreference.Cost, AllocationMode = AllocationMode.GreedyBestRoute, DefaultUnitProductionCost = 2d },
                new TrafficTypeDefinition { Name = "Finished", RoutingPreference = RoutingPreference.Cost, AllocationMode = AllocationMode.GreedyBestRoute, DefaultUnitProductionCost = 3d, DefaultUnitSalePrice = 20d }
            ],
            Nodes =
            [
                new NodeModel
                {
                    Id = "raw",
                    Name = "Raw",
                    LayerId = layerId,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Raw", Production = 10d }]
                },
                new NodeModel
                {
                    Id = "factory",
                    Name = "Factory",
                    LayerId = layerId,
                    TrafficProfiles =
                    [
                        new NodeTrafficProfile
                        {
                            TrafficType = "Raw",
                            CanTransship = true
                        },
                        new NodeTrafficProfile
                        {
                            TrafficType = "Finished",
                            Production = 5d,
                            InputRequirements = [new ProductionInputRequirement { TrafficType = "Raw", InputQuantity = 2d, OutputQuantity = 1d }]
                        }
                    ]
                },
                new NodeModel
                {
                    Id = "buyer",
                    Name = "Buyer",
                    LayerId = layerId,
                    TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Finished", Consumption = 5d }]
                }
            ],
            Edges =
            [
                new EdgeModel { Id = "raw-factory", FromNodeId = "raw", ToNodeId = "factory", LayerId = layerId, Capacity = 100d, Cost = 1d, Time = 1d },
                new EdgeModel { Id = "factory-buyer", FromNodeId = "factory", ToNodeId = "buyer", LayerId = layerId, Capacity = 100d, Cost = 1d, Time = 1d }
            ]
        };
    }
}

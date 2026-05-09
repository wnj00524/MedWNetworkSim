using MedWNetworkSim.App.Agents;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public sealed class SimulationActorEconomicLedger
{
    public string ActorId { get; set; } = string.Empty;
    public double SalesRevenue { get; set; }
    public double PurchaseCost { get; set; }
    public double ProductionCost { get; set; }
    public double TransportCost { get; set; }
    public double TaxesPaid { get; set; }
    public double TaxesReceived { get; set; }
    public double Profit { get; set; }
    public double CashDelta { get; set; }
}

public sealed class TrafficEconomicSettlementResult
{
    public IReadOnlyList<TrafficSimulationOutcome> Outcomes { get; init; } = [];
    public IReadOnlyDictionary<string, SimulationActorEconomicLedger> LedgersByActorId { get; init; } = new Dictionary<string, SimulationActorEconomicLedger>(StringComparer.OrdinalIgnoreCase);
}

public sealed class TrafficEconomicSettlementService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public TrafficEconomicSettlementResult Settle(NetworkModel network, IReadOnlyList<TrafficSimulationOutcome> outcomes, IReadOnlyDictionary<string, SimulationActorState>? actorsById = null)
    {
        actorsById ??= new Dictionary<string, SimulationActorState>(Comparer);
        var nodesById = network.Nodes.ToDictionary(n => n.Id, n => n, Comparer);
        var defsByType = network.TrafficTypes.ToDictionary(t => t.Name, t => t, Comparer);
        var taxActorId = actorsById.Values.FirstOrDefault(a => a.IsEnabled && a.Kind == SimulationActorKind.Government)?.Id;
        var ledgers = new Dictionary<string, SimulationActorEconomicLedger>(Comparer);

        SimulationActorEconomicLedger Ledger(string id)
        {
            if (!ledgers.TryGetValue(id, out var l)) { l = new SimulationActorEconomicLedger { ActorId = id }; ledgers[id] = l; }
            return l;
        }

        string? ResolveActor(string nodeId)
        {
            var byControl = actorsById.Values.FirstOrDefault(a => a.ControlledNodeIds.Contains(nodeId, Comparer))?.Id;
            if (!string.IsNullOrWhiteSpace(byControl)) return byControl;
            return nodesById.TryGetValue(nodeId, out var node) ? node.ControllingActor : null;
        }

        var settled = outcomes.Select(o =>
        {
            var def = defsByType.GetValueOrDefault(o.TrafficType) ?? new TrafficTypeDefinition { Name = o.TrafficType };
            var allocs = o.Allocations.Select(a =>
            {
                nodesById.TryGetValue(a.ProducerNodeId, out var producerNode);
                var profile = producerNode?.TrafficProfiles.FirstOrDefault(p => Comparer.Equals(p.TrafficType, o.TrafficType));
                var sellerId = ResolveActor(a.ProducerNodeId);
                var buyerId = ResolveActor(a.ConsumerNodeId);
                var saleUnitPrice = profile?.UnitPrice > 0d ? profile.UnitPrice : def.DefaultUnitSalePrice;
                var baseProductionCost = profile?.ProductionCostPerUnit ?? def.DefaultUnitProductionCost;
                var salesTaxRate = profile?.SalesTaxRate ?? def.SalesTaxRate;
                var transportPerUnit = a.TotalCost + a.BidCostPerUnit;
                var totalTransportCost = transportPerUnit * a.Quantity;
                var productionPerUnit = a.SourceUnitCostPerUnit + baseProductionCost;
                var totalProductionCost = productionPerUnit * a.Quantity;
                var saleRevenue = saleUnitPrice * a.Quantity;
                var salesTax = saleRevenue * salesTaxRate;
                var routeTax = totalTransportCost * def.RouteTaxRate;
                var totalTax = salesTax + routeTax;
                var profit = saleRevenue - (totalTransportCost + totalProductionCost + totalTax);

                if (!string.IsNullOrWhiteSpace(sellerId))
                {
                    var s = Ledger(sellerId!); s.SalesRevenue += saleRevenue; s.TransportCost += totalTransportCost; s.ProductionCost += totalProductionCost; s.TaxesPaid += totalTax; s.Profit += profit; s.CashDelta += profit;
                }
                if (!string.IsNullOrWhiteSpace(buyerId))
                {
                    var b = Ledger(buyerId!); b.PurchaseCost += saleRevenue; b.TaxesPaid += totalTax; b.CashDelta -= (saleRevenue + totalTax);
                }
                if (!string.IsNullOrWhiteSpace(taxActorId))
                {
                    var g = Ledger(taxActorId!); g.TaxesReceived += totalTax; g.CashDelta += totalTax;
                }

                return new RouteAllocation
                {
                    Period = a.Period,
                    TrafficType = a.TrafficType,
                    RoutingPreference = a.RoutingPreference,
                    AllocationMode = a.AllocationMode,
                    ProducerNodeId = a.ProducerNodeId,
                    ProducerName = a.ProducerName,
                    ConsumerNodeId = a.ConsumerNodeId,
                    ConsumerName = a.ConsumerName,
                    Quantity = a.Quantity,
                    IsLocalSupply = a.IsLocalSupply,
                    TotalTime = a.TotalTime,
                    TotalCost = a.TotalCost,
                    BidCostPerUnit = a.BidCostPerUnit,
                    SourceUnitCostPerUnit = a.SourceUnitCostPerUnit,
                    DeliveredCostPerUnit = a.DeliveredCostPerUnit,
                    TotalMovementCost = a.TotalMovementCost,
                    TotalScore = a.TotalScore,
                    PathNodeNames = a.PathNodeNames,
                    PathNodeIds = a.PathNodeIds,
                    PathEdgeIds = a.PathEdgeIds,
                    SaleUnitPrice = saleUnitPrice,
                    SaleRevenue = saleRevenue,
                    TransportCostPerUnit = transportPerUnit,
                    TotalTransportCost = totalTransportCost,
                    ProductionCostPerUnit = productionPerUnit,
                    TotalProductionCost = totalProductionCost,
                    TaxPerUnit = a.Quantity > 0d ? totalTax / a.Quantity : 0d,
                    TotalTax = totalTax,
                    Profit = profit,
                    SellerActorId = sellerId,
                    BuyerActorId = buyerId,
                    TaxAuthorityActorId = taxActorId
                };
            }).ToList();

            return new TrafficSimulationOutcome
            {
                TrafficType = o.TrafficType,
                RoutingPreference = o.RoutingPreference,
                AllocationMode = o.AllocationMode,
                TotalProduction = o.TotalProduction,
                TotalConsumption = o.TotalConsumption,
                TotalDelivered = o.TotalDelivered,
                UnusedSupply = o.UnusedSupply,
                UnmetDemand = o.UnmetDemand,
                NoPermittedPathDemand = o.NoPermittedPathDemand,
                Notes = o.Notes,
                Allocations = allocs,
                TotalSalesRevenue = allocs.Sum(x => x.SaleRevenue),
                TotalTransportCost = allocs.Sum(x => x.TotalTransportCost),
                TotalProductionCost = allocs.Sum(x => x.TotalProductionCost),
                TotalTax = allocs.Sum(x => x.TotalTax),
                TotalProfit = allocs.Sum(x => x.Profit)
            };
        }).ToList();

        return new TrafficEconomicSettlementResult { Outcomes = settled, LedgersByActorId = ledgers };
    }
}

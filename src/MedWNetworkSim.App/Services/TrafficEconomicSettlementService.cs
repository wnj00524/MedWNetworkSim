using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public sealed class SimulationActorEconomicLedger
{
    public string ActorId { get; init; } = string.Empty;
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

    public TrafficEconomicSettlementResult Settle(NetworkModel network, IReadOnlyList<TrafficSimulationOutcome> outcomes, IReadOnlyDictionary<string, SimulationActorState>? actorMap = null)
    {
        actorMap ??= new Dictionary<string, SimulationActorState>(Comparer);
        var gov = actorMap.Values.FirstOrDefault(a => a.IsEnabled && a.Kind == SimulationActorKind.Government)?.Id;
        var ledger = new Dictionary<string, SimulationActorEconomicLedger>(Comparer);
        var enriched = new List<TrafficSimulationOutcome>();
        foreach (var outcome in outcomes)
        {
            var traffic = network.TrafficTypes.FirstOrDefault(t => Comparer.Equals(t.Name, outcome.TrafficType)) ?? new TrafficTypeDefinition();
            var allocations = new List<RouteAllocation>();
            foreach (var a in outcome.Allocations)
            {
                var producer = network.Nodes.FirstOrDefault(n => Comparer.Equals(n.Id, a.ProducerNodeId));
                var consumer = network.Nodes.FirstOrDefault(n => Comparer.Equals(n.Id, a.ConsumerNodeId));
                var pp = producer?.TrafficProfiles.FirstOrDefault(p => Comparer.Equals(p.TrafficType, outcome.TrafficType));
                var seller = ResolveActor(a.ProducerNodeId, producer?.ControllingActor, actorMap);
                var buyer = ResolveActor(a.ConsumerNodeId, consumer?.ControllingActor, actorMap);
                var salePrice = pp?.UnitPrice > 0d ? pp.UnitPrice : Math.Max(0d, traffic.DefaultUnitSalePrice);
                var baseProd = pp?.ProductionCostPerUnit ?? traffic.DefaultUnitProductionCost;
                var salesTaxRate = pp?.SalesTaxRate ?? traffic.SalesTaxRate;
                var transportPer = a.TotalCost + a.BidCostPerUnit;
                var totalTransport = transportPer * a.Quantity;
                var prodPer = a.SourceUnitCostPerUnit + baseProd;
                var totalProd = prodPer * a.Quantity;
                var revenue = salePrice * a.Quantity;
                var tax = revenue * Math.Max(0d, salesTaxRate) + totalTransport * Math.Max(0d, traffic.RouteTaxRate);
                var profit = revenue - (totalTransport + totalProd + tax);
                var updated = new RouteAllocation { Period=a.Period,TrafficType=a.TrafficType,RoutingPreference=a.RoutingPreference,AllocationMode=a.AllocationMode,ProducerNodeId=a.ProducerNodeId,ProducerName=a.ProducerName,ConsumerNodeId=a.ConsumerNodeId,ConsumerName=a.ConsumerName,Quantity=a.Quantity,IsLocalSupply=a.IsLocalSupply,TotalTime=a.TotalTime,TotalCost=a.TotalCost,BidCostPerUnit=a.BidCostPerUnit,SourceUnitCostPerUnit=a.SourceUnitCostPerUnit,DeliveredCostPerUnit=a.DeliveredCostPerUnit,TotalMovementCost=a.TotalMovementCost,TotalScore=a.TotalScore,PathNodeNames=a.PathNodeNames,PathNodeIds=a.PathNodeIds,PathEdgeIds=a.PathEdgeIds,SaleUnitPrice=salePrice,SaleRevenue=revenue,TransportCostPerUnit=transportPer,TotalTransportCost=totalTransport,ProductionCostPerUnit=prodPer,TotalProductionCost=totalProd,TaxPerUnit=a.Quantity>0?tax/a.Quantity:0d,TotalTax=tax,Profit=profit,SellerActorId=seller,BuyerActorId=buyer,TaxAuthorityActorId=gov};
                allocations.Add(updated);
                if (!string.IsNullOrWhiteSpace(seller))
                {
                    var l=Get(ledger,seller); l.SalesRevenue+=revenue;l.ProductionCost+=totalProd;l.TransportCost+=totalTransport;l.TaxesPaid+=tax;l.Profit+=profit;l.CashDelta+=profit;
                }
                if (!string.IsNullOrWhiteSpace(buyer))
                {
                    var l=Get(ledger,buyer); l.PurchaseCost+=revenue;l.TaxesPaid+=tax;l.CashDelta+=-(revenue+tax);
                }
                if (!string.IsNullOrWhiteSpace(gov))
                { var l=Get(ledger,gov!); l.TaxesReceived+=tax;l.CashDelta+=tax; }
            }
            enriched.Add(new TrafficSimulationOutcome{TrafficType=outcome.TrafficType,RoutingPreference=outcome.RoutingPreference,AllocationMode=outcome.AllocationMode,TotalProduction=outcome.TotalProduction,TotalConsumption=outcome.TotalConsumption,TotalDelivered=outcome.TotalDelivered,UnusedSupply=outcome.UnusedSupply,UnmetDemand=outcome.UnmetDemand,NoPermittedPathDemand=outcome.NoPermittedPathDemand,Allocations=allocations,Notes=outcome.Notes,TotalSalesRevenue=allocations.Sum(x=>x.SaleRevenue),TotalTransportCost=allocations.Sum(x=>x.TotalTransportCost),TotalProductionCost=allocations.Sum(x=>x.TotalProductionCost),TotalTax=allocations.Sum(x=>x.TotalTax),TotalProfit=allocations.Sum(x=>x.Profit)});
        }
        return new TrafficEconomicSettlementResult{Outcomes=enriched,LedgersByActorId=ledger};
    }
    private static string? ResolveActor(string nodeId,string? fallback,IReadOnlyDictionary<string,SimulationActorState> actors)=>actors.Values.FirstOrDefault(a=>a.ControlledNodeIds.Contains(nodeId,Comparer))?.Id??fallback;
    private static SimulationActorEconomicLedger Get(Dictionary<string,SimulationActorEconomicLedger> map,string id){if(!map.TryGetValue(id,out var l)){l=new SimulationActorEconomicLedger{ActorId=id};map[id]=l;}return l;}
}

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

public sealed record TrafficEconomicSettlementResult(
    IReadOnlyList<TrafficSimulationOutcome> Outcomes,
    IReadOnlyDictionary<string, SimulationActorEconomicLedger> Ledgers);

public sealed class TrafficEconomicSettlementService
{
    private const double Epsilon = 0.000001d;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public TrafficEconomicSettlementResult Settle(
        NetworkModel network,
        IReadOnlyList<TrafficSimulationOutcome> outcomes,
        IReadOnlyDictionary<string, SimulationActorState>? actorsById = null)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(outcomes);

        actorsById ??= new Dictionary<string, SimulationActorState>(Comparer);
        var nodesById = network.Nodes.ToDictionary(node => node.Id, node => node, Comparer);
        var definitionsByTraffic = network.TrafficTypes
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Name))
            .GroupBy(definition => definition.Name, Comparer)
            .ToDictionary(group => group.Key, group => group.First(), Comparer);
        var ledgers = new Dictionary<string, SimulationActorEconomicLedger>(Comparer);

        var enrichedOutcomes = outcomes
            .Select(outcome =>
            {
                var allocations = outcome.Allocations
                    .Select(allocation => EnrichAllocation(allocation, nodesById, definitionsByTraffic, actorsById, ledgers))
                    .ToList();

                return new TrafficSimulationOutcome
                {
                    TrafficType = outcome.TrafficType,
                    RoutingPreference = outcome.RoutingPreference,
                    AllocationMode = outcome.AllocationMode,
                    TotalProduction = outcome.TotalProduction,
                    TotalConsumption = outcome.TotalConsumption,
                    TotalDelivered = outcome.TotalDelivered,
                    UnusedSupply = outcome.UnusedSupply,
                    UnmetDemand = outcome.UnmetDemand,
                    NoPermittedPathDemand = outcome.NoPermittedPathDemand,
                    TotalSalesRevenue = allocations.Sum(allocation => allocation.SaleRevenue),
                    TotalTransportCost = allocations.Sum(allocation => allocation.TotalTransportCost),
                    TotalProductionCost = allocations.Sum(allocation => allocation.TotalProductionCost),
                    TotalTax = allocations.Sum(allocation => allocation.TotalTax),
                    TotalProfit = allocations.Sum(allocation => allocation.Profit),
                    Allocations = allocations,
                    Notes = outcome.Notes.ToList()
                };
            })
            .ToList();

        return new TrafficEconomicSettlementResult(enrichedOutcomes, ledgers);
    }

    private static RouteAllocation EnrichAllocation(
        RouteAllocation allocation,
        IReadOnlyDictionary<string, NodeModel> nodesById,
        IReadOnlyDictionary<string, TrafficTypeDefinition> definitionsByTraffic,
        IReadOnlyDictionary<string, SimulationActorState> actorsById,
        IDictionary<string, SimulationActorEconomicLedger> ledgers)
    {
        definitionsByTraffic.TryGetValue(allocation.TrafficType, out var definition);
        nodesById.TryGetValue(allocation.ProducerNodeId, out var producer);
        nodesById.TryGetValue(allocation.ConsumerNodeId, out var consumer);

        var producerProfile = producer?.TrafficProfiles
            .FirstOrDefault(profile => Comparer.Equals(profile.TrafficType, allocation.TrafficType));
        var saleUnitPrice = ResolveSaleUnitPrice(producerProfile, definition);
        var baseProductionCost = ResolveBaseProductionCost(producerProfile, definition);
        var propagatedProductionCost = Math.Max(0d, allocation.SourceUnitCostPerUnit);
        var productionCostPerUnit = propagatedProductionCost > Epsilon
            ? propagatedProductionCost
            : baseProductionCost;
        var transportCostPerUnit = Math.Max(0d, allocation.TotalCost) + Math.Max(0d, allocation.BidCostPerUnit);
        var totalTransportCost = transportCostPerUnit * allocation.Quantity;
        var totalProductionCost = productionCostPerUnit * allocation.Quantity;
        var saleRevenue = saleUnitPrice * allocation.Quantity;
        var salesTaxRate = ResolveSalesTaxRate(producerProfile, definition);
        var salesTax = saleRevenue * salesTaxRate;
        var routeTax = totalTransportCost * Math.Max(0d, definition?.RouteTaxRate ?? 0d);
        var totalTax = salesTax + routeTax;
        var profit = saleRevenue - (totalTransportCost + totalProductionCost + totalTax);
        var taxPerUnit = allocation.Quantity > Epsilon ? totalTax / allocation.Quantity : 0d;
        var sellerActorId = ResolveActorForNode(allocation.ProducerNodeId, producer, actorsById);
        var buyerActorId = ResolveActorForNode(allocation.ConsumerNodeId, consumer, actorsById);
        var taxAuthorityActorId = actorsById.Values
            .Where(actor => actor.IsEnabled && actor.Kind == SimulationActorKind.Government)
            .OrderBy(actor => actor.Id, Comparer)
            .Select(actor => actor.Id)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(sellerActorId))
        {
            var seller = GetLedger(ledgers, sellerActorId);
            seller.SalesRevenue += saleRevenue;
            seller.ProductionCost += totalProductionCost;
            seller.TransportCost += totalTransportCost;
            seller.TaxesPaid += totalTax;
            seller.Profit += profit;
            seller.CashDelta += profit;
        }

        if (!string.IsNullOrWhiteSpace(buyerActorId))
        {
            var buyer = GetLedger(ledgers, buyerActorId);
            buyer.PurchaseCost += saleRevenue;
            buyer.TaxesPaid += totalTax;
            buyer.CashDelta -= saleRevenue + totalTax;
        }

        if (!string.IsNullOrWhiteSpace(taxAuthorityActorId))
        {
            var taxAuthority = GetLedger(ledgers, taxAuthorityActorId);
            taxAuthority.TaxesReceived += totalTax;
            taxAuthority.CashDelta += totalTax;
        }

        return CopyWithEconomics(
            allocation,
            saleUnitPrice,
            saleRevenue,
            transportCostPerUnit,
            totalTransportCost,
            productionCostPerUnit,
            totalProductionCost,
            taxPerUnit,
            totalTax,
            profit,
            sellerActorId,
            buyerActorId,
            taxAuthorityActorId);
    }

    private static string? ResolveActorForNode(
        string nodeId,
        NodeModel? node,
        IReadOnlyDictionary<string, SimulationActorState> actorsById)
    {
        var controlledActor = actorsById.Values
            .Where(actor => actor.ControlledNodeIds.Contains(nodeId, Comparer))
            .OrderBy(actor => actor.Id, Comparer)
            .FirstOrDefault();
        if (controlledActor is not null)
        {
            return controlledActor.Id;
        }

        return string.IsNullOrWhiteSpace(node?.ControllingActor) ? null : node.ControllingActor;
    }

    private static double ResolveSaleUnitPrice(NodeTrafficProfile? producerProfile, TrafficTypeDefinition? definition)
    {
        if (producerProfile?.UnitPrice > Epsilon)
        {
            return producerProfile.UnitPrice;
        }

        return Math.Max(0d, definition?.DefaultUnitSalePrice ?? 0d);
    }

    private static double ResolveBaseProductionCost(NodeTrafficProfile? producerProfile, TrafficTypeDefinition? definition)
    {
        if (producerProfile?.ProductionCostPerUnit is { } profileCost)
        {
            return Math.Max(0d, profileCost);
        }

        return Math.Max(0d, definition?.DefaultUnitProductionCost ?? 0d);
    }

    private static double ResolveSalesTaxRate(NodeTrafficProfile? producerProfile, TrafficTypeDefinition? definition)
    {
        if (producerProfile?.SalesTaxRate is { } profileRate)
        {
            return Math.Max(0d, profileRate);
        }

        return Math.Max(0d, definition?.SalesTaxRate ?? 0d);
    }

    private static SimulationActorEconomicLedger GetLedger(IDictionary<string, SimulationActorEconomicLedger> ledgers, string actorId)
    {
        if (!ledgers.TryGetValue(actorId, out var ledger))
        {
            ledger = new SimulationActorEconomicLedger { ActorId = actorId };
            ledgers[actorId] = ledger;
        }

        return ledger;
    }

    private static RouteAllocation CopyWithEconomics(
        RouteAllocation allocation,
        double saleUnitPrice,
        double saleRevenue,
        double transportCostPerUnit,
        double totalTransportCost,
        double productionCostPerUnit,
        double totalProductionCost,
        double taxPerUnit,
        double totalTax,
        double profit,
        string? sellerActorId,
        string? buyerActorId,
        string? taxAuthorityActorId)
    {
        return new RouteAllocation
        {
            Period = allocation.Period,
            TrafficType = allocation.TrafficType,
            RoutingPreference = allocation.RoutingPreference,
            AllocationMode = allocation.AllocationMode,
            ProducerNodeId = allocation.ProducerNodeId,
            ProducerName = allocation.ProducerName,
            ConsumerNodeId = allocation.ConsumerNodeId,
            ConsumerName = allocation.ConsumerName,
            Quantity = allocation.Quantity,
            IsLocalSupply = allocation.IsLocalSupply,
            TotalTime = allocation.TotalTime,
            TotalCost = allocation.TotalCost,
            BidCostPerUnit = allocation.BidCostPerUnit,
            SourceUnitCostPerUnit = allocation.SourceUnitCostPerUnit,
            DeliveredCostPerUnit = allocation.DeliveredCostPerUnit,
            TotalMovementCost = allocation.TotalMovementCost,
            SaleUnitPrice = saleUnitPrice,
            SaleRevenue = saleRevenue,
            TransportCostPerUnit = transportCostPerUnit,
            TotalTransportCost = totalTransportCost,
            ProductionCostPerUnit = productionCostPerUnit,
            TotalProductionCost = totalProductionCost,
            TaxPerUnit = taxPerUnit,
            TotalTax = totalTax,
            Profit = profit,
            SellerActorId = sellerActorId,
            BuyerActorId = buyerActorId,
            TaxAuthorityActorId = taxAuthorityActorId,
            TotalScore = allocation.TotalScore,
            PathNodeNames = allocation.PathNodeNames.ToList(),
            PathNodeIds = allocation.PathNodeIds.ToList(),
            PathEdgeIds = allocation.PathEdgeIds.ToList()
        };
    }
}

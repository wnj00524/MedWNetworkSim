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

    public double TaxesPaidBySeller { get; set; }

    public double TaxesPaidByBuyer { get; set; }

    public double TaxesPaid { get; set; }

    public double TaxesReceivedByAuthority { get; set; }

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
        var edgesById = network.Edges.ToDictionary(edge => edge.Id, edge => edge, Comparer);
        var definitionsByTraffic = network.TrafficTypes
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Name))
            .GroupBy(definition => definition.Name, Comparer)
            .ToDictionary(group => group.Key, group => group.First(), Comparer);
        var actorIdByNodeId = SimulationActorNodeOwnership.BuildNodeActorLookup(
            network.Nodes,
            actorsById,
            requireEnabledControlledActors: false);
        var defaultTaxAuthorityActorId = actorsById.Values
            .Where(actor => actor.IsEnabled && actor.Kind == SimulationActorKind.Government)
            .OrderBy(actor => actor.Id, Comparer)
            .Select(actor => actor.Id)
            .FirstOrDefault();
        var routeTaxRules = network.RouteTaxRules
            .Where(rule =>
                rule.IsActive &&
                rule.TaxRate > 0d &&
                !string.IsNullOrWhiteSpace(rule.EdgeId) &&
                !string.IsNullOrWhiteSpace(rule.TrafficType) &&
                !string.IsNullOrWhiteSpace(rule.TaxAuthorityActorId))
            .ToList();
        var ledgers = new Dictionary<string, SimulationActorEconomicLedger>(Comparer);

        var enrichedOutcomes = outcomes
            .Select(outcome =>
            {
                var allocations = outcome.Allocations
                    .Select(allocation => EnrichAllocation(
                        allocation,
                        nodesById,
                        edgesById,
                        definitionsByTraffic,
                        routeTaxRules,
                        actorIdByNodeId,
                        defaultTaxAuthorityActorId,
                        ledgers))
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
        IReadOnlyDictionary<string, EdgeModel> edgesById,
        IReadOnlyDictionary<string, TrafficTypeDefinition> definitionsByTraffic,
        IReadOnlyList<RouteTaxRule> routeTaxRules,
        IReadOnlyDictionary<string, string> actorIdByNodeId,
        string? defaultTaxAuthorityActorId,
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
        var legacyRouteTax = totalTransportCost * Math.Max(0d, definition?.RouteTaxRate ?? 0d);
        var routeTaxByAuthority = CalculateRouteTaxByAuthority(allocation, edgesById, routeTaxRules);
        var routeTax = legacyRouteTax + routeTaxByAuthority.Values.Sum();
        var totalTax = salesTax + routeTax;
        var profit = saleRevenue - (totalTransportCost + totalProductionCost + totalTax);
        var taxPerUnit = allocation.Quantity > Epsilon ? totalTax / allocation.Quantity : 0d;
        actorIdByNodeId.TryGetValue(allocation.ProducerNodeId, out var sellerActorId);
        actorIdByNodeId.TryGetValue(allocation.ConsumerNodeId, out var buyerActorId);
        var taxAuthorityActorId = routeTaxByAuthority.Keys.OrderBy(id => id, Comparer).FirstOrDefault()
            ?? defaultTaxAuthorityActorId;

        if (!string.IsNullOrWhiteSpace(sellerActorId))
        {
            var seller = GetLedger(ledgers, sellerActorId);
            seller.SalesRevenue += saleRevenue;
            seller.ProductionCost += totalProductionCost;
            seller.TransportCost += totalTransportCost;
            seller.TaxesPaidBySeller += totalTax;
            seller.TaxesPaid += totalTax;
            seller.Profit += profit;
            seller.CashDelta += profit;
        }

        if (!string.IsNullOrWhiteSpace(buyerActorId))
        {
            var buyer = GetLedger(ledgers, buyerActorId);
            buyer.PurchaseCost += saleRevenue;
            buyer.CashDelta -= saleRevenue;
        }

        if (!string.IsNullOrWhiteSpace(defaultTaxAuthorityActorId))
        {
            var defaultTaxAuthority = GetLedger(ledgers, defaultTaxAuthorityActorId);
            var defaultTax = salesTax + legacyRouteTax;
            defaultTaxAuthority.TaxesReceivedByAuthority += defaultTax;
            defaultTaxAuthority.TaxesReceived += defaultTax;
            defaultTaxAuthority.CashDelta += defaultTax;
        }

        foreach (var (authorityActorId, authorityRouteTax) in routeTaxByAuthority)
        {
            var taxAuthority = GetLedger(ledgers, authorityActorId);
            taxAuthority.TaxesReceivedByAuthority += authorityRouteTax;
            taxAuthority.TaxesReceived += authorityRouteTax;
            taxAuthority.CashDelta += authorityRouteTax;
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

    private static Dictionary<string, double> CalculateRouteTaxByAuthority(
        RouteAllocation allocation,
        IReadOnlyDictionary<string, EdgeModel> edgesById,
        IReadOnlyList<RouteTaxRule> routeTaxRules)
    {
        var taxByAuthority = new Dictionary<string, double>(Comparer);
        if (allocation.Quantity <= Epsilon || allocation.PathEdgeIds.Count == 0 || routeTaxRules.Count == 0)
        {
            return taxByAuthority;
        }

        foreach (var edgeId in allocation.PathEdgeIds)
        {
            if (!edgesById.TryGetValue(edgeId, out var edge))
            {
                continue;
            }

            var edgeTransportCost = Math.Max(0d, edge.Cost) * allocation.Quantity;
            if (edgeTransportCost <= Epsilon)
            {
                continue;
            }

            foreach (var rule in routeTaxRules.Where(rule =>
                Comparer.Equals(rule.EdgeId, edgeId) &&
                Comparer.Equals(rule.TrafficType, allocation.TrafficType)))
            {
                var tax = edgeTransportCost * Math.Max(0d, rule.TaxRate);
                taxByAuthority.TryGetValue(rule.TaxAuthorityActorId, out var existing);
                taxByAuthority[rule.TaxAuthorityActorId] = existing + tax;
            }
        }

        return taxByAuthority;
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

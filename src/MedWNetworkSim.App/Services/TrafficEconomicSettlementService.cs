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

/// <summary>
/// Enriches route allocations with network-level economic totals without any actor identity model.
/// </summary>
public sealed class TrafficEconomicSettlementService
{
    private const double Epsilon = 0.000001d;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public TrafficEconomicSettlementResult Settle(NetworkModel network, IReadOnlyList<TrafficSimulationOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(outcomes);

        var nodesById = network.Nodes.ToDictionary(node => node.Id, node => node, Comparer);
        var definitionsByTraffic = network.TrafficTypes
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Name))
            .GroupBy(definition => definition.Name, Comparer)
            .ToDictionary(group => group.Key, group => group.First(), Comparer);

        var enrichedOutcomes = outcomes
            .Select(outcome =>
            {
                var allocations = outcome.Allocations
                    .Select(allocation => EnrichAllocation(allocation, nodesById, definitionsByTraffic))
                    .ToList();

                var salesRevenue = 0d;
                var transportCost = 0d;
                var productionCost = 0d;
                var tax = 0d;
                var profit = 0d;

                // Iterate once to calculate all metrics, replacing 5 O(N) LINQ Sum calls
                foreach (var allocation in allocations)
                {
                    salesRevenue += allocation.SaleRevenue;
                    transportCost += allocation.TotalTransportCost;
                    productionCost += allocation.TotalProductionCost;
                    tax += allocation.TotalTax;
                    profit += allocation.Profit;
                }

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
                    TotalSalesRevenue = salesRevenue,
                    TotalTransportCost = transportCost,
                    TotalProductionCost = productionCost,
                    TotalTax = tax,
                    TotalProfit = profit,
                    Allocations = allocations,
                    Notes = outcome.Notes.ToList()
                };
            })
            .ToList();

        return new TrafficEconomicSettlementResult(
            enrichedOutcomes,
            new Dictionary<string, SimulationActorEconomicLedger>(StringComparer.OrdinalIgnoreCase));
    }

    public TrafficEconomicSettlementResult Settle(
        NetworkModel network,
        IReadOnlyList<TrafficSimulationOutcome> outcomes,
        IReadOnlyDictionary<string, MedWNetworkSim.App.Agents.SimulationActorState>? actorsById)
    {
        return Settle(network, outcomes);
    }

    private static RouteAllocation EnrichAllocation(
        RouteAllocation allocation,
        IReadOnlyDictionary<string, NodeModel> nodesById,
        IReadOnlyDictionary<string, TrafficTypeDefinition> definitionsByTraffic)
    {
        definitionsByTraffic.TryGetValue(allocation.TrafficType, out var definition);
        nodesById.TryGetValue(allocation.ProducerNodeId, out var producer);
        nodesById.TryGetValue(allocation.ConsumerNodeId, out var consumer);

        var producerProfile = producer?.TrafficProfiles
            .FirstOrDefault(profile => Comparer.Equals(profile.TrafficType, allocation.TrafficType));
        var consumerProfile = consumer?.TrafficProfiles
            .FirstOrDefault(profile => Comparer.Equals(profile.TrafficType, allocation.TrafficType));
        var saleUnitPrice = ResolveSaleUnitPrice(allocation, producerProfile, consumerProfile, definition);
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
        var totalTax = saleRevenue * salesTaxRate;
        var profit = saleRevenue - (totalTransportCost + totalProductionCost + totalTax);
        var taxPerUnit = allocation.Quantity > Epsilon ? totalTax / allocation.Quantity : 0d;

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
            TotalScore = allocation.TotalScore,
            PathNodeNames = allocation.PathNodeNames.ToList(),
            PathNodeIds = allocation.PathNodeIds.ToList(),
            PathEdgeIds = allocation.PathEdgeIds.ToList()
        };
    }

    private static double ResolveSaleUnitPrice(
        RouteAllocation allocation,
        NodeTrafficProfile? producerProfile,
        NodeTrafficProfile? consumerProfile,
        TrafficTypeDefinition? definition)
    {
        var consumerPremium = Math.Max(0d, consumerProfile?.ConsumerPremiumPerUnit ?? 0d);
        var declaredSalePrice = producerProfile?.UnitPrice > Epsilon
            ? producerProfile.UnitPrice
            : Math.Max(0d, definition?.DefaultUnitSalePrice ?? 0d);
        var productionCost = allocation.SourceUnitCostPerUnit > Epsilon
            ? Math.Max(0d, allocation.SourceUnitCostPerUnit)
            : ResolveBaseProductionCost(producerProfile, definition);
        var transportCost = allocation.DeliveredCostPerUnit > Epsilon
            ? Math.Max(0d, allocation.DeliveredCostPerUnit - productionCost)
            : Math.Max(0d, allocation.TotalCost) + Math.Max(0d, allocation.BidCostPerUnit);
        var deliveredCostFloor = productionCost + transportCost;
        return Math.Max(declaredSalePrice, deliveredCostFloor) + consumerPremium;
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
}

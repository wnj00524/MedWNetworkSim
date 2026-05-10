using System.Collections.ObjectModel;

namespace MedWNetworkSim.App.Services.AgentEconomy;

public readonly record struct AgentId(string Value) : IComparable<AgentId> { public int CompareTo(AgentId other) => string.CompareOrdinal(Value, other.Value); }
public readonly record struct NodeId(string Value) : IComparable<NodeId> { public int CompareTo(NodeId other) => string.CompareOrdinal(Value, other.Value); }
public readonly record struct EdgeId(string Value) : IComparable<EdgeId> { public int CompareTo(EdgeId other) => string.CompareOrdinal(Value, other.Value); }
public readonly record struct GoodId(string Value) : IComparable<GoodId> { public int CompareTo(GoodId other) => string.CompareOrdinal(Value, other.Value); }
public readonly record struct MarketId(string Value) : IComparable<MarketId> { public int CompareTo(MarketId other) => string.CompareOrdinal(Value, other.Value); }

public sealed record MovementProfile(string Name, IReadOnlySet<string> AllowedTags);
public sealed record InventoryStack(GoodId GoodId, decimal Quantity);
public sealed record Good(GoodId Id, string Name);
public sealed record Recipe(GoodId OutputGoodId, decimal OutputQuantity, IReadOnlyList<InventoryStack> Inputs);
public sealed record ProductionCapability(Recipe Recipe, decimal MaxRunsPerTick);
public sealed record Edge(EdgeId Id, NodeId From, NodeId To, bool IsUndirected, decimal Distance, decimal Cost, decimal Capacity, IReadOnlySet<string> RequiredMovementTags);

public sealed record WorldGraph(IReadOnlyDictionary<NodeId, string> Nodes, IReadOnlyDictionary<EdgeId, Edge> Edges)
{
    public IEnumerable<Edge> Outgoing(NodeId node)
    {
        foreach (var edge in Edges.Values.OrderBy(e => e.Id))
        {
            if (edge.From.Equals(node)) yield return edge;
            if (edge.IsUndirected && edge.To.Equals(node)) yield return edge with { From = edge.To, To = edge.From };
        }
    }
}

public sealed record MarketOffer(GoodId GoodId, decimal UnitPrice, decimal Quantity, bool IsSellOffer);
public sealed record Market(MarketId Id, NodeId NodeId, decimal Cash, IReadOnlyList<MarketOffer> Offers);
public sealed record KnownMarket(MarketId MarketId, NodeId NodeId, int ObservedTick, double Confidence) { public bool IsStale(int currentTick, int staleAfterTicks) => currentTick - ObservedTick >= staleAfterTicks; }
public sealed record AgentKnowledge(IReadOnlyDictionary<MarketId, KnownMarket> KnownMarkets);

public abstract record Goal;
public sealed record AcquireGoodGoal(GoodId GoodId, decimal Quantity) : Goal;
public sealed record SellGoodGoal(GoodId GoodId, decimal Quantity) : Goal;
public sealed record ProduceGoodGoal(GoodId GoodId, decimal Quantity) : Goal;
public sealed record MoveToNodeGoal(NodeId NodeId) : Goal;
public sealed record MaintainCashGoal(decimal MinimumCash) : Goal;
public sealed record MaintainInventoryGoal(GoodId GoodId, decimal MinimumQuantity) : Goal;

public abstract record RulePredicate;
public sealed record AtNodePredicate(NodeId NodeId) : RulePredicate;
public sealed record HasGoodAtLeastPredicate(GoodId GoodId, decimal Quantity) : RulePredicate;
public sealed record CashAtLeastPredicate(decimal Amount) : RulePredicate;
public sealed record KnowsMarketForGoodPredicate(GoodId GoodId) : RulePredicate;
public sealed record EdgeAllowedPredicate(EdgeId EdgeId) : RulePredicate;

public abstract record RuleAction;
public sealed record MoveAlongEdgeAction(EdgeId EdgeId) : RuleAction;
public sealed record BuyGoodAction(MarketId MarketId, GoodId GoodId, decimal Quantity, decimal MaxUnitPrice) : RuleAction;
public sealed record SellGoodAction(MarketId MarketId, GoodId GoodId, decimal Quantity, decimal MinUnitPrice) : RuleAction;
public sealed record ProduceGoodAction(GoodId GoodId, decimal DesiredRuns) : RuleAction;
public sealed record DiscoverLocalMarketAction : RuleAction;

public sealed record AgentRule(IReadOnlyList<RulePredicate> Predicates, RuleAction Action);
public sealed record Agent(AgentId Id, NodeId CurrentNodeId, decimal Cash, IReadOnlyDictionary<GoodId, decimal> Inventory, IReadOnlyList<Goal> Goals, IReadOnlyList<AgentRule> Rules, MovementProfile MovementProfile, AgentKnowledge Knowledge, IReadOnlyList<ProductionCapability> ProductionCapabilities);
public sealed record TickOptions(int CurrentTick, int KnowledgeStaleAfterTicks = 3);
public sealed record WorldState(WorldGraph Graph, IReadOnlyDictionary<AgentId, Agent> Agents, IReadOnlyDictionary<MarketId, Market> Markets, IReadOnlyDictionary<GoodId, Good> Goods)
{
    public static IReadOnlyDictionary<GoodId, decimal> CloneInventory(IReadOnlyDictionary<GoodId, decimal> inventory) => new ReadOnlyDictionary<GoodId, decimal>(new Dictionary<GoodId, decimal>(inventory));
}

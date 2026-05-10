using MedWNetworkSim.App.Services.AgentEconomy;

namespace MedWNetworkSim.Tests;

public sealed class AgentEconomySimulationTests
{
    [Fact]
    public void RestrictedEdge_AllowsHauler_BlocksWalker()
    {
        var edge = new Edge(new EdgeId("e1"), new NodeId("a"), new NodeId("b"), false, 1, 1, 1, new HashSet<string> { "hauler" });
        var graph = new WorldGraph(new Dictionary<NodeId, string> { [new("a")] = "A", [new("b")] = "B" }, new Dictionary<EdgeId, Edge> { [edge.Id] = edge });
        var hauler = BuildAgent("hauler", "a", new HashSet<string> { "hauler" }, new AgentRule(Array.Empty<RulePredicate>(), new MoveAlongEdgeAction(edge.Id)));
        var walker = BuildAgent("walker", "a", new HashSet<string>(), new AgentRule(Array.Empty<RulePredicate>(), new MoveAlongEdgeAction(edge.Id)));
        var state = new WorldState(graph, new Dictionary<AgentId, Agent> { [hauler.Id] = hauler, [walker.Id] = walker }, new Dictionary<MarketId, Market>(), new Dictionary<GoodId, Good>());

        var next = SimulationTickEngine.Step(state, new TickOptions(1));
        Assert.Equal(new NodeId("b"), next.Agents[hauler.Id].CurrentNodeId);
        Assert.Equal(new NodeId("a"), next.Agents[walker.Id].CurrentNodeId);
    }

    [Fact]
    public void Production_SupportsSuccess_InsufficientInput_AndCapacityLimit()
    {
        var grain = new GoodId("grain"); var flour = new GoodId("flour");
        var recipe = new Recipe(flour, 1, new[] { new InventoryStack(grain, 2m) });
        var prod = new ProductionCapability(recipe, 1);
        var agent = new Agent(new("p"), new("n"), 0, new Dictionary<GoodId, decimal> { [grain] = 5 }, Array.Empty<Goal>(), new[] { new AgentRule(Array.Empty<RulePredicate>(), new ProduceGoodAction(flour, 2)) }, new MovementProfile("m", new HashSet<string>()), new AgentKnowledge(new Dictionary<MarketId, KnownMarket>()), new[] { prod });
        var state = new WorldState(new WorldGraph(new Dictionary<NodeId, string> { [new("n")] = "N" }, new Dictionary<EdgeId, Edge>()), new Dictionary<AgentId, Agent> { [agent.Id] = agent }, new Dictionary<MarketId, Market>(), new Dictionary<GoodId, Good>());

        var oneTick = SimulationTickEngine.Step(state, new TickOptions(1));
        Assert.Equal(3m, oneTick.Agents[agent.Id].Inventory[grain]);
        Assert.Equal(1m, oneTick.Agents[agent.Id].Inventory[flour]);

        var twoTick = SimulationTickEngine.Step(oneTick, new TickOptions(2));
        Assert.Equal(1m, twoTick.Agents[agent.Id].Inventory[grain]);
        Assert.Equal(2m, twoTick.Agents[agent.Id].Inventory[flour]);

        var threeTick = SimulationTickEngine.Step(twoTick, new TickOptions(3));
        Assert.Equal(1m, threeTick.Agents[agent.Id].Inventory[grain]);
        Assert.Equal(2m, threeTick.Agents[agent.Id].Inventory[flour]);
    }

    [Fact]
    public void BuySellAndTransport_AfterDiscovery()
    {
        var grain = new GoodId("grain");
        var buyMarketId = new MarketId("m1");
        var sellMarketId = new MarketId("m2");
        var edge = new Edge(new("e"), new("a"), new("b"), true, 1, 1, 1, new HashSet<string>());
        var graph = new WorldGraph(new Dictionary<NodeId, string> { [new("a")] = "A", [new("b")] = "B" }, new Dictionary<EdgeId, Edge> { [edge.Id] = edge });
        var rules = new AgentRule[] {
            new(Array.Empty<RulePredicate>(), new DiscoverLocalMarketAction()),
            new(new RulePredicate[]{ new AtNodePredicate(new("a")) }, new BuyGoodAction(buyMarketId, grain, 3, 5)),
            new(new RulePredicate[]{ new AtNodePredicate(new("a")) }, new MoveAlongEdgeAction(edge.Id)),
            new(Array.Empty<RulePredicate>(), new DiscoverLocalMarketAction()),
            new(new RulePredicate[]{ new AtNodePredicate(new("b")), new HasGoodAtLeastPredicate(grain, 1) }, new SellGoodAction(sellMarketId, grain, 2, 6))
        };
        var agent = BuildAgent("t", "a", new HashSet<string>(), rules);
        var markets = new Dictionary<MarketId, Market>
        {
            [buyMarketId] = new(buyMarketId, new("a"), 100, new[] { new MarketOffer(grain, 4, 10, true) }),
            [sellMarketId] = new(sellMarketId, new("b"), 200, new[] { new MarketOffer(grain, 7, 10, false) })
        };
        var state = new WorldState(graph, new Dictionary<AgentId, Agent> { [agent.Id] = agent }, markets, new Dictionary<GoodId, Good>());
        var next = SimulationTickEngine.Step(state, new TickOptions(1));
        Assert.Equal(new NodeId("b"), next.Agents[agent.Id].CurrentNodeId);
        Assert.Equal(1m, next.Agents[agent.Id].Inventory[grain]);
        Assert.Equal(52m, next.Agents[agent.Id].Cash);
    }

    [Fact]
    public void UnknownMarketBlocksTrade_UntilDiscovery_AndStaleKnowledgeLowersConfidence()
    {
        var grain = new GoodId("grain"); var marketId = new MarketId("m1");
        var agent = BuildAgent("a", "n", new HashSet<string>(),
            new AgentRule(Array.Empty<RulePredicate>(), new BuyGoodAction(marketId, grain, 1, 5)),
            new AgentRule(Array.Empty<RulePredicate>(), new DiscoverLocalMarketAction()),
            new AgentRule(Array.Empty<RulePredicate>(), new BuyGoodAction(marketId, grain, 1, 5)));
        var state = new WorldState(new WorldGraph(new Dictionary<NodeId, string> { [new("n")] = "N" }, new Dictionary<EdgeId, Edge>()),
            new Dictionary<AgentId, Agent> { [agent.Id] = agent },
            new Dictionary<MarketId, Market> { [marketId] = new(marketId, new("n"), 100, new[] { new MarketOffer(grain, 5, 5, true) }) },
            new Dictionary<GoodId, Good>());

        var step1 = SimulationTickEngine.Step(state, new TickOptions(1));
        Assert.Equal(1m, step1.Agents[agent.Id].Inventory[grain]);
        var known = step1.Agents[agent.Id].Knowledge.KnownMarkets[marketId];
        Assert.Equal(1.0, known.Confidence);

        var step5 = SimulationTickEngine.Step(step1, new TickOptions(5));
        Assert.True(step5.Agents[agent.Id].Knowledge.KnownMarkets[marketId].Confidence < 1.0);
    }

    private static Agent BuildAgent(string id, string node, IReadOnlySet<string> tags, params AgentRule[] rules)
        => new(new AgentId(id), new NodeId(node), 50, new Dictionary<GoodId, decimal>(), Array.Empty<Goal>(), rules, new MovementProfile(id, tags), new AgentKnowledge(new Dictionary<MarketId, KnownMarket>()), Array.Empty<ProductionCapability>());
}

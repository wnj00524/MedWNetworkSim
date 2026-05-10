namespace MedWNetworkSim.App.Services.AgentEconomy;

public static class AgentEconomyScenarioBuilder
{
    public static WorldState BuildFarmFactoryPortCityScenario()
    {
        var farm = new NodeId("farm"); var factory = new NodeId("factory"); var port = new NodeId("port"); var city = new NodeId("city");
        var grain = new GoodId("grain"); var flour = new GoodId("flour"); var tools = new GoodId("tools");
        var cityMarketId = new MarketId("city-market");
        var graph = new WorldGraph(
            new Dictionary<NodeId, string> { [farm] = "Farm", [factory] = "Factory", [port] = "Port", [city] = "City" },
            new Dictionary<EdgeId, Edge>
            {
                [new EdgeId("farm-factory")] = new(new EdgeId("farm-factory"), farm, factory, true, 5, 1, 100, new HashSet<string>()),
                [new EdgeId("factory-port")] = new(new EdgeId("factory-port"), factory, port, true, 6, 1, 100, new HashSet<string>{"hauler"}),
                [new EdgeId("port-city")] = new(new EdgeId("port-city"), port, city, true, 4, 1, 100, new HashSet<string>())
            });

        var recipe = new Recipe(flour, 1, new[] { new InventoryStack(grain, 2m) });
        var factoryAgent = new Agent(new AgentId("factory-agent"), factory, 25, new Dictionary<GoodId, decimal> { [grain] = 4 }, Array.Empty<Goal>(), new[] { new AgentRule(Array.Empty<RulePredicate>(), new ProduceGoodAction(flour, 1)) }, new MovementProfile("factory", new HashSet<string>()), new AgentKnowledge(new Dictionary<MarketId, KnownMarket>()), new[] { new ProductionCapability(recipe, 1) });
        var farmer = new Agent(new AgentId("farmer"), farm, 10, new Dictionary<GoodId, decimal> { [grain] = 5 }, Array.Empty<Goal>(), Array.Empty<AgentRule>(), new MovementProfile("walker", new HashSet<string>()), new AgentKnowledge(new Dictionary<MarketId, KnownMarket>()), Array.Empty<ProductionCapability>());
        var hauler = new Agent(new AgentId("hauler"), factory, 10, new Dictionary<GoodId, decimal>(), Array.Empty<Goal>(), Array.Empty<AgentRule>(), new MovementProfile("hauler", new HashSet<string> { "hauler" }), new AgentKnowledge(new Dictionary<MarketId, KnownMarket>()), Array.Empty<ProductionCapability>());
        var trader = new Agent(new AgentId("trader"), city, 50, new Dictionary<GoodId, decimal>(), Array.Empty<Goal>(), Array.Empty<AgentRule>(), new MovementProfile("trader", new HashSet<string>()), new AgentKnowledge(new Dictionary<MarketId, KnownMarket>()), Array.Empty<ProductionCapability>());

        var cityMarket = new Market(cityMarketId, city, 500, new[] { new MarketOffer(flour, 8, 100, false), new MarketOffer(tools, 15, 20, true) });
        return new WorldState(graph,
            new Dictionary<AgentId, Agent> { [farmer.Id] = farmer, [hauler.Id] = hauler, [factoryAgent.Id] = factoryAgent, [trader.Id] = trader },
            new Dictionary<MarketId, Market> { [cityMarketId] = cityMarket },
            new Dictionary<GoodId, Good> { [grain] = new(grain, "Grain"), [flour] = new(flour, "Flour"), [tools] = new(tools, "Tools") });
    }
}

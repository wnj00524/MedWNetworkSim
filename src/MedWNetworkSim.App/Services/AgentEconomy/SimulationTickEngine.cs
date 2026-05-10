namespace MedWNetworkSim.App.Services.AgentEconomy;

public static class SimulationTickEngine
{
    public static WorldState Step(WorldState state, TickOptions options)
    {
        var markets = state.Markets.ToDictionary(k => k.Key, v => v.Value);
        var agents = state.Agents.ToDictionary(k => k.Key, v => v.Value);

        foreach (var agentId in agents.Keys.OrderBy(k => k))
        {
            var agent = agents[agentId];
            foreach (var rule in agent.Rules)
            {
                if (rule.Predicates.All(p => EvaluatePredicate(p, agent, state, options.CurrentTick)))
                {
                    agent = ExecuteAction(rule.Action, agent, state.Graph, markets, options);
                }
            }

            agent = UpdateKnowledgeStaleness(agent, options);
            agents[agentId] = agent;
        }

        return state with { Agents = agents, Markets = markets };
    }

    private static Agent UpdateKnowledgeStaleness(Agent agent, TickOptions options)
    {
        var updated = agent.Knowledge.KnownMarkets.ToDictionary(
            x => x.Key,
            x => x.Value.IsStale(options.CurrentTick, options.KnowledgeStaleAfterTicks)
                ? x.Value with { Confidence = Math.Max(0.1, x.Value.Confidence - 0.25) }
                : x.Value);
        return agent with { Knowledge = new AgentKnowledge(updated) };
    }

    private static bool EvaluatePredicate(RulePredicate predicate, Agent agent, WorldState state, int currentTick) => predicate switch
    {
        AtNodePredicate p => agent.CurrentNodeId.Equals(p.NodeId),
        HasGoodAtLeastPredicate p => agent.Inventory.TryGetValue(p.GoodId, out var qty) && qty >= p.Quantity,
        CashAtLeastPredicate p => agent.Cash >= p.Amount,
        KnowsMarketForGoodPredicate p => agent.Knowledge.KnownMarkets.Keys.Any(mid => state.Markets[mid].Offers.Any(o => o.GoodId.Equals(p.GoodId))),
        EdgeAllowedPredicate p => state.Graph.Edges.TryGetValue(p.EdgeId, out var edge) && CanTraverse(agent.MovementProfile, edge),
        _ => false
    };

    private static Agent ExecuteAction(RuleAction action, Agent agent, WorldGraph graph, Dictionary<MarketId, Market> markets, TickOptions options) => action switch
    {
        MoveAlongEdgeAction move => MoveAlongEdge(agent, graph, move.EdgeId),
        DiscoverLocalMarketAction => DiscoverLocal(agent, markets, options.CurrentTick),
        BuyGoodAction buy => Buy(agent, markets, buy),
        SellGoodAction sell => Sell(agent, markets, sell),
        ProduceGoodAction produce => Produce(agent, produce),
        _ => agent
    };

    private static Agent MoveAlongEdge(Agent agent, WorldGraph graph, EdgeId edgeId)
    {
        if (!graph.Edges.TryGetValue(edgeId, out var edge)) return agent;
        var oriented = edge.From.Equals(agent.CurrentNodeId) ? edge : edge.IsUndirected && edge.To.Equals(agent.CurrentNodeId) ? edge with { From = edge.To, To = edge.From } : null;
        if (oriented is null || !CanTraverse(agent.MovementProfile, oriented)) return agent;
        return agent with { CurrentNodeId = oriented.To };
    }

    private static bool CanTraverse(MovementProfile movementProfile, Edge edge) => edge.RequiredMovementTags.Count == 0 || edge.RequiredMovementTags.All(movementProfile.AllowedTags.Contains);

    private static Agent DiscoverLocal(Agent agent, Dictionary<MarketId, Market> markets, int tick)
    {
        var known = new Dictionary<MarketId, KnownMarket>(agent.Knowledge.KnownMarkets);
        foreach (var market in markets.Values.Where(m => m.NodeId.Equals(agent.CurrentNodeId)))
        {
            known[market.Id] = new KnownMarket(market.Id, market.NodeId, tick, 1.0);
        }

        return agent with { Knowledge = new AgentKnowledge(known) };
    }

    private static Agent Buy(Agent agent, Dictionary<MarketId, Market> markets, BuyGoodAction action)
    {
        if (!agent.Knowledge.KnownMarkets.ContainsKey(action.MarketId) || !markets.TryGetValue(action.MarketId, out var market) || market.NodeId != agent.CurrentNodeId) return agent;
        var offerIdx = market.Offers.Select((offer, idx) => (offer, idx)).FirstOrDefault(x => x.offer.IsSellOffer && x.offer.GoodId.Equals(action.GoodId) && x.offer.UnitPrice <= action.MaxUnitPrice && x.offer.Quantity > 0);
        if (offerIdx.offer is null) return agent;
        var affordable = decimal.Floor(agent.Cash / offerIdx.offer.UnitPrice);
        var qty = new[] { action.Quantity, offerIdx.offer.Quantity, affordable }.Min();
        if (qty <= 0) return agent;
        var cost = qty * offerIdx.offer.UnitPrice;
        var inventory = agent.Inventory.ToDictionary(k => k.Key, v => v.Value);
        inventory[action.GoodId] = inventory.GetValueOrDefault(action.GoodId) + qty;
        var offers = market.Offers.ToList();
        offers[offerIdx.idx] = offerIdx.offer with { Quantity = offerIdx.offer.Quantity - qty };
        markets[action.MarketId] = market with { Cash = market.Cash + cost, Offers = offers };
        return agent with { Cash = agent.Cash - cost, Inventory = inventory };
    }

    private static Agent Sell(Agent agent, Dictionary<MarketId, Market> markets, SellGoodAction action)
    {
        if (!agent.Knowledge.KnownMarkets.ContainsKey(action.MarketId) || !markets.TryGetValue(action.MarketId, out var market) || market.NodeId != agent.CurrentNodeId) return agent;
        var offerIdx = market.Offers.Select((offer, idx) => (offer, idx)).FirstOrDefault(x => !x.offer.IsSellOffer && x.offer.GoodId.Equals(action.GoodId) && x.offer.UnitPrice >= action.MinUnitPrice && x.offer.Quantity > 0);
        if (offerIdx.offer is null) return agent;
        var inv = agent.Inventory.ToDictionary(k => k.Key, v => v.Value);
        var available = inv.GetValueOrDefault(action.GoodId);
        var marketCanPay = decimal.Floor(market.Cash / offerIdx.offer.UnitPrice);
        var qty = new[] { action.Quantity, available, offerIdx.offer.Quantity, marketCanPay }.Min();
        if (qty <= 0) return agent;
        var value = qty * offerIdx.offer.UnitPrice;
        inv[action.GoodId] = available - qty;
        var offers = market.Offers.ToList();
        offers[offerIdx.idx] = offerIdx.offer with { Quantity = offerIdx.offer.Quantity - qty };
        markets[action.MarketId] = market with { Cash = market.Cash - value, Offers = offers };
        return agent with { Cash = agent.Cash + value, Inventory = inv };
    }

    private static Agent Produce(Agent agent, ProduceGoodAction action)
    {
        var cap = agent.ProductionCapabilities.FirstOrDefault(x => x.Recipe.OutputGoodId.Equals(action.GoodId));
        if (cap is null) return agent;
        var inv = agent.Inventory.ToDictionary(k => k.Key, v => v.Value);
        var runsFromInput = cap.Recipe.Inputs.Select(i => Math.Floor((double)(inv.GetValueOrDefault(i.GoodId) / i.Quantity))).DefaultIfEmpty(0).Min();
        var runs = Math.Min((decimal)runsFromInput, Math.Min(cap.MaxRunsPerTick, action.DesiredRuns));
        if (runs <= 0) return agent;
        foreach (var input in cap.Recipe.Inputs)
        {
            inv[input.GoodId] = inv.GetValueOrDefault(input.GoodId) - (input.Quantity * runs);
        }
        inv[action.GoodId] = inv.GetValueOrDefault(action.GoodId) + (cap.Recipe.OutputQuantity * runs);
        return agent with { Inventory = inv };
    }
}

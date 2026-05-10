# Agent economy simulation

This slice introduces deterministic tick-based economy simulation in `MedWNetworkSim.App.Services.AgentEconomy`.

## Model

- Strong IDs: `AgentId`, `NodeId`, `EdgeId`, `GoodId`, `MarketId`.
- `WorldState` contains graph, agents, markets, and goods.
- `Agent` tracks location, inventory, cash, goals, rules, movement profile, production capabilities, and imperfect market knowledge.
- `WorldGraph` contains nodes and directed/undirected edges. Edges define distance/cost/capacity and required movement tags.
- `Market` contains buy/sell offers and market cash.
- `AgentKnowledge` stores known markets with observed tick and confidence.

## Tick order and determinism

`SimulationTickEngine.Step(WorldState, TickOptions)`:
1. Processes agents in stable sorted `AgentId` order.
2. Evaluates each rule's predicates and executes structured actions.
3. Updates knowledge confidence for stale entries using `KnowledgeStaleAfterTicks`.

No wall-clock randomness is used.

## Rule/goal shape

- Goals: acquire/sell/produce/move/maintain cash/maintain inventory.
- Predicates: `AtNode`, `HasGoodAtLeast`, `CashAtLeast`, `KnowsMarketForGood`, `EdgeAllowed`.
- Actions: `MoveAlongEdge`, `BuyGood`, `SellGood`, `ProduceGood`, `DiscoverLocalMarket`.

Rules are declarative C# record structures (no executable scripts).

## Example scenario builder

`AgentEconomyScenarioBuilder.BuildFarmFactoryPortCityScenario()` creates:
- Nodes: farm, factory, port, city.
- Goods: grain, flour, tools.
- Agents: farmer, hauler, factory-agent, trader.
- Restricted edge between factory and port requiring `hauler` movement tag.
- Factory recipe: `2 grain -> 1 flour`.
- City market with flour buy demand and tools sell supply.

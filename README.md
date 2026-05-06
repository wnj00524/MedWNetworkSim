# MedWNetworkSim

MedWNetworkSim is a .NET network simulation and analysis tool for modelling constrained movement through graph-based systems.

It supports networks where **traffic types** move between **nodes** over **edges** subject to routing, capacity, cost, timing, storage, production, consumption, policy, and scenario constraints.

The current solution is centred on an **Avalonia desktop application** with shared UI, presentation, interaction, and rendering libraries. The repository also retains legacy WPF/CLI implementation code under `src/MedWNetworkSim.App`, which is used by the shared presentation layer and documented command-line workflows.

---

## What it is useful for

MedWNetworkSim can be used to explore:

- supply and demand imbalance
- congestion and bottlenecks
- route choice behaviour
- capacity constraints
- edge and node utilisation
- unmet demand and backlog
- multi-period timeline dynamics
- scenario changes such as failures, closures, demand spikes, and route cost changes
- policy-aware routing and flow blocking
- economic summaries and issue explanations

Typical use cases include logistics planning, supply-chain modelling, infrastructure flow analysis, resource distribution, service-flow scenarios, and prototype network design.

---

## Current application focus

The primary application in the current solution is:

```text
src/MedWNetworkSim.App.Avalonia/

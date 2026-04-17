# MedWNetworkSim

MedWNetworkSim is a .NET desktop application for building, editing, importing, and simulating routed flow networks.

It is designed for graph-based scenarios where **nodes** produce, consume, store, transform, or transship **traffic types** across **edges** with time, cost, and capacity constraints. It supports both one-shot simulation and timeline-based analysis, making it useful for bottleneck analysis, route testing, network design, and period-based flow modelling.

## What the application does

MedWNetworkSim lets you:

- build and edit networks visually in a Windows desktop UI
- open and save network files as JSON
- import and export GraphML
- import OpenStreetMap road networks from both `*.osm` and `*.pbf`
- simplify imported OSM networks while preserving reachability, key junctions, named-road transitions, and overall shape
- calculate OSM edge metrics using collapsed path distance rather than only straight-line retention
- assign traffic types with different routing strategies and flow split policies
- control permissions on edges by traffic type
- run both single-run and timeline simulations
- model storage, transhipment, production, consumption, and recipe-style local input requirements
- analyse demand backlog, route selection, utilisation, and edge pressure
- work with hierarchical subnetworks through composite nodes and external interfaces
- export reports from the desktop application
- run the verification project to exercise regression and behaviour scenarios

## Current repository structure

The repository currently centres on two main projects:

- [`src/MedWNetworkSim.App`](src/MedWNetworkSim.App) — the main WPF desktop application
- [`src/MedWNetworkSim.App.Verification`](src/MedWNetworkSim.App.Verification) — a verification console project with scenario-based checks

The verification project is useful when you want to confirm expected simulator behaviour after code changes.

## Core model

### Nodes

A node is a place in the network. Depending on configuration, a node can:

- produce traffic
- consume traffic
- store traffic
- transship traffic
- transform local inputs into outputs
- expose an external interface for hierarchical subnetworks

### Edges

An edge is a route between nodes. Edges can carry:

- travel time
- cost
- capacity
- one-way or bidirectional movement
- traffic-specific permission rules

### Traffic types

A traffic type is a category of flow moving through the network. Different traffic types can use different route-choice settings and different flow-splitting behaviour.

## Key simulation features

### Routing and flow control

The simulator supports route-choice combinations including:

- fastest versus cheapest routing preferences
- deterministic system-optimal routing
- stochastic user-responsive routing
- single-path and multi-path flow splitting
- legacy allocation-mode compatibility for older JSON files

This makes it possible to compare how different classes of flow behave under congestion, scarcity, and mixed priorities.

### Timeline simulation

Timeline mode advances the model period by period. This allows you to test cases where:

- supply starts later than demand
- demand recurs over time
- travel takes multiple periods
- edge occupancy persists while traffic is in flight
- storage is replenished over repeated cycles
- production only occurs in specific windows
- recipe outputs depend on local precursor availability

### Production, storage, and recipes

Traffic profiles can include:

- multiple production windows
- multiple consumption windows
- store replenishment behaviour
- local precursor requirements
- inherited landed-cost behaviour for transformed outputs
- validation against cyclic recipe dependencies

### Capacity and pressure behaviour

The model includes:

- durable edge occupancy across periods
- shared occupancy on bidirectional edges
- waiting behaviour when downstream edges are blocked
- transhipment-capacity blocking
- demand backlog tracking for unserved demand
- edge pressure and utilisation reporting in the UI

## OpenStreetMap import

The application includes OpenStreetMap import support for:

- `*.osm` XML files
- `*.pbf` files

The OSM importer is designed to make large road extracts usable for simulation by simplifying the imported graph while preserving important network properties. Based on the current verification coverage, the importer includes checks for:

- parser selection by file extension
- parity between OSM XML and PBF imports
- rejection of invalid PBF input
- rejection of files with no supported roads
- retention of T-junctions, crossroads, dead ends, articulation points, mandatory nodes, and named-road transitions
- reduction in node count without orphaning retained nodes
- preservation of reachability, shape anchors, and collapsed-path distance
- deterministic naming, including direct, derived, and fallback naming paths

## Hierarchical subnetworks

MedWNetworkSim supports hierarchical modelling through composite nodes and subnetworks. This allows a parent network to route into a child network through declared external interface nodes.

This is useful when you want to:

- keep a regional network readable while embedding detailed local networks
- test alternative internal layouts behind the same external interface
- validate that external interface selection changes routing outcomes

## Desktop UI and analysis workflow

The desktop application includes workflow surfaces for:

- canvas editing
- layers
- inspector panels
- reports drawer
- legend
- canvas-only mode

The current verification suite also indicates support for:

- route selection in reports highlighting the route on the canvas
- empty-state messaging in reports
- timeline reset clearing edge pressure visuals
- opening and closing inspector, layers, reports, and legend panels independently

## File formats

### JSON

JSON is the main working format for MedWNetworkSim. It is the best choice when you want to preserve simulator behaviour and configuration.

Use JSON when you need to keep:

- traffic definitions
- node traffic profiles
- capacities
- routing configuration
- timeline windows
- recipe inputs
- layout and positions
- hierarchical subnetwork references

### GraphML

GraphML is useful for structural interchange with other graph tools, but JSON is the better long-term working format for full simulator fidelity.

## Reports and outputs

The current top-level README mentions report export from the desktop application, and the verification suite confirms report-oriented UI flows such as route selection, empty states, and routing summaries.

If you are updating reporting behaviour, the most relevant code areas are likely under:

- [`src/MedWNetworkSim.App`](src/MedWNetworkSim.App)
- [`src/MedWNetworkSim.App.Verification`](src/MedWNetworkSim.App.Verification)

## Getting started

### Prerequisites

- .NET 7 SDK or later
- Visual Studio 2022 or later
- Windows for the WPF desktop application

### Build the solution

```bash
dotnet build MedWNetworkSim.slnx
```

### Run the desktop application

```bash
dotnet run --project .\src\MedWNetworkSim.App\MedWNetworkSim.App.csproj
```

### Run the verification project

```bash
dotnet run --project .\src\MedWNetworkSim.App.Verification\MedWNetworkSim.App.Verification.csproj
```

## Suggested first steps

A practical way to explore the tool is:

1. Open the desktop application.
2. Load a sample network if one is bundled in your build, or create a new network.
3. Define traffic types.
4. Add nodes and edges.
5. Configure capacities, costs, times, and permissions.
6. Run a simulation.
7. Review the reports and inspector output.
8. Switch to timeline mode when period-by-period behaviour matters.
9. Try OSM import if you want to create a road-based network quickly.

## Documentation links

The currently indexed repository contents do **not** show a populated `docs/` directory with stable end-user guides. The most useful linked entry points presently visible are:

- [Main application project](src/MedWNetworkSim.App)
- [Verification project](src/MedWNetworkSim.App.Verification)
- [Issue tracker](https://github.com/wnj00524/MedWNetworkSim/issues)

If additional documentation files are added later, this section should be expanded to link to them directly.

## Who this is for

MedWNetworkSim is a good fit for users who want to explore constrained movement through a graph without moving to a full agent-based world model.

Typical uses include:

- logistics and supply routing
- bottleneck testing
- network design exploration
- infrastructure and service-flow scenarios
- timeline-based demand and replenishment analysis
- scenario prototyping before moving to more complex simulation systems

## Contributing

When contributing:

- keep README claims aligned with the codebase and verification coverage
- prefer JSON examples that reflect the current model
- add verification scenarios for new simulation behaviour
- update this README when user-visible functionality changes

## License

This repository currently presents itself as MIT-licensed in the existing top-level README.

## Support and feedback

For bugs, gaps in documentation, or feature requests, use the repository issue tracker:

- [Open an issue](https://github.com/wnj00524/MedWNetworkSim/issues)

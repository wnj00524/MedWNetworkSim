# MedWNetworkSim

MedWNetworkSim is a .NET network simulation and analysis tool for building, editing, importing, and testing routed flow networks.

The primary desktop application is the **Avalonia UI** version. It is designed for visually modelling networks where nodes produce, consume, store, transform, and transship traffic types across constrained edges with time, cost, and capacity rules.

Use it to explore bottlenecks, unmet demand, congestion, route choice, supply distribution, and timeline-based network behaviour.

## What the tool does

MedWNetworkSim lets you:

- build and edit networks visually
- create new blank networks
- open and save network files as JSON
- import and export GraphML
- import OpenStreetMap road networks
- define custom traffic types
- configure node production, consumption, storage, and transhipment
- configure edge travel time, cost, capacity, and traffic permissions
- run single-step and timeline-based simulations
- inspect unmet need, edge pressure, utilisation, and route behaviour
- export reports for analysis
- test behaviour changes using the verification project

## Primary application

The main desktop app is:

- `src/MedWNetworkSim.App.Avalonia`

Supporting projects in the solution include:

- `src/MedWNetworkSim.UI`
- `src/MedWNetworkSim.Presentation`
- `src/MedWNetworkSim.Rendering`
- `src/MedWNetworkSim.Interaction`
- `src/MedWNetworkSim.App.Verification`

The Avalonia application uses a Fluent theme and serves as the main user-facing GUI.

## Core concepts

### Nodes

A node represents a place in the network.

Depending on configuration, a node can:

- produce traffic
- consume traffic
- store traffic
- transship traffic
- transform local inputs into outputs
- participate in hierarchical network structures

### Edges

An edge represents a route between nodes.

Edges can model:

- travel time
- monetary or abstract cost
- limited capacity
- one-way or bidirectional flow
- traffic-type permissions or restrictions

### Traffic types

A traffic type is a named category of flow moving through the network.

Different traffic types can use different routing and allocation behaviour, allowing the same network to represent multiple classes of goods, services, or movement.

## Simulation capabilities

### Routing and flow choice

The simulator supports a range of route-choice and allocation behaviours, including:

- fastest versus cheapest preference
- deterministic routing
- responsive routing
- single-path and split-flow behaviour
- traffic-specific routing settings

This makes it possible to compare how different traffic classes behave under scarcity, congestion, and competing priorities.

### Timeline simulation

Timeline mode advances the network period by period.

This is useful when you need to model:

- delayed supply arrival
- recurring demand
- multi-period travel
- persistent edge occupancy
- repeated production and replenishment
- staged transformation chains
- backlog growth and recovery over time

### Production, storage, and transformation

Traffic profiles can model:

- production
- consumption
- storage
- transhipment
- local input requirements
- transformed outputs derived from precursor inputs

### Pressure, backlog, and bottlenecks

The model can be used to analyse:

- unmet demand
- persistent shortages
- edge pressure
- utilisation
- blocked downstream movement
- transhipment constraints
- route competition between traffic types

## OpenStreetMap import

MedWNetworkSim supports OpenStreetMap import for road-based networks.

The importer is intended to make large source graphs more usable for simulation by simplifying them while preserving important structure.

This supports workflows such as:

- importing a real road network
- simplifying it to key junctions
- preserving meaningful route shape
- deriving edge distances from collapsed road paths
- using imported geography as a simulation network

## File formats

### JSON

JSON is the main working format for MedWNetworkSim.

Use JSON when you want to preserve simulation configuration such as:

- traffic definitions
- node traffic profiles
- capacities
- costs and times
- routing settings
- timeline windows
- permissions
- layout positions
- hierarchical network data

### GraphML

GraphML is supported for graph interchange with external tools.

Use GraphML when you need portability of network structure. Use JSON when you need full simulator fidelity.

## User workflow

A typical workflow is:

1. Create a new blank network or open an existing JSON file.
2. Define the traffic types you want to simulate.
3. Add or edit nodes and edges on the canvas.
4. Configure node and edge behaviour in the UI.
5. Run a simulation.
6. Inspect pressure, unmet demand, route use, and utilisation.
7. Export reports if needed.
8. Switch to timeline mode when period-based behaviour matters.
9. Import GraphML or OSM data when starting from external network sources.

## Build and run

### Prerequisites

- .NET 8 SDK or later
- Visual Studio 2022 or later, or another .NET-compatible IDE

### Build the solution

```bash
dotnet build MedWNetworkSim.slnx
```

### Run the Avalonia application

```bash
dotnet run --project ./src/MedWNetworkSim.App.Avalonia/MedWNetworkSim.App.Avalonia.csproj
```

### Run the verification project

```bash
dotnet run --project ./src/MedWNetworkSim.App.Verification/MedWNetworkSim.App.Verification.csproj
```

## Repository structure

```text
src/
  MedWNetworkSim.App.Avalonia/      Primary Avalonia desktop app
  MedWNetworkSim.UI/                Shared UI shell and views
  MedWNetworkSim.Presentation/      View models and presentation logic
  MedWNetworkSim.Rendering/         Rendering logic
  MedWNetworkSim.Interaction/       Interaction and editing behaviour
  MedWNetworkSim.App.Verification/  Scenario and regression verification
```

## Who this tool is for

MedWNetworkSim is useful for users who want to model and test constrained movement through a graph without needing a full agent-based simulation.

Typical uses include:

- logistics and supply routing
- bottleneck analysis
- infrastructure modelling
- service-flow scenarios
- demand and replenishment analysis
- route policy testing
- network design exploration
- prototype scenario modelling

## Current focus

The current primary focus of the project is the Avalonia GUI experience and feature completeness of the editing and analysis workflow.

That includes making sure users can:

- create and edit networks entirely within the Avalonia app
- inspect node and edge state clearly
- define and edit traffic types directly in the UI
- understand why shortages and pressure occur
- use reports and canvas inspection together

## Documentation

Current key entry points:

- `README.md`
- `src/MedWNetworkSim.App.Avalonia`
- `src/MedWNetworkSim.App.Verification`
- `docs/`
- GitHub Issues for bugs and feature requests

## Contributing

When contributing:

- keep README claims aligned with actual implemented behaviour
- prioritise Avalonia UI functionality and clarity
- add verification coverage for simulation behaviour changes
- update user-facing documentation when workflows change
- prefer examples that reflect the current supported model

## License

This repository is MIT licensed.

## Feedback and issues

For bugs, feature requests, or documentation gaps, use the GitHub issue tracker:

- https://github.com/wnj00524/MedWNetworkSim/issues

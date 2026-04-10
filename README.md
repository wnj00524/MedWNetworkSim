# MedWNetworkSim

MedWNetworkSim is a desktop application for testing how things move through a network.

In this app, a **network** is made of places and connections:
- **Nodes** are places, such as clinics, depots, homes, collection points, warehouses, or hubs.
- **Edges** are the routes between those places.
- **Traffic types** are the kinds of things being moved through the network.

You can use it to answer practical questions such as:
- Where does supply come from?
- Where does demand exist, and when is it active?
- Which routes are fastest or cheapest?
- What happens when a route or a transfer point has limited capacity?
- What changes over time if supply, demand, and storage are scheduled by period?

## What the application can do

MedWNetworkSim can:
- create a new network from scratch
- open an existing network saved as JSON
- load a sample network so you can explore the application quickly
- import and export GraphML files
- let you build and edit a network visually in the desktop app
- run a one-off simulation to show how movement would be routed
- run a timeline simulation to show what happens period by period
- loop timeline schedules on a repeating cycle length
- support multiple production and consumption windows for one profile
- gate production on local precursor inputs
- export reports in HTML or CSV
- work from the command line as well as the desktop interface

## How the model works

The simulator uses a few core ideas.

### Nodes
A node is a place in the network.

A node can do more than one job. Depending on how you set it up, a node can:
- **produce** traffic, meaning it supplies something
- **consume** traffic, meaning it needs something
- **transship** traffic, meaning it can act as an intermediate stop
- **store** traffic, meaning it can hold inventory for later use
- transform local precursor traffic into another output traffic type
- activate on one schedule window or several separate windows

A single node can do different jobs for different traffic types.

Example:
- A hospital might consume medical supplies.
- A depot might produce transport capacity for one traffic type and store another.
- A hub might exist mainly to pass traffic through.

### Edges
An edge is a connection between two nodes.

An edge can have:
- a **time** value
- a **cost** value
- an optional **capacity** limit
- either **one-way** or **bidirectional** movement

### Traffic types
A traffic type is the kind of thing being moved.

Examples might include:
- waste
- reusable equipment
- medicine
- staff transport
- collected stock

Each traffic type can have its own routing preference:
- **speed**: prefer the fastest route
- **cost**: prefer the cheapest route
- **totalCost**: prefer the lowest combined time and cost

### Local production inputs
Production can depend on other traffic types already being present at the same node.

Examples:
- bread may require wheat and water
- a processing output may require a consumable precursor
- a packaging flow may require empty containers before finished goods can be produced

## What you see in the app

The desktop app lets you:
- create and edit nodes and edges
- set network name and description
- assign traffic roles to nodes
- drag nodes around the canvas
- set a timeline loop length
- edit multiple production and consumption windows for a profile
- define local precursor input ratios for production
- auto-arrange node positions
- view routed movements and summaries
- export reports

The app also supports a **Canvas Only** view so you can focus on the visual network layout.

## Two ways to simulate

### 1. Run Simulation
This is the quick, one-shot analysis.

Use this when you want to see how the current network would route movement right now.

### 2. Timeline mode
This moves the model forward one period at a time.

Use this when timing matters, for example:
- supply starts in period 3
- demand ends in period 8
- goods take time to travel
- inventory is stored and released later
- one output cannot be produced until the right precursor traffic is already present locally

Timeline mode can also run on a repeating loop. For example:
- a loop length of `12` means schedules are checked against cycle periods `1` to `12`
- after period `12`, the next effective schedule period is `1` again

This is useful for repeating weekly, monthly, or seasonal patterns without rebuilding the same schedule manually.

### Production windows and recipes
Each traffic profile can now use:
- one production window or many production windows
- one consumption window or many consumption windows
- zero, one, or many local input requirements per unit of output

## Reports

The application can export results as:
- **HTML** for a readable report
- **CSV** for spreadsheet work

In timeline reports, the application can show:
- the simulated period
- the effective cycle period when looping is enabled
- edge flow, occupancy, and utilisation

Reports can be exported from the desktop app or from the command line.

## Files and formats

### JSON
JSON is the main working format for this application.

It preserves the full MedWNetworkSim model, including newer features such as:
- traffic definitions
- node roles
- schedules
- storage
- premiums
- timeline loop length
- multiple production and consumption windows
- local precursor input requirements
- capacities
- positions and layout

If you want the most complete and reliable save format, use JSON.

### GraphML
GraphML is useful for exchanging graph structures with other tools.

However, JSON is the better long-term format when you want to keep the full simulator behaviour.

## Getting started

For most people, the easiest path is:
1. Open the app.
2. Load the sample network or create a new one.
3. Add traffic types.
4. Add nodes.
5. Add edges.
6. Set what each node produces, consumes, stores, or passes through.
7. Add any schedule windows, loop settings, or precursor input requirements if timing or recipes matter.
8. Run a simulation.
9. Export a report.

## Running the application

Build:

```bash
dotnet build MedWNetworkSim.slnx
```

Run the desktop app:

```bash
dotnet run --project .\src\MedWNetworkSim.App\MedWNetworkSim.App.csproj
```

The project includes a bundled sample network for exploration.

## Documentation

- [CLI Reference](docs/CLI_REFERENCE.md)
- [Network Authoring Guide](docs/NETWORK_AUTHORING.md)

## Command-line highlights

The CLI now supports:
- setting a network timeline loop length
- setting repeated `--production-window` and `--consumption-window` values
- setting repeated `--input TrafficType:Quantity` precursor requirements
- clearing windows and inputs explicitly
- using `--gui` or `--force-gui` to open the WPF app even when arguments are present

## Who this is for

This application is useful for anyone who wants to explore movement through a network, even if they are not a developer.

That includes people working on:
- logistics planning
- service delivery models
- transport flows
- supply and demand scenarios
- storage and bottleneck analysis
- period-based movement over time
- repeating-cycle scheduling
- local transformation and precursor-constrained production

## Plain-English glossary

- **Node**: a place in the network
- **Edge**: a route between places
- **Traffic type**: a category of thing being moved
- **Producer**: a node that supplies traffic
- **Consumer**: a node that needs traffic
- **Transshipment**: a node acting as a pass-through stop
- **Store**: a node that keeps inventory for later
- **Capacity**: the maximum amount a route or node can handle
- **Timeline**: a step-by-step simulation over periods

- **Loop length**: the number of periods in the repeating timeline cycle
- **Period window**: an inclusive active range such as `1-3` or `8-10`
- **Input requirement**: a local precursor traffic type consumed to produce another traffic type
- **Occupancy**: how much of an edge’s concurrent capacity is currently in use

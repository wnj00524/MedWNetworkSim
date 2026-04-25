# MedWNetworkSim

MedWNetworkSim is a .NET-based network simulation and analysis tool for modelling constrained flow across graph-based systems.

It allows you to build, import, edit, and simulate networks where traffic moves between nodes under capacity, cost, and time constraints.

---

## Overview

MedWNetworkSim is designed for analysing:

- supply and demand imbalance
- congestion and bottlenecks
- route choice behaviour
- capacity constraints
- multi-period (timeline) flow dynamics

The primary application is a **desktop GUI built with Avalonia**.

---

## Key Capabilities

### Network Modelling

- Create and edit networks visually
- Add, remove, and configure nodes and edges
- Define multiple traffic types
- Configure:
  - production
  - consumption
  - storage
  - transformation
  - transhipment

### Simulation

- Run single-step simulations
- Run timeline-based simulations (multi-period)
- Analyse:
  - unmet demand
  - backlog growth
  - edge pressure
  - utilisation
  - routing behaviour

### Routing Behaviour

Supports multiple routing strategies:

- fastest vs cheapest routing
- deterministic vs responsive routing
- single-path vs split-flow allocation
- traffic-specific routing rules

### Import / Export

- JSON (full simulation fidelity)
- GraphML (structure-only interchange)
- OpenStreetMap (OSM) import with:
  - graph simplification to key junctions
  - preserved network shape
  - distance-based edge costs

### Analysis & Reporting

- Inspect state directly on the canvas
- Identify:
  - shortages
  - congestion
  - routing conflicts
- Export reports for further analysis

---

## Core Concepts

### Nodes

Nodes represent locations in the network.

They can:

- produce traffic
- consume traffic
- store traffic
- transform inputs into outputs
- act as transhipment points

### Edges

Edges represent routes between nodes.

They support:

- travel time
- cost
- capacity limits
- directionality
- traffic permissions

### Traffic Types

Traffic types define what flows through the network.

Each type can have:

- independent routing behaviour
- separate constraints
- unique production/consumption rules

---

## Typical Workflow

1. Create a new network or open a JSON file  
2. Define traffic types  
3. Add and configure nodes and edges  
4. Run a simulation  
5. Inspect results (pressure, unmet demand, routing)  
6. Export reports if needed  
7. Use timeline mode for multi-period scenarios  

---

## Repository Structure

```text
src/
  MedWNetworkSim.App.Avalonia/      Main desktop application
  MedWNetworkSim.UI/                Shared UI components
  MedWNetworkSim.Presentation/      View models and logic
  MedWNetworkSim.Rendering/         Canvas and visual rendering
  MedWNetworkSim.Interaction/       Editing and interaction logic
  MedWNetworkSim.App.Verification/  Simulation verification tests
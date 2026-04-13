# Network Authoring Guide

This guide explains how to build a MedWNetworkSim network in a way that is easy to understand and easy to maintain.

## Three ways to build a network

This project supports three main authoring paths:
- editing directly in the desktop app
- editing the JSON file by hand
- creating or updating the network through the CLI

For most people, the desktop app is the easiest place to start.

## Recommended workflow for non-technical users

A practical workflow is:
1. Start in the desktop app.
2. Load the bundled sample or create a new network.
3. Add one traffic type first, then confirm the basic flow works before adding timing or recipes.
4. Add a small number of nodes.
5. Connect them with edges.
6. Set what each node does for that traffic type.
7. Run a simulation.
8. Only then add more traffic types, storage, schedules, or capacity constraints.

This keeps the model understandable while you are learning it.

## Start with plain-language questions

Before building the network, write down answers to these questions:
- What is being moved?
- Where does it start?
- Where does it need to end up?
- Which places can act as transfer points?
- Are any routes slower, more expensive, or limited in capacity?
- Does timing matter?
- Does the same timing repeat in a cycle?
- Does any output depend on precursor traffic already being present at the same node?
- Does any place need to store stock between periods?

If you can answer those questions, you can usually build the network successfully.

## The core building blocks

## 1. Traffic types
A traffic type is a category of thing being moved through the network.

Examples:
- waste
- medicine
- reusable containers
- staff transport
- parcels

Each traffic type has its own routing preference, so different categories can behave differently in the same model.

### Good practice
Use separate traffic types when the items:
- behave differently
- have different priorities
- compete for shared capacity
- should be reported separately

## 2. Nodes
A node is a place in the network.

Typical examples:
- clinic
- depot
- warehouse
- household
- transfer hub
- treatment centre

A node can hold **multiple traffic profiles**, usually one per traffic type. That means the same place can behave differently depending on what is moving through it.

## 3. Profiles
A profile answers this question:

**For this traffic type, what does this node do?**

A profile can make a node:
- produce
- consume
- transship
- store

Important profile fields include:
- `production`
- `consumption`
- `canTransship`
- `consumerPremiumPerUnit`
- schedule windows
- repeated schedule windows
- local input requirements for production
- optional store behaviour

### How to think about roles
- **Producer**: the node creates supply
- **Consumer**: the node creates demand
- **Transshipment**: the node can be used as an intermediate stop
- **Store**: the node can hold inventory for later periods

A useful modelling habit is to ask, for each traffic type at each node:
- Does this place supply anything?
- Does it need anything?
- Can traffic pass through it?
- Should it store inventory?

### Production recipes and precursor inputs
Some outputs may require other traffic types to be present locally before they can be produced.

Examples:
- bread may require wheat and water
- a processing output may require a consumable precursor
- a packaging flow may require empties before finished units can be created

These are local transformation rules at one node, not route requests by themselves.

## 4. Edges
An edge is a route between two nodes.

An edge can include:
- time
- cost
- capacity
- direction

This is how you represent the structure of movement.

### Good practice
Use time and cost consistently.

For example:
- If time represents hours, keep all edge times in hours.
- If cost represents a delivery cost, keep all edge costs in the same units.

## JSON should be your main save format

The JSON format is the most complete format in this project. It preserves the full MedW model, including:
- traffic definitions
- node shapes and positions
- node transhipment capacities
- traffic profiles
- schedules
- store settings
- consumer premiums
- edge cost, time, capacity, and direction

GraphML is still useful for graph exchange, but JSON is better when you want to preserve the full behaviour of the simulator.

## Understanding capacity

Capacity matters in two main places.

### Edge capacity
Edge capacity limits how much total traffic can move along a route.

Use this when a route itself is the bottleneck.

Examples:
- limited vehicle space
- route throughput limits
- constrained road, bridge, or transfer corridor

### Node transshipment capacity
Node transshipment capacity limits how much total traffic can use a node as an intermediate stop.

Use this when a place, not a route, is the bottleneck.

Examples:
- a sorting point with limited handling staff
- a depot with limited docking capacity
- a small transfer station

### Capacity bidding
Traffic types can bid for scarce capacity using `capacityBidPerUnit`, and consumers can add a node-specific premium using `consumerPremiumPerUnit`.

In plain English, this means some traffic can effectively be treated as more valuable or higher priority when capacity is limited.

## Understanding timeline mode

Timeline mode is the step-by-step version of the simulation.

In timeline mode:
- scheduled production and demand activate by period or by any matching window
- traffic moves across edges over time instead of arriving instantly
- in-flight traffic stays in motion between periods
- store nodes can accumulate inventory and release it later

This is useful when the timing of movement matters, not just the final routing result.

### Looping timelines
The network can also use a repeating loop length.

If the loop length is `12`, then:
- absolute period 1 uses effective period 1
- absolute period 12 uses effective period 12
- absolute period 13 uses effective period 1 again

This is useful for repeating cycles such as:
- weekly demand
- monthly production plans
- seasonal collection or delivery patterns

### Multiple windows
Each profile can now define:
- multiple production windows
- multiple consumption windows

The profile is active if any matching window includes the current effective period.
This makes it easier to represent split shifts, seasonal bursts, or recurring delivery windows.

### Example
Suppose:
- Node A produces in period 1
- Node C consumes in period 3
- the route is A → B → C
- each edge has time `1`

Traffic leaves A, reaches B after one period, and reaches C after another. The timing is part of the model, not just the route shape. The application’s UI and docs describe this period-by-period behaviour directly.

## Understanding local production inputs

Production can be constrained by local precursor stock.

Example:
- Bread production at a bakery is set to `10`
- Bread requires `Wheat:1` and `Water:2`
- Local availability is `Wheat 6` and `Water 14`

The bakery can only produce `6` Bread, because Wheat is the limiting input.

If the required precursor set is not available locally, production of that output traffic halts for that period.

## A safe way to build a good model

A strong authoring pattern is to build the network in layers.

### Layer 1: structure
Create:
- traffic type
- nodes
- edges

Do not add advanced options yet.

### Layer 2: roles
Set:
- which nodes produce
- which nodes consume
- which nodes transship

Run a basic simulation.

### Layer 3: realism
Then add:
- route costs
- route times
- capacities
- premiums
- schedules
- repeating windows
- precursor input requirements
- storage

Run the simulation again and compare results.

This makes it much easier to spot modelling mistakes.

## Common modelling mistakes

### Making every node do everything
If too many nodes are producers, consumers, and transshipment points all at once, the model becomes difficult to interpret.

### Using too many traffic types too early
Start with one traffic type until the structure behaves correctly.

### Adding recipes before confirming the base flow
First confirm the plain producer-consumer path works, then add input requirements.

### Mixing units
Do not mix different meanings for time or cost in the same model.

### Using GraphML as the only working copy
GraphML is useful for exchange, but JSON is safer as the master file because it preserves more simulator-specific detail.

### Forgetting that store is separate from role
A node can store traffic, but that is separate from whether it produces, consumes, or transships.

## When to use the desktop app, JSON, or CLI

### Use the desktop app when you want:
- visual editing
- drag-and-drop layout
- direct inspection of routes and summaries
- easier experimentation

### Use JSON when you want:
- a full-fidelity saved model
- timeline loop length
- repeated windows
- precursor input requirements
- version control
- manual review of the data structure

For exact JSON property names, enum values, validation rules, and a compact hand-authored example, see [Network JSON Reference](NETWORK_JSON_REFERENCE.md).

### Use the CLI when you want:
- repeatable loop-length changes
- repeatable windows and recipe inputs
- repeatable setup
- scripting
- batch updates
- automated reporting

## Keeping documentation in sync

The repository already separates documentation into three roles:
- `README.md` for overview and quick start
- `docs/CLI_REFERENCE.md` for command syntax
- `docs/NETWORK_AUTHORING.md` for modelling guidance

That is a good structure and worth keeping.

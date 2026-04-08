# MedWNetworkSim

WPF network simulator for modelling multi-traffic movement across producer, consumer, and transhipment nodes.

## What It Does

- Creates a new network in-app or loads an existing JSON network file.
- Loads the bundled sample network for quick exploration.
- Imports and exports GraphML files through a dedicated popup window with explicit import and export file pickers.
- Lets users edit the network name and description directly in the app.
- Lets users create and edit traffic types, nodes, node roles, and edges directly in the app.
- Draws the network on a draggable canvas.
- Auto-positions nodes when `x` and `y` are omitted from the input file.
- Includes an `Auto Arrange` action to regenerate node positions for the whole network.
- Models optional edge capacities and consumes them during routing.
- Models optional shared node transhipment capacities and consumes them during routing.
- Lets each node participate in any number of traffic types.
- Allows the same node to produce, tranship, and consume different traffic types.
- Supports per-traffic routing priorities:
  - `speed`: minimise edge time
  - `cost`: minimise edge cost
  - `totalCost`: minimise `time + cost`
- Simulates routed movements from producers to consumers through valid transhipment nodes.
- Shows simulation outputs in-app, including routed movements, consumer costs, and traffic summaries filtered by traffic type.
- Saves the current network, including updated node positions, back to JSON.

## Editing In App

- Use `New Network`, `Load Sample`, and `Open JSON...` from the main toolbar to start from a blank model, the bundled sample, or a saved JSON file.
- Use `Save JSON...` to save the current in-memory network back to the app's JSON format.
- Use `GraphML...` to open the GraphML popup. It provides separate `Import File` and `Export File` paths, `Browse...` buttons, `Load GraphML`, and `Save GraphML`.
- In the GraphML popup, choose a default traffic type, a default node role, and an optional default capacity for imported nodes that do not already carry MedW-specific traffic data.
- Maintain traffic types in the `Network Editor` tab, including routing preference and optional `capacityBidPerUnit` used for both edge and node-transhipment-capacity competition.
- Add and remove nodes in the main editor grid, including each node's optional shared `transhipmentCapacity`.
- Open `Open Node Editor...` to edit one node in a dedicated window with dropdown-based traffic-role editing.
- In the node editor, choose the node, then choose one of its traffic-role entries, then set:
  - `Traffic Type`
  - `Role`
  - `Production`
  - `Consumption`
- Add and remove edges in the `Edges` grid. `From` and `To` are chosen from the existing node list rather than typed freehand.
- Use the `Node Roles` tab to review the currently selected node and traffic-role entry before opening the dedicated editor window.
- Drag nodes on the canvas to refine the layout visually, or use `Auto Arrange` to regenerate positions.

## Simulation Outputs

- The left-hand overview shows network totals and per-traffic summaries after a run.
- The `Consumer Costs` tab shows local quantity, imported quantity, blended unit cost, and total movement cost for each consumer.
- The `Routed Movements` tab shows each routed allocation, including source, producer, consumer, quantity, path, transit cost, bid cost, and landed cost.
- The `Node Roles` tab provides a quick selection surface for node traffic-role entries and links back into the dedicated node editor.

## Run It

```powershell
dotnet build MedWNetworkSim.slnx
dotnet run --project .\src\MedWNetworkSim.App\MedWNetworkSim.App.csproj
```

The app ships with a bundled sample file at [sample-network.json](src/MedWNetworkSim.App/Samples/sample-network.json).

## GraphML Format

- GraphML export preserves the full MedW network by writing graph metadata, traffic definitions, node coordinates, node transhipment capacities, node traffic profiles, and edge properties into GraphML `<data>` elements.
- GraphML import restores those MedW-specific payloads when they are present.
- The `GraphML...` popup loads from the chosen import path and writes to the chosen export path. Importing replaces the current in-memory network.
- When you import a more generic GraphML file that only contains graph structure, the `GraphML...` popup can synthesize a starter traffic-role entry per node from the chosen default traffic type, node role, and optional capacity.
- Leaving the default traffic type or role on none keeps imported nodes structural only.
- When the default role includes transhipment, the default capacity becomes that node's shared `transhipmentCapacity`. Producer or consumer defaults still fall back to `1` unit if they need a starter quantity and no amount is available in the GraphML input.

## JSON Format

The app uses a simple custom JSON format:

```json
{
  "name": "Example Network",
  "description": "Optional description",
  "trafficTypes": [
    {
      "name": "Infectious Waste",
      "description": "Optional description",
      "routingPreference": "speed",
      "capacityBidPerUnit": 1.5
    }
  ],
  "nodes": [
    {
      "id": "N1",
      "name": "Clinic A",
      "transhipmentCapacity": 24,
      "trafficProfiles": [
        {
          "trafficType": "Infectious Waste",
          "production": 40,
          "consumption": 0,
          "canTransship": false
        }
      ]
    }
  ],
  "edges": [
    {
      "id": "E1",
      "fromNodeId": "N1",
      "toNodeId": "N2",
      "time": 3.5,
      "cost": 6.0,
      "capacity": 20,
      "isBidirectional": true
    }
  ]
}
```

`x` and `y` are optional. If they are omitted, the app generates an initial layout when the file is loaded, and those generated positions are then saved back out if you use `Save JSON...`.

`capacity` is optional on edges. If it is omitted, the edge is treated as having unlimited capacity.

`transhipmentCapacity` is optional on nodes. If it is omitted, the node can tranship unlimited flow whenever the active traffic role allows transhipment.

`capacityBidPerUnit` is optional on a traffic type. If omitted, `speed` traffic defaults to a bid of `1` per constrained bottleneck resource and other traffic types default to `0`.

## Routing Rules

- Edge weights are shared across traffic types through `time` and `cost`.
- Edge capacity is optional, but when present it is shared across all traffic routed through that edge.
- Node transhipment capacity is optional, but when present it is shared across all traffic routed through that node as an intermediate stop.
- Traffic types can place a per-unit bid on constrained edge or node transhipment capacity.
- A traffic type chooses how those edge values are scored.
- Producer nodes are any nodes with `production > 0` for that traffic.
- Consumer nodes are any nodes with `consumption > 0` for that traffic.
- Intermediate nodes must have `canTransship: true` for that same traffic.
- Local producer-to-consumer matching on the same node is handled before network routing.
- Capacity competition is resolved across all traffic types together. Higher bids win access to scarce edge or node-transhipment capacity first, then the normal route score breaks ties.
- Bid premiums are added to the landed movement cost when the route is genuinely bottlenecked by finite edge or node-transhipment capacity.

## Notes

- Omit `capacity` on an edge when you want it to behave as unlimited.
- Omit `transhipmentCapacity` on a node when you want it to behave as unlimited.
- The consumer-cost view shows local and imported movement costs separately, plus the blended movement cost seen at each consumer node.
- Routing is path-based and allocates producer supply to consumer demand using the best available routes under the chosen traffic preference and capacity bidding.
- `Auto Arrange` only updates node positions. It does not throw away in-memory edits to nodes, roles, or traffic types.

## Code Structure

- [MainWindow.xaml](src/MedWNetworkSim.App/MainWindow.xaml) defines the main shell: canvas, summary panes, simulation results, and the in-app editor grids.
- [MainWindowViewModel.cs](src/MedWNetworkSim.App/ViewModels/MainWindowViewModel.cs) is the application coordinator. It loads/saves networks, keeps editor selections in sync, and triggers simulation.
- [GraphMlTransferWindow.xaml](src/MedWNetworkSim.App/GraphMlTransferWindow.xaml) and [GraphMlTransferWindow.xaml.cs](src/MedWNetworkSim.App/GraphMlTransferWindow.xaml.cs) provide the dedicated GraphML import/export dialog and file-picking workflow.
- [NodeEditorWindow.xaml](src/MedWNetworkSim.App/NodeEditorWindow.xaml) provides the dedicated dropdown-driven node editing workflow.
- [GraphMlFileService.cs](src/MedWNetworkSim.App/Services/GraphMlFileService.cs) translates between the in-memory MedW model and GraphML, including fallback default-node synthesis for generic GraphML imports.
- [NetworkFileService.cs](src/MedWNetworkSim.App/Services/NetworkFileService.cs) normalizes and validates JSON data and applies automatic layout.
- [NetworkSimulationEngine.cs](src/MedWNetworkSim.App/Services/NetworkSimulationEngine.cs) performs routing, capacity competition, bid-cost calculation, and consumer-cost summarization.
- [NodeTrafficRoleCatalog.cs](src/MedWNetworkSim.App/Models/NodeTrafficRoleCatalog.cs) centralizes the named producer, consumer, and transhipment role combinations used by the UI and GraphML import mapping.
- The `Models` folder contains the persisted JSON shape.
- The `ViewModels` folder contains the editable UI state and display helpers used by WPF binding.

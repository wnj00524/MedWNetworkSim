# MedWNetworkSim

WPF network simulator for modelling multi-traffic movement across producer, consumer, and transhipment nodes.

## What It Does

- Loads a JSON network file.
- Lets users create a new network and edit traffic types, nodes, node roles, and edges directly in the app.
- Draws the network on a draggable canvas.
- Auto-positions nodes when `x` and `y` are omitted from the input file.
- Includes an `Auto Arrange` action to regenerate node positions for the whole network.
- Models optional edge capacities and consumes them during routing.
- Lets each node participate in any number of traffic types.
- Allows the same node to produce, tranship, and consume different traffic types.
- Supports per-traffic routing priorities:
  - `speed`: minimise edge time
  - `cost`: minimise edge cost
  - `totalCost`: minimise `time + cost`
- Simulates routed movements from producers to consumers through valid transhipment nodes.
- Saves the current network, including updated node positions, back to JSON.

## Editing In App

- Use `New Network` to start from an empty model.
- Maintain traffic types in the `Network Editor` tab, including routing preference and optional `capacityBidPerUnit`.
- Add and remove nodes in the main editor grid.
- Open `Open Node Editor...` to edit one node in a dedicated window.
- In the node editor, choose the node, then choose one of its traffic-role entries, then set:
  - `Traffic Type`
  - `Role`
  - `Production`
  - `Consumption`
- Add and remove edges in the `Edges` grid. `From` and `To` are chosen from the existing node list rather than typed freehand.
- Drag nodes on the canvas to refine the layout visually, or use `Auto Arrange` to regenerate positions.

## Run It

```powershell
dotnet build MedWNetworkSim.slnx
dotnet run --project .\src\MedWNetworkSim.App\MedWNetworkSim.App.csproj
```

The app ships with a bundled sample file at [sample-network.json](src/MedWNetworkSim.App/Samples/sample-network.json).

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

`capacity` is also optional. If it is omitted, the edge is treated as having unlimited capacity.

`capacityBidPerUnit` is optional on a traffic type. If omitted, `speed` traffic defaults to a bid of `1` per constrained bottleneck edge and other traffic types default to `0`.

## Routing Rules

- Edge weights are shared across traffic types through `time` and `cost`.
- Edge capacity is optional, but when present it is shared across all traffic routed through that edge.
- Traffic types can place a per-unit bid on constrained edge capacity.
- A traffic type chooses how those edge values are scored.
- Producer nodes are any nodes with `production > 0` for that traffic.
- Consumer nodes are any nodes with `consumption > 0` for that traffic.
- Intermediate nodes must have `canTransship: true` for that same traffic.
- Local producer-to-consumer matching on the same node is handled before network routing.
- Capacity competition is resolved across all traffic types together. Higher bids win access to scarce edge capacity first, then the normal route score breaks ties.
- Bid premiums are added to the landed movement cost when the route is genuinely bottlenecked by finite edge capacity.

## Notes

- Omit `capacity` on an edge when you want it to behave as unlimited.
- The consumer-cost view shows local and imported movement costs separately, plus the blended movement cost seen at each consumer node.
- Routing is path-based and allocates producer supply to consumer demand using the best available routes under the chosen traffic preference and capacity bidding.
- `Auto Arrange` only updates node positions. It does not throw away in-memory edits to nodes, roles, or traffic types.

## Code Structure

- [MainWindow.xaml](src/MedWNetworkSim.App/MainWindow.xaml) defines the main shell: canvas, summary panes, simulation results, and the in-app editor grids.
- [MainWindowViewModel.cs](src/MedWNetworkSim.App/ViewModels/MainWindowViewModel.cs) is the application coordinator. It loads/saves networks, keeps editor selections in sync, and triggers simulation.
- [NodeEditorWindow.xaml](src/MedWNetworkSim.App/NodeEditorWindow.xaml) provides the dedicated dropdown-driven node editing workflow.
- [NetworkFileService.cs](src/MedWNetworkSim.App/Services/NetworkFileService.cs) normalizes and validates JSON data and applies automatic layout.
- [NetworkSimulationEngine.cs](src/MedWNetworkSim.App/Services/NetworkSimulationEngine.cs) performs routing, capacity competition, bid-cost calculation, and consumer-cost summarization.
- The `Models` folder contains the persisted JSON shape.
- The `ViewModels` folder contains the editable UI state and display helpers used by WPF binding.

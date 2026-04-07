# MedWNetworkSim

WPF network simulator for modelling multi-traffic movement across producer, consumer, and transhipment nodes.

## What It Does

- Loads a JSON network file.
- Draws the network on a draggable canvas.
- Auto-positions nodes when `x` and `y` are omitted from the input file.
- Includes an `Auto Arrange` action to regenerate node positions for the whole network.
- Lets each node participate in any number of traffic types.
- Allows the same node to produce, tranship, and consume different traffic types.
- Supports per-traffic routing priorities:
  - `speed`: minimise edge time
  - `cost`: minimise edge cost
  - `totalCost`: minimise `time + cost`
- Simulates routed movements from producers to consumers through valid transhipment nodes.
- Saves the current network, including updated node positions, back to JSON.

## Run It

```powershell
dotnet build MedWNetworkSim.slnx
dotnet run --project .\src\MedWNetworkSim.App\MedWNetworkSim.App.csproj
```

The app ships with a bundled sample file at [sample-network.json](/C:/Users/jdwil/source/repos/Codex/MedWNetworkSim/src/MedWNetworkSim.App/Samples/sample-network.json).

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
      "routingPreference": "speed"
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
      "isBidirectional": true
    }
  ]
}
```

`x` and `y` are optional. If they are omitted, the app generates an initial layout when the file is loaded, and those generated positions are then saved back out if you use `Save JSON...`.

## Routing Rules

- Edge weights are shared across traffic types through `time` and `cost`.
- A traffic type chooses how those edge values are scored.
- Producer nodes are any nodes with `production > 0` for that traffic.
- Consumer nodes are any nodes with `consumption > 0` for that traffic.
- Intermediate nodes must have `canTransship: true` for that same traffic.
- Local producer-to-consumer matching on the same node is handled before network routing.

## Notes

- The current simulator does not model edge capacities.
- Routing is path-based and allocates producer supply to consumer demand using the best available routes under the chosen traffic preference.

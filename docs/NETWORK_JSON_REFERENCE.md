# Network JSON Reference

This reference is for people creating or reviewing MedWNetworkSim network files by hand, in scripts, or in version control.

JSON is the full-fidelity save format for the WPF app. It preserves traffic definitions, nodes, edges, schedules, recipes, storage, route-choice settings, and canvas layout. Property names are camel case, and enum values are also camel case.

## Minimal Network

```json
{
  "name": "Tiny flour route",
  "description": "Farm ships wheat to a mill, and the mill produces flour.",
  "trafficTypes": [
    {
      "name": "Wheat",
      "routingPreference": "totalCost"
    },
    {
      "name": "Flour",
      "routingPreference": "totalCost"
    }
  ],
  "nodes": [
    {
      "id": "FARM",
      "name": "Farm",
      "x": 120,
      "y": 180,
      "trafficProfiles": [
        {
          "trafficType": "Wheat",
          "production": 20
        }
      ]
    },
    {
      "id": "MILL",
      "name": "Mill",
      "x": 420,
      "y": 180,
      "trafficProfiles": [
        {
          "trafficType": "Wheat",
          "consumption": 20
        },
        {
          "trafficType": "Flour",
          "production": 12,
          "inputRequirements": [
            {
              "trafficType": "Wheat",
              "quantityPerOutputUnit": 1
            }
          ]
        }
      ]
    },
    {
      "id": "BAKERY",
      "name": "Bakery",
      "x": 720,
      "y": 180,
      "trafficProfiles": [
        {
          "trafficType": "Flour",
          "consumption": 12
        }
      ]
    }
  ],
  "edges": [
    {
      "id": "FARM-MILL",
      "fromNodeId": "FARM",
      "toNodeId": "MILL",
      "time": 1,
      "cost": 1,
      "isBidirectional": true
    },
    {
      "id": "MILL-BAKERY",
      "fromNodeId": "MILL",
      "toNodeId": "BAKERY",
      "time": 1,
      "cost": 1,
      "isBidirectional": true
    }
  ]
}
```

## Top-Level Object

| Property | Type | Required | Meaning |
| --- | --- | --- | --- |
| `name` | string | No | Display name. Blank values normalize to `Untitled Network`. |
| `description` | string | No | Free-text note shown in the editor. |
| `timelineLoopLength` | integer or null | No | Repeating timeline cycle length. Values below `1` disable looping. |
| `defaultAllocationMode` | enum | No | Allocation mode used when the app creates new traffic types. |
| `simulationSeed` | integer | No | Deterministic seed for stochastic route-choice behavior. |
| `trafficTypes` | array | No | Declared traffic type definitions. Referenced traffic types are back-filled if omitted. |
| `timelineEvents` | array | No | Optional timeline overlays that multiply production, consumption, or route cost. |
| `nodes` | array | Yes | Places in the graph. Each node needs a unique non-empty `id`. |
| `edges` | array | No | Routes between nodes. Each route must reference existing node ids. |

## Traffic Types

Traffic types define how a commodity, traveler class, or flow category chooses routes.

```json
{
  "name": "Bread",
  "description": "Finished food from flour.",
  "routingPreference": "speed",
  "allocationMode": "greedyBestRoute",
  "routeChoiceModel": "stochasticUserResponsive",
  "flowSplitPolicy": "multiPath",
  "routeChoiceSettings": {
    "maxCandidateRoutes": 3,
    "priority": 1,
    "informationAccuracy": 1,
    "routeDiversity": 0.25,
    "congestionSensitivity": 1,
    "rerouteThreshold": 0.1,
    "stickiness": 0.3,
    "iterationCount": 4,
    "internalizeCongestion": true
  },
  "capacityBidPerUnit": 1.2
}
```

| Property | Type | Required | Meaning |
| --- | --- | --- | --- |
| `name` | string | Yes | Traffic type name. This is matched case-insensitively during normalization. |
| `description` | string | No | Human-readable notes. |
| `routingPreference` | enum | No | Route score preference: `speed`, `cost`, or `totalCost`. |
| `allocationMode` | enum | No | Legacy allocation behavior: `greedyBestRoute` or `proportionalBranchDemand`. |
| `routeChoiceModel` | enum | No | `systemOptimal` or `stochasticUserResponsive`. |
| `flowSplitPolicy` | enum | No | `singlePath` or `multiPath`. If omitted in older JSON, it is inferred from `allocationMode`. |
| `routeChoiceSettings` | object | No | Advanced route-choice tuning. Omit unless you need it. |
| `capacityBidPerUnit` | number or null | No | Non-negative priority bid for constrained edges or transhipment nodes. |

Route choice setting numbers must be finite and non-negative. `maxCandidateRoutes` and `iterationCount` must be at least `1`. `informationAccuracy` and `stickiness` are capped to `1` when normalized.

## Nodes

Nodes are places on the network canvas.

```json
{
  "id": "MILL",
  "name": "Mill",
  "shape": "building",
  "x": 420,
  "y": 180,
  "transhipmentCapacity": 40,
  "placeType": "Workshop",
  "loreDescription": "A water mill outside the market gate.",
  "controllingActor": "Miller's Guild",
  "tags": [ "food", "processing" ],
  "templateId": "mill",
  "trafficProfiles": []
}
```

| Property | Type | Required | Meaning |
| --- | --- | --- | --- |
| `id` | string | Yes | Stable unique id used by edges and timeline events. |
| `name` | string | No | Display name. Blank values normalize to the node id. |
| `shape` | enum | No | Canvas shape: `square`, `circle`, `person`, `car`, or `building`. |
| `x`, `y` | number or null | No | Canvas coordinates for the node center. Missing coordinates are auto-laid out. |
| `transhipmentCapacity` | number or null | No | Shared limit for traffic using this node as an intermediate stop. Omit for unlimited. |
| `placeType` | string or null | No | Optional worldbuilding category. |
| `loreDescription` | string or null | No | Optional creator-facing description. |
| `controllingActor` | string or null | No | Optional owner, faction, or operating actor. |
| `tags` | array of strings | No | Optional tags. Blank and duplicate tags are removed during normalization. |
| `templateId` | string or null | No | Optional id of the place template that created the node. |
| `trafficProfiles` | array | No | Per-traffic behavior at this node. |

## Node Traffic Profiles

A traffic profile says what one node does for one traffic type.

```json
{
  "trafficType": "Flour",
  "production": 12,
  "consumption": 0,
  "consumerPremiumPerUnit": 0,
  "canTransship": true,
  "productionWindows": [
    { "startPeriod": 1, "endPeriod": 6 },
    { "startPeriod": 9, "endPeriod": 12 }
  ],
  "inputRequirements": [
    {
      "trafficType": "Wheat",
      "quantityPerOutputUnit": 1
    }
  ],
  "isStore": true,
  "storeCapacity": 30
}
```

| Property | Type | Required | Meaning |
| --- | --- | --- | --- |
| `trafficType` | string | Yes | Traffic type this profile controls. |
| `production` | number | No | Non-negative supply amount for this traffic type. |
| `consumption` | number | No | Non-negative demand amount for this traffic type. |
| `consumerPremiumPerUnit` | number | No | Non-negative extra bid for this node consuming this traffic type. |
| `canTransship` | boolean | No | Whether the node can act as an intermediate stop for this traffic type. |
| `productionStartPeriod`, `productionEndPeriod` | integer or null | No | Legacy single production window. Prefer `productionWindows` for new files. |
| `consumptionStartPeriod`, `consumptionEndPeriod` | integer or null | No | Legacy single consumption window. Prefer `consumptionWindows` for new files. |
| `productionWindows` | array | No | Inclusive active windows for production. Empty means always active unless legacy fields are set. |
| `consumptionWindows` | array | No | Inclusive active windows for consumption or storage intake. Empty means always active unless legacy fields are set. |
| `inputRequirements` | array | No | Local precursor traffic required per unit of this profile's output. |
| `isStore` | boolean | No | Whether received traffic can be stored in timeline mode. |
| `storeCapacity` | number or null | No | Non-negative inventory cap for this traffic type at this node. Omit for unlimited. |

If a node contains duplicate profiles for the same `trafficType`, the loader collapses them into one profile. It sums `production` and `consumption`, keeps the maximum `consumerPremiumPerUnit`, ORs `canTransship` and `isStore`, merges windows and input requirements, and keeps the first non-null `storeCapacity`.

### Period Windows

Period windows are inclusive:

```json
{ "startPeriod": 2, "endPeriod": 5 }
```

`startPeriod` or `endPeriod` may be omitted or null for an open-ended range. Periods must be integers greater than or equal to `0`, and `startPeriod` cannot be greater than `endPeriod`.

With `timelineLoopLength`, windows are checked against the effective cycle period. For example, with loop length `12`, absolute period `13` behaves like effective period `1`.

### Recipe Input Requirements

Input requirements model local production recipes:

```json
{
  "trafficType": "Flour",
  "quantityPerOutputUnit": 1
}
```

The `trafficType` names a precursor that must be available at the same node, and `quantityPerOutputUnit` is the amount consumed for each unit of output. The value must be finite and greater than `0`.

For costing, produced traffic inherits the weighted-average landed unit cost of the local precursor supply at the producing node. Downstream transport of the produced traffic increases delivered cost downstream only; it does not rewrite the production-node source cost.

Static mode supports acyclic recipe dependencies. If traffic types form a cycle such as A requiring B while B requires A, static inherited recipe costing fails fast instead of guessing a fixed point.

## Edges

Edges connect nodes and define route cost, time, capacity, and direction.

```json
{
  "id": "MILL-BAKERY",
  "fromNodeId": "MILL",
  "toNodeId": "BAKERY",
  "time": 1,
  "cost": 2,
  "capacity": 25,
  "isBidirectional": true,
  "routeType": "Road",
  "accessNotes": "Open to traders",
  "seasonalRisk": "Muddy in winter",
  "tollNotes": "Bridge toll applies",
  "securityNotes": "Guarded by the town watch"
}
```

| Property | Type | Required | Meaning |
| --- | --- | --- | --- |
| `id` | string | No | Stable edge id. Blank ids are generated from endpoint ids. |
| `fromNodeId` | string | Yes | Source node id. |
| `toNodeId` | string | Yes | Target node id. |
| `time` | number | No | Non-negative route time score. |
| `cost` | number | No | Non-negative route cost score. |
| `capacity` | number or null | No | Non-negative shared route capacity. Omit for unlimited. |
| `isBidirectional` | boolean | No | If true, traffic can move both ways. If false, only `fromNodeId` to `toNodeId`. |
| `routeType` | string or null | No | Optional worldbuilding route category. |
| `accessNotes` | string or null | No | Optional access or permission notes. |
| `seasonalRisk` | string or null | No | Optional seasonal or hazard notes. |
| `tollNotes` | string or null | No | Optional toll or fee notes. |
| `securityNotes` | string or null | No | Optional safety or control notes. |

Every edge endpoint must reference an existing node id. `time`, `cost`, and `capacity` must be finite and non-negative.

## Timeline Events

Timeline events temporarily multiply existing production, consumption, or route cost inputs while active.

```json
{
  "id": "winter-roads",
  "name": "Winter roads",
  "startPeriod": 10,
  "endPeriod": 12,
  "effects": [
    {
      "effectType": "routeCostMultiplier",
      "edgeId": "MILL-BAKERY",
      "multiplier": 1.5
    },
    {
      "effectType": "productionMultiplier",
      "nodeId": "FARM",
      "trafficType": "Wheat",
      "multiplier": 0.5
    }
  ]
}
```

| Property | Type | Required | Meaning |
| --- | --- | --- | --- |
| `id` | string | No | Stable event id. Blank ids are generated as `event-1`, `event-2`, and so on. |
| `name` | string | No | Display name. Blank values normalize to the event id. |
| `startPeriod`, `endPeriod` | integer or null | No | Inclusive active period window. |
| `effects` | array | No | Multipliers applied while the event is active. |

Timeline event effects use:
- `productionMultiplier`: requires `nodeId`, `trafficType`, and `multiplier`.
- `consumptionMultiplier`: requires `nodeId`, `trafficType`, and `multiplier`.
- `routeCostMultiplier`: requires `edgeId` and `multiplier`.

Multipliers must be finite and non-negative. Production and consumption effects must reference an existing node profile. Route cost effects must reference an existing edge.

## Validation and Normalization Rules

The loader normalizes files before the app uses or saves them:

- Empty network names become `Untitled Network`.
- Node ids and edge endpoint ids are trimmed.
- Node ids and explicit edge ids must be unique.
- Blank edge ids are generated from endpoints.
- Optional text fields are trimmed; blank optional text becomes null.
- Missing node coordinates are auto-laid out.
- Missing traffic definitions are created for traffic types referenced by node profiles or input requirements.
- Duplicate traffic profiles on one node are merged.
- Duplicate input requirements for one output are summed by precursor traffic type.
- Legacy single schedule fields are mirrored into the first window when saving.
- `timelineLoopLength` values below `1` become null.

Use finite, non-negative numbers for quantities, premiums, costs, times, capacities, bids, route-choice tuning, and multipliers unless a property says it must be greater than `0`.

## Creator Checklist

Before sharing a JSON network:

- Every node has a stable, unique `id`.
- Every edge references existing `fromNodeId` and `toNodeId` values.
- Traffic type names are spelled consistently across `trafficTypes`, `trafficProfiles`, and `inputRequirements`.
- Time and cost use consistent units throughout the file.
- Capacities mean the same kind of quantity as the traffic flows they constrain.
- Recipe dependencies are acyclic if you need static inherited recipe costing.
- Stores have `isStore: true` and an optional `storeCapacity`.
- Timeline windows use the same period convention as `timelineLoopLength`.
- The file opens in the desktop app and saves cleanly after normalization.

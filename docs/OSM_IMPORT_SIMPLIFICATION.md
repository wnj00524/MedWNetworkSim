# OSM import simplification notes

The OSM import pipeline keeps only topology-significant raw nodes as simulation nodes.

- Endpoints and dead-ends are retained so terminals remain reachable.
- Junctions/branching points are retained so intersections preserve connectivity.
- Intermediate geometry points that only bend a road are collapsed away.

This keeps the simulation graph much smaller and avoids treating drawing geometry as route decision points.

## Distance and default metrics

When collapsing raw OSM runs between retained nodes, the simplifier accumulates distance across **every raw segment** in the run. That accumulated pre-collapse path length is the canonical imported distance.

Default metrics are then computed from this value:

- `Time = max(collapsedPathLengthKm, 0.1)`
- `Cost = max(collapsedPathLengthKm, 0.1)`

This intentionally avoids straight-line endpoint distance after simplification.

## Imported node naming

Retained nodes are named deterministically with this priority:

1. Direct node tags (`name`, `ref`, `junction:name`, `official_name`).
2. Derived junction/terminal labels from connected way names/refs.
3. Fallback synthetic label (`OSM {nodeId}`).

If available, `LoreDescription` is also populated with a concise imported context string.

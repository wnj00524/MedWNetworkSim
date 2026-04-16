# Perishability Pressure Map + Cause Attribution + Report Export

## Goal
Add a timeline-aware **perishability pressure map** that explains *why* pressure exists at each node/edge, surfaces that in tooltips, and exports it in timeline reports.

## Smallest Safe Change Sequence

1. **Add step-result telemetry structures in `TemporalNetworkSimulationEngine` (no UI/report wiring yet).**
   - Introduce immutable pressure/cause snapshot records.
   - Extend `TemporalSimulationStepResult` to carry those snapshots.
   - Populate with zero/default values first to keep behavior stable.

2. **Capture pressure causes inside `Advance` in `TemporalNetworkSimulationEngine`.**
   - Track causes at each branch that creates backlog, expires stock, blocks movement, or consumes scarce capacity.
   - Finalize into a per-period snapshot dictionary keyed by node and edge IDs.

3. **Thread telemetry into view models (`MainWindowViewModel`, `NodeViewModel`, `EdgeViewModel`).**
   - Extend existing `ApplyTimelineVisuals` methods (add overloads or optional args) to accept pressure summaries.
   - Keep existing color/flow logic unchanged initially; only append new tooltip/timeline text.

4. **Add timeline report export columns/sections in `ReportExportService`.**
   - Add pressure totals and top causes per period, then per node/edge rows.
   - Keep existing report sections intact and append new ones to minimize regression risk.

5. **Optional persistence toggle in `NetworkFileService` only if needed.**
   - If pressure map is always derived at runtime, no schema change is needed.
   - If user-configurable weights/thresholds are required, normalize/validate them here last.

## Exact Types/Methods to Edit

### `src/MedWNetworkSim.App/Services/TemporalNetworkSimulationEngine.cs`

- **Method:** `Advance(...)`
  - Create per-step mutable collectors:
    - `Dictionary<string, PressureAccumulator>` for nodes.
    - `Dictionary<string, PressureAccumulator>` for edges.
  - Record events at these points:
    - `ExpireNodeTraffic(...)` and `ExpireInFlightMovements(...)` for spoilage pressure.
    - `AddScheduledNodeChanges(...)` for unmet consumption/input demand pressure.
    - `BuildAvailableResourceCapacity(...)`, `PlanNewAllocations(...)`, and `TryMoveMovementToNextEdge(...)` for capacity/route blocking pressure.
  - Emit immutable snapshots into step result.

- **Method:** `PlanNewAllocations(...)`
  - Capture unmet demand/supply residue after allocation and map to causes.
  - Capture where local allocations were skipped (store/recipe exceptions) as explanatory causes.

- **Method:** `ApplyCommittedState(...)` and `CompleteArrival(...)`
  - Attribute pressure relief (negative deltas) when backlog is reduced or store receipts are fulfilled.

- **Method:** `ExpireInFlightMovements(...)`
  - Emit cause entries when movement shelf life reaches zero.

- **Type:** `TemporalSimulationStepResult`
  - Extend with:
    - `IReadOnlyDictionary<string, NodePressureSnapshot> NodePressureById`
    - `IReadOnlyDictionary<string, EdgePressureSnapshot> EdgePressureById`
    - `IReadOnlyList<PressureEvent> PressureEvents` (optional, for report detail).

- **Add new nested types:**
  - `enum PressureCauseKind`
  - `readonly record struct PressureEvent(...)`
  - `readonly record struct NodePressureSnapshot(...)`
  - `readonly record struct EdgePressureSnapshot(...)`
  - `sealed class PressureAccumulator`

### `src/MedWNetworkSim.App/ViewModels/MainWindowViewModel.cs`

- **Method:** `AdvanceTimeline()`
  - After `stepResult` is returned, pass pressure snapshots to view model apply methods.

- **Method:** `ApplyTimelineVisuals(TemporalSimulationStepResult stepResult)`
  - For each edge and node, fetch pressure snapshot from step result and pass through.
  - Keep current flow visuals unchanged in first pass; add pressure only as metadata/tooltips.

- **Method:** `ClearTimelineVisuals()`
  - Clear pressure-specific properties on nodes/edges.

- **Method:** `ExportTimelineReport(...)`
  - No signature change required if `SaveTimelineReport` reads from enriched `periodResults`.

### `src/MedWNetworkSim.App/ViewModels/NodeViewModel.cs`

- **Method:** `ApplyTimelineVisuals(...)`
  - Extend signature to accept node pressure snapshot (or add overload).
  - Store fields:
    - `double perishabilityPressureScore`
    - `Dictionary<PressureCauseKind,double> causeBreakdown`
    - `string pressureNarrative`

- **Method:** `ClearTimelineVisuals()`
  - Reset pressure fields.

- **Properties to extend:**
  - `TimelineSummaryLabel`
  - `FullTrafficSummary`
  - Add `PressureSummaryLabel` and include top causes for tooltip text.

### `src/MedWNetworkSim.App/ViewModels/EdgeViewModel.cs`

- **Method:** `ApplySimulationVisuals(...)` or add dedicated `ApplyTimelinePressure(...)`
  - Add fields for edge pressure score and top cause.

- **Property:** `EdgeToolTipText`
  - Append pressure line(s): score + top causes + blocked/expired quantities.

- **Method:** `ClearSimulationVisuals()`
  - Also clear timeline pressure fields if shared method is used.

### `src/MedWNetworkSim.App/Services/ReportExportService.cs`

- **Method:** `BuildTimelineHtmlReport(...)`
  - Add per-period pressure summary table.
  - Add node/edge pressure tables with score and cause attribution columns.

- **Method:** `BuildTimelineCsvReport(...)`
  - Append sections:
    - `Timeline Pressure Overview`
    - `Node Pressure by Period`
    - `Edge Pressure by Period`
    - optional `Pressure Events`.

### `src/MedWNetworkSim.App/Services/NetworkFileService.cs`

- **Only if persistence/config needed:**
  - `NormalizeAndValidate(...)`
  - `LoadJson(...)` explicit-policy read path as needed.
  - Validate pressure weighting config ranges (non-negative finite).

## Proposed Data Structures

Use additive, immutable-at-boundary structures.

```csharp
public enum PressureCauseKind
{
    DemandBacklog,
    InputShortage,
    StoreCapacitySaturation,
    EdgeCapacitySaturation,
    TranshipmentCapacitySaturation,
    RouteUnavailable,
    PerishedInNodeInventory,
    PerishedInTransit,
    TimelineShock
}

public readonly record struct PressureEvent(
    int Period,
    string EntityId,
    bool IsEdge,
    string TrafficType,
    PressureCauseKind Cause,
    double Quantity,
    double WeightedImpact,
    string Detail);

public readonly record struct NodePressureSnapshot(
    double Score,
    double BacklogQuantity,
    double ExpiredQuantity,
    IReadOnlyDictionary<PressureCauseKind, double> CauseWeights,
    string TopCause);

public readonly record struct EdgePressureSnapshot(
    double Score,
    double BlockedQuantity,
    double ExpiredInTransitQuantity,
    double Utilization,
    IReadOnlyDictionary<PressureCauseKind, double> CauseWeights,
    string TopCause);
```

`PressureAccumulator` should be mutable only within one `Advance` call, with helper methods:
- `Add(cause, quantity, weight = 1.0, detail = null)`
- `ApplyRelief(cause, quantity)`
- `ToNodeSnapshot()` / `ToEdgeSnapshot()`.

### Suggested score formula (deterministic and cheap)

Use weighted impact to avoid introducing a separate normalization pass:

```csharp
weightedImpact = quantity * weight;
score = Sum(weightedImpact by cause);
```

Recommended v1 weights:

| Cause | Weight |
|---|---:|
| `DemandBacklog` | 1.00 |
| `InputShortage` | 1.10 |
| `StoreCapacitySaturation` | 0.90 |
| `EdgeCapacitySaturation` | 1.00 |
| `TranshipmentCapacitySaturation` | 1.00 |
| `RouteUnavailable` | 1.20 |
| `PerishedInNodeInventory` | 1.40 |
| `PerishedInTransit` | 1.60 |
| `TimelineShock` | 0.75 |

This keeps pressure scores explainable and directly proportional to affected quantity.

## Concrete capture points (v1 instrumentation map)

Use these exact hook points in `TemporalNetworkSimulationEngine`:

1. `ExpireNodeTraffic(nodeStates)`:
   - Add node `PerishedInNodeInventory` pressure when available/store batches expire between periods.
2. `ExpireInFlightMovements(...)`:
   - Add edge (current edge) and arrival-node `PerishedInTransit` pressure when shelf life hits zero.
3. `AddScheduledNodeChanges(...)`:
   - When `DemandBacklog` increases from unmet consumption, add `DemandBacklog`.
   - When recipe input demand is synthesized (`AddImplicitRecipeDemand`) and remains unmet, add `InputShortage`.
4. `BuildAvailableResourceCapacity(...)` + `PlanNewAllocations(...)`:
   - For any demand remaining after allocation, attribute:
     - `EdgeCapacitySaturation` if candidate routes exist but constrained by edge cap.
     - `TranshipmentCapacitySaturation` if constrained by node transhipment cap.
     - `RouteUnavailable` if no viable route exists.
5. `ApplyCommittedState(...)` / `CompleteArrival(...)`:
   - Apply relief (negative deltas) as backlog is reduced/reservations fulfilled, then clamp accumulator floors at zero.
6. `ApplyTimelineEventOverlay(...)`:
   - If an active event changes production/consumption/cost multipliers, emit `TimelineShock` notes for affected entities.

## Exact signature changes (to avoid churn)

Prefer additive/optional parameters so existing callers compile early.

- `NodeViewModel`
  - Keep existing method and add overload:
    - `ApplyTimelineVisuals(double availableSupply, double demandBacklog, double storeInventory, NodePressureSnapshot? pressure)`
- `EdgeViewModel`
  - Add method (do not widen every `ApplySimulationVisuals` call immediately):
    - `ApplyTimelinePressure(EdgePressureSnapshot? pressure)`
- `MainWindowViewModel`
  - In `ApplyTimelineVisuals(stepResult)`, call:
    - `edge.ApplyTimelinePressure(pressureOrNull);`
    - `node.ApplyTimelineVisuals(..., pressureOrNull);`
- `TemporalSimulationStepResult`
  - Add new properties at the end of the record constructor to minimize positional breakage:
    - `IReadOnlyDictionary<string, NodePressureSnapshot> NodePressureById`
    - `IReadOnlyDictionary<string, EdgePressureSnapshot> EdgePressureById`
    - `IReadOnlyList<PressureEvent> PressureEvents`

## Export schema additions (append-only)

`ReportExportService` append-only plan:

- HTML (`BuildTimelineHtmlReport`)
  1. Add `<h3>Pressure Overview</h3>` inside each period block.
  2. Add `<h3>Node Pressure</h3>` table with columns:
     - Node, Score, Backlog Qty, Expired Qty, Top Cause, Cause Breakdown
  3. Add `<h3>Edge Pressure</h3>` table with columns:
     - Edge, Score, Blocked Qty, Expired In Transit, Utilization, Top Cause
- CSV (`BuildTimelineCsvReport`)
  - Append sections:
    - `Timeline Pressure Overview`
    - `Node Pressure by Period`
    - `Edge Pressure by Period`
    - `Pressure Events` (optional, controllable by count threshold)

## Network file implications (`NetworkFileService`)

No schema expansion is required for v1 runtime-derived pressure.

Only add `NetworkFileService` changes if v2 introduces user-configurable weighting:
- Optional object: `pressureSettings` with cause weights and enable flag.
- Validation rules:
  - finite, non-negative doubles
  - unknown causes rejected with actionable error message
  - omitted settings use built-in defaults from engine constants

## Threading Path (Simulation -> VM -> Tooltip/Report)

1. **Simulation layer (`TemporalNetworkSimulationEngine`)**
   - Build and fill accumulators during `Advance`.
   - Freeze to `NodePressureSnapshot` / `EdgePressureSnapshot` when creating `TemporalSimulationStepResult`.

2. **View-model orchestration (`MainWindowViewModel`)**
   - In `ApplyTimelineVisuals(stepResult)`, map each `NodeViewModel.Id`/`EdgeViewModel.Id` to snapshot dictionaries.
   - Call node/edge apply methods with both existing flow/state data and new pressure data.

3. **Node/edge presentation (`NodeViewModel`/`EdgeViewModel`)**
   - Maintain derived labels (`PressureSummaryLabel`) with stable formatting.
   - Append to existing tooltip strings (`FullTrafficSummary`, `EdgeToolTipText`) to avoid XAML binding churn.

4. **Report export (`ReportExportService`)**
   - Read pressure snapshots from each period result.
   - Emit aggregate period score + per-entity detail rows.

## Compile-Risk Areas Before Coding

1. **`TemporalSimulationStepResult` signature expansion risk**
   - It is constructed in-engine and consumed in `MainWindowViewModel` and `ReportExportService`; constructor parameter order mistakes are likely.

2. **Method signature drift in VM apply methods**
   - `NodeViewModel.ApplyTimelineVisuals(...)` and `EdgeViewModel.ApplySimulationVisuals(...)` are called from multiple places; adding required parameters can cascade compile errors.

3. **Name collisions with existing nested records**
   - `TemporalNetworkSimulationEngine` already has many nested records; keep new type names explicit (`NodePressureSnapshot`, not `PressureSnapshot`).

4. **Case-insensitive ID dictionary consistency**
   - Existing code relies on `StringComparer.OrdinalIgnoreCase`; pressure dictionaries should do the same to avoid silent misses in tooltip/report lookups.

5. **Tooltip property notification churn**
   - `EdgeToolTipText` and `FullTrafficSummary` are derived strings; forgetting `OnPropertyChanged(...)` for new pressure fields leads to stale UI.

6. **CSV/HTML schema append strategy**
   - Existing reports are consumed by users/scripts; append new sections/columns rather than reordering existing ones to minimize downstream breakage.

## Minimal Acceptance Checks (after implementation)

1. Build succeeds.
2. One timeline step with perishable traffic shows non-empty pressure tooltip text on at least one node.
3. A forced bottleneck (low edge capacity) produces edge pressure cause attribution.
4. Timeline CSV/HTML include new pressure sections with deterministic headers.
5. Reset timeline clears pressure visuals/tooltips.

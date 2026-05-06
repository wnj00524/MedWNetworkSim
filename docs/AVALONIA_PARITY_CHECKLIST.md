# Avalonia Functional Parity Checklist

This note tracks user-facing workflows that historically lived in the WPF path and their current Avalonia status.

## Workflow parity map

| WPF feature/workflow | Avalonia status | Notes |
|---|---|---|
| Open network JSON (file picker) | **Migrated** | Canvas-first floating command bar Open command uses Avalonia storage picker and loads via `WorkspaceViewModel.OpenNetwork`. |
| Save network JSON (file picker) | **Migrated** | Canvas-first floating command bar Save command uses Avalonia storage picker and saves via `WorkspaceViewModel.SaveNetwork`. |
| Import GraphML | **Migrated** | Floating command bar Import GraphML command opens the picker and imports with `WorkspaceViewModel.ImportGraphMl`. |
| Export GraphML | **Migrated** | Floating command bar Export GraphML command saves GraphML via `WorkspaceViewModel.ExportGraphMl`. |
| Node editor dialog | **Migrated (drawer + full editor)** | Selecting or double-clicking a node opens the right context drawer; full node editing remains available from the drawer. |
| Edge editor dialog | **Migrated (dedicated route workspace)** | Route selection shows quick edits in the context drawer; double-click/Edit Route opens the route workspace for full permissions and capacity editing. |
| Traffic type editor window | **Migrated** | Traffic Types rail action opens the dedicated traffic workspace with summary, routing, economics, default access, and validation tabs. |
| Network properties window | **Migrated** | Network Details remains available from the floating command bar and persists name/notes/loop length. |
| Bulk apply traffic role / multi-selection edit | **Migrated** | Multi-selection context drawer supports shared place/capacity edits and bulk traffic role apply. |
| Current report export | **Migrated** | Reports and Analytics workspaces expose current report export instead of keeping report controls on the Network canvas. |
| Timeline report export | **Migrated** | Reports and Analytics workspaces expose timeline CSV export. |
| OSM import flow + options dialog | **Migrated** | OSM Import is a dedicated workspace with map canvas and collapsible options drawer; OSM controls are no longer permanent Network clutter. |
| Agents | **Migrated** | Agents now live in a dedicated workspace with actor cards, status/budget summary, editor drawer, permissions, and decision logs. |
| Facility planning | **Migrated** | Facility Planning is a dedicated coverage workspace with canvas integration, coverage cards, uncovered nodes, overlap, and facility comparison. |
| Visual analytics | **Migrated** | Analytics is a cockpit with KPI strip, interactive Sankey, top issues, timeline/pressure surface, node ranking, route heatmap, filters, and export actions. |
| Canvas render fallback | **Migrated** | Graph canvas still reports actionable render failures and shows the fallback panel instead of failing silently. |

## Accessibility and keyboard behavior in Avalonia shell

- The Network workspace is canvas-first: the left rail is icon-only workspace navigation, and common commands live in a small floating icon command bar.
- Icon-only command buttons use `PathIcon` geometry, tooltips, automation names, disabled state styling, and active/selected classes where relevant.
- Inspector editing fields provide labels and inline validation message text inside a collapsible right drawer.
- Canvas remains focusable and keeps keyboard interactions, including Escape returning to Select mode when no higher-priority drawer/workspace consumes it.
- Right-click context menus remain on the canvas.
- Fallback/error canvas panel remains visible on render failures.
- Visual analytics avoids color-only meaning by pairing color with labels, legends, KPI titles, and tooltip text.

## Where major Avalonia workflows now live

- Shell layout, rail, floating command bar, canvas workspace, drawers, and dedicated workspaces: `src/MedWNetworkSim.UI/AvaloniaShell.cs`
- Reusable UI chrome and analytics surface: `src/MedWNetworkSim.UI/WorkspaceChromeControls.cs`
- Core workspace commands and editing behavior: `src/MedWNetworkSim.Presentation/WorkspacePresentation.cs`
- Sankey renderer: `src/MedWNetworkSim.Rendering/VisualAnalytics/Sankey/SankeyRendering.cs`
- Pie renderer: `src/MedWNetworkSim.Rendering/PieChartRendering.cs`

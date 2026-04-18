# Avalonia Functional Parity Checklist

This note tracks user-facing workflows that historically lived in the WPF path and their current Avalonia status.

## Workflow parity map

| WPF feature/workflow | Avalonia status | Notes |
|---|---|---|
| Open network JSON (file picker) | **Migrated** | Top command bar **Open** command now uses Avalonia storage picker and loads via `WorkspaceViewModel.OpenNetwork`. |
| Save network JSON (file picker) | **Migrated** | Top command bar **Save** command now uses Avalonia storage picker and saves via `WorkspaceViewModel.SaveNetwork`. |
| Import GraphML | **Migrated** | Top command bar **Import** command opens GraphML picker and imports with `WorkspaceViewModel.ImportGraphMl`. |
| Export GraphML | **Migrated** | Top command bar **Export** command saves GraphML via `WorkspaceViewModel.ExportGraphMl`. |
| Node editor dialog | **Migrated (inspector form)** | Right inspector supports editable node name/type/capacity and traffic profile production/consumption for selected node. |
| Edge editor dialog | **Migrated (inspector form)** | Right inspector supports editable route type/time/cost/capacity for selected edge. |
| Traffic type editor window | **Partial** | Inspector now supports add/remove traffic types; richer per-traffic tuning controls remain a deliberate gap. |
| Network properties window | **Migrated (inspector form)** | When nothing is selected, inspector edits network name/description/timeline length. |
| Bulk apply traffic role / multi-selection edit | **Partial** | Multi-selection inspector supports bulk place-type updates; richer role/capacity bulk operations remain a deliberate gap. |
| Current report export | **Migrated** | Bottom strip Reports tab includes current report export command. |
| Timeline report export | **Migrated** | Bottom strip Reports tab includes timeline report export command. |
| OSM import flow + options dialog | **Missing (deliberate gap)** | Not yet exposed in Avalonia shell; requires additional options UI and progress surface. |

## Accessibility and keyboard behavior in Avalonia shell

- Interactive command surfaces are real controls (buttons, tabs, text fields), so they participate in keyboard focus and tab navigation.
- Inspector editing fields provide clear labels and inline validation message text.
- Canvas remains focusable and keeps keyboard interactions (selection/navigation shortcuts) via `GraphCanvasControl` keyboard handling.
- Fallback/error canvas panel remains visible on render failures (no silent failure).
- Status and helper text avoids color-only meaning by including explicit wording.

## Where major Avalonia workflows now live

- Shell layout, command bar, tool rail, canvas host, inspector, and bottom strip: `src/MedWNetworkSim.UI/AvaloniaShell.cs`
- Core workspace commands and editing behavior (open/save/import/export, inspector apply, simulation, report export): `src/MedWNetworkSim.Presentation/WorkspacePresentation.cs`


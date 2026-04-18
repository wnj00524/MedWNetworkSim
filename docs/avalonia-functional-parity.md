# Avalonia / WPF Functional Parity Audit

## Current Parity Status

| Workflow | Status | Notes |
| --- | --- | --- |
| Open network JSON | Working | Available in the main shell. Labeling and placement need normalization. |
| Save network JSON | Working | Quick save and save-as exist. Terminology is inconsistent across the shell. |
| Import GraphML | Working | Implemented through `GraphMlTransferWindow`, but not surfaced as a first-class top-level workflow. |
| Export GraphML | Working | Implemented through `GraphMlTransferWindow`, but naming and discoverability need work. |
| Node editor | Working | `NodeEditorWindow` exists and is feature-rich. Inspector does not yet act as a contextual first pass. |
| Edge editor | Working | `EdgeEditorWindow` exists and is feature-rich. Inspector parity is incomplete. |
| Traffic type editor | Partial | Dedicated editor exists, but grouping, validation, and shell entry points need cleanup. |
| Network properties | Working | Dedicated window exists and is usable. Inspector fallback for no selection is incomplete. |
| Bulk multi-selection editing | Partial | Current batch editor applies one traffic role to all nodes. Safe scoped multi-edit is not complete. |
| Current report export | Working | `ReportExportWindow` supports current exports. Wording and consistency need polish. |
| Timeline report export | Working | Implemented in `ReportExportWindow`. Validation and explanatory text can improve. |
| OSM import flow + options | Partial | Main shell launches file picker and options dialog, then imports. Busy/error/success feedback needs polish and stronger UX. |
| Keyboard accessibility | Partial | Canvas keyboard support is strong. Dialog and inspector consistency still need work. |
| Visible focus states | Partial | Some global focus styling exists, but not every control and helper surface uses it consistently. |
| Clear validation and error messaging | Partial | Many flows rely on modal exceptions. Field-local validation is limited. |
| Unified theme across shell, dialogs, inspector, drawer, and helper states | Partial | Shared resources exist in `src/MedWNetworkSim.App/App.xaml`, but several windows still use one-off sizing, wording, and visual patterns. |

## Files Expected To Be Touched

- `docs/avalonia-functional-parity.md`
- `src/MedWNetworkSim.App/App.xaml`
- `src/MedWNetworkSim.App/MainWindow.xaml`
- `src/MedWNetworkSim.App/MainWindow.xaml.cs`
- `src/MedWNetworkSim.App/InspectorPanelControl.xaml`
- `src/MedWNetworkSim.App/InspectorPanelControl.xaml.cs`
- `src/MedWNetworkSim.App/OsmImportOptionsWindow.xaml`
- `src/MedWNetworkSim.App/OsmImportOptionsWindow.xaml.cs`
- `src/MedWNetworkSim.App/TrafficTypeEditorWindow.xaml`
- `src/MedWNetworkSim.App/TrafficTypeEditorWindow.xaml.cs`
- `src/MedWNetworkSim.App/BulkApplyTrafficRoleWindow.xaml`
- `src/MedWNetworkSim.App/BulkApplyTrafficRoleWindow.xaml.cs`
- `src/MedWNetworkSim.App/ReportExportWindow.xaml`
- `src/MedWNetworkSim.App/ReportExportWindow.xaml.cs`
- `src/MedWNetworkSim.App/ReportsDrawerControl.xaml`
- `src/MedWNetworkSim.App/ViewModels/MainWindowViewModel.cs`
- `src/MedWNetworkSim.App/ViewModels/InspectorPanelViewModel.cs`
- `src/MedWNetworkSim.App/ViewModels/TrafficTypeEditorViewModel.cs`
- `src/MedWNetworkSim.App/ViewModels/BulkApplyTrafficRoleWindowViewModel.cs`
- `src/MedWNetworkSim.App/ViewModels/ReportExportWindowViewModel.cs`
- Supporting view models or service files only where required for safe wiring and validation.

## Manual Test Checklist

### Shell

- Open a JSON network from the main shell.
- Save the current network to JSON.
- Import GraphML from a visible shell entry point.
- Export GraphML from a visible shell entry point.
- Confirm top-level command labels are direct and consistent.
- Confirm helper text is concise and actionable.

### Editing

- Select no item and confirm the inspector presents network properties.
- Select one node and confirm core edits are available and saved.
- Select one edge and confirm core edits are available and saved.
- Select multiple items and confirm bulk edit entry points are discoverable.
- Open the traffic type editor from the shell and inspector.
- Open the bulk edit workflow from the shell and inspector.

### Reports and Import

- Export the current report to each supported format.
- Export the timeline report and validate period handling.
- Complete the OSM import flow from launch to success feedback.
- Trigger an OSM import error and confirm the message is actionable.

### Accessibility

- Tab through the shell, inspector, dialogs, and reports drawer.
- Confirm all actionable controls show a visible focus state.
- Confirm validation text appears near the relevant field.
- Confirm no state relies on color alone.
- Confirm canvas keyboard shortcuts still work.

## Deferred Items

- None yet. Add only intentionally deferred work with a reason once implementation reveals a safe deferral.

# Avalonia / WPF Functional Parity Audit

## Current Parity Status

| Workflow | Status | Notes |
| --- | --- | --- |
| Open network JSON | Complete | Exposed from the top-level shell with direct wording. |
| Save network JSON | Complete | Save path is clear from the top-level shell and preserves existing quick-save behavior. |
| Import GraphML | Complete | Exposed as a top-level shell action and clarified in the shared GraphML surface. |
| Export GraphML | Complete | Exposed as a top-level shell action and clarified in the shared GraphML surface. |
| Node editor | Complete | Dedicated editor remains available, and the inspector now supports quick contextual edits plus a launch point to the full editor. |
| Edge editor | Complete | Dedicated editor remains available, and the inspector now supports quick contextual edits plus a launch point to the full editor. |
| Traffic type editor | Complete | Promoted as the advanced traffic workflow with grouped settings, helper text, and local validation. |
| Network properties | Complete | Available from the shell and as the inspector fallback when nothing is selected. |
| Bulk multi-selection editing | Complete | Multi-place selection now opens a scoped batch editor with shared place type, traffic role/profile, and transhipment capacity edits. |
| Current report export | Complete | Export surface now uses an explicit report type and destination flow. |
| Timeline report export | Complete | Export surface now uses an explicit report type and destination flow, including period control. |
| OSM import flow + options | Complete | Shell action, file picker, themed options, live progress, and success/failure feedback are all connected. |
| Keyboard accessibility | Complete | Canvas keyboard behavior remains in place, and the shell/dialog pass added broader focus coverage and clearer keyboard-facing wording. |
| Visible focus states | Complete | Shared focus styling now covers more controls across the shell, dialogs, and menus. |
| Clear validation and error messaging | Complete | Inspector, traffic, bulk edit, export, and import flows now show more proximal or explicit guidance. |
| Unified theme across shell, dialogs, inspector, drawer, and helper states | Complete | Shared WPF tokens now drive the shell, inspector, reports, GraphML, OSM, and traffic workflows more consistently. |

## Files Touched

- `docs/avalonia-functional-parity.md`
- `src/MedWNetworkSim.App/App.xaml`
- `src/MedWNetworkSim.App/GraphMlTransferWindow.xaml`
- `src/MedWNetworkSim.App/MainWindow.xaml`
- `src/MedWNetworkSim.App/MainWindow.xaml.cs`
- `src/MedWNetworkSim.App/InspectorPanelControl.xaml`
- `src/MedWNetworkSim.App/InspectorPanelControl.xaml.cs`
- `src/MedWNetworkSim.App/OsmImportOptionsWindow.xaml`
- `src/MedWNetworkSim.App/OsmImportOptionsWindow.xaml.cs`
- `src/MedWNetworkSim.App/TrafficTypeEditorWindow.xaml`
- `src/MedWNetworkSim.App/BulkApplyTrafficRoleWindow.xaml`
- `src/MedWNetworkSim.App/BulkApplyTrafficRoleWindow.xaml.cs`
- `src/MedWNetworkSim.App/Models/BulkApplyTrafficRoleOptions.cs`
- `src/MedWNetworkSim.App/Models/ReportExportKind.cs`
- `src/MedWNetworkSim.App/ReportExportWindow.xaml`
- `src/MedWNetworkSim.App/ReportExportWindow.xaml.cs`
- `src/MedWNetworkSim.App/ReportsDrawerControl.xaml`
- `src/MedWNetworkSim.App/ViewModels/MainWindowViewModel.cs`
- `src/MedWNetworkSim.App/ViewModels/TrafficTypeDefinitionEditorViewModel.cs`
- `src/MedWNetworkSim.App/ViewModels/BulkApplyTrafficRoleWindowViewModel.cs`
- `src/MedWNetworkSim.App/ViewModels/ReportExportWindowViewModel.cs`

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
- Confirm `Alt+Click` or `Ctrl+Space` can build a multi-place selection for bulk editing.

## Deferred Items

- Avalonia-specific parity work remains intentionally limited because the active parity surfaces named in the checklist live in `src/MedWNetworkSim.App`. The Avalonia host project was kept buildable, but this pass did not create a second UI implementation for the same workflows.

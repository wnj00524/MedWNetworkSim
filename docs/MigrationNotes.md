# Avalonia Migration Notes

## Direction

The primary UI path now moves toward `MedWNetworkSim.App.Avalonia` with:

- Avalonia for shell composition, inspector, command surfaces, simulation strip, and status feedback
- a render-driven `GraphCanvasControl` in `MedWNetworkSim.UI`
- explicit scene, viewport, and render-pass types in `MedWNetworkSim.Rendering`
- isolated hit-testing and interaction logic in `MedWNetworkSim.Interaction`

## Reuse from the WPF app

The migration keeps the current .NET simulation/domain stack alive by linking the existing neutral models and services into `MedWNetworkSim.Presentation`:

- `Models/*`
- `Services/*` except `AppThemeManager`

That means the current network normalization, static simulation, temporal simulation, and report-oriented summaries remain reusable while the UI shell is replaced.

## What is still legacy

The current WPF project remains in the repository as a reference path for:

- file-picker and dialog flows
- some richer editor sub-dialogs
- older view-models that still mix WPF brush/visibility concerns with editor state

Those pieces are no longer the intended long-term UI host.

## Checkpoints

1. Phase 1: Avalonia shell project created.
2. Phase 2: custom canvas control and explicit viewport/scene/renderer added.
3. Phase 3: interaction controller added for selection, pan, zoom, drag, marquee, and connect.
4. Phase 4: premium dark workstation shell and semantic zoom renderer added.
5. Phase 5: animated flow overlay and reduced-motion toggle added.
6. Phase 6: inspector and simulation/report strip moved into the Avalonia shell.
7. Phase 7: optional depth layer kept behind scene/render flags.

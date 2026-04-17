# Avalonia Black Screen Fix

## Root Cause

The startup black screen was caused by a combination of issues:

- Avalonia bootstrap was incomplete. The app had no `App.axaml`, no Fluent theme registration, and `App.Initialize()` did not load XAML resources.
- Avalonia package versions were inconsistent between the app and UI projects.
- The shell used a very dark palette, so any startup failure looked like a black screen.
- `GraphCanvasControl` could return from `Render()` without drawing anything when the view model was missing, the control had zero bounds, or Skia bitmap setup failed.

## What Changed

- Added `App.axaml` and loaded `FluentTheme` during startup.
- Updated `App.Initialize()` to call `AvaloniaXamlLoader.Load(this)` and kept standard desktop startup in `OnFrameworkInitializationCompleted()`.
- Normalized Avalonia package versions across the Avalonia app and UI projects.
- Added high-contrast shell smoke-test visuals so window layout is obviously visible during startup.
- Wrapped shell and graph startup in visible fallback surfaces instead of allowing a blank region.
- Hardened `GraphCanvasControl` so it always paints a background and placeholder or error message when live rendering is unavailable.
- Added render-path tracing for bounds, render calls, bitmap creation, and missing view-model state.
- Added sizing guards so the canvas host cannot collapse to zero.
- Added an environment-variable-based isolation mode to swap the live canvas for a diagnostic panel when needed.

## Classification

- Bootstrap/theme initialization: fixed
- Layout sizing resilience: fixed
- Shell startup fallback: fixed
- Skia/custom render fallback and diagnostics: fixed

The result should now be a visibly rendered shell on launch, with the graph area either showing live content or a clear fallback message instead of a silent black screen.

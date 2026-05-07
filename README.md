# MedWNetworkSim

MedWNetworkSim is a .NET 8 network simulation and analysis tool for modelling constrained movement through graph-based systems. It focuses on traffic or resource types moving between nodes over edges/routes while accounting for capacity, cost, timing, routing, storage, production, consumption, policy, and scenario constraints.

The current solution is centred on an Avalonia desktop application. Shared presentation, interaction, rendering, and UI libraries support the Avalonia shell, while legacy model, import, service, visual analytics, insight, agent, and geospatial code remains under `src/MedWNetworkSim.App` and is linked into the presentation project.

## Current status

The repository currently targets .NET 8 and uses Avalonia 11.3.12 for the desktop UI. The primary application project is `src/MedWNetworkSim.App.Avalonia`, which is configured as a Windows desktop executable, version `2.0.3`, published as a self-contained `win-x64` single-file app.

The active solution file is `MedWNetworkSim.slnx`. It includes the Avalonia app plus shared projects for UI, presentation, interaction, rendering, and verification. The older `src/MedWNetworkSim.App` code is not listed as an active solution project, but parts of it are compiled into the presentation layer through linked source files.

## What the tool is useful for

MedWNetworkSim is intended for exploring graph-based flow problems such as logistics, supply chains, infrastructure networks, resource distribution, service-flow scenarios, and prototype network designs.

It can be used to inspect or reason about:

- supply and demand imbalance
- congestion and bottlenecks
- route choice behaviour
- capacity constraints
- route and node utilisation
- unmet demand and backlog
- multi-period timeline dynamics
- scenario events such as failures, closures, demand spikes, production/consumption changes, and route cost changes
- policy-aware routing and blocked flows
- economic summaries, agent activity, and issue explanations

## Repository layout

```text
.
├── MedWNetworkSim.slnx
├── Directory.Build.props
├── Directory.Build.targets
├── docs/
├── examples/
├── src/
│   ├── MedWNetworkSim.App.Avalonia/
│   ├── MedWNetworkSim.Avalonia.Verification/
│   ├── MedWNetworkSim.App.Verification/
│   ├── MedWNetworkSim.App/
│   ├── MedWNetworkSim.Interaction/
│   ├── MedWNetworkSim.Presentation/
│   ├── MedWNetworkSim.Rendering/
│   └── MedWNetworkSim.UI/
└── tests/
    └── MedWNetworkSim.Tests/
```

## Main projects

| Project | Role |
| --- | --- |
| `src/MedWNetworkSim.App.Avalonia` | Primary Avalonia desktop application entry point. Starts the app with Avalonia classic desktop lifetime, shows a splash window, configures dependency injection, and opens the main shell. |
| `src/MedWNetworkSim.UI` | Avalonia UI layer. References rendering, interaction, and presentation projects and contains shell, views, controls, dashboard theme, and dialog code. |
| `src/MedWNetworkSim.Presentation` | Presentation/view-model layer. References rendering and interaction projects and links legacy model, import, service, visual analytics, insights, agent, and geo code from `src/MedWNetworkSim.App`. |
| `src/MedWNetworkSim.Rendering` | Rendering library targeting .NET 8 with SkiaSharp. |
| `src/MedWNetworkSim.Interaction` | Interaction library targeting .NET 8 and referencing rendering. |
| `src/MedWNetworkSim.Avalonia.Verification` | Console-style verification project that references the Avalonia UI project. |
| `tests/MedWNetworkSim.Tests` | xUnit test project referencing the presentation and UI projects. |

## Technology stack

- .NET 8
- C# with nullable reference types and implicit usings enabled
- Avalonia 11.3.12
- Avalonia Fluent theme and Avalonia DataGrid
- SkiaSharp 3.119.1
- OsmSharp 6.2.0
- Microsoft.Extensions.DependencyInjection
- xUnit for tests

## Building

Install the .NET 8 SDK, then build the active solution from the repository root:

```bash
dotnet restore MedWNetworkSim.slnx
dotnet build MedWNetworkSim.slnx
```

## Running the desktop application

From the repository root:

```bash
dotnet run --project src/MedWNetworkSim.App.Avalonia/MedWNetworkSim.App.Avalonia.csproj
```

The app uses Avalonia platform detection and starts with a classic desktop lifetime. On startup it attempts to show a splash screen, builds application services, and opens the main shell window.

## Publishing

The Avalonia app project is configured for a self-contained Windows x64 single-file publish. To publish using the project defaults:

```bash
dotnet publish src/MedWNetworkSim.App.Avalonia/MedWNetworkSim.App.Avalonia.csproj -c Release
```

The project file currently sets:

```text
RuntimeIdentifier = win-x64
SelfContained = true
PublishSingleFile = true
IncludeNativeLibrariesForSelfExtract = true
PublishTrimmed = false
Version = 2.0.3
```

## Testing

Run the xUnit tests from the repository root:

```bash
dotnet test tests/MedWNetworkSim.Tests/MedWNetworkSim.Tests.csproj
```

Or run tests as part of the solution build workflow:

```bash
dotnet test MedWNetworkSim.slnx
```

## Notes for contributors

- Treat `src/MedWNetworkSim.App.Avalonia` as the current application entry point.
- Treat `MedWNetworkSim.slnx` as the active solution file.
- Be careful when editing `src/MedWNetworkSim.App`: although it is not listed as a project in the active solution, source files from that directory are linked into `MedWNetworkSim.Presentation` and can affect the Avalonia application.
- Keep shared UI behaviour in `MedWNetworkSim.UI`, presentation state and commands in `MedWNetworkSim.Presentation`, interaction behaviours in `MedWNetworkSim.Interaction`, and drawing/rendering concerns in `MedWNetworkSim.Rendering`.
- The app currently suppresses NuGet audit warnings `NU1903` and `NU1904` in several projects. Review package versions deliberately when changing dependencies.

## License

No license file was identified during this README refresh. Add a license file before treating the repository as redistributable open-source software.

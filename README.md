# MedWNetworkSim

**A .NET-based network simulation tool for modeling traffic flows, node demands, and routing strategies.**

---

## Table of Contents
1. [Key Features](#key-features)
2. [Getting Started](#getting-started)
3. [Usage](#usage)
4. [Configuration & Customization](#configuration--customization)
5. [Reports & Export](#reports--export)
6. [Accessibility & UX](#accessibility--ux)
7. [Contributing](#contributing)
8. [License & Contact](#license--contact)

---

## Key Features
- Define traffic types and permissions on network edges (binary or percentage-based).
- Visualize node-specific demand backlogs.
- Compare routing strategies: speed vs. cost, stochastic vs. system-optimal, single-path vs. split-flow.
- Export detailed reports in CSV and JSON formats.
- Configure default behaviors for new nodes and edges.
- Maximize main window on launch.

## Getting Started
### Prerequisites
- [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0)
- Visual Studio 2022 or later

### Installation
1. Clone the repository:
```bash
git clone https://github.com/wnj00524/MedWNetworkSim.git
```
2. Open `MedWNetworkSim.sln` in Visual Studio.
3. Build the solution (Ctrl+Shift+B) and run the application (F5).

## Usage
### Simulating a Network
1. Open or create a network file.
2. Add nodes and edges via the GUI.
3. Assign traffic types and permissions for each edge.
4. Configure routing options:
   - Example: `Speed + SystemOptimal + SplitFlow`
   - Example: `Cost + Stochastic + SinglePath`
5. Run simulation and monitor node demand and traffic flow.

### Importing OpenStreetMap road data
- Use **File → Import OpenStreetMap File…** to import either:
  - `*.osm` (OSM XML)
  - `*.pbf` (OSM PBF)
- The importer reuses the same mapping, simplification, and validation pipeline for both formats.
- Only supported road/highway-tagged ways are imported for simulation.
- Large real-world map extracts are simplified during import to keep graphs tractable.
- Very large `.pbf` files can take longer to process; the app shows stage-by-stage import progress.

### Exporting Reports
- Export detailed simulation data via `File → Export`.
- Reports include node-specific backlogs, edge traffic states, and routing statistics.

## Configuration & Customization
- **Edge Permissions**: Set default permission states globally.
- **Node Defaults**: Pre-set initial traffic demands and backlog thresholds.
- **Network File Example**:
```json
{
  "nodes": [
    {"id": "Node1", "demand": {"Traffic1": 100, "Traffic2": 50}},
    {"id": "Node2", "demand": {"Traffic1": 50}}
  ],
  "edges": [
    {"from": "Node1", "to": "Node2", "permissions": {"Traffic1": 100, "Traffic2": 0}}
  ]
}
```

## Accessibility & UX
- Keyboard navigation for all interactive elements.
- Clear labels, concise instructions, and consistent terminology.
- Immediate, actionable error messages.
- High-contrast UI and visible focus states.
- Consistent layout and logical grouping of elements for better comprehension【8†source】【9†source】.

## Contributing
- Fork the repository and create a new branch per feature or bugfix.
- Follow .NET coding conventions.
- Test GUI interactions, edge permissions, and report exports.
- Submit pull requests for review.

## License & Contact
- **License**: MIT License
- **GitHub Issues**: [Open an issue](https://github.com/wnj00524/MedWNetworkSim/issues)

# CLI Reference

MedWNetworkSim can be used without opening the GUI. The CLI supports both:

- running reports from an existing network file
- creating and editing a network file directly from the command line

## Help

Any of these will print the built-in help text:

```powershell
MedWNetworkSim.App.exe help
MedWNetworkSim.App.exe -h
MedWNetworkSim.App.exe -help
MedWNetworkSim.App.exe --help
```

## Commands

### `run`

Runs a simulation and exports a report.

```powershell
MedWNetworkSim.App.exe run --file .\network.json --output .\report.html
MedWNetworkSim.App.exe run --file .\network.json --mode timeline --report timeline --turns 12 --output .\timeline.csv
```

Options:

- `--file` or `--network`: input JSON network file
- `--output`: output report file
- `--mode`: `simulation` or `timeline`
- `--report`: `current` or `timeline`
- `--turns`: required for timeline mode

Notes:

- `simulation/current` matches the GUI `Run Simulation` report flow.
- `.html` and `.htm` output HTML.
- `.csv` output CSV.
- unknown or missing extensions default to CSV.

### `new`

Creates a new network JSON file.

```powershell
MedWNetworkSim.App.exe new --file .\demo.json --name "Demo Network"
MedWNetworkSim.App.exe new --file .\demo.json --name "Demo Network" --description "Created from the CLI" --overwrite
```

Options:

- `--file`: destination JSON file
- `--name`: optional network name
- `--description`: optional description
- `--overwrite`: replace an existing file

### `set-network`

Updates the network name or description in an existing file.

```powershell
MedWNetworkSim.App.exe set-network --file .\demo.json --name "Updated Demo"
MedWNetworkSim.App.exe set-network --file .\demo.json --description "Routing test network"
```

### `add-traffic`

Creates or updates a traffic type.

```powershell
MedWNetworkSim.App.exe add-traffic --file .\demo.json --name Waste --preference cost --bid 1.5
MedWNetworkSim.App.exe add-traffic --file .\demo.json --name Reusables --description "Backhaul flow" --preference totalCost
```

Options:

- `--name`: traffic type name
- `--description`: optional description
- `--preference`: `speed`, `cost`, or `totalCost`
- `--bid`: optional `capacityBidPerUnit`; use `none` to clear it

### `add-node`

Creates or updates a node.

```powershell
MedWNetworkSim.App.exe add-node --file .\demo.json --id N1 --name "Clinic A" --shape building --x 100 --y 120
MedWNetworkSim.App.exe add-node --file .\demo.json --id N2 --name "Hub" --shape circle --transhipment-capacity 10
```

Options:

- `--id`: node id
- `--name`: display name
- `--shape`: `square`, `circle`, `person`, `car`, or `building`
- `--x`, `--y`: canvas position
- `--transhipment-capacity`: optional shared node bottleneck; use `none` to clear it

### `set-profile`

Creates or updates one node/traffic profile.

```powershell
MedWNetworkSim.App.exe set-profile --file .\demo.json --node N1 --traffic Waste --role producer --production 25
MedWNetworkSim.App.exe set-profile --file .\demo.json --node N2 --traffic Waste --role consumer+transship --consumption 25 --premium 2
MedWNetworkSim.App.exe set-profile --file .\demo.json --node N3 --traffic Waste --role consumer --store --store-capacity 40
```

Options:

- `--node`: node id
- `--traffic`: traffic type name
- `--role`: one of:
  - `producer`
  - `consumer`
  - `transship`
  - `producer+consumer`
  - `producer+transship`
  - `consumer+transship`
  - `all`
  - `none`
- `--production`: production amount
- `--consumption`: consumption amount
- `--premium`: consumer premium per unit
- `--production-start`, `--production-end`
- `--consumption-start`, `--consumption-end`
- `--store` or `--no-store`
- `--store-capacity`: optional storage limit; use `none` to clear it

Notes:

- When a role includes producer or consumer and no explicit amount is provided, the CLI preserves the existing positive value or falls back to `1`.
- Setting a profile back to an empty state removes that profile row from the node.

### `add-edge`

Creates or updates an edge.

```powershell
MedWNetworkSim.App.exe add-edge --file .\demo.json --id E1 --from N1 --to N2 --time 1 --cost 4 --direction bidirectional
MedWNetworkSim.App.exe add-edge --file .\demo.json --id E2 --from N2 --to N3 --time 2 --cost 6 --capacity 12 --direction one-way
```

Options:

- `--id`: optional edge id
- `--from`, `--to`: node ids
- `--time`: edge time
- `--cost`: edge cost
- `--capacity`: optional edge capacity; use `none` to clear it
- `--direction`: `one-way` or `bidirectional`

### `auto-arrange`

Recomputes node positions using the same layout logic as the GUI.

```powershell
MedWNetworkSim.App.exe auto-arrange --file .\demo.json
```

## Legacy positional mode

The older report-only positional syntax still works:

```powershell
MedWNetworkSim.App.exe .\network.json simulation current .\report.html
MedWNetworkSim.App.exe .\network.json timeline timeline .\timeline.csv 12
```

## End-to-end example

```powershell
MedWNetworkSim.App.exe new --file .\demo.json --name "CLI Demo"
MedWNetworkSim.App.exe add-traffic --file .\demo.json --name Waste --preference cost --bid 1.5
MedWNetworkSim.App.exe add-node --file .\demo.json --id N1 --name "Clinic A" --shape building --x 100 --y 120
MedWNetworkSim.App.exe add-node --file .\demo.json --id N2 --name "Hub" --shape circle --x 240 --y 120 --transhipment-capacity 10
MedWNetworkSim.App.exe set-profile --file .\demo.json --node N1 --traffic Waste --role producer --production 25
MedWNetworkSim.App.exe set-profile --file .\demo.json --node N2 --traffic Waste --role consumer+transship --consumption 25 --premium 2
MedWNetworkSim.App.exe add-edge --file .\demo.json --id E1 --from N1 --to N2 --time 1 --cost 4 --direction bidirectional
MedWNetworkSim.App.exe run --file .\demo.json --output .\demo-report.html
```

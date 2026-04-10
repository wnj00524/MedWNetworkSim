# CLI Reference

This guide explains how to use MedWNetworkSim from the command line in plain language.

You do **not** need to open the desktop app to use these commands.

The command-line mode is useful when you want to:
- create network files quickly
- make repeatable test networks
- update a model in a consistent way
- generate reports without opening the visual app

## Before you begin

The application runs in command-line mode when you start it with arguments.

Typical project run pattern:

```bash
dotnet run --project .\src\MedWNetworkSim.App\MedWNetworkSim.App.csproj -- <command>
```

If you are using the built `.exe`, replace the start of each example with the executable name instead.

Use `--gui` or `--force-gui` if you want the desktop app to open even though arguments are present.

## Get help

Any of these will show the built-in help:

```bash
MedWNetworkSim.App.exe help
MedWNetworkSim.App.exe -h
MedWNetworkSim.App.exe -help
MedWNetworkSim.App.exe --help
```

## The basic idea

Most command-line work follows this pattern:
1. Create or open a JSON network file.
2. Add traffic types.
3. Add nodes.
4. Set each node’s role for each traffic type.
5. Add edges.
6. Run a report.

## Commands

## `run`
Runs a simulation and writes a report file.

Example:

```bash
MedWNetworkSim.App.exe run --file .\network.json --output .\report.html
```

Timeline example:

```bash
MedWNetworkSim.App.exe run --file .\network.json --mode timeline --report timeline --turns 12 --output .\timeline.csv
```

Use this when you already have a network file and want results.

Important options:
- `--file` or `--network`: the JSON network file to read
- `--output`: where to save the report
- `--mode`: `simulation` or `timeline`
- `--report`: `current` or `timeline`
- `--turns`: required for timeline mode
- timeline loop length is read from the network file itself

What the options mean:
- **simulation/current** = one-off snapshot analysis
- **timeline/timeline** = step-by-step period analysis

Output format is chosen from the output filename:
- `.html` or `.htm` gives an HTML report
- `.csv` gives a CSV report
- any other extension is treated as CSV by default

## `new`
Creates a new JSON network file.

Example:

```bash
MedWNetworkSim.App.exe new --file .\demo.json --name "Demo Network"
```

With description and overwrite:

```bash
MedWNetworkSim.App.exe new --file .\demo.json --name "Demo Network" --description "Created from the CLI" --overwrite
```

Use this when you want to start a model from scratch.

Options:
- `--file`: where to save the network
- `--name`: network name
- `--description`: network description
- `--overwrite`: replace an existing file

## `set-network`
Changes the network’s name, description, or timeline loop length in an existing file.

Examples:

```bash
MedWNetworkSim.App.exe set-network --file .\demo.json --name "Updated Demo"
MedWNetworkSim.App.exe set-network --file .\demo.json --description "Routing test network"
MedWNetworkSim.App.exe set-network --file .\demo.json --loop-length 12
MedWNetworkSim.App.exe set-network --file .\demo.json --loop-length none
```

Use this when the model already exists and you only want to update its top-level details.

Additional option:
- `--loop-length`: repeating cycle length for timeline mode; use `none` to clear it

## `add-traffic`
Creates or updates a traffic type.

Examples:

```bash
MedWNetworkSim.App.exe add-traffic --file .\demo.json --name Waste --preference cost --bid 1.5
MedWNetworkSim.App.exe add-traffic --file .\demo.json --name Reusables --description "Backhaul flow" --preference totalCost
```

Options:
- `--name`: traffic type name
- `--description`: explanation of the traffic type
- `--preference`: `speed`, `cost`, or `totalCost`
- `--bid`: optional bid value used when traffic types compete for scarce capacity; use `none` to clear it

Plain-English meaning:
- Use **speed** when time matters most.
- Use **cost** when expense matters most.
- Use **totalCost** when you want a balance of both.

## `add-node`
Creates or updates a node.

Examples:

```bash
MedWNetworkSim.App.exe add-node --file .\demo.json --id N1 --name "Clinic A" --shape building --x 100 --y 120
MedWNetworkSim.App.exe add-node --file .\demo.json --id N2 --name "Hub" --shape circle --transhipment-capacity 10
```

Options:
- `--id`: short identifier for the node
- `--name`: display name shown to users
- `--shape`: `square`, `circle`, `person`, `car`, or `building`
- `--x`, `--y`: canvas position
- `--transhipment-capacity`: optional limit on how much total traffic can pass through this node as an intermediate stop; use `none` to clear it

## `set-profile`
Creates or updates one node/traffic profile.

This is one of the most important commands.

A **profile** means: “for this traffic type, what does this node do?”

Examples:

```bash
MedWNetworkSim.App.exe set-profile --file .\demo.json --node N1 --traffic Waste --role producer --production 25
MedWNetworkSim.App.exe set-profile --file .\demo.json --node N2 --traffic Waste --role consumer+transship --consumption 25 --premium 2
MedWNetworkSim.App.exe set-profile --file .\demo.json --node N3 --traffic Waste --role consumer --store --store-capacity 40
```

Options:
- `--node`: the node id
- `--traffic`: the traffic type name
- `--role`: what the node does for that traffic
- `--production`: amount supplied
- `--consumption`: amount needed
- `--premium`: extra consumer premium per unit
- `--production-start`, `--production-end`: legacy single production window fields
- `--consumption-start`, `--consumption-end`: legacy single consumption window fields
- `--production-window`: repeatable production window such as `1-3`, `5-`, or `-7`
- `--consumption-window`: repeatable consumption window such as `2-4`, `8-`, or `-12`
- `--clear-production-windows`: remove all production windows
- `--clear-consumption-windows`: remove all consumption windows
- `--input`: repeatable local input requirement in the form `TrafficType:Quantity`
- `--clear-inputs`: remove all precursor input requirements
- `--store` or `--no-store`: whether the node stores inventory
- `--store-capacity`: optional storage limit; use `none` to clear it

Allowed roles:
- `producer`
- `consumer`
- `transship`
- `producer+consumer`
- `producer+transship`
- `consumer+transship`
- `all`
- `none`

How to think about this:
- **producer** = the node supplies something
- **consumer** = the node needs something
- **transship** = the node can be used as an intermediate stop
- **store** = separate from role; this controls inventory behaviour

Useful behaviour to know:
- If you set a role that includes producer or consumer but do not give an amount, the CLI keeps the existing positive value or defaults to `1`.
- Repeating `--production-window`, `--consumption-window`, or `--input` adds multiple rows in one command.
- Window syntax is inclusive:
  - `1-3` means periods 1 through 3
  - `5-` means period 5 onward
  - `-7` means from the start through period 7
- Input requirements are local production prerequisites.
- Example:
  - `--input Wheat:1 --input Water:2`
  - means one unit of output needs one Wheat and two Water already present at that node
- If the required precursor set is not available locally, production for that output traffic is halted for that period.
- If a profile is reduced back to an empty state, the app removes that profile from the node.

Window examples:

```bash
MedWNetworkSim.App.exe set-profile --file .\demo.json --node Bakery --traffic Bread --production-window 1-3 --production-window 8-10
MedWNetworkSim.App.exe set-profile --file .\demo.json --node Town --traffic Bread --consumption-window 1-12
MedWNetworkSim.App.exe set-profile --file .\demo.json --node Town --traffic Bread --clear-consumption-windows
```

Input requirement examples:

```bash
MedWNetworkSim.App.exe set-profile --file .\demo.json --node Bakery --traffic Bread --input Wheat:1 --input Water:2
MedWNetworkSim.App.exe set-profile --file .\demo.json --node Bakery --traffic Bread --clear-inputs
```

## `add-edge`
Creates or updates a route between two nodes.

Examples:

```bash
MedWNetworkSim.App.exe add-edge --file .\demo.json --id E1 --from N1 --to N2 --time 1 --cost 4 --direction bidirectional
MedWNetworkSim.App.exe add-edge --file .\demo.json --id E2 --from N2 --to N3 --time 2 --cost 6 --capacity 12 --direction one-way
```

Options:
- `--id`: optional edge id
- `--from`, `--to`: the two node ids
- `--time`: travel time
- `--cost`: movement cost
- `--capacity`: optional route limit; use `none` to clear it
- `--direction`: `one-way` or `bidirectional`

## `auto-arrange`
Recomputes node positions automatically.

Example:

```bash
MedWNetworkSim.App.exe auto-arrange --file .\demo.json
```

Use this when you want the application to clean up layout positions for you.

## A simple end-to-end example

This example creates a very small network.

### 1. Create a new file
```bash
MedWNetworkSim.App.exe new --file .\demo.json --name "Simple Demo"
```

### 2. Add a traffic type
```bash
MedWNetworkSim.App.exe add-traffic --file .\demo.json --name Waste --preference cost
```

### 3. Add nodes
```bash
MedWNetworkSim.App.exe add-node --file .\demo.json --id N1 --name "Clinic" --shape building
MedWNetworkSim.App.exe add-node --file .\demo.json --id N2 --name "Hub" --shape circle
MedWNetworkSim.App.exe add-node --file .\demo.json --id N3 --name "Processor" --shape building
```

### 4. Set roles
```bash
MedWNetworkSim.App.exe set-profile --file .\demo.json --node N1 --traffic Waste --role producer --production 10
MedWNetworkSim.App.exe set-profile --file .\demo.json --node N2 --traffic Waste --role transship
MedWNetworkSim.App.exe set-profile --file .\demo.json --node N3 --traffic Waste --role consumer --consumption 10
```

### 5. Add routes
```bash
MedWNetworkSim.App.exe add-edge --file .\demo.json --from N1 --to N2 --time 1 --cost 2 --direction bidirectional
MedWNetworkSim.App.exe add-edge --file .\demo.json --from N2 --to N3 --time 1 --cost 3 --direction bidirectional
```

### 6. Run a report
```bash
MedWNetworkSim.App.exe run --file .\demo.json --output .\report.html
```

## Legacy positional mode

The older positional syntax still works for report generation. The built-in help text documents it explicitly.

## When to use the CLI and when not to

The CLI is best when you want:
- explicit control over:
  - timeline loop length
  - repeating schedule windows
  - local precursor requirements
- repeatable model setup
- automation
- quick changes to many files
- scripted report generation

The desktop app is better when you want:
- visual editing
- drag-and-drop layout
- canvas interaction
- quick inspection of the model and results
- easier editing of several windows or input rows at once

## GUI override

If you want the WPF desktop app to open instead of command-line mode, start with:

```bash
MedWNetworkSim.App.exe --gui
```

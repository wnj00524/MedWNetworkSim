# Network Authoring Guide

This project supports three authoring paths:

- direct editing in the WPF GUI
- editing the native JSON files by hand
- scripting network creation and updates through the CLI

## Recommended workflow

For most users:

1. Start with the GUI if you want visual layout, drag/drop editing, or GraphML import/export.
2. Use the CLI when you want repeatable setup, test fixtures, or automation.
3. Keep the native JSON as the source of truth when you need the full feature set.

## Native JSON is the most complete format

The JSON format currently preserves the full MedW model, including:

- traffic definitions
- node shapes and positions
- node transhipment capacities
- traffic profiles
- schedules
- store settings
- consumer premiums
- edge cost, time, capacity, and direction

GraphML support is useful for graph interchange, but JSON remains the best persistence format for the newer simulator features.

## Authoring from the CLI

The CLI supports practical upsert-style network editing:

- `new` creates a file
- `set-network` edits network metadata
- `add-traffic` creates or updates a traffic type
- `add-node` creates or updates a node
- `set-profile` creates or updates one node/traffic profile
- `add-edge` creates or updates an edge
- `auto-arrange` recalculates node positions

That makes it suitable for:

- generating repeatable demo networks
- seeding fixtures for tests
- batch editing a family of networks
- producing reports in CI or scheduled jobs

## Role model reminders

Each node can carry multiple traffic profiles, one per traffic type.

Within a profile, the important fields are:

- `production`
- `consumption`
- `canTransship`
- `consumerPremiumPerUnit`
- schedule windows
- optional store behavior

The CLI `set-profile --role ...` command is just a friendly wrapper around those fields.

Examples:

- `producer` means `production > 0`
- `consumer` means `consumption > 0`
- `transship` means `canTransship: true`
- `all` means producer, consumer, and transhipment together
- `store` is separate from role and is controlled with `--store`

## Capacity model reminders

- Edge `capacity` limits how much total traffic can traverse that edge.
- Node `transhipmentCapacity` limits how much total traffic can use a node as an intermediate stop.
- Traffic types can bid for scarce capacity with `capacityBidPerUnit`.
- Individual consumers can add a node-specific premium with `consumerPremiumPerUnit`.

## Timeline model reminders

In timeline mode:

- scheduled production and demand activate period by period
- traffic moves one edge-time step at a time
- in-flight traffic persists between periods
- store nodes can accumulate inventory and release it later

## Suggested GitHub usage

This repository works best on GitHub when you keep these files in sync:

- `README.md` for the quickstart and capability overview
- `docs/CLI_REFERENCE.md` for the exact CLI syntax
- `docs/NETWORK_AUTHORING.md` for authoring guidance and modeling rules

If you publish new features:

1. update the README summary
2. update the CLI reference if command behavior changed
3. update this guide if the modeling rules changed

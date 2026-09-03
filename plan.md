1.  **Refactor NodeState aggregations in `WorkspacePresentation.cs`**
    -   In the `WorkspacePresentation.ApplySimulationOutcomes` and `WorkspacePresentation.BuildReportMetrics` methods, there are multiple LINQ aggregations filtering and summarizing `DemandBacklog` from `timeline.NodeStates`.
    -   These run on the UI thread when simulation outcomes are applied, causing allocations for iterators, lambda closures, grouping, and lists.
    -   Replace the redundant LINQ operations `timeline.NodeStates.Where(..).GroupBy(..).Select(..)` with a single-pass manual loop over `timeline.NodeStates` that aggregates `DemandBacklog` per `NodeId` and per `TrafficType`.
    -   Pre-calculate these dictionaries `backlogByNode` and `backlogByTrafficType` before updating the `Scene.Nodes`, then access them via `TryGetValue` during the `Scene.Nodes` loop.
2.  **Verify UI behavior**
    -   Run tests in `tests/MedWNetworkSim.Tests/MedWNetworkSim.Tests.csproj` and `dotnet build`.
3.  **Complete pre-commit steps**
    -   Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.
4.  **Submit PR**
    -   Submit with "⚡ Bolt: [performance improvement]" title.

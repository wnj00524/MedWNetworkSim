1. **Optimize `NetworkSimulationEngine.cs` LINQ allocations during path resolution:**
   - In `AllocateProportionallyFromNode`, `pathNodeIds.Concat([branch.ToNodeId]).ToList()` and `pathEdgeIds.Concat([branch.EdgeId]).ToList()` generate a massive number of allocations on the hot path due to `Concat`, array allocation for the single item `[branch.ToNodeId]`, and `ToList()`.
   - Replaced these with manually pre-sized lists and a `for` loop (or indexer population) and `.Add(branch.ToNodeId)`, which prevents multiple object array and enumerator allocations per branch evaluation.
2. **Optimize `MixedRouting.cs` LINQ allocations during stochastic ranking:**
   - In `FindCandidateRoutes`, `current.PathNodeIds.Concat([arc.ToNodeId]).ToList()` and `current.PathEdgeIds.Concat([arc.EdgeId]).ToList()` do the same thing inside the `PriorityQueue` loop.
   - Replaced these with pre-sized lists and manual addition, saving enumerator and delegate allocations on route expansion.
3. **Verify the change:**
   - Ensure the `dotnet test tests/MedWNetworkSim.Tests/MedWNetworkSim.Tests.csproj` runs and passes.
4. **Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.**
5. **Submit PR:** Create PR with title "⚡ Bolt: [performance improvement]".

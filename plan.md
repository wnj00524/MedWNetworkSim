1. **Optimize Array Allocations on Hot Paths in NetworkSimulationEngine.cs**
   - In `AllocateProportionallyFromNode`, replace `pathNodeIds.Concat([branch.ToNodeId]).ToList()` with a pre-sized list and a manual loop + `Add`.
   - Replace `pathEdgeIds.Concat([branch.EdgeId]).ToList()` with a pre-sized list and a manual loop + `Add`.
   - This eliminates multiple enumerator, array, and delegate allocations in a highly recursive hot loop.
2. **Run Tests**
   - Run the unit tests via `dotnet test tests/MedWNetworkSim.Tests/MedWNetworkSim.Tests.csproj`.
3. **Complete Pre-Commit Steps**
   - Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.
4. **Submit PR**
   - Submit a PR with the performance changes.

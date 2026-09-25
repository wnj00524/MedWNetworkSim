1. **Refactor nodesById in MultiOriginIsochroneService.cs**:
   - Use `replace_with_git_merge_diff` in `Compute` (~line 51) and `ComputeCostsFromOrigin` (~line 130) to replace `allNodes.Where().ToDictionary()` with a pre-sized manual `foreach` loop.
2. **Refactor bestCostByNode and bestOriginByNode in MultiOriginIsochroneService.cs**:
   - Use `replace_with_git_merge_diff` in `Compute` (~line 93) to replace `bestCostById.Where().ToDictionary()` and `bestOriginById.Where().ToDictionary()` with pre-sized manual `foreach` loops.
3. **Refactor coveringOriginsByNode in MultiOriginIsochroneService.cs**:
   - Use `replace_with_git_merge_diff` in `Compute` (~line 99) to replace `coveringOriginsById.Where().ToDictionary()` with a pre-sized manual `foreach` loop.
4. **Refactor costMap in MultiOriginIsochroneService.cs**:
   - Use `replace_with_git_merge_diff` in `ComputeCostsFromOrigin` (~line 140) to replace `costMap.Where().ToDictionary()` with a manual `foreach` loop.
5. **Compile and Test**:
   - Use `run_in_bash_session` to run `dotnet build` and `dotnet test tests/MedWNetworkSim.Tests/MedWNetworkSim.Tests.csproj` to verify correctness.
6. **Update bolt.md (Journal)**:
   - Use `run_in_bash_session` with `cat << 'EOF' >> .jules/bolt.md` to append the following exact text:
     ```
     ## 2024-05-18 - Replacing Multiple ToDictionary Allocations with Pre-sized Dictionary and foreach
     **Learning:** In C#, executing multiple LINQ `.ToDictionary()` allocations on collections inside hot paths like simulation services (e.g. MultiOriginIsochroneService) allocates massive amounts of redundant enumerators, delegates, and intermediate dictionary structures, causing unnecessary memory allocation and garbage collection pauses.
     **Action:** Replace multiple `.ToDictionary()` allocations with a single manual `foreach` loop that populates pre-allocated dictionaries to avoid LINQ overhead entirely.
     ```
7. **Pre-commit**:
   - Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.
8. **Submit PR**:
   - Use `submit` to create PR with title `⚡ Bolt: Replace multiple LINQ ToDictionary calls with manual loops in MultiOriginIsochroneService`.
   - Include exact description text:
     ```
     💡 What: Replaced LINQ `.ToDictionary()` calls with manual `foreach` loops on pre-sized dictionaries in `MultiOriginIsochroneService.cs`.
     🎯 Why: LINQ creates intermediate enumerator, closure, and delegate allocations. Replacing them with explicit loops prevents O(N log N) sorting costs and saves GC pressure.
     📊 Impact: Reduces memory allocations during calculation and scenario event processing.
     🔬 Measurement: Check if calculation paths are significantly faster due to the absence of heap allocations.
     ```

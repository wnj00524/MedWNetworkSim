1. **Optimize Node Backlog Calculation in `WorkspacePresentation.cs`**
   - Use the `replace_with_git_merge_diff` tool to edit `src/MedWNetworkSim.Presentation/WorkspacePresentation.cs` around lines 9423 and 9439.
   - Replace the LINQ chains (`.Where().GroupBy().Select().OrderByDescending().FirstOrDefault()`) for `topUnmetNode` and `topTrafficBacklog` with manual tracking using `foreach` loops and `CollectionsMarshal.GetValueRefOrAddDefault`.
   - Use `run_in_bash_session` to run `dotnet build src/MedWNetworkSim.Presentation/MedWNetworkSim.Presentation.csproj` to verify the code compiles correctly.

2. **Run tests to verify changes**
   - Use `run_in_bash_session` to run `dotnet test tests/MedWNetworkSim.Tests/MedWNetworkSim.Tests.csproj` to ensure no regressions are introduced.

3. Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.

4. **Submit the PR**
   - Use the `submit` tool to create a PR with the title '⚡ Bolt: Optimize node backlog calculation in WorkspacePresentation' and the description:
     💡 What: Replaced multiple LINQ `.Where().GroupBy().Select().OrderByDescending().FirstOrDefault()` chains in `WorkspacePresentation.cs` with manual `foreach` loops using `CollectionsMarshal.GetValueRefOrAddDefault`.
     🎯 Why: These LINQ operations were executed on the UI thread during UI updates, causing excessive `IGrouping`, enumerator, and anonymous object allocations, leading to garbage collection pressure and rendering jank.
     📊 Impact: Reduces UI thread allocations significantly, eliminating O(N^2) complexity and redundant sub-group allocations, improving overall UI responsiveness during network simulation updates.
     🔬 Measurement: Profile the application's memory usage and UI thread latency during simulation updates; observe reduced GC pauses.

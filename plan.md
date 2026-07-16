1. *Optimize UI Thread Allocations in WorkspacePresentation*
   - In `WorkspacePresentation.cs`, there are multiple LINQ `.ToDictionary()` and `.GroupBy().ToDictionary()` calls executed repeatedly on the UI thread (e.g., in `BuildTimelineOutcomes`, `ApplySimulationOutcomes`, `ApplyFacilityPlanningVisuals`, `RefreshLayerItems`). These create immense GC pressure by allocating redundant enumerators, delegates, and intermediate dictionary structures.
   - I will replace these with pre-sized, manual `foreach` loops allocating native `Dictionary` objects directly, utilizing `capacity` based on existing collection lengths to minimize resizing.
2. *Run Tests*
   - Run `dotnet test` to ensure these performance tweaks do not break determinism or correct logic.
3. *Complete pre-commit steps*
   - Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.
4. *Submit PR*
   - Create a pull request outlining the memory allocations eliminated by removing these specific LINQ expressions.

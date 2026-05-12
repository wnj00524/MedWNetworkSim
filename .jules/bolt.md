## 2026-05-10 - O(N^2) Lookup inside UI Render Loop
**Learning:** The edge rendering loop `BuildSceneFromNetwork` does an O(M) `FirstOrDefault` lookup into the `network.Layers` collection for every single edge (O(N)), leading to an O(N*M) bottleneck during scene graph construction.
**Action:** Pre-compute lookup dictionaries (e.g., `network.Layers.ToDictionary(l => l.Id)`) *outside* of hot render loops like `foreach (var edge in network.Edges)` to turn O(N*M) lookups into O(N+M).
## 2026-05-11 - O(N^2) Lookup inside Selection Deletion Loop
**Learning:** The `DeleteSelection` method in `WorkspacePresentation` performs an O(M) `IsLockedLayer` lookup for every selected node and edge (O(N)), causing an O(N*M) bottleneck when deleting large selections. The unoptimized `IsLockedLayer` method uses `FirstOrDefault` over the `network.Layers` collection.
**Action:** Pre-compute a `HashSet` of locked layer IDs outside of the iteration loops (e.g., `network.Layers.Where(l => l.IsLocked).Select(l => l.Id).ToHashSet()`) to enable O(1) lookups.

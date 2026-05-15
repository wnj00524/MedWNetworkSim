## 2026-05-10 - O(N^2) Lookup inside UI Render Loop
**Learning:** The edge rendering loop `BuildSceneFromNetwork` does an O(M) `FirstOrDefault` lookup into the `network.Layers` collection for every single edge (O(N)), leading to an O(N*M) bottleneck during scene graph construction.
**Action:** Pre-compute lookup dictionaries (e.g., `network.Layers.ToDictionary(l => l.Id)`) *outside* of hot render loops like `foreach (var edge in network.Edges)` to turn O(N*M) lookups into O(N+M).
## 2026-05-11 - O(N^2) Lookup inside Selection Deletion Loop
**Learning:** The `DeleteSelection` method in `WorkspacePresentation` performs an O(M) `IsLockedLayer` lookup for every selected node and edge (O(N)), causing an O(N*M) bottleneck when deleting large selections. The unoptimized `IsLockedLayer` method uses `FirstOrDefault` over the `network.Layers` collection.
**Action:** Pre-compute a `HashSet` of locked layer IDs outside of the iteration loops (e.g., `network.Layers.Where(l => l.IsLocked).Select(l => l.Id).ToHashSet()`) to enable O(1) lookups.
## 2026-05-12 - O(N^2) Lookup inside Simulation Refresh Loop
**Learning:** The `RefreshSimulationDisplayState` and `ResetTimeline` methods perform an O(N) `First` lookup into the `network.Nodes` collection for every single node in the scene, and an O(E) `First` lookup into the `network.Edges` collection for every edge in the scene. This causes a severe O(N^2 + E^2) bottleneck during animation or timeline resets, scaling terribly with large networks.
**Action:** Pre-compute lookup dictionaries (`network.Nodes.ToDictionary` and `network.Edges.ToDictionary`) outside the `foreach` scene iteration loops to reduce the lookups to O(1), bringing the overall update complexity to O(N + E).
## 2026-05-13 - O(N^2) Lookup inside Layer Refresh Loop
**Learning:** The `RefreshLayerItems` method in `WorkspacePresentation` performs an O(N) `Count()` lookup over `network.Nodes` and `network.Edges` for every single layer in the network. This causes an O(L * (N + E)) bottleneck when layers are updated, which slows down the UI with many layers.
**Action:** Pre-compute lookup dictionaries for node and edge counts by layer outside the iteration loop using `.GroupBy(x => x.LayerId).ToDictionary(g => g.Key, g => g.Count())` to turn the lookups into O(1). Note: `LayerId` is a `Guid`, so avoid using string fallbacks or comparers.

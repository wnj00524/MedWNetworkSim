## 2026-05-10 - O(N^2) Lookup inside UI Render Loop
**Learning:** The edge rendering loop `BuildSceneFromNetwork` does an O(M) `FirstOrDefault` lookup into the `network.Layers` collection for every single edge (O(N)), leading to an O(N*M) bottleneck during scene graph construction.
**Action:** Pre-compute lookup dictionaries (e.g., `network.Layers.ToDictionary(l => l.Id)`) *outside* of hot render loops like `foreach (var edge in network.Edges)` to turn O(N*M) lookups into O(N+M).

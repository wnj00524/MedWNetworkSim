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
## 2026-05-14 - O(N^2) LINQ Evaluation inside Nested Routing Loop
**Learning:** `BuildCandidateRoutes` runs repeatedly during capacity bidding (inside a `while (true)` loop). In its original form, it performed `context.Demand.Where(...).Select(...)` within the body of an outer `foreach` loop over `context.Supply`. This led to the `Demand` dictionary being repeatedly iterated and filtered on *every* producer iteration, causing severe O(P * C) allocation and evaluation bottlenecks.
**Action:** When a method processes combinations from two collections using nested loops, always pre-compute the filtered/projected sequences (e.g., `ToList()`) *outside* of the outer loop.
## 2026-05-15 - O(P * C) Dijkstra Execution inside Capacity Bidding
**Learning:** The `NetworkSimulationEngine` originally executed a full Dijkstra pass (`FindBestRoute`) inside a nested loop of all $P$ active producers and $C$ active consumers. This resulted in the same graph traversal logic being repeatedly executed for the same producer, resulting in an $O(P \times C)$ bottleneck.
**Action:** Replace single-target `FindBestRoute` algorithms with batched multi-target `FindBestRoutes` algorithms when finding shortest paths from a single producer to many consumers. A single Dijkstra pass can discover the shortest paths to all $C$ targets simultaneously, dropping the total traversal time complexity to $O(P)$.
## 2026-05-16 - O(P * C) LINQ Evaluation inside Candidate Route Builder Loop
**Learning:** `BuildCandidateRoutes` inside `NetworkSimulationEngine` contained an `O(P * C)` operation inside its producer loop: `var targetConsumers = activeConsumers.Where(id => !Comparer.Equals(producerNodeId, id)).ToHashSet(Comparer);`. This repeatedly evaluated a LINQ query over all consumers ($C$) for every producer ($P$), causing significant CPU and memory overhead during capacity bidding.
**Action:** Replaced the repetitive `Where` filter with a fast O(1) subset operation by creating a copy of the pre-computed `activeConsumers` `HashSet` and removing the current `producerNodeId` via `targetConsumers.Remove(producerNodeId);`. When finding subsets inside a hot loop, prefer copying HashSets and removing items over repeating LINQ queries.

## 2024-05-23 - Optimize LINQ Allocations in Network Simulation Engine
**Learning:** Found multiple places in `NetworkSimulationEngine.cs` where LINQ `.Count()` and `.Select().Min()` inside inner loops on the simulation path were allocating delegates and enumerators on the hot path (e.g., inside `GetPathRemainingCapacity` and `CountBottleneckResources`).
**Action:** Replaced these LINQ chains with `for` and `foreach` loops respectively to minimize garbage generation during greedy route allocation, reducing allocations during repeated graph evaluations.
## 2024-05-24 - Optimize LINQ Allocations in Candidate Route Builder
**Learning:** Found that `BuildCandidateRoutes` and `FindBestRoutes` in `NetworkSimulationEngine.cs` were using LINQ `Where`, `Select`, `ToHashSet`, and `Sum` inside the hot loops that execute during capacity bidding (e.g., when finding active consumers/producers, and calculating total route score/cost/time). This allocated significant amounts of enumerators and delegates, reducing performance in large simulations.
**Action:** Replaced these LINQ chains with standard `for` and `foreach` loops to minimize garbage generation, eliminating redundant allocation inside the routing bottleneck.
## 2024-05-25 - Avoid O(N) Hashset Copy In Hot Loops
**Learning:** Found multiple places in `TemporalNetworkSimulationEngine.cs` and `MixedRouting.cs` where `new HashSet<string>(activeConsumers, Comparer)` was being called inside an `O(P)` hot loop to filter out the `producerNodeId` via `.Remove(producerNodeId)`. Copy constructors for `HashSet` are `O(N)`, which caused severe GC overhead that bottlenecked graph traversal logic.
**Action:** Replace `HashSet` copying inside hot loops with iterative passes over the original collection alongside `O(1)` conditional filtering (`if (consumerNodeId == producerNodeId) continue;`).

## 2024-05-25 - Be Careful Replacing LINQ Min with For Loops
**Learning:** When attempting to remove LINQ `.Select().DefaultIfEmpty(0d).Min()` inside `GetPathRemainingCapacity`, I replaced it with a `for` loop but incorrectly added `minCapacity == double.PositiveInfinity ? 0d : minCapacity;`. This broke capacity calculations on paths made entirely of unconstrained edges (which expect `PositiveInfinity`).
**Action:** When replacing LINQ `.Min()` or `.DefaultIfEmpty()` aggregations with manual `for` loops (e.g., for calculating capacities), ensure that traversing empty collections correctly defaults to `double.PositiveInfinity` (or the equivalent semantically unconstrained value) rather than inadvertently returning `0`.

## 2024-05-26 - O(P * C) Allocation bottleneck in multi-target Dijkstra loops
**Learning:** Found that `FindBestRoutes` and `BuildCandidateRoutes` were repeatedly instantiating `HashSet<string>` copies of target consumers (`targetConsumers` and `remainingConsumers`) for every active producer during simulation bidding. Copy constructors for `HashSet` are $O(N)$, resulting in a massive $O(P \times C)$ memory allocation overhead.
**Action:** Replaced the collection copies with simple integer state variables (`consumersToFind`) that track the count of remaining targets, decrementing when a node is settled. When checking for early exit conditions in batched search algorithms inside hot loops, always use integer tracking counters rather than allocating sets or collections.
## 2026-05-26 - Replaced SelectMany().OrderByDescending().ThenBy() with O(N) scan in MedWNetworkSim hot path
**Learning:** When selecting a single best element via `FirstOrDefault()` after sorting operations on collections via LINQ `SelectMany` -> `OrderBy` -> `ThenBy` (specifically in `AllocateGreedyBestRoutes` inside `NetworkSimulationEngine`), the performance overhead of sorting ((N \log N)$) and lambda delegate allocations creates immense GC pressure.
**Action:** Manually unroll complex chained LINQ queries into explicit O(N) linear scans utilizing standard `foreach` loops and explicit `.CompareTo` conditional blocks. Ensure to map `OrderByDescending` logic to `cmp > 0` vs `cmp < 0` correctly during tiebreakers while maintaining stable-sort constraints.
## 2026-05-27 - [NetworkSimulationEngine Branch Routing Optimization]
**Learning:** [When resolving capacity iteratively in loops (e.g., in `AllocateAcrossBranchRoutes`), LINQ sorting operations (`.OrderBy().ThenBy()`) on static properties inside the loop cause massive redundant O(N log N) overhead and delegate closure allocations.]
**Action:** [Hoist static sorting constraints completely outside the capacity resolution `while` loop into a pre-allocated `List<T>`, allowing the inner loop to run as a fast O(N) linear scan.]
## $(date +%Y-%m-%d) - [Optimize GroupBy Redundant Enumerations]
**Learning:** In C#, applying multiple LINQ `Where()`, `ToList()`, and `Sum()` aggregations inside a `Select` projection on grouped data triggers redundant iterations over the group's elements and creates unnecessary temporary lists and delegate closures.
**Action:** Replace multiple LINQ aggregations on `IEnumerable` groupings with a single `foreach` loop that accumulates all required metrics at once. This shifts the time complexity per group from O(k*N) to strictly O(N) and drastically cuts heap allocations.

## 2026-05-29 - Optimize GroupBy Redundant Enumerations
**Learning:** In C#, applying multiple LINQ `Where()`, `ToList()`, and `Sum()` aggregations inside a `Select` projection on grouped data triggers redundant iterations over the group's elements and creates unnecessary temporary lists and delegate closures.
**Action:** Replace multiple LINQ aggregations on `IEnumerable` groupings with a single `foreach` loop that accumulates all required metrics at once. This shifts the time complexity per group from O(k*N) to strictly O(N) and drastically cuts heap allocations.
## 2024-05-31 - Replace LINQ Count with Foreach
**Learning:** Found that `CountBottleneckResources` in `TemporalNetworkSimulationEngine.cs` used LINQ `Count` with a lambda expression over `pathResourceIds` inside a hot capacity-bidding loop. This allocated delegates and enumerators heavily, causing GC pressure during network routing calculations.
**Action:** Replaced the LINQ `Count` with standard `foreach` loops to minimize garbage generation, matching the optimization done in `NetworkSimulationEngine.cs`.

## 2025-02-12 - Replacing LINQ in the Temporal Simulation Engine
**Learning:** In C# hot loops such as those within `NetworkSimulationEngine` or `TemporalNetworkSimulationEngine`, standard LINQ methods like `.Where()`, `.Select()`, `.ToList()`, and `.ToDictionary()` cause significant memory allocations per call due to enumerator, delegate, and closure instances. This leads to garbage collection pressure and measurable slowdowns during the inner simulation loop.
**Action:** Replace LINQ method chains with manual `for` or `foreach` loops on the hot path (like the `Advance` method or timeline/occupancy calculation functions). This approach avoids closures and enumerator allocations entirely while retaining the identical computational result.
## $(date +%Y-%m-%d) - Avoiding IDictionary Interfaces for ReadOnly Conversions
**Learning:** In C#, declaring variables as `IDictionary<TKey, TValue>` prevents implicit conversion to `IReadOnlyDictionary<TKey, TValue>` when passed as method arguments, causing CS1503 compilation errors. Furthermore, using `.Capacity` on a `List` or interface when pre-sizing new collections is unsafe and causes errors.
**Action:** Always declare new collections with `var` or as concrete `Dictionary<TKey, TValue>` to allow implicit `IReadOnlyDictionary` conversions, and strictly use `.Count` instead of `.Capacity` when pre-sizing allocations.
## 2024-05-31 - Preserving Sort Order in LINQ to foreach Refactoring
**Learning:** When refactoring a LINQ `.OrderBy(cost).ThenBy(name).ThenBy(id).FirstOrDefault()` chain into a single O(N) `foreach` loop scan, replacing the initial conditions with a strict less-than check (`<`) is not sufficient to preserve the precise behavior if ties exist.
**Action:** Always replicate the exact tie-breaker logic from the `ThenBy` clauses within an `else if (currentVal == bestVal)` branch inside the loop, using the original exact comparator types and strict equality checks rather than epsilon-based approximation.
## 2026-05-31 - Optimize LINQ Sum in properties and lambda bodies
**Learning:** In C#, using LINQ `.Sum()` inside properties (e.g. `AvailableSupply`) or lambda bodies evaluated frequently in the simulation loop causes continuous delegate allocation and enumerator overhead. This is measurable on hot paths where graph traversal updates properties often.
**Action:** Replace `property => collection.Sum(x => x.prop)` with explicit `get` blocks containing `foreach` loops on the collection. Although more verbose, it completely eliminates GC pressure and runs much faster on hot paths.
## 2024-05-24 - BuildStaticRecipeCostOrder LINQ Dictionary Initialization Overhead
**Learning:** In C#, replacing `Enumerable.Select` returning an anonymous type into a `.ToDictionary()` call with a manual `for` loop and indexer initialization (`dict[key] = value`) inside hot paths significantly reduces allocations and speeds up execution by avoiding hidden enumerator and delegate generation. Note that this changes behavior slightly on duplicate keys (throwing vs overwriting).
**Action:** Actively scan for `.ToDictionary()` inside heavily utilized setup or iterative loops. Convert these to pre-sized `new Dictionary<K, V>(count)` and standard loops to eliminate enumerator GC pressure.

## 2024-06-08 - Hoisting Dictionary Construction to Avoid $O(P \times C \times E)$ Allocation Bottleneck
**Learning:** In C# graph reachability algorithms, if a `.ToDictionary()` call on the entire graph's edges (like `network.Edges.ToDictionary(...)`) is located inside a nested loop structure (e.g., inside `HasPermittedPath` called repeatedly by `producers.Any()` and `consumers.Any()`), it creates an enormous hidden memory and performance bottleneck, effectively multiplying an $O(E)$ operation by $O(P \times C)$.
**Action:** When inspecting hot paths or reachability checks in simulation engines, explicitly look for dictionary or collection allocations inside the traversal or checking logic, and hoist them out as parameters to be constructed exactly once per network/batch. Replace LINQ allocations with manual, pre-sized `foreach` loops to eliminate continuous delegate and closure overhead.
## $(date +%Y-%m-%d) - format.sh FileNotFoundException
**Learning:** The `format.sh` script in the repository fails with a `FileNotFoundException` because `src/MedWNetworkSim.App/MedWNetworkSim.App.csproj` does not exist.
**Action:** Use the native `dotnet format` command for linting and formatting instead.
## $(date +%Y-%m-%d) - Preserve GroupBy Determinism when refactoring to Dictionary
**Learning:** When refactoring LINQ `GroupBy` calls to manual `Dictionary` iterations to reduce allocation overhead, it is easy to accidentally introduce non-determinism. `Enumerable.GroupBy` yields elements in the exact order their keys first appeared, while `Dictionary` enumeration uses hash buckets (which are randomized per execution in modern .NET). This loss of determinism can be fatal in simulation engines.
**Action:** When manually replacing `GroupBy` with a `Dictionary`, always track the order of keys explicitly as they are first encountered (e.g., using a `List<TKey> orderedKeys`) and iterate over that list instead of the dictionary keys or values.
## 2025-02-17 - Avoid LINQ multiple enumerator allocations during clones and sorting
**Learning:** Using LINQ `.Select().Clone()` with `AddRange()` and passing `List<T>` to an `IEnumerable<T>` parameter creates unnecessary garbage collection overhead through enumerator and delegate allocations. Furthermore, `.OrderBy().ThenBy()` forces multiple sequence allocations for sorting operations that could be done in-place.
**Action:** Use pre-sized loops to add clones directly, change `IEnumerable<T>` parameters to `List<T>` to avoid boxing the list enumerator, and replace LINQ sorting with `List.Sort()` using a stable identifier (`Sequence`) to ensure determinism without O(N log N) allocation overhead.
## 2024-06-15 - Optimize Dictionary allocations in C# hot loops
**Learning:** In C#, LINQ `.ToDictionary` allocates a new dictionary, delegates, and enumerators. Using `.Any()` afterwards also introduces another O(N) pass.
**Action:** Replace `.ToDictionary` and subsequent `.Any()` combinations with a manual `Dictionary` pre-allocated by count, populated via a `foreach` loop, and track boolean flags (e.g., `hasFiniteEdges`) inside the same loop to avoid multiple iterations and delegate allocations.
## 2024-05-31 - Optimize Dictionary Key Intersections
**Learning:** Using `dictA.Keys.Intersect(dictB.Keys).ToList()` creates a hidden `HashSet<T>` allocation from `Intersect`, multiple enumerator instantiations, and O(N) GC pressure.
**Action:** Replace LINQ `Intersect` on dictionary keys with a manual `foreach` loop over `dictA.Keys` that evaluates `dictB.ContainsKey(key)` and adds matches to a pre-allocated or dynamically sizing `List<T>`.

## 2024-05-31 - Eliminate Anonymous Object Allocation in SelectMany
**Learning:** Chained LINQ queries utilizing `.SelectMany` that project into anonymous objects (`Select(req => new { parent.Prop, req })`) create immense memory pressure because an object is allocated on the heap for every element in the sequence during evaluation.
**Action:** Refactor such chains into standard, nested `foreach` loops using simple local scalar variables instead of intermediate anonymous objects to bypass heap allocation entirely.

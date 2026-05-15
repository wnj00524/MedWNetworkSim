1. **Identify the Bottleneck**: In `src/MedWNetworkSim.Presentation/WorkspacePresentation.cs`, the `RefreshLayerItems` method is called repeatedly. Inside this method, a loop iterates over `network.Layers`, and for each layer, it performs an O(N) lookup (`network.Nodes.Count(...)` and `network.Edges.Count(...)`) to count the nodes and edges belonging to that layer. With N layers, this results in O(L * (N + E)) performance, which is effectively O(N^2) if L scales with N, creating a performance bottleneck when there are many layers, nodes, and edges.

2. **Pre-compute Node and Edge Counts by Layer**:
   - Instead of doing `.Count(node => node.LayerId == layer.Id)` inside the loop, we can group the nodes and edges by `LayerId` outside of the loop.
   - We will create dictionary lookups:
     ```csharp
     var nodeCountsByLayer = network.Nodes.GroupBy(node => node.LayerId).ToDictionary(g => g.Key, g => g.Count(), Comparer);
     var edgeCountsByLayer = network.Edges.GroupBy(edge => edge.LayerId).ToDictionary(g => g.Key, g => g.Count(), Comparer);
     ```
   - Then, inside the `network.Layers` loop, we can just do O(1) dictionary lookups:
     ```csharp
     NodeCount = nodeCountsByLayer.TryGetValue(layer.Id, out var n) ? n : 0,
     EdgeCount = edgeCountsByLayer.TryGetValue(layer.Id, out var e) ? e : 0,
     ```

3. **Verify the Fix**: Make the modifications in `src/MedWNetworkSim.Presentation/WorkspacePresentation.cs`. Check format (`dotnet format`), run `dotnet build`, and `dotnet test`.

4. **Complete Pre-Commit Steps**: Run the required `pre_commit_instructions` tests.

5. **Submit PR**: Submit the PR with the performance fix and add a journal entry.

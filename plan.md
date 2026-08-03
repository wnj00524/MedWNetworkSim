1. **Analyze `WorkspacePresentation.CreateEconomicMetrics` performance bottleneck**:
    * Currently, `CreateEconomicMetrics` uses LINQ to group edge flows from allocations: `allocations.SelectMany(...).GroupBy(...).ToDictionary(...)`.
    * This creates many short-lived objects (`IGrouping`, tuples, enumerators) and puts significant pressure on the Garbage Collector (GC), especially in a hot loop (like a GUI updating metrics per tick).
2. **Refactor the LINQ query to manual loops**:
    * Replace `var flowByEdge = allocations.SelectMany(...).GroupBy(...).ToDictionary(...)` with a pre-sized `Dictionary<string, double>` populated via standard `foreach` loops.
    * This change replaces $O(N \log N)$ operations and heavy LINQ allocation with a single $O(N)$ pass, directly addressing a critical GC pressure point on the UI thread as noted in `.jules/bolt.md`.
3. **Refactor `utilisation` LINQ**:
    * Change `var utilisation = network.Edges.Where(...).Select(...).ToList();` to use a manual `foreach` loop that populates a `List<double>` using `.Add()`. This further reduces enumerator and delegate allocations.
4. **Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.**
5. **Submit a Pull Request**:
    * Provide a summary with 💡 **What**, 🎯 **Why**, 📊 **Impact**, and 🔬 **Measurement**.


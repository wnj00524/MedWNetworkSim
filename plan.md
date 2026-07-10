1. **Optimize `ScenarioEditorViewModel` Properties `NodeIdOptions`, `EdgeIdOptions`, `TrafficTypeOptions`**
   - **File:** `src/MedWNetworkSim.Presentation/WorkspacePresentation.cs`
   - **Change:**
     The properties `NodeIdOptions`, `EdgeIdOptions`, and `TrafficTypeOptions` currently re-evaluate a LINQ query (`.Select().OrderBy().ToList()`) every time they are accessed:
     ```csharp
     public IReadOnlyList<string> NodeIdOptions => network.Nodes.Select(node => node.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
     ```
     This allocates a new list, creates enumerators, and performs a sort each time the property is requested, which can be expensive in the UI layer (especially during databinding).
     We should cache these lists as private fields and only invalidate/rebuild them when `RaiseReferenceDataChanged()` is called.
     Since `ScenarioEditorViewModel` holds a reference to `network` and has `RaiseReferenceDataChanged()` which raises property changes, we can cache the result in nullable backing fields:
     ```csharp
     private IReadOnlyList<string>? nodeIdOptions;
     public IReadOnlyList<string> NodeIdOptions => nodeIdOptions ??= network.Nodes.Select(node => node.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
     ```
     And then in `RaiseReferenceDataChanged`:
     ```csharp
     nodeIdOptions = null;
     edgeIdOptions = null;
     trafficTypeOptions = null;
     Raise(nameof(NodeIdOptions));
     // ...
     ```

2. **Check for other similar properties**
   - We will also optimize `TargetIdOptions` and `ScenarioDefinitions` if they have similar issues. `TargetIdOptions` relies on `NodeIdOptions`/`EdgeIdOptions` so it's already fast once they are cached.

3. **Complete pre-commit steps**
   - Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.

4. **Submit PR**
   - Create PR with title `⚡ Bolt: [performance improvement] Cache expensive UI options in ScenarioEditorViewModel`

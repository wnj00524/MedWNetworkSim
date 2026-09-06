import re

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'r') as f:
    content = f.read()

# Replace ToDictionary with manual loop around line 7950
search_block = """    private IReadOnlyDictionary<string, SimulationActorState> BuildSimulationActorMap() => SimulationActors
        .Where(actor => !string.IsNullOrWhiteSpace(actor.Id))
        .GroupBy(actor => actor.Id, Comparer)
        .ToDictionary(group => group.Key, group => group.First(), Comparer);"""

replace_block = """    private IReadOnlyDictionary<string, SimulationActorState> BuildSimulationActorMap()
    {
        var map = new Dictionary<string, SimulationActorState>(Comparer);
        foreach (var actor in SimulationActors)
        {
            if (!string.IsNullOrWhiteSpace(actor.Id))
            {
                map.TryAdd(actor.Id, actor);
            }
        }
        return map;
    }"""

content = content.replace(search_block, replace_block)

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'w') as f:
    f.write(content)

print("Patch applied.")

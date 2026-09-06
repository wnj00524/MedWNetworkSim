import re

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'r') as f:
    content = f.read()

search_block = """            var nodeStateLookup = new Dictionary<string, TemporalNetworkSimulationEngine.TemporalNodeTrafficState>(Comparer);
            var nodeBacklogByTrafficLookup = new Dictionary<string, Dictionary<string, double>>(Comparer);

            foreach (var pair in timeline.NodeStates)
            {
                if (!nodeStateLookup.ContainsKey(pair.Key.NodeId))
                {
                    nodeStateLookup[pair.Key.NodeId] = pair.Value;
                }

                if (pair.Value.DemandBacklog > 0d)
                {
                    if (!nodeBacklogByTrafficLookup.TryGetValue(pair.Key.NodeId, out var dict))
                    {
                        dict = new Dictionary<string, double>(Comparer);
                        nodeBacklogByTrafficLookup[pair.Key.NodeId] = dict;
                    }
                    ref var backlog = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(dict, pair.Key.TrafficType, out _);
                    backlog += pair.Value.DemandBacklog;
                }
            }"""

replace_block = """            var nodeStateLookup = new Dictionary<string, TemporalNetworkSimulationEngine.TemporalNodeStateSnapshot>(Comparer);
            var nodeBacklogByTrafficLookup = new Dictionary<string, Dictionary<string, double>>(Comparer);

            foreach (var pair in timeline.NodeStates)
            {
                if (!nodeStateLookup.ContainsKey(pair.Key.NodeId))
                {
                    nodeStateLookup[pair.Key.NodeId] = pair.Value;
                }

                if (pair.Value.DemandBacklog > 0d)
                {
                    if (!nodeBacklogByTrafficLookup.TryGetValue(pair.Key.NodeId, out var dict))
                    {
                        dict = new Dictionary<string, double>(Comparer);
                        nodeBacklogByTrafficLookup[pair.Key.NodeId] = dict;
                    }
                    ref var backlog = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(dict, pair.Key.TrafficType, out _);
                    backlog += pair.Value.DemandBacklog;
                }
            }"""

content = content.replace(search_block, replace_block)

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'w') as f:
    f.write(content)

print("Patch 6 applied.")

import re

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'r') as f:
    content = f.read()

search_block = """        if (timeline is not null)
        {
            foreach (var node in Scene.Nodes)
            {
                if (!nodesById.TryGetValue(node.Id, out var nodeModel)) continue;
                var state = timeline.NodeStates
                    .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id))
                    .Select(pair => pair.Value)
                    .FirstOrDefault();
                var backlogByTraffic = timeline.NodeStates
                    .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id) && pair.Value.DemandBacklog > 0d)
                    .GroupBy(pair => pair.Key.TrafficType, pair => pair.Value.DemandBacklog, Comparer)
                    .Select(group => new KeyValuePair<string, double>(group.Key, group.Sum()))
                    .ToList();
                var pressure = timeline.NodePressureById.GetValueOrDefault(node.Id);
                node.MetricsLabel = string.Empty;
                node.DetailLines = BuildNodeDetailLines(nodeModel, backlogByTraffic, pressure.Score > 0d ? pressure : null);
                UpdateSceneNodeLayout(node, nodeModel, pressure.Score > 0d ? pressure : null, graphRenderer.GetZoomTier(Viewport.Zoom));
                node.HasWarning = pressure.Score > 0d || state.DemandBacklog > 0d;
            }
        }"""

replace_block = """        if (timeline is not null)
        {
            var nodeStateLookup = new Dictionary<string, TemporalNetworkSimulationEngine.TemporalNodeTrafficState>(Comparer);
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
            }

            foreach (var node in Scene.Nodes)
            {
                if (!nodesById.TryGetValue(node.Id, out var nodeModel)) continue;

                var state = nodeStateLookup.GetValueOrDefault(node.Id);

                var backlogByTraffic = new List<KeyValuePair<string, double>>();
                if (nodeBacklogByTrafficLookup.TryGetValue(node.Id, out var trafficDict))
                {
                    foreach (var pair in trafficDict)
                    {
                        backlogByTraffic.Add(new KeyValuePair<string, double>(pair.Key, pair.Value));
                    }
                }

                var pressure = timeline.NodePressureById.GetValueOrDefault(node.Id);
                node.MetricsLabel = string.Empty;
                node.DetailLines = BuildNodeDetailLines(nodeModel, backlogByTraffic, pressure.Score > 0d ? pressure : null);
                UpdateSceneNodeLayout(node, nodeModel, pressure.Score > 0d ? pressure : null, graphRenderer.GetZoomTier(Viewport.Zoom));
                node.HasWarning = pressure.Score > 0d || (state != null && state.DemandBacklog > 0d);
            }
        }"""

content = content.replace(search_block, replace_block)

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'w') as f:
    f.write(content)

print("Patch 5 applied.")

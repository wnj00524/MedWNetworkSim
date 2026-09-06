import re

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'r') as f:
    content = f.read()

search_block = """            var topUnmetNode = timeline.NodeStates
                .Where(pair => pair.Value.DemandBacklog > 0d)
                .GroupBy(pair => pair.Key.NodeId, pair => pair.Value.DemandBacklog, Comparer)
                .Select(group => new { NodeId = group.Key, Backlog = group.Sum() })
                .OrderByDescending(item => item.Backlog)
                .FirstOrDefault();
            if (topUnmetNode is not null)
            {
                metrics.Add(new ReportMetricViewModel
                {
                    Label = "Top unmet-need node",
                    Value = $"{ResolveNodeName(topUnmetNode.NodeId)} {ReportExportService.FormatNumber(topUnmetNode.Backlog)}",
                    Activate = () => SelectNodeForEdit(topUnmetNode.NodeId)
                });
            }

            var topTrafficBacklog = timeline.NodeStates
                .Where(pair => pair.Value.DemandBacklog > 0d)
                .GroupBy(pair => pair.Key.TrafficType, pair => pair.Value.DemandBacklog, Comparer)
                .Select(group => new { TrafficType = group.Key, Backlog = group.Sum() })
                .OrderByDescending(item => item.Backlog)
                .FirstOrDefault();
            if (topTrafficBacklog is not null)"""

replace_block = """            var nodeBacklogMap = new Dictionary<string, double>(Comparer);
            var trafficBacklogMap = new Dictionary<string, double>(Comparer);

            foreach (var pair in timeline.NodeStates)
            {
                if (pair.Value.DemandBacklog > 0d)
                {
                    ref var nodeBacklog = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(nodeBacklogMap, pair.Key.NodeId, out _);
                    nodeBacklog += pair.Value.DemandBacklog;

                    ref var trafficBacklog = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(trafficBacklogMap, pair.Key.TrafficType, out _);
                    trafficBacklog += pair.Value.DemandBacklog;
                }
            }

            string? topUnmetNodeId = null;
            double topUnmetNodeBacklog = 0d;
            foreach (var pair in nodeBacklogMap)
            {
                if (topUnmetNodeId is null || pair.Value > topUnmetNodeBacklog)
                {
                    topUnmetNodeId = pair.Key;
                    topUnmetNodeBacklog = pair.Value;
                }
            }

            if (topUnmetNodeId is not null)
            {
                var nodeIdCapture = topUnmetNodeId;
                metrics.Add(new ReportMetricViewModel
                {
                    Label = "Top unmet-need node",
                    Value = $"{ResolveNodeName(topUnmetNodeId)} {ReportExportService.FormatNumber(topUnmetNodeBacklog)}",
                    Activate = () => SelectNodeForEdit(nodeIdCapture)
                });
            }

            string? topTrafficBacklogType = null;
            double topTrafficBacklogValue = 0d;
            foreach (var pair in trafficBacklogMap)
            {
                if (topTrafficBacklogType is null || pair.Value > topTrafficBacklogValue)
                {
                    topTrafficBacklogType = pair.Key;
                    topTrafficBacklogValue = pair.Value;
                }
            }

            if (topTrafficBacklogType is not null)"""

content = content.replace(search_block, replace_block)

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'w') as f:
    f.write(content)

print("Patch 3 applied.")

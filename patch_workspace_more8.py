import re

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'r') as f:
    content = f.read()

search_block = """    public IEnumerable<FlowDataPoint> GetFlowSeries()
    {
        if (lastOutcomes.Count == 0 && lastTimelineStepResult is null)
        {
            return [];
        }

        if (lastTimelineStepResult is not null)
        {
            return lastTimelineStepResult.NodeStates
                .GroupBy(pair => pair.Key.TrafficType, StringComparer.OrdinalIgnoreCase)
                .Select(group => new FlowDataPoint(
                    group.Key,
                    group.Sum(pair => pair.Value.AvailableSupply + pair.Value.DemandBacklog),
                    lastTimelineStepResult.Allocations
                        .Where(allocation => string.Equals(allocation.TrafficType, group.Key, StringComparison.OrdinalIgnoreCase))
                        .Sum(allocation => allocation.Quantity),
                    group.Sum(pair => pair.Value.DemandBacklog),
                    group.Sum(pair => pair.Value.AvailableSupply)))
                .OrderBy(point => point.Label, Comparer)
                .ToList();
        }"""

replace_block = """    public IEnumerable<FlowDataPoint> GetFlowSeries()
    {
        if (lastOutcomes.Count == 0 && lastTimelineStepResult is null)
        {
            return [];
        }

        if (lastTimelineStepResult is not null)
        {
            var supplyBacklogMap = new Dictionary<string, (double AvailableSupply, double DemandBacklog)>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in lastTimelineStepResult.NodeStates)
            {
                ref var entry = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(supplyBacklogMap, pair.Key.TrafficType, out _);
                entry.AvailableSupply += pair.Value.AvailableSupply;
                entry.DemandBacklog += pair.Value.DemandBacklog;
            }

            var allocationMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var allocation in lastTimelineStepResult.Allocations)
            {
                ref var qty = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(allocationMap, allocation.TrafficType, out _);
                qty += allocation.Quantity;
            }

            var result = new List<FlowDataPoint>(supplyBacklogMap.Count);
            foreach (var pair in supplyBacklogMap)
            {
                var allocQty = allocationMap.GetValueOrDefault(pair.Key, 0d);
                result.Add(new FlowDataPoint(
                    pair.Key,
                    pair.Value.AvailableSupply + pair.Value.DemandBacklog,
                    allocQty,
                    pair.Value.DemandBacklog,
                    pair.Value.AvailableSupply));
            }
            result.Sort((a, b) => Comparer.Compare(a.Label, b.Label));
            return result;
        }"""

content = content.replace(search_block, replace_block)

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'w') as f:
    f.write(content)

print("Patch 8 applied.")

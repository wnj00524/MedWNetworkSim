import re

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'r') as f:
    content = f.read()

# Replace GroupBy ToDictionary around line 9158
search_block = """        var productionPrices = allocations.Count > 0
            ? allocations
                .Where(allocation => allocation.Quantity > 0d)
                .GroupBy(allocation => allocation.ProducerNodeId, Comparer)
                .Select(group => WeightedAverage(group, allocation => allocation.SourceUnitCostPerUnit))
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .Distinct()
                .OrderBy(value => value)
                .Select(value => ReportExportService.FormatNumber(value))
                .ToList()"""

# We can optimize this but let's look at it closer.

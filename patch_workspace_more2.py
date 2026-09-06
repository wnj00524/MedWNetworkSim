import re

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'r') as f:
    content = f.read()

# Replace GroupBy ToDictionary around line 9158
search_block = """    private string BuildTrafficPriceSummary(string trafficType)
    {
        var allocations = GetCurrentAllocations()
            .Where(allocation => Comparer.Equals(allocation.TrafficType, trafficType))
            .ToList();
        var productionPrices = allocations.Count > 0
            ? allocations
                .Where(allocation => allocation.Quantity > 0d)
                .GroupBy(allocation => allocation.ProducerNodeId, Comparer)
                .Select(group => WeightedAverage(group, allocation => allocation.SourceUnitCostPerUnit))
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .Distinct()
                .OrderBy(value => value)
                .Select(value => ReportExportService.FormatNumber(value))
                .ToList()
            : network.Nodes
            .SelectMany(node => node.TrafficProfiles)
            .Where(profile => profile.Production > 0d && Comparer.Equals(profile.TrafficType, trafficType))
            .Select(profile => Math.Max(0d, profile.UnitPrice))
            .Distinct()
            .OrderBy(value => value)
            .Select(value => ReportExportService.FormatNumber(value))
            .ToList();
        var consumptionPrices = allocations.Count > 0
            ? allocations
                .Where(allocation => allocation.Quantity > 0d)
                .GroupBy(allocation => allocation.ConsumerNodeId, Comparer)
                .Select(group => WeightedAverage(group, allocation => allocation.DeliveredCostPerUnit))
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .Distinct()
                .OrderBy(value => value)
                .Select(value => ReportExportService.FormatNumber(value))
                .ToList()
            : network.Nodes
            .SelectMany(node => node.TrafficProfiles)
            .Where(profile => profile.Consumption > 0d && Comparer.Equals(profile.TrafficType, trafficType))
            .Select(profile => Math.Max(0d, profile.UnitPrice))
            .Distinct()
            .OrderBy(value => value)
            .Select(value => ReportExportService.FormatNumber(value))
            .ToList();"""

replace_block = """    private string BuildTrafficPriceSummary(string trafficType)
    {
        var allocations = new List<RouteAllocation>();
        foreach (var allocation in GetCurrentAllocations())
        {
            if (Comparer.Equals(allocation.TrafficType, trafficType))
            {
                allocations.Add(allocation);
            }
        }

        List<string> productionPrices;
        List<string> consumptionPrices;

        if (allocations.Count > 0)
        {
            var prodMap = new Dictionary<string, (double TotalValue, double TotalQuantity)>(Comparer);
            var consMap = new Dictionary<string, (double TotalValue, double TotalQuantity)>(Comparer);

            foreach (var allocation in allocations)
            {
                if (allocation.Quantity > 0d)
                {
                    ref var prodEntry = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(prodMap, allocation.ProducerNodeId, out _);
                    prodEntry.TotalValue += allocation.Quantity * allocation.SourceUnitCostPerUnit;
                    prodEntry.TotalQuantity += allocation.Quantity;

                    ref var consEntry = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(consMap, allocation.ConsumerNodeId, out _);
                    consEntry.TotalValue += allocation.Quantity * allocation.DeliveredCostPerUnit;
                    consEntry.TotalQuantity += allocation.Quantity;
                }
            }

            var prodUniquePrices = new HashSet<double>();
            foreach (var pair in prodMap)
            {
                if (pair.Value.TotalQuantity > 0d)
                {
                    prodUniquePrices.Add(pair.Value.TotalValue / pair.Value.TotalQuantity);
                }
            }
            var prodPriceList = prodUniquePrices.ToList();
            prodPriceList.Sort();
            productionPrices = new List<string>(prodPriceList.Count);
            foreach (var val in prodPriceList)
            {
                productionPrices.Add(ReportExportService.FormatNumber(val));
            }

            var consUniquePrices = new HashSet<double>();
            foreach (var pair in consMap)
            {
                if (pair.Value.TotalQuantity > 0d)
                {
                    consUniquePrices.Add(pair.Value.TotalValue / pair.Value.TotalQuantity);
                }
            }
            var consPriceList = consUniquePrices.ToList();
            consPriceList.Sort();
            consumptionPrices = new List<string>(consPriceList.Count);
            foreach (var val in consPriceList)
            {
                consumptionPrices.Add(ReportExportService.FormatNumber(val));
            }
        }
        else
        {
            var prodUniquePrices = new HashSet<double>();
            var consUniquePrices = new HashSet<double>();

            foreach (var node in network.Nodes)
            {
                foreach (var profile in node.TrafficProfiles)
                {
                    if (Comparer.Equals(profile.TrafficType, trafficType))
                    {
                        if (profile.Production > 0d)
                        {
                            prodUniquePrices.Add(Math.Max(0d, profile.UnitPrice));
                        }
                        if (profile.Consumption > 0d)
                        {
                            consUniquePrices.Add(Math.Max(0d, profile.UnitPrice));
                        }
                    }
                }
            }

            var prodPriceList = prodUniquePrices.ToList();
            prodPriceList.Sort();
            productionPrices = new List<string>(prodPriceList.Count);
            foreach (var val in prodPriceList)
            {
                productionPrices.Add(ReportExportService.FormatNumber(val));
            }

            var consPriceList = consUniquePrices.ToList();
            consPriceList.Sort();
            consumptionPrices = new List<string>(consPriceList.Count);
            foreach (var val in consPriceList)
            {
                consumptionPrices.Add(ReportExportService.FormatNumber(val));
            }
        }"""

content = content.replace(search_block, replace_block)

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'w') as f:
    f.write(content)

print("Patch 2 applied.")

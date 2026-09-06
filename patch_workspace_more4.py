import re

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'r') as f:
    content = f.read()

search_block = """            {
                metrics.Add(new ReportMetricViewModel
                {
                    Label = "Top traffic backlog",
                    Value = $"{topTrafficBacklog.TrafficType} {ReportExportService.FormatNumber(topTrafficBacklog.Backlog)}",
                    Activate = () =>
                    {
                        SelectedInspectorTab = InspectorTabTarget.TrafficTypes;
                        SelectedTrafficDefinitionItem = TrafficDefinitions.FirstOrDefault(item => Comparer.Equals(item.Name, topTrafficBacklog.TrafficType));
                        NotifyVisualChanged();
                    }
                });
            }"""

replace_block = """            {
                var trafficTypeCapture = topTrafficBacklogType;
                metrics.Add(new ReportMetricViewModel
                {
                    Label = "Top traffic backlog",
                    Value = $"{topTrafficBacklogType} {ReportExportService.FormatNumber(topTrafficBacklogValue)}",
                    Activate = () =>
                    {
                        SelectedInspectorTab = InspectorTabTarget.TrafficTypes;
                        SelectedTrafficDefinitionItem = TrafficDefinitions.FirstOrDefault(item => Comparer.Equals(item.Name, trafficTypeCapture));
                        NotifyVisualChanged();
                    }
                });
            }"""

content = content.replace(search_block, replace_block)

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'w') as f:
    f.write(content)

print("Patch 4 applied.")

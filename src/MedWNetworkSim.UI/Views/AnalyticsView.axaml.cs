using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using MedWNetworkSim.Presentation;

namespace MedWNetworkSim.UI.Views;

public partial class AnalyticsView : UserControl
{
    private readonly ComboBox? trafficTypeFilterComboBox;

    public AnalyticsView()
    {
        AvaloniaXamlLoader.Load(this);

        var trafficTypeFilterHost = this.FindControl<ContentControl>("TrafficTypeFilterHost");
        if (trafficTypeFilterHost is null)
        {
            return;
        }

        trafficTypeFilterComboBox = new ComboBox
        {
            MinWidth = 180
        };
        AutomationProperties.SetName(trafficTypeFilterComboBox, "Traffic type");
        trafficTypeFilterComboBox.Bind(
            ItemsControl.ItemsSourceProperty,
            new Binding(nameof(WorkspaceViewModel.SankeyTrafficTypeNameOptions)));
        trafficTypeFilterComboBox.Bind(
            SelectingItemsControl.SelectedItemProperty,
            new Binding(nameof(WorkspaceViewModel.SankeyTrafficTypeFilterSelection), BindingMode.TwoWay));
        trafficTypeFilterComboBox.SelectionChanged += (_, _) => ApplyTrafficTypeSelection();
        trafficTypeFilterHost.Content = trafficTypeFilterComboBox;
    }

    public void ApplyTrafficTypeSelection()
    {
        if (DataContext is WorkspaceViewModel workspace
            && trafficTypeFilterComboBox?.SelectedItem is string selectedTrafficType
            && !string.Equals(workspace.SankeyTrafficTypeFilterSelection, selectedTrafficType, StringComparison.Ordinal))
        {
            workspace.SankeyTrafficTypeFilterSelection = selectedTrafficType;
        }
    }

    public void SelectTrafficType(string trafficType)
    {
        if (trafficTypeFilterComboBox is null)
        {
            return;
        }

        trafficTypeFilterComboBox.SelectedItem = trafficType;
        if (DataContext is WorkspaceViewModel workspace
            && !string.Equals(workspace.SankeyTrafficTypeFilterSelection, trafficType, StringComparison.Ordinal))
        {
            workspace.SankeyTrafficTypeFilterSelection = trafficType;
        }
    }
}

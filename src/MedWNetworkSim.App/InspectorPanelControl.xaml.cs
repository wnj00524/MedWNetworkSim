using System.Windows;
using System.Windows.Controls;
using MedWNetworkSim.App.ViewModels;

namespace MedWNetworkSim.App;

public partial class InspectorPanelControl : UserControl
{
    public InspectorPanelControl()
    {
        InitializeComponent();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private Window? HostWindow => Window.GetWindow(this);

    private void OpenNetworkProperties_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || HostWindow is not MainWindow owner)
        {
            return;
        }

        var window = new NetworkPropertiesWindow(ViewModel)
        {
            Owner = owner
        };

        window.ShowDialog();
    }

    private void OpenTrafficTypes_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || HostWindow is not MainWindow owner)
        {
            return;
        }

        var window = new TrafficTypeEditorWindow(new TrafficTypeEditorViewModel(ViewModel))
        {
            Owner = owner
        };

        window.ShowDialog();
    }

    private void OpenNodeEditor_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || HostWindow is not MainWindow owner)
        {
            return;
        }

        var window = new NodeEditorWindow(ViewModel)
        {
            Owner = owner
        };

        window.ShowDialog();
    }

    private void OpenEdgeEditor_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || HostWindow is not MainWindow owner)
        {
            return;
        }

        var window = new EdgeEditorWindow(new EdgeEditorViewModel(ViewModel))
        {
            Owner = owner
        };

        window.ShowDialog();
    }

    private void OpenBulkEdit_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || HostWindow is not MainWindow owner)
        {
            return;
        }

        var window = new BulkApplyTrafficRoleWindow(ViewModel)
        {
            Owner = owner
        };

        window.ShowDialog();
    }
}

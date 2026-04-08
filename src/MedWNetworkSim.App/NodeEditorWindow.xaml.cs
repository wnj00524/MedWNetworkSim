using System.Windows;
using MedWNetworkSim.App.ViewModels;

namespace MedWNetworkSim.App;

public partial class NodeEditorWindow : Window
{
    public NodeEditorWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        // The window edits the same shared view model as the main screen, so changes are live immediately.
        DataContext = ViewModel;
    }

    public MainWindowViewModel ViewModel { get; }

    private void AddNode_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.AddNode);
    }

    private void RemoveNode_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.RemoveSelectedNode);
    }

    private void AddTrafficRole_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.AddTrafficProfileToSelectedNode);
    }

    private void ApplyTrafficRoleToAllNodes_Click(object sender, RoutedEventArgs e)
    {
        var window = new BulkApplyTrafficRoleWindow(ViewModel)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void RemoveTrafficRole_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.RemoveSelectedTrafficProfileFromNode);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ExecuteWithErrorHandling(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "MedW Network Simulator",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

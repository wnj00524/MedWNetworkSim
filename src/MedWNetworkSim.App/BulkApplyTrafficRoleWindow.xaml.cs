using System.Windows;
using MedWNetworkSim.App.ViewModels;

namespace MedWNetworkSim.App;

public partial class BulkApplyTrafficRoleWindow : Window
{
    private readonly MainWindowViewModel mainWindowViewModel;

    public BulkApplyTrafficRoleWindow(MainWindowViewModel mainWindowViewModel)
    {
        InitializeComponent();
        this.mainWindowViewModel = mainWindowViewModel;
        ViewModel = new BulkApplyTrafficRoleWindowViewModel(
            mainWindowViewModel.GetAvailableTrafficTypeNames(),
            mainWindowViewModel.SelectedNodeTrafficType,
            mainWindowViewModel.SelectedNodeRoleName,
            mainWindowViewModel.SelectedNodeTrafficProfile?.Production,
            mainWindowViewModel.SelectedNodeTrafficProfile?.Consumption,
            mainWindowViewModel.SelectedNode?.TranshipmentCapacity);
        DataContext = ViewModel;
    }

    public BulkApplyTrafficRoleWindowViewModel ViewModel { get; }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(() =>
        {
            var options = ViewModel.BuildOptions();
            mainWindowViewModel.ApplyTrafficRoleToAllNodes(options);
            Close();
        });
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

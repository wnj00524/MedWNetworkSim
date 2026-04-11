using System.Windows;
using MedWNetworkSim.App.ViewModels;

namespace MedWNetworkSim.App;

public partial class NetworkPropertiesWindow : Window
{
    public NetworkPropertiesWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
    }

    public MainWindowViewModel ViewModel { get; }

    private void ApplyDefaultAllocationModeToAll_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.ApplyDefaultAllocationModeToAllTrafficDefinitions);
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

using System.Windows;
using MedWNetworkSim.App.ViewModels;

namespace MedWNetworkSim.App;

public partial class EdgeEditorWindow : Window
{
    public EdgeEditorWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
    }

    public MainWindowViewModel ViewModel { get; }

    private void AddEdge_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.AddEdge);
    }

    private void RemoveEdge_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.RemoveSelectedEdge);
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

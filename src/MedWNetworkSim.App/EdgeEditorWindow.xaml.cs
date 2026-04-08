using System.Windows;
using MedWNetworkSim.App.ViewModels;

namespace MedWNetworkSim.App;

public partial class EdgeEditorWindow : Window
{
    public EdgeEditorWindow(EdgeEditorViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
    }

    public EdgeEditorViewModel ViewModel { get; }

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

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.Dispose();
        base.OnClosed(e);
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

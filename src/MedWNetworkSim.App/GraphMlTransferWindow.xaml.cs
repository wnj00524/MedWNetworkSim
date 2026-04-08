using System.Windows;
using Microsoft.Win32;
using MedWNetworkSim.App.ViewModels;

namespace MedWNetworkSim.App;

public partial class GraphMlTransferWindow : Window
{
    private readonly MainWindowViewModel mainWindowViewModel;

    public GraphMlTransferWindow(MainWindowViewModel mainWindowViewModel)
    {
        InitializeComponent();
        this.mainWindowViewModel = mainWindowViewModel;
        ViewModel = new GraphMlTransferWindowViewModel(
            mainWindowViewModel.GetAvailableTrafficTypeNames(),
            mainWindowViewModel.SuggestedGraphMlFileName,
            mainWindowViewModel.HasNetwork);
        DataContext = ViewModel;
    }

    public GraphMlTransferWindowViewModel ViewModel { get; }

    private void ImportGraphMl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import GraphML graph",
            Filter = "GraphML (*.graphml;*.xml)|*.graphml;*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ExecuteWithErrorHandling(() =>
        {
            var options = ViewModel.BuildTransferOptions();
            mainWindowViewModel.LoadFromGraphMl(dialog.FileName, options);
            Close();
        });
    }

    private void ExportGraphMl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export GraphML graph",
            Filter = "GraphML (*.graphml)|*.graphml|XML (*.xml)|*.xml|All files (*.*)|*.*",
            FileName = ViewModel.SuggestedExportFileName,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ExecuteWithErrorHandling(() =>
        {
            var options = ViewModel.BuildTransferOptions();
            mainWindowViewModel.SaveToGraphMl(dialog.FileName, options);
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

using System.Windows;
using System.IO;
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

    private void BrowseImportGraphMl_Click(object sender, RoutedEventArgs e)
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

        ViewModel.ImportFilePath = dialog.FileName;
    }

    private void ImportGraphMl_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(() =>
        {
            var options = ViewModel.BuildTransferOptions();
            var importPath = GetValidatedImportPath();
            mainWindowViewModel.LoadFromGraphMl(importPath, options);
            Close();
        });
    }

    private void BrowseExportGraphMl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export GraphML graph",
            Filter = "GraphML (*.graphml)|*.graphml|XML (*.xml)|*.xml|All files (*.*)|*.*",
            FileName = string.IsNullOrWhiteSpace(ViewModel.ExportFilePath)
                ? ViewModel.SuggestedExportFileName
                : Path.GetFileName(ViewModel.ExportFilePath),
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ViewModel.ExportFilePath = dialog.FileName;
    }

    private void ExportGraphMl_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(() =>
        {
            var options = ViewModel.BuildTransferOptions();
            var exportPath = GetNormalizedExportPath();
            mainWindowViewModel.SaveToGraphMl(exportPath, options);
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

    private string GetValidatedImportPath()
    {
        if (string.IsNullOrWhiteSpace(ViewModel.ImportFilePath))
        {
            throw new InvalidOperationException("Choose a GraphML file to import.");
        }

        var importPath = Path.GetFullPath(ViewModel.ImportFilePath.Trim());
        if (!File.Exists(importPath))
        {
            throw new FileNotFoundException("The selected GraphML import file was not found.", importPath);
        }

        return importPath;
    }

    private string GetNormalizedExportPath()
    {
        if (string.IsNullOrWhiteSpace(ViewModel.ExportFilePath))
        {
            throw new InvalidOperationException("Choose a GraphML file path to save.");
        }

        var exportPath = ViewModel.ExportFilePath.Trim();
        if (!Path.HasExtension(exportPath))
        {
            exportPath += ".graphml";
        }

        return Path.GetFullPath(exportPath);
    }
}

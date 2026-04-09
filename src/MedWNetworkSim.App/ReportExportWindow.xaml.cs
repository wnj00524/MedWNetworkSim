using Microsoft.Win32;
using System.IO;
using System.Windows;
using MedWNetworkSim.App.ViewModels;

namespace MedWNetworkSim.App;

public partial class ReportExportWindow : Window
{
    private readonly MainWindowViewModel mainWindowViewModel;

    public ReportExportWindow(MainWindowViewModel mainWindowViewModel)
    {
        InitializeComponent();
        this.mainWindowViewModel = mainWindowViewModel;
        ViewModel = new ReportExportWindowViewModel(mainWindowViewModel.SuggestedReportFileName);
        DataContext = ViewModel;
    }

    public ReportExportWindowViewModel ViewModel { get; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export report",
            Filter = "Markdown report (*.md)|*.md|Text file (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = Path.GetFileName(ViewModel.ReportPath),
            OverwritePrompt = true
        };

        var currentDirectory = Path.GetDirectoryName(ViewModel.ReportPath);
        if (!string.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory))
        {
            dialog.InitialDirectory = currentDirectory;
        }

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.ReportPath = dialog.FileName;
        }
    }

    private void ExportCurrentReport_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(() =>
        {
            var path = NormalizeReportPath(ViewModel.ReportPath);
            mainWindowViewModel.ExportCurrentReport(path);
            ViewModel.ReportPath = path;
        });
    }

    private void ExportTimelineReport_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(() =>
        {
            var path = NormalizeReportPath(ViewModel.ReportPath);
            mainWindowViewModel.ExportTimelineReport(path, ViewModel.GetTimelinePeriods());
            ViewModel.ReportPath = path;
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string NormalizeReportPath(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            throw new InvalidOperationException("Choose a report file path before exporting.");
        }

        if (!Path.HasExtension(reportPath))
        {
            return reportPath + ".md";
        }

        return reportPath;
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

using Microsoft.Win32;
using System.IO;
using System.Windows;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.ViewModels;

namespace MedWNetworkSim.App;

public partial class ReportExportWindow : Window
{
    private readonly MainWindowViewModel mainWindowViewModel;

    public ReportExportWindow(MainWindowViewModel mainWindowViewModel)
    {
        InitializeComponent();
        this.mainWindowViewModel = mainWindowViewModel;
        ViewModel = new ReportExportWindowViewModel(mainWindowViewModel.SuggestedReportFilePath);
        DataContext = ViewModel;
    }

    public ReportExportWindowViewModel ViewModel { get; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export report",
            Filter = ViewModel.SelectedFormat == ReportExportFormat.Csv
                ? "CSV report (*.csv)|*.csv|All files (*.*)|*.*"
                : "Markdown report (*.md)|*.md|All files (*.*)|*.*",
            FileName = Path.GetFileName(ViewModel.ReportPath),
            DefaultExt = ViewModel.SelectedFormat == ReportExportFormat.Csv ? ".csv" : ".md",
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
            var path = NormalizeReportPath(ViewModel.ReportPath, ViewModel.SelectedFormat);
            mainWindowViewModel.ExportCurrentReport(path, ViewModel.SelectedFormat);
            ViewModel.ReportPath = path;
        });
    }

    private void ExportTimelineReport_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(() =>
        {
            var path = NormalizeReportPath(ViewModel.ReportPath, ViewModel.SelectedFormat);
            mainWindowViewModel.ExportTimelineReport(path, ViewModel.GetTimelinePeriods(), ViewModel.SelectedFormat);
            ViewModel.ReportPath = path;
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string NormalizeReportPath(string reportPath, ReportExportFormat format)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            throw new InvalidOperationException("Choose a report file path before exporting.");
        }

        return ReportExportWindowViewModel.ApplyFormatExtension(reportPath, format);
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

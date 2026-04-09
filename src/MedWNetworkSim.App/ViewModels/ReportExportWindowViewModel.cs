using System.IO;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class ReportExportWindowViewModel : ObservableObject
{
    private string reportPath;
    private string timelinePeriodsText = "12";
    private ReportExportFormat selectedFormat = ReportExportFormat.Html;

    public ReportExportWindowViewModel(string suggestedReportPath)
    {
        reportPath = suggestedReportPath;
    }

    public Array FormatOptions { get; } = Enum.GetValues(typeof(ReportExportFormat));

    public string ReportPath
    {
        get => reportPath;
        set
        {
            if (!SetProperty(ref reportPath, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanExport));
        }
    }

    public string TimelinePeriodsText
    {
        get => timelinePeriodsText;
        set => SetProperty(ref timelinePeriodsText, value);
    }

    public ReportExportFormat SelectedFormat
    {
        get => selectedFormat;
        set
        {
            if (!SetProperty(ref selectedFormat, value))
            {
                return;
            }

            ReportPath = ApplyFormatExtension(ReportPath, value);
        }
    }

    public bool CanExport => !string.IsNullOrWhiteSpace(ReportPath);

    public int GetTimelinePeriods()
    {
        if (!int.TryParse(TimelinePeriodsText, out var periods) || periods <= 0)
        {
            throw new InvalidOperationException("Timeline periods must be a whole number greater than zero.");
        }

        return periods;
    }

    public static string ApplyFormatExtension(string path, ReportExportFormat format)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var desiredExtension = format == ReportExportFormat.Csv ? ".csv" : ".html";
        if (!Path.HasExtension(path))
        {
            return path + desiredExtension;
        }

        return Path.ChangeExtension(path, desiredExtension);
    }
}

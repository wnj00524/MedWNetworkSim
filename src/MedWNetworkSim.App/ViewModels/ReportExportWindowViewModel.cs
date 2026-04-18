using System.IO;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class ReportExportWindowViewModel : ObservableObject
{
    private string reportPath;
    private string timelinePeriodsText = "12";
    private ReportExportFormat selectedFormat = ReportExportFormat.Html;
    private ReportExportKind selectedExportKind;

    public ReportExportWindowViewModel(string suggestedReportPath, ReportExportKind initialExportKind = ReportExportKind.Current)
    {
        reportPath = suggestedReportPath;
        selectedExportKind = initialExportKind;
    }

    public Array FormatOptions { get; } = Enum.GetValues(typeof(ReportExportFormat));

    public Array ExportKindOptions { get; } = Enum.GetValues(typeof(ReportExportKind));

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

    public ReportExportKind SelectedExportKind
    {
        get => selectedExportKind;
        set
        {
            if (!SetProperty(ref selectedExportKind, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsTimelineExport));
            OnPropertyChanged(nameof(ExportDescription));
            OnPropertyChanged(nameof(ExportButtonText));
        }
    }

    public bool IsTimelineExport => SelectedExportKind == ReportExportKind.Timeline;

    public string ExportDescription => IsTimelineExport
        ? "Timeline report exports per-period route movements, edge usage, node activity, and overall totals across the number of periods you choose."
        : "Current report exports the latest one-shot network overview, traffic definitions, places, routes, traffic outcomes, consumer costs, and routed movements.";

    public string ExportButtonText => IsTimelineExport ? "Export timeline report" : "Export current report";

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

        var desiredExtension = format switch
        {
            ReportExportFormat.Csv => ".csv",
            ReportExportFormat.Json => ".json",
            _ => ".html"
        };
        if (!Path.HasExtension(path))
        {
            return path + desiredExtension;
        }

        return Path.ChangeExtension(path, desiredExtension);
    }
}

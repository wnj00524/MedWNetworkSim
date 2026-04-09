namespace MedWNetworkSim.App.ViewModels;

public sealed class ReportExportWindowViewModel : ObservableObject
{
    private string reportPath;
    private string timelinePeriodsText = "12";

    public ReportExportWindowViewModel(string suggestedFileName)
    {
        reportPath = suggestedFileName;
    }

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

    public bool CanExport => !string.IsNullOrWhiteSpace(ReportPath);

    public int GetTimelinePeriods()
    {
        if (!int.TryParse(TimelinePeriodsText, out var periods) || periods <= 0)
        {
            throw new InvalidOperationException("Timeline periods must be a whole number greater than zero.");
        }

        return periods;
    }
}

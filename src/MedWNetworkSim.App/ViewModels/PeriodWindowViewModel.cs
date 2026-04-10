using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class PeriodWindowViewModel : ObservableObject
{
    private int? startPeriod;
    private int? endPeriod;

    public PeriodWindowViewModel(PeriodWindow window)
    {
        startPeriod = window.StartPeriod;
        endPeriod = window.EndPeriod;
    }

    public int? StartPeriod
    {
        get => startPeriod;
        set => SetProperty(ref startPeriod, value);
    }

    public int? EndPeriod
    {
        get => endPeriod;
        set => SetProperty(ref endPeriod, value);
    }

    public PeriodWindow ToModel()
    {
        return new PeriodWindow
        {
            StartPeriod = StartPeriod,
            EndPeriod = EndPeriod
        };
    }
}

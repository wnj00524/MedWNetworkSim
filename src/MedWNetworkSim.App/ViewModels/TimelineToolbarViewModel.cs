namespace MedWNetworkSim.App.ViewModels;

public sealed class TimelineToolbarViewModel : ObservableObject
{
    private int currentPeriod;
    private string headline = "Timeline not started";

    public int CurrentPeriod
    {
        get => currentPeriod;
        set => SetProperty(ref currentPeriod, value);
    }

    public string Headline
    {
        get => headline;
        set => SetProperty(ref headline, value);
    }
}

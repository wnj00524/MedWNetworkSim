namespace MedWNetworkSim.App.ViewModels;

public sealed class LayerVisibilityState(string trafficType) : ObservableObject
{
    private bool isVisible = true;
    private bool isHighlighted;

    public event EventHandler? LayerStateChanged;

    public string TrafficType { get; } = trafficType;

    public bool IsVisible
    {
        get => isVisible;
        set
        {
            if (SetProperty(ref isVisible, value))
            {
                LayerStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsHighlighted
    {
        get => isHighlighted;
        set
        {
            if (SetProperty(ref isHighlighted, value))
            {
                LayerStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}

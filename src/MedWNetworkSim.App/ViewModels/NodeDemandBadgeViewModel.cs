using System.Windows.Media;

namespace MedWNetworkSim.App.ViewModels;

public sealed class NodeDemandBadgeViewModel
{
    public NodeDemandBadgeViewModel(string trafficType, double quantity, string icon, Brush accentBrush)
    {
        TrafficType = trafficType;
        Quantity = quantity;
        Icon = icon;
        AccentBrush = accentBrush;
    }

    public string TrafficType { get; }

    public double Quantity { get; }

    public string Icon { get; }

    public Brush AccentBrush { get; }

    public string QuantityLabel => Quantity.ToString("0.##");

    public string AutomationLabel => $"{TrafficType} backlog {QuantityLabel} units";
}

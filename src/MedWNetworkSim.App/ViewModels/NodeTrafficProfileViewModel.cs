using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class NodeTrafficProfileViewModel : ObservableObject
{
    private string trafficType;
    private double production;
    private double consumption;
    private bool canTransship;

    public NodeTrafficProfileViewModel(NodeTrafficProfile profile)
    {
        trafficType = profile.TrafficType;
        production = profile.Production;
        consumption = profile.Consumption;
        canTransship = profile.CanTransship;
    }

    public string TrafficType
    {
        get => trafficType;
        set
        {
            if (!SetProperty(ref trafficType, value))
            {
                return;
            }

            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public double Production
    {
        get => production;
        set
        {
            if (!SetProperty(ref production, value))
            {
                return;
            }

            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public double Consumption
    {
        get => consumption;
        set
        {
            if (!SetProperty(ref consumption, value))
            {
                return;
            }

            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public bool CanTransship
    {
        get => canTransship;
        set
        {
            if (!SetProperty(ref canTransship, value))
            {
                return;
            }

            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public string RoleSummary
    {
        get
        {
            var parts = new List<string>();

            if (Production > 0)
            {
                parts.Add($"P {Production:0.##}");
            }

            if (CanTransship)
            {
                parts.Add("T");
            }

            if (Consumption > 0)
            {
                parts.Add($"C {Consumption:0.##}");
            }

            return parts.Count == 0 ? "No traffic role" : string.Join("  ", parts);
        }
    }
}

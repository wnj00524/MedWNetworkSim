using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class NodeTrafficProfileViewModel(NodeTrafficProfile profile) : ObservableObject
{
    public string TrafficType { get; } = profile.TrafficType;

    public double Production { get; } = profile.Production;

    public double Consumption { get; } = profile.Consumption;

    public bool CanTransship { get; } = profile.CanTransship;

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

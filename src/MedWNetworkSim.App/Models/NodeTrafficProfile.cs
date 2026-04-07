namespace MedWNetworkSim.App.Models;

public sealed class NodeTrafficProfile
{
    public string TrafficType { get; set; } = string.Empty;

    public double Production { get; set; }

    public double Consumption { get; set; }

    public bool CanTransship { get; set; }
}

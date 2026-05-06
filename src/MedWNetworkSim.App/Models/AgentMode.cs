namespace MedWNetworkSim.App.Models;

/// <summary>
/// Controls whether node demand can be fulfilled by unrestricted supply or by actor-permitted local sellers only.
/// </summary>
public enum AgentMode
{
    Off,
    SellLocal
}

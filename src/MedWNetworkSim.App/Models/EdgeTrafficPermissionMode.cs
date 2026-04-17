namespace MedWNetworkSim.App.Models;

/// <summary>
/// Describes whether an edge can carry a traffic type, cannot carry it, or can carry it up to a limit.
/// </summary>
public enum EdgeTrafficPermissionMode
{
    Permitted,
    Blocked,
    Limited
}

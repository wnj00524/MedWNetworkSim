namespace MedWNetworkSim.App.Models;

/// <summary>
/// Describes whether a traffic type concentrates or splits flow across routes.
/// </summary>
public enum FlowSplitPolicy
{
    /// <summary>
    /// Choose one route for each allocation decision.
    /// </summary>
    SinglePath,

    /// <summary>
    /// Split flow across multiple feasible routes.
    /// </summary>
    MultiPath
}

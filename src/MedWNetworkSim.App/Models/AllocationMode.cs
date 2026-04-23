namespace MedWNetworkSim.App.Models;

/// <summary>
/// Describes how available supply is assigned to reachable demand.
/// </summary>
public enum AllocationMode
{
    /// <summary>
    /// Repeatedly choose the current best producer-to-consumer route.
    /// </summary>
    GreedyBestRoute,

    /// <summary>
    /// Split flow across outgoing branches according to reachable downstream demand.
    /// </summary>
    ProportionalBranchDemand
}

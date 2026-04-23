namespace MedWNetworkSim.App.Models;

/// <summary>
/// Describes how a traffic type scores alternative routes.
/// </summary>
public enum RoutingPreference
{
    /// <summary>
    /// Prefer routes with the lowest total edge time.
    /// </summary>
    Speed,

    /// <summary>
    /// Prefer routes with the lowest total edge cost.
    /// </summary>
    Cost,

    /// <summary>
    /// Prefer routes with the lowest combined time plus cost score.
    /// </summary>
    TotalCost
}

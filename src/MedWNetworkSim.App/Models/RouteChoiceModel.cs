namespace MedWNetworkSim.App.Models;

/// <summary>
/// Describes who controls route choice for a traffic type.
/// </summary>
public enum RouteChoiceModel
{
    /// <summary>
    /// A central planner chooses routes with congestion internalized.
    /// </summary>
    SystemOptimal,

    /// <summary>
    /// Decentralized users choose routes stochastically from congestion-sensitive perceptions.
    /// </summary>
    StochasticUserResponsive
}

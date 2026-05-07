using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services.Pathfinding;

namespace MedWNetworkSim.Presentation.ViewModels;
/// <summary>
/// Represents a data model for facility planning view entities within the simulation.
/// </summary>

public sealed class FacilityPlanningViewModel
{
    /// <summary>
    /// Gets or sets the facilities.
    /// </summary>
    public required IReadOnlyCollection<NodeModel> Facilities { get; init; }
    /// <summary>
    /// Gets or sets the budget.
    /// </summary>
    public required double Budget { get; init; }
    /// <summary>
    /// Gets or sets the result.
    /// </summary>
    public MultiOriginIsochroneResult? Result { get; init; }
    /// <summary>
    /// Gets or sets the reachable nodes.
    /// </summary>
    public string ReachableNodes => (Result?.ReachableNodes.Count ?? 0).ToString();
    /// <summary>
    /// Gets or sets the uncovered nodes.
    /// </summary>
    public string UncoveredNodes => (Result?.UncoveredNodes.Count ?? 0).ToString();
    /// <summary>
    /// Gets or sets the overlap nodes.
    /// </summary>
    public string OverlapNodes => (Result?.OverlapNodes.Count ?? 0).ToString();
}

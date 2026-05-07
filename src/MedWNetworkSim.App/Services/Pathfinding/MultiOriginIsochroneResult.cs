using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services.Pathfinding;
/// <summary>
/// Represents the multi origin isochrone result component.
/// </summary>

public sealed class MultiOriginIsochroneResult
{
    /// <summary>
    /// Gets or sets the best cost by node.
    /// </summary>
    public required IReadOnlyDictionary<NodeModel, double> BestCostByNode { get; init; }
    /// <summary>
    /// Gets or sets the best origin by node.
    /// </summary>
    public required IReadOnlyDictionary<NodeModel, NodeModel> BestOriginByNode { get; init; }
    /// <summary>
    /// Gets the collection of covering origins by node associated with this entity.
    /// </summary>
    public required IReadOnlyDictionary<NodeModel, IReadOnlyList<NodeModel>> CoveringOriginsByNode { get; init; }
    /// <summary>
    /// Gets or sets the reachable nodes.
    /// </summary>
    public required IReadOnlyCollection<NodeModel> ReachableNodes { get; init; }
    /// <summary>
    /// Gets or sets the uncovered nodes.
    /// </summary>
    public required IReadOnlyCollection<NodeModel> UncoveredNodes { get; init; }
    /// <summary>
    /// Gets or sets the overlap nodes.
    /// </summary>
    public required IReadOnlyCollection<NodeModel> OverlapNodes { get; init; }
}

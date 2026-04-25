using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services.Pathfinding;

public sealed class MultiOriginIsochroneResult
{
    public required IReadOnlyDictionary<NodeModel, double> BestCostByNode { get; init; }
    public required IReadOnlyDictionary<NodeModel, NodeModel> BestOriginByNode { get; init; }
    public required IReadOnlyDictionary<NodeModel, IReadOnlyList<NodeModel>> CoveringOriginsByNode { get; init; }
    public required IReadOnlyCollection<NodeModel> ReachableNodes { get; init; }
    public required IReadOnlyCollection<NodeModel> UncoveredNodes { get; init; }
    public required IReadOnlyCollection<NodeModel> OverlapNodes { get; init; }
}

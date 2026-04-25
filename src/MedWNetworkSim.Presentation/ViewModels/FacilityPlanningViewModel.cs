using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services.Pathfinding;

namespace MedWNetworkSim.Presentation.ViewModels;

public sealed class FacilityPlanningViewModel
{
    public required IReadOnlyCollection<NodeModel> Facilities { get; init; }
    public required double Budget { get; init; }
    public MultiOriginIsochroneResult? Result { get; init; }
    public string ReachableNodes => (Result?.ReachableNodes.Count ?? 0).ToString();
    public string UncoveredNodes => (Result?.UncoveredNodes.Count ?? 0).ToString();
    public string OverlapNodes => (Result?.OverlapNodes.Count ?? 0).ToString();
}

using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Agents;
/// <summary>
/// Represents the simulation actor node ownership component.
/// </summary>

internal static class SimulationActorNodeOwnership
{
    /// <summary>
    /// Executes the build node actor lookup operation.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildNodeActorLookup(
        IEnumerable<NodeModel> nodes,
        IReadOnlyDictionary<string, SimulationActorState> actorsById,
        bool requireEnabledControlledActors)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var actor in actorsById.Values.OrderBy(actor => actor.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(actor.Id) ||
                (requireEnabledControlledActors && !actor.IsEnabled))
            {
                continue;
            }

            foreach (var nodeId in actor.ControlledNodeIds.Where(nodeId => !string.IsNullOrWhiteSpace(nodeId)))
            {
                lookup.TryAdd(nodeId, actor.Id);
            }
        }

        return lookup;
    }
}

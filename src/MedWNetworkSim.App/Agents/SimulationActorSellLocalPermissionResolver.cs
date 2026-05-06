using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Agents;

public static class SimulationActorSellLocalPermissionResolver
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static bool HasSellLocalAgentMode(NetworkModel network) => network.AgentMode == AgentMode.SellLocal;

    public static bool IsEnforced(NetworkModel network) => HasSellLocalAgentMode(network);

    public static bool ShouldLimitMeetingNodeDemand(NetworkModel network) => network.LimitMeetingNodeDemandBySellLocalPermission;

    public static bool CanSellLocal(NetworkModel network, string nodeId, string trafficType)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(trafficType))
        {
            return false;
        }

        return network.Actors
            .Where(actor => actor.IsEnabled && actor.ControlledNodeIds.Any(controlled => Comparer.Equals(controlled, nodeId)))
            .Any(actor => HasExplicitSellLocalPermission(actor, nodeId, trafficType));
    }

    public static HashSet<string> BuildPermittedSellerNodeSet(NetworkModel network, string trafficType)
    {
        var permitted = new HashSet<string>(Comparer);
        foreach (var actor in network.Actors.Where(actor => actor.IsEnabled))
        {
            foreach (var nodeId in actor.ControlledNodeIds.Where(nodeId => !string.IsNullOrWhiteSpace(nodeId)))
            {
                if (HasExplicitSellLocalPermission(actor, nodeId, trafficType))
                {
                    permitted.Add(nodeId);
                }
            }
        }

        return permitted;
    }

    private static bool HasExplicitSellLocalPermission(SimulationActorState actor, string nodeId, string trafficType)
    {
        var capability = actor.Capability;
        if (capability is null || capability.Permissions.Count == 0)
        {
            return false;
        }

        if (!capability.AllowAllTrafficTypes && !capability.AllowedTrafficTypes.Any(type => Comparer.Equals(type, trafficType)))
        {
            return false;
        }

        var matching = capability.Permissions
            .Where(permission => permission.ActionKind == SimulationActorActionKind.SellLocal)
            .Where(permission => permission.TrafficType is null || Comparer.Equals(permission.TrafficType, trafficType))
            .Where(permission => permission.NodeId is null || Comparer.Equals(permission.NodeId, nodeId))
            .Where(permission => permission.EdgeId is null)
            .ToList();

        return matching.Count > 0 && matching.Any(permission => permission.IsAllowed) && !matching.Any(permission => !permission.IsAllowed);
    }
}

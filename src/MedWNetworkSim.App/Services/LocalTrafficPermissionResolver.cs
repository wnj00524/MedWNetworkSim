using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

/// <summary>
/// Resolves network-native same-node demand permissions without any actor model.
/// </summary>
public static class LocalTrafficPermissionResolver
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static bool ShouldLimitMeetingNodeDemand(NetworkModel network) => network.LimitMeetingNodeDemandBySellLocalPermission;

    public static bool IsEnforced(NetworkModel network) => false;

    public static bool CanReceiveMeetingNodeDemand(NetworkModel network, string nodeId, string trafficType)
    {
        if (!ShouldLimitMeetingNodeDemand(network))
        {
            return true;
        }

        var node = network.Nodes.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, nodeId));
        var profile = node?.TrafficProfiles.FirstOrDefault(candidate => Comparer.Equals(candidate.TrafficType, trafficType));
        return profile is { Production: > 0d };
    }

    public static HashSet<string> BuildPermittedSellerNodeSet(NetworkModel network, string trafficType)
    {
        return network.Nodes
            .Where(node => CanReceiveMeetingNodeDemand(network, node.Id, trafficType))
            .Select(node => node.Id)
            .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
            .ToHashSet(Comparer);
    }
}

using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

/// <summary>
/// Applies network-level scenario changes to a cloned network model.
/// </summary>
public sealed class NetworkScenarioChangeApplier
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public (NetworkModel Network, IReadOnlyList<NetworkScenarioChangeOutcome> Outcomes) Apply(
        NetworkModel network,
        IReadOnlyList<NetworkScenarioChange> changes,
        IReadOnlyDictionary<string, double>? currentFlowByEdgeId = null)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(changes);

        var clone = NetworkModelCloneUtility.Clone(network);
        var outcomes = new List<NetworkScenarioChangeOutcome>(changes.Count);
        var flowByEdgeId = currentFlowByEdgeId ?? new Dictionary<string, double>(Comparer);

        foreach (var change in changes)
        {
            var outcome = ApplySingle(clone, change, flowByEdgeId);
            outcomes.Add(outcome);
        }

        return (clone, outcomes);
    }

    private static NetworkScenarioChangeOutcome ApplySingle(
        NetworkModel network,
        NetworkScenarioChange change,
        IReadOnlyDictionary<string, double> currentFlowByEdgeId)
    {
        var (applied, reason) = change.Kind switch
        {
            NetworkChangeKind.NoOp => (true, "No-op accepted."),
            NetworkChangeKind.AdjustProduction => ApplyNodeProfileChange(network, change, profile => profile.Production),
            NetworkChangeKind.AdjustConsumption => ApplyNodeProfileChange(network, change, profile => profile.Consumption),
            NetworkChangeKind.AdjustTrafficPrice => ApplyNodeProfileChange(network, change, profile => profile.UnitPrice),
            NetworkChangeKind.AdjustEdgeCapacity => ApplyEdgeCapacityChange(network, change, currentFlowByEdgeId),
            NetworkChangeKind.AdjustEdgeCost => ApplyEdgeCostChange(network, change),
            NetworkChangeKind.SetTrafficPermission => ApplyTrafficPermissionChange(network, change),
            NetworkChangeKind.ClearTrafficRestrictions => ClearTrafficRestrictions(network, change),
            _ => (false, $"Unsupported change kind '{change.Kind}'.")
        };

        return new NetworkScenarioChangeOutcome
        {
            Change = change,
            Applied = applied,
            Reason = reason
        };
    }

    private static (bool Applied, string Reason) ApplyNodeProfileChange(
        NetworkModel network,
        NetworkScenarioChange change,
        Func<NodeTrafficProfile, double> valueSelector)
    {
        if (string.IsNullOrWhiteSpace(change.TargetNodeId) || string.IsNullOrWhiteSpace(change.TrafficType))
        {
            return (false, "Node and traffic type are required.");
        }

        var node = network.Nodes.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, change.TargetNodeId));
        if (node is null)
        {
            return (false, $"Node '{change.TargetNodeId}' was not found.");
        }

        var profile = node.TrafficProfiles.FirstOrDefault(candidate => Comparer.Equals(candidate.TrafficType, change.TrafficType));
        if (profile is null)
        {
            return (false, $"Node '{node.Id}' does not have traffic profile '{change.TrafficType}'.");
        }

        var updated = Math.Max(0d, change.AbsoluteValue ?? valueSelector(profile) + change.DeltaValue);
        if (change.Kind == NetworkChangeKind.AdjustProduction)
        {
            profile.Production = updated;
            return (true, "Production updated.");
        }

        if (change.Kind == NetworkChangeKind.AdjustConsumption)
        {
            profile.Consumption = updated;
            return (true, "Consumption updated.");
        }

        profile.UnitPrice = updated;
        return (true, "Traffic price updated.");
    }

    private static (bool Applied, string Reason) ApplyEdgeCapacityChange(
        NetworkModel network,
        NetworkScenarioChange change,
        IReadOnlyDictionary<string, double> currentFlowByEdgeId)
    {
        if (string.IsNullOrWhiteSpace(change.TargetEdgeId))
        {
            return (false, "Target edge is required.");
        }

        var edge = network.Edges.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, change.TargetEdgeId));
        if (edge is null)
        {
            return (false, $"Edge '{change.TargetEdgeId}' was not found.");
        }

        var existingCapacity = edge.Capacity ?? Math.Max(1d, currentFlowByEdgeId.GetValueOrDefault(edge.Id));
        var updatedCapacity = Math.Max(0d, change.AbsoluteValue ?? existingCapacity + change.DeltaValue);
        var currentFlow = currentFlowByEdgeId.GetValueOrDefault(edge.Id);

        if (!change.AllowReduceBelowCurrentFlow && updatedCapacity < currentFlow)
        {
            return (false, $"Cannot reduce capacity below current routed flow ({currentFlow:0.##}).");
        }

        edge.Capacity = updatedCapacity;
        return (true, "Edge capacity updated.");
    }

    private static (bool Applied, string Reason) ApplyEdgeCostChange(NetworkModel network, NetworkScenarioChange change)
    {
        if (string.IsNullOrWhiteSpace(change.TargetEdgeId))
        {
            return (false, "Target edge is required.");
        }

        var edge = network.Edges.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, change.TargetEdgeId));
        if (edge is null)
        {
            return (false, $"Edge '{change.TargetEdgeId}' was not found.");
        }

        edge.Cost = Math.Max(0d, change.AbsoluteValue ?? edge.Cost + change.DeltaValue);
        return (true, "Edge cost updated.");
    }

    private static (bool Applied, string Reason) ApplyTrafficPermissionChange(NetworkModel network, NetworkScenarioChange change)
    {
        if (string.IsNullOrWhiteSpace(change.TargetEdgeId) || string.IsNullOrWhiteSpace(change.TrafficType))
        {
            return (false, "Edge and traffic type are required.");
        }

        var edge = network.Edges.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, change.TargetEdgeId));
        if (edge is null)
        {
            return (false, $"Edge '{change.TargetEdgeId}' was not found.");
        }

        var existing = edge.TrafficPermissions.FirstOrDefault(rule => Comparer.Equals(rule.TrafficType, change.TrafficType));
        if (existing is null)
        {
            edge.TrafficPermissions.Add(new EdgeTrafficPermissionRule
            {
                TrafficType = change.TrafficType,
                Mode = EdgeTrafficPermissionMode.Blocked,
                IsActive = true
            });
        }
        else
        {
            existing.Mode = EdgeTrafficPermissionMode.Blocked;
            existing.IsActive = true;
        }

        return (true, "Traffic permission updated.");
    }

    private static (bool Applied, string Reason) ClearTrafficRestrictions(NetworkModel network, NetworkScenarioChange change)
    {
        if (string.IsNullOrWhiteSpace(change.TargetEdgeId))
        {
            return (false, "Target edge is required.");
        }

        var edge = network.Edges.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, change.TargetEdgeId));
        if (edge is null)
        {
            return (false, $"Edge '{change.TargetEdgeId}' was not found.");
        }

        foreach (var permission in edge.TrafficPermissions.Where(permission =>
                     string.IsNullOrWhiteSpace(change.TrafficType) ||
                     Comparer.Equals(permission.TrafficType, change.TrafficType)))
        {
            permission.IsActive = true;
            permission.Mode = EdgeTrafficPermissionMode.Permitted;
        }

        return (true, "Traffic restrictions cleared.");
    }
}

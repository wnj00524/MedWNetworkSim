using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.Agents;

public sealed class SimulationActorActionApplier
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public (NetworkModel Network, IReadOnlyList<SimulationActorActionOutcome> Outcomes) Apply(
        NetworkModel network,
        IReadOnlyList<SimulationActorAction> actions,
        IReadOnlyDictionary<string, SimulationActorState> actors,
        IReadOnlyDictionary<string, double> currentFlowByEdgeId)
    {
        var clone = NetworkModelCloneUtility.Clone(network);
        var outcomes = new List<SimulationActorActionOutcome>();
        var spentByActorId = new Dictionary<string, double>(Comparer);

        foreach (var action in actions)
        {
            var (applied, reason) = ApplySingle(clone, action, actors, currentFlowByEdgeId, spentByActorId);
            outcomes.Add(new SimulationActorActionOutcome { Action = action, Applied = applied, Reason = reason });

            if (applied && action.Cost > 0d)
            {
                spentByActorId.TryGetValue(action.ActorId, out var spent);
                spentByActorId[action.ActorId] = spent + action.Cost;
            }
        }

        return (clone, outcomes);
    }

    private static (bool Applied, string Reason) ApplySingle(
        NetworkModel network,
        SimulationActorAction action,
        IReadOnlyDictionary<string, SimulationActorState> actors,
        IReadOnlyDictionary<string, double> currentFlowByEdgeId,
        IReadOnlyDictionary<string, double> spentByActorId)
    {
        if (!actors.TryGetValue(action.ActorId, out var actor))
        {
            return (false, $"Unknown actor '{action.ActorId}'.");
        }

        if (!actor.IsEnabled)
        {
            return (false, "Actor is disabled.");
        }

        spentByActorId.TryGetValue(actor.Id, out var spentThisTick);
        var availableCash = Math.Max(0d, actor.Cash - spentThisTick);
        if (action.Cost > 0d && availableCash < action.Cost)
        {
            return (false, "Insufficient actor cash for action cost.");
        }

        switch (action.Kind)
        {
            case SimulationActorActionKind.NoOp:
                return (true, "NoOp accepted.");

            case SimulationActorActionKind.AdjustProduction:
            case SimulationActorActionKind.AdjustConsumption:
            case SimulationActorActionKind.AdjustTrafficPrice:
                return ApplyNodeProfileAction(network, action);

            case SimulationActorActionKind.AdjustEdgeCapacity:
            case SimulationActorActionKind.SubsidiseCapacity:
                return ApplyEdgeCapacityAction(network, action, currentFlowByEdgeId);

            case SimulationActorActionKind.AdjustEdgeCost:
            case SimulationActorActionKind.TaxRoute:
            case SimulationActorActionKind.PreferRoute:
                return ApplyEdgeCostAction(network, action);

            case SimulationActorActionKind.BanTrafficOnEdge:
                return ApplyRouteBan(network, action);

            case SimulationActorActionKind.AdjustRoutePermission:
                return ApplyRoutePermission(network, action);

            default:
                return (false, $"Action kind '{action.Kind}' is not supported.");
        }
    }

    private static (bool Applied, string Reason) ApplyNodeProfileAction(NetworkModel network, SimulationActorAction action)
    {
        if (string.IsNullOrWhiteSpace(action.TargetNodeId) || string.IsNullOrWhiteSpace(action.TrafficType))
        {
            return (false, "Node and traffic type are required.");
        }

        var node = network.Nodes.FirstOrDefault(n => Comparer.Equals(n.Id, action.TargetNodeId));
        if (node is null)
        {
            return (false, $"Node '{action.TargetNodeId}' was not found.");
        }

        var profile = node.TrafficProfiles.FirstOrDefault(p => Comparer.Equals(p.TrafficType, action.TrafficType));
        if (profile is null)
        {
            return (false, $"Node '{node.Id}' does not have traffic profile '{action.TrafficType}'.");
        }

        if (action.Kind == SimulationActorActionKind.AdjustProduction)
        {
            profile.Production = Math.Max(0d, action.AbsoluteValue ?? profile.Production + action.DeltaValue);
            return (true, "Production updated.");
        }

        if (action.Kind == SimulationActorActionKind.AdjustConsumption)
        {
            profile.Consumption = Math.Max(0d, action.AbsoluteValue ?? profile.Consumption + action.DeltaValue);
            return (true, "Consumption updated.");
        }

        profile.UnitPrice = Math.Max(0d, action.AbsoluteValue ?? profile.UnitPrice + action.DeltaValue);
        return (true, "Traffic price updated.");
    }

    private static (bool Applied, string Reason) ApplyEdgeCapacityAction(
        NetworkModel network,
        SimulationActorAction action,
        IReadOnlyDictionary<string, double> currentFlowByEdgeId)
    {
        if (string.IsNullOrWhiteSpace(action.TargetEdgeId))
        {
            return (false, "Target edge is required.");
        }

        var edge = network.Edges.FirstOrDefault(e => Comparer.Equals(e.Id, action.TargetEdgeId));
        if (edge is null)
        {
            return (false, $"Edge '{action.TargetEdgeId}' was not found.");
        }

        var existing = edge.Capacity ?? Math.Max(1d, currentFlowByEdgeId.GetValueOrDefault(edge.Id));
        var updated = Math.Max(0d, action.AbsoluteValue ?? existing + action.DeltaValue);
        var routedFlow = currentFlowByEdgeId.GetValueOrDefault(edge.Id);

        if (!action.IsForced && updated < routedFlow)
        {
            return (false, $"Cannot reduce capacity below current routed flow ({routedFlow:0.##}).");
        }

        edge.Capacity = updated;
        return (true, "Edge capacity updated.");
    }

    private static (bool Applied, string Reason) ApplyEdgeCostAction(NetworkModel network, SimulationActorAction action)
    {
        if (string.IsNullOrWhiteSpace(action.TargetEdgeId))
        {
            return (false, "Target edge is required.");
        }

        var edge = network.Edges.FirstOrDefault(e => Comparer.Equals(e.Id, action.TargetEdgeId));
        if (edge is null)
        {
            return (false, $"Edge '{action.TargetEdgeId}' was not found.");
        }

        edge.Cost = Math.Max(0d, action.AbsoluteValue ?? edge.Cost + action.DeltaValue);
        return (true, "Edge cost updated.");
    }

    private static (bool Applied, string Reason) ApplyRouteBan(NetworkModel network, SimulationActorAction action)
    {
        if (string.IsNullOrWhiteSpace(action.TargetEdgeId) || string.IsNullOrWhiteSpace(action.TrafficType))
        {
            return (false, "Edge and traffic type are required for bans.");
        }

        var edge = network.Edges.FirstOrDefault(e => Comparer.Equals(e.Id, action.TargetEdgeId));
        if (edge is null)
        {
            return (false, $"Edge '{action.TargetEdgeId}' was not found.");
        }

        var existing = edge.TrafficPermissions.FirstOrDefault(p => Comparer.Equals(p.TrafficType, action.TrafficType));
        if (existing is null)
        {
            edge.TrafficPermissions.Add(new EdgeTrafficPermissionRule
            {
                TrafficType = action.TrafficType,
                Mode = EdgeTrafficPermissionMode.Blocked,
                IsActive = true
            });
        }
        else
        {
            existing.Mode = EdgeTrafficPermissionMode.Blocked;
            existing.IsActive = true;
        }

        return (true, "Traffic ban applied.");
    }

    private static (bool Applied, string Reason) ApplyRoutePermission(NetworkModel network, SimulationActorAction action)
    {
        if (string.IsNullOrWhiteSpace(action.TargetEdgeId))
        {
            return (false, "Target edge is required.");
        }

        var edge = network.Edges.FirstOrDefault(e => Comparer.Equals(e.Id, action.TargetEdgeId));
        if (edge is null)
        {
            return (false, $"Edge '{action.TargetEdgeId}' was not found.");
        }

        foreach (var permission in edge.TrafficPermissions)
        {
            permission.IsActive = true;
            if (permission.Mode == EdgeTrafficPermissionMode.Blocked)
            {
                permission.Mode = EdgeTrafficPermissionMode.Permitted;
            }
        }

        return (true, "Route permissions relaxed.");
    }

}

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

        var capability = actor.Capability ?? SimulationActorCapabilityCatalog.ForKind(actor.Id, actor.Kind);
        capability.Permissions ??= [];
        var hasExplicitAllows = capability.Permissions.Any(p => p.IsAllowed);
        var matching = capability.Permissions
            .Where(p =>
                p.ActionKind == action.Kind &&
                (p.TrafficType == null || Comparer.Equals(p.TrafficType, action.TrafficType)) &&
                (p.NodeId == null || Comparer.Equals(p.NodeId, action.TargetNodeId)) &&
                (p.EdgeId == null || Comparer.Equals(p.EdgeId, action.TargetEdgeId)))
            .ToList();

        if (matching.Any(p => !p.IsAllowed))
        {
            return (false, "Permission explicitly denied.");
        }

        var isExplicitlyAllowed = matching.Any(p => p.IsAllowed);
        if (hasExplicitAllows && !isExplicitlyAllowed)
        {
            return (false, "Permission is not explicitly allowed.");
        }

        if (!isExplicitlyAllowed && !capability.AllowedActionKinds.Contains(action.Kind))
        {
            return (false, $"Actor capability does not allow action '{action.Kind}'.");
        }

        if (!isExplicitlyAllowed &&
            !string.IsNullOrWhiteSpace(action.TrafficType) &&
            !capability.AllowAllTrafficTypes &&
            !capability.AllowedTrafficTypes.Contains(action.TrafficType, Comparer))
        {
            return (false, $"Actor capability does not allow traffic type '{action.TrafficType}'.");
        }

        spentByActorId.TryGetValue(actor.Id, out var spentThisTick);
        var spendLimit = actor.Budget > 0d ? actor.Budget : actor.Cash;
        var availableFunds = Math.Max(0d, spendLimit - spentThisTick);
        if (action.Cost > 0d && availableFunds < action.Cost &&
            action.Kind != SimulationActorActionKind.AdjustEdgeCapacity &&
            action.Kind != SimulationActorActionKind.SubsidiseCapacity)
        {
            return (false, "Insufficient actor funds for action cost.");
        }

        (bool Applied, string Reason) FinalizePermissionResult((bool Applied, string Reason) result) =>
            isExplicitlyAllowed && result.Applied
                ? (true, $"Permission explicitly allowed. {result.Reason}")
                : result;

        switch (action.Kind)
        {
            case SimulationActorActionKind.NoOp:
                return FinalizePermissionResult((true, "NoOp accepted."));

            case SimulationActorActionKind.AdjustProduction:
            case SimulationActorActionKind.AdjustConsumption:
            case SimulationActorActionKind.AdjustTrafficPrice:
            case SimulationActorActionKind.BuyTraffic:
            case SimulationActorActionKind.SellTraffic:
                return FinalizePermissionResult(ApplyNodeProfileAction(network, action));

            case SimulationActorActionKind.AdjustEdgeCapacity:
            case SimulationActorActionKind.SubsidiseCapacity:
                return FinalizePermissionResult(ApplyEdgeCapacityAction(network, action, currentFlowByEdgeId, availableFunds));

            case SimulationActorActionKind.AdjustEdgeCost:
            case SimulationActorActionKind.PreferRoute:
                return FinalizePermissionResult(ApplyEdgeCostAction(network, action));

            case SimulationActorActionKind.TaxRoute:
                return FinalizePermissionResult(ApplyRouteTaxAction(network, action));

            case SimulationActorActionKind.BanTrafficOnEdge:
                return FinalizePermissionResult(ApplyRouteBan(network, action));

            case SimulationActorActionKind.AdjustRoutePermission:
                return FinalizePermissionResult(ApplyRoutePermission(network, action));

            case SimulationActorActionKind.SetNodePolicy:
            case SimulationActorActionKind.SetEdgePolicy:
                return (false, "Action type is not yet supported by the network model.");

            default:
                return (false, "Action type is not yet supported by the network model.");
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

        if (action.Kind == SimulationActorActionKind.SellTraffic)
        {
            profile.Production = Math.Max(0d, action.AbsoluteValue ?? profile.Production + action.DeltaValue);
            return (true, "Sell traffic intent updated production.");
        }

        if (action.Kind == SimulationActorActionKind.AdjustConsumption)
        {
            profile.Consumption = Math.Max(0d, action.AbsoluteValue ?? profile.Consumption + action.DeltaValue);
            return (true, "Consumption updated.");
        }

        if (action.Kind == SimulationActorActionKind.BuyTraffic)
        {
            return (false, "BuyTraffic is a bounded market order and does not mutate consumption directly.");
        }

        profile.UnitPrice = Math.Max(0d, action.AbsoluteValue ?? profile.UnitPrice + action.DeltaValue);
        return (true, "Traffic price updated.");
    }

    private static (bool Applied, string Reason) ApplyEdgeCapacityAction(
        NetworkModel network,
        SimulationActorAction action,
        IReadOnlyDictionary<string, double> currentFlowByEdgeId,
        double availableActorFunds)
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
        var routedFlow = currentFlowByEdgeId.GetValueOrDefault(edge.Id);
        var updated = Math.Max(0d, action.AbsoluteValue ?? existing + action.DeltaValue);
        var requestedDelta = action.AbsoluteValue.HasValue
            ? updated - existing
            : action.DeltaValue;
        var expansionDelta = Math.Max(0d, requestedDelta);
        var capacityUnitCost = ResolveCapacityUnitCost(action, expansionDelta);
        var requestedCost = expansionDelta * capacityUnitCost;

        if (!action.IsForced && updated < routedFlow)
        {
            return (false, $"Cannot reduce capacity below current routed flow ({routedFlow:0.##}).");
        }

        if (updated < existing - 0.000001d)
        {
            action.Cost = 0d;
            edge.Capacity = updated;
            return (true, $"Edge capacity reduced to {updated:0.##}; current capacity {existing:0.##}.");
        }

        if (expansionDelta <= 0.000001d)
        {
            action.Cost = 0d;
            return (false, $"No capacity expansion: requested delta {expansionDelta:0.##}; current capacity {existing:0.##}.");
        }

        if (availableActorFunds < requestedCost)
        {
            var affordableDelta = Math.Max(0d, availableActorFunds) / capacityUnitCost;
            if (affordableDelta <= 0.000001d)
            {
                action.Cost = 0d;
                return (false, $"No capacity expansion: requested delta {requestedDelta:0.##}, affordable delta 0, current capacity {existing:0.##}, unit cost {capacityUnitCost:0.##}.");
            }

            action.DeltaValue = affordableDelta;
            action.AbsoluteValue = null;
            action.Cost = affordableDelta * capacityUnitCost;
            edge.Capacity = existing + affordableDelta;
            return (true, $"Edge capacity scaled to affordable delta {affordableDelta:0.##} from requested delta {expansionDelta:0.##}; current capacity {existing:0.##}; unit cost {capacityUnitCost:0.##}.");
        }

        action.Cost = requestedCost;
        edge.Capacity = updated;
        return (true, $"Edge capacity updated by {expansionDelta:0.##}; current capacity {existing:0.##}; unit cost {capacityUnitCost:0.##}; cost {action.Cost:0.##}.");
    }

    private static double ResolveCapacityUnitCost(SimulationActorAction action, double requestedDelta)
    {
        if (requestedDelta > 0.000001d && action.Cost >= requestedDelta)
        {
            return Math.Max(0.000001d, action.Cost / requestedDelta);
        }

        return 1d;
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

    private static (bool Applied, string Reason) ApplyRouteTaxAction(NetworkModel network, SimulationActorAction action)
    {
        if (string.IsNullOrWhiteSpace(action.TargetEdgeId) || string.IsNullOrWhiteSpace(action.TrafficType))
        {
            return (false, "Edge and traffic type are required for route taxes.");
        }

        var edge = network.Edges.FirstOrDefault(e => Comparer.Equals(e.Id, action.TargetEdgeId));
        if (edge is null)
        {
            return (false, $"Edge '{action.TargetEdgeId}' was not found.");
        }

        var existing = network.RouteTaxRules.FirstOrDefault(rule =>
            Comparer.Equals(rule.EdgeId, action.TargetEdgeId) &&
            Comparer.Equals(rule.TrafficType, action.TrafficType) &&
            Comparer.Equals(rule.TaxAuthorityActorId, action.ActorId));
        var taxRate = Math.Max(0d, action.AbsoluteValue ?? (existing?.TaxRate ?? 0d) + action.DeltaValue);

        if (existing is null)
        {
            network.RouteTaxRules.Add(new RouteTaxRule
            {
                EdgeId = action.TargetEdgeId,
                TrafficType = action.TrafficType,
                TaxRate = taxRate,
                TaxAuthorityActorId = action.ActorId,
                IsActive = true
            });
        }
        else
        {
            existing.TaxRate = taxRate;
            existing.IsActive = true;
        }

        return (true, "Route tax rule updated.");
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

    private static double EstimateCheapestInboundRouteCost(NetworkModel network, SimulationActorAction action)
    {
        if (string.IsNullOrWhiteSpace(action.TargetNodeId) || string.IsNullOrWhiteSpace(action.TrafficType))
        {
            return 0d;
        }

        var producerIds = network.Nodes
            .Where(node => node.TrafficProfiles.Any(profile =>
                Comparer.Equals(profile.TrafficType, action.TrafficType) &&
                profile.Production > 0d))
            .Select(node => node.Id)
            .ToList();
        if (producerIds.Count == 0)
        {
            return 0d;
        }

        var resolver = new EdgeTrafficPermissionResolver();
        var adjacency = new Dictionary<string, List<(string ToNodeId, EdgeModel Edge)>>(Comparer);
        foreach (var edge in network.Edges)
        {
            AddArc(edge.FromNodeId, edge.ToNodeId, edge);
            if (edge.IsBidirectional)
            {
                AddArc(edge.ToNodeId, edge.FromNodeId, edge);
            }
        }

        var best = double.PositiveInfinity;
        foreach (var producerId in producerIds)
        {
            var candidate = FindCheapestRouteCost(
                network,
                resolver,
                adjacency,
                producerId,
                action.TargetNodeId,
                action.TrafficType);
            best = Math.Min(best, candidate);
        }

        return double.IsPositiveInfinity(best) ? 0d : Math.Max(0d, best);

        void AddArc(string fromNodeId, string toNodeId, EdgeModel edge)
        {
            if (!adjacency.TryGetValue(fromNodeId, out var arcs))
            {
                arcs = [];
                adjacency[fromNodeId] = arcs;
            }

            arcs.Add((toNodeId, edge));
        }
    }

    private static double FindCheapestRouteCost(
        NetworkModel network,
        EdgeTrafficPermissionResolver resolver,
        IReadOnlyDictionary<string, List<(string ToNodeId, EdgeModel Edge)>> adjacency,
        string producerId,
        string consumerId,
        string trafficType)
    {
        var distances = new Dictionary<string, double>(Comparer) { [producerId] = 0d };
        var queue = new PriorityQueue<string, double>();
        queue.Enqueue(producerId, 0d);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            var cost = distances[nodeId];
            if (Comparer.Equals(nodeId, consumerId))
            {
                return cost;
            }

            if (!adjacency.TryGetValue(nodeId, out var arcs))
            {
                continue;
            }

            foreach (var (toNodeId, edge) in arcs)
            {
                if (resolver.Resolve(network, edge, trafficType).Mode == EdgeTrafficPermissionMode.Blocked)
                {
                    continue;
                }

                var nextCost = cost + Math.Max(0d, edge.Cost);
                if (distances.TryGetValue(toNodeId, out var existing) && existing <= nextCost)
                {
                    continue;
                }

                distances[toNodeId] = nextCost;
                queue.Enqueue(toNodeId, nextCost);
            }
        }

        return double.PositiveInfinity;
    }

}

using System.Collections.Frozen;
using MedWNetworkSim.App.Agents;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

internal sealed class SimulationExecutionCache
{
    private long projectedRevision = long.MinValue;
    private NetworkModel? projectedNetwork;
    private Dictionary<OverlayContextKey, CompiledNetworkSimulationContext> contextsByOverlayKey = [];

    public CompiledNetworkSimulationContext GetTemporalContext(NetworkModel network, int effectivePeriod)
    {
        var projected = GetProjectedNetwork(network, out var revision);
        var activeEvents = GetActiveTimelineEvents(projected, effectivePeriod);
        var activeSignature = GetTimelineSignature(activeEvents);
        var overlayKey = new OverlayContextKey(revision, effectivePeriod, activeSignature);
        if (contextsByOverlayKey.TryGetValue(overlayKey, out var existing))
        {
            return existing;
        }

        var effectiveNetwork = activeEvents.Count == 0
            ? projected
            : ApplyTimelineOverlay(projected, activeEvents);
        var compiled = CompiledNetworkSimulationContext.Create(
            projected,
            effectiveNetwork,
            revision,
            effectivePeriod,
            activeSignature);

        contextsByOverlayKey[overlayKey] = compiled;
        return compiled;
    }

    public CompiledNetworkSimulationContext GetStaticContext(NetworkModel network)
    {
        var projected = GetProjectedNetwork(network, out var revision);
        var overlayKey = new OverlayContextKey(revision, 0, 0);
        if (contextsByOverlayKey.TryGetValue(overlayKey, out var existing))
        {
            return existing;
        }

        var compiled = CompiledNetworkSimulationContext.Create(projected, projected, revision, 0, 0);
        contextsByOverlayKey[overlayKey] = compiled;
        return compiled;
    }

    private NetworkModel GetProjectedNetwork(NetworkModel network, out long revision)
    {
        revision = NetworkRevisionHasher.Compute(network);
        if (projectedNetwork is not null && revision == projectedRevision)
        {
            return projectedNetwork;
        }

        projectedNetwork = HierarchicalNetworkProjection.ProjectForSimulation(network);
        projectedRevision = revision;
        contextsByOverlayKey = [];
        return projectedNetwork;
    }

    private static List<TimelineEventModel> GetActiveTimelineEvents(NetworkModel network, int effectivePeriod)
    {
        if (network.TimelineEvents.Count == 0)
        {
            return [];
        }

        var active = new List<TimelineEventModel>();
        foreach (var timelineEvent in network.TimelineEvents)
        {
            if (timelineEvent.StartPeriod.HasValue && effectivePeriod < timelineEvent.StartPeriod.Value)
            {
                continue;
            }

            if (timelineEvent.EndPeriod.HasValue && effectivePeriod > timelineEvent.EndPeriod.Value)
            {
                continue;
            }

            active.Add(timelineEvent);
        }

        return active;
    }

    private static int GetTimelineSignature(IReadOnlyList<TimelineEventModel> activeEvents)
    {
        var hash = new HashCode();
        for (var index = 0; index < activeEvents.Count; index++)
        {
            var timelineEvent = activeEvents[index];
            hash.Add(timelineEvent.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            hash.Add(timelineEvent.StartPeriod.GetValueOrDefault());
            hash.Add(timelineEvent.EndPeriod.GetValueOrDefault());
            hash.Add(timelineEvent.Effects.Count);
            foreach (var effect in timelineEvent.Effects)
            {
                hash.Add((int)effect.EffectType);
                hash.Add(effect.NodeId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                hash.Add(effect.EdgeId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                hash.Add(effect.TrafficType ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                hash.Add(effect.Multiplier);
            }
        }

        return hash.ToHashCode();
    }

    private static NetworkModel ApplyTimelineOverlay(NetworkModel network, IReadOnlyList<TimelineEventModel> activeEvents)
    {
        var overlay = CloneNetwork(network);
        var nodeIndexById = new Dictionary<string, int>(overlay.Nodes.Count, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < overlay.Nodes.Count; index++)
        {
            nodeIndexById[overlay.Nodes[index].Id] = index;
        }

        var edgeIndexById = new Dictionary<string, int>(overlay.Edges.Count, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < overlay.Edges.Count; index++)
        {
            edgeIndexById[overlay.Edges[index].Id] = index;
        }

        foreach (var timelineEvent in activeEvents)
        {
            foreach (var effect in timelineEvent.Effects)
            {
                switch (effect.EffectType)
                {
                    case TimelineEventEffectType.ProductionMultiplier:
                        ApplyNodeTrafficMultiplier(overlay, effect, nodeIndexById, profile => profile.Production *= effect.Multiplier);
                        break;
                    case TimelineEventEffectType.ConsumptionMultiplier:
                        ApplyNodeTrafficMultiplier(overlay, effect, nodeIndexById, profile => profile.Consumption *= effect.Multiplier);
                        break;
                    case TimelineEventEffectType.RouteCostMultiplier:
                        ApplyEdgeMultiplier(overlay, effect, edgeIndexById);
                        break;
                }
            }
        }

        return overlay;
    }

    private static void ApplyNodeTrafficMultiplier(
        NetworkModel overlay,
        TimelineEventEffectModel effect,
        IReadOnlyDictionary<string, int> nodeIndexById,
        Action<NodeTrafficProfile> apply)
    {
        if (string.IsNullOrWhiteSpace(effect.NodeId) || !nodeIndexById.TryGetValue(effect.NodeId, out var nodeIndex))
        {
            return;
        }

        var profiles = overlay.Nodes[nodeIndex].TrafficProfiles;
        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            if (string.IsNullOrWhiteSpace(effect.TrafficType) ||
                string.Equals(profile.TrafficType, effect.TrafficType, StringComparison.OrdinalIgnoreCase))
            {
                apply(profile);
            }
        }
    }

    private static void ApplyEdgeMultiplier(
        NetworkModel overlay,
        TimelineEventEffectModel effect,
        IReadOnlyDictionary<string, int> edgeIndexById)
    {
        if (string.IsNullOrWhiteSpace(effect.EdgeId) || !edgeIndexById.TryGetValue(effect.EdgeId, out var edgeIndex))
        {
            return;
        }

        overlay.Edges[edgeIndex].Cost *= effect.Multiplier;
    }

    private static NetworkModel CloneNetwork(NetworkModel network)
    {
        var clone = new NetworkModel
        {
            Name = network.Name,
            Description = network.Description,
            TimelineLoopLength = network.TimelineLoopLength,
            DefaultAllocationMode = network.DefaultAllocationMode,
            SimulationSeed = network.SimulationSeed,
            FacilityModeEnabled = network.FacilityModeEnabled,
            AgentMode = network.AgentMode,
            LimitMeetingNodeDemandBySellLocalPermission = network.LimitMeetingNodeDemandBySellLocalPermission,
            LockLayoutToMap = network.LockLayoutToMap,
            FacilityCoverageThreshold = network.FacilityCoverageThreshold,
            Layers = network.Layers,
            ScenarioDefinitions = network.ScenarioDefinitions,
            PolicyRules = network.PolicyRules,
            TrafficTypes = network.TrafficTypes,
            TimelineEvents = network.TimelineEvents,
            EdgeTrafficPermissionDefaults = network.EdgeTrafficPermissionDefaults,
            RouteTaxRules = network.RouteTaxRules,
            Subnetworks = network.Subnetworks,
            Actors = network.Actors,
            ActorDecisions = network.ActorDecisions,
            ActorMetrics = network.ActorMetrics,
            ActorActionOutcomes = network.ActorActionOutcomes,
            AgentActionLogs = network.AgentActionLogs,
            PreAgentMutationNetwork = network.PreAgentMutationNetwork,
            ActorTick = network.ActorTick
        };

        clone.Nodes = new List<NodeModel>(network.Nodes.Count);
        foreach (var node in network.Nodes)
        {
            clone.Nodes.Add(CloneNode(node));
        }

        clone.Edges = new List<EdgeModel>(network.Edges.Count);
        foreach (var edge in network.Edges)
        {
            clone.Edges.Add(CloneEdge(edge));
        }

        return clone;
    }

    private static NodeModel CloneNode(NodeModel node)
    {
        return new NodeModel
        {
            Id = node.Id,
            Name = node.Name,
            Shape = node.Shape,
            NodeKind = node.NodeKind,
            ReferencedSubnetworkId = node.ReferencedSubnetworkId,
            IsExternalInterface = node.IsExternalInterface,
            InterfaceName = node.InterfaceName,
            LayerId = node.LayerId,
            X = node.X,
            Y = node.Y,
            TranshipmentCapacity = node.TranshipmentCapacity,
            Latitude = node.Latitude,
            Longitude = node.Longitude,
            OsmId = node.OsmId,
            OsmName = node.OsmName,
            OsmHighwayType = node.OsmHighwayType,
            IsFacility = node.IsFacility,
            FacilityCapacity = node.FacilityCapacity,
            PlaceType = node.PlaceType,
            LoreDescription = node.LoreDescription,
            ControllingActor = node.ControllingActor,
            Tags = [.. node.Tags],
            TemplateId = node.TemplateId,
            TrafficProfiles = [.. node.TrafficProfiles.Select(CloneProfile)]
        };
    }

    private static NodeTrafficProfile CloneProfile(NodeTrafficProfile profile)
    {
        return new NodeTrafficProfile
        {
            TrafficType = profile.TrafficType,
            Production = profile.Production,
            Consumption = profile.Consumption,
            ProductionCostPerUnit = profile.ProductionCostPerUnit,
            ConsumerPremiumPerUnit = profile.ConsumerPremiumPerUnit,
            UnitPrice = profile.UnitPrice,
            SalesTaxRate = profile.SalesTaxRate,
            CanTransship = profile.CanTransship,
            ProductionStartPeriod = profile.ProductionStartPeriod,
            ProductionEndPeriod = profile.ProductionEndPeriod,
            ConsumptionStartPeriod = profile.ConsumptionStartPeriod,
            ConsumptionEndPeriod = profile.ConsumptionEndPeriod,
            IsStore = profile.IsStore,
            StoreCapacity = profile.StoreCapacity,
            Inventory = profile.Inventory,
            HoldingCostPerTime = profile.HoldingCostPerTime,
            Revenue = profile.Revenue,
            Profit = profile.Profit,
            ShortagePenalty = profile.ShortagePenalty,
            ProductionWindows = [.. profile.ProductionWindows.Select(window => new PeriodWindow
            {
                StartPeriod = window.StartPeriod,
                EndPeriod = window.EndPeriod
            })],
            ConsumptionWindows = [.. profile.ConsumptionWindows.Select(window => new PeriodWindow
            {
                StartPeriod = window.StartPeriod,
                EndPeriod = window.EndPeriod
            })],
            InputRequirements = [.. profile.InputRequirements.Select(requirement => new ProductionInputRequirement
            {
                TrafficType = requirement.TrafficType,
                InputQuantity = requirement.InputQuantity,
                OutputQuantity = requirement.OutputQuantity,
                QuantityPerOutputUnit = requirement.QuantityPerOutputUnit
            })]
        };
    }

    private static EdgeModel CloneEdge(EdgeModel edge)
    {
        return new EdgeModel
        {
            Id = edge.Id,
            FromNodeId = edge.FromNodeId,
            FromInterfaceNodeId = edge.FromInterfaceNodeId,
            ToNodeId = edge.ToNodeId,
            ToInterfaceNodeId = edge.ToInterfaceNodeId,
            LayerId = edge.LayerId,
            Time = edge.Time,
            Cost = edge.Cost,
            Capacity = edge.Capacity,
            IsBidirectional = edge.IsBidirectional,
            RouteType = edge.RouteType,
            AccessNotes = edge.AccessNotes,
            SeasonalRisk = edge.SeasonalRisk,
            TollNotes = edge.TollNotes,
            SecurityNotes = edge.SecurityNotes,
            TrafficPermissions = [.. edge.TrafficPermissions.Select(permission => new EdgeTrafficPermissionRule
            {
                TrafficType = permission.TrafficType,
                Mode = permission.Mode,
                LimitKind = permission.LimitKind,
                LimitValue = permission.LimitValue,
                IsActive = permission.IsActive
            })]
        };
    }

    private readonly record struct OverlayContextKey(long Revision, int EffectivePeriod, int ActiveTimelineEventSignature);
}

public sealed class CompiledNetworkSimulationContext
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public NetworkModel ProjectedNetwork { get; }
    public NetworkModel EffectiveNetwork { get; }
    public long Revision { get; }
    public int EffectivePeriod { get; }
    public int ActiveTimelineEventSignature { get; }
    public FrozenDictionary<string, NodeModel> NodesById { get; }
    public FrozenDictionary<string, EdgeModel> EdgesById { get; }
    public FrozenDictionary<string, TrafficTypeDefinition> TrafficDefinitionsByName { get; }
    public string[] OrderedTrafficNames { get; }
    public NodeModel[] NodesByIndex { get; }
    public EdgeModel[] EdgesByIndex { get; }
    public string[] NodeIdsByIndex { get; }
    public string[] EdgeIdsByIndex { get; }
    public FrozenDictionary<string, int> NodeIndexById { get; }
    public FrozenDictionary<string, int> EdgeIndexById { get; }
    public FrozenDictionary<string, int> TrafficTypeIndexByName { get; }
    public IndexedGraphArc[][] AdjacencyByNodeIndex { get; }
    public double[] BaseEdgeCapacityByIndex { get; }
    public double[] BaseNodeTranshipmentCapacityByIndex { get; }
    public FrozenDictionary<string, FrozenDictionary<string, NodeTrafficProfile?>> NodeProfilesByTrafficType { get; }
    public FrozenDictionary<string, FrozenDictionary<string, NodeTrafficProfile>> NodeProfilesByNodeAndTraffic { get; }
    public FrozenDictionary<string, FrozenSet<string>> PermittedSellerNodeIdsByTrafficType { get; }
    public FrozenDictionary<string, FrozenSet<string>> MeetingDemandEligibleNodeIdsByTrafficType { get; }
    public ulong[] AllowedTrafficMaskByEdgeIndex { get; }

    private CompiledNetworkSimulationContext(
        NetworkModel projectedNetwork,
        NetworkModel effectiveNetwork,
        long revision,
        int effectivePeriod,
        int activeTimelineEventSignature,
        FrozenDictionary<string, NodeModel> nodesById,
        FrozenDictionary<string, EdgeModel> edgesById,
        FrozenDictionary<string, TrafficTypeDefinition> trafficDefinitionsByName,
        string[] orderedTrafficNames,
        NodeModel[] nodesByIndex,
        EdgeModel[] edgesByIndex,
        string[] nodeIdsByIndex,
        string[] edgeIdsByIndex,
        FrozenDictionary<string, int> nodeIndexById,
        FrozenDictionary<string, int> edgeIndexById,
        FrozenDictionary<string, int> trafficTypeIndexByName,
        IndexedGraphArc[][] adjacencyByNodeIndex,
        double[] baseEdgeCapacityByIndex,
        double[] baseNodeTranshipmentCapacityByIndex,
        FrozenDictionary<string, FrozenDictionary<string, NodeTrafficProfile?>> nodeProfilesByTrafficType,
        FrozenDictionary<string, FrozenDictionary<string, NodeTrafficProfile>> nodeProfilesByNodeAndTraffic,
        FrozenDictionary<string, FrozenSet<string>> permittedSellerNodeIdsByTrafficType,
        FrozenDictionary<string, FrozenSet<string>> meetingDemandEligibleNodeIdsByTrafficType,
        ulong[] allowedTrafficMaskByEdgeIndex)
    {
        ProjectedNetwork = projectedNetwork;
        EffectiveNetwork = effectiveNetwork;
        Revision = revision;
        EffectivePeriod = effectivePeriod;
        ActiveTimelineEventSignature = activeTimelineEventSignature;
        NodesById = nodesById;
        EdgesById = edgesById;
        TrafficDefinitionsByName = trafficDefinitionsByName;
        OrderedTrafficNames = orderedTrafficNames;
        NodesByIndex = nodesByIndex;
        EdgesByIndex = edgesByIndex;
        NodeIdsByIndex = nodeIdsByIndex;
        EdgeIdsByIndex = edgeIdsByIndex;
        NodeIndexById = nodeIndexById;
        EdgeIndexById = edgeIndexById;
        TrafficTypeIndexByName = trafficTypeIndexByName;
        AdjacencyByNodeIndex = adjacencyByNodeIndex;
        BaseEdgeCapacityByIndex = baseEdgeCapacityByIndex;
        BaseNodeTranshipmentCapacityByIndex = baseNodeTranshipmentCapacityByIndex;
        NodeProfilesByTrafficType = nodeProfilesByTrafficType;
        NodeProfilesByNodeAndTraffic = nodeProfilesByNodeAndTraffic;
        PermittedSellerNodeIdsByTrafficType = permittedSellerNodeIdsByTrafficType;
        MeetingDemandEligibleNodeIdsByTrafficType = meetingDemandEligibleNodeIdsByTrafficType;
        AllowedTrafficMaskByEdgeIndex = allowedTrafficMaskByEdgeIndex;
    }

    public static CompiledNetworkSimulationContext Create(
        NetworkModel projectedNetwork,
        NetworkModel effectiveNetwork,
        long revision,
        int effectivePeriod,
        int activeTimelineEventSignature)
    {
        var nodesByIndex = effectiveNetwork.Nodes.ToArray();
        var edgesByIndex = effectiveNetwork.Edges.ToArray();
        var nodeIdsByIndex = new string[nodesByIndex.Length];
        var edgeIdsByIndex = new string[edgesByIndex.Length];
        var nodeIndexById = new Dictionary<string, int>(nodesByIndex.Length, Comparer);
        var edgeIndexById = new Dictionary<string, int>(edgesByIndex.Length, Comparer);
        var nodesById = new Dictionary<string, NodeModel>(nodesByIndex.Length, Comparer);
        var edgesById = new Dictionary<string, EdgeModel>(edgesByIndex.Length, Comparer);
        var baseNodeCapacity = new double[nodesByIndex.Length];
        var baseEdgeCapacity = new double[edgesByIndex.Length];

        for (var index = 0; index < nodesByIndex.Length; index++)
        {
            var node = nodesByIndex[index];
            nodeIdsByIndex[index] = node.Id;
            nodeIndexById[node.Id] = index;
            nodesById[node.Id] = node;
            baseNodeCapacity[index] = node.TranshipmentCapacity ?? double.PositiveInfinity;
        }

        for (var index = 0; index < edgesByIndex.Length; index++)
        {
            var edge = edgesByIndex[index];
            edgeIdsByIndex[index] = edge.Id;
            edgeIndexById[edge.Id] = index;
            edgesById[edge.Id] = edge;
            baseEdgeCapacity[index] = edge.Capacity ?? double.PositiveInfinity;
        }

        var trafficDefinitionsByName = effectiveNetwork.TrafficTypes
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Name))
            .GroupBy(definition => definition.Name, Comparer)
            .Select(group => group.First())
            .ToDictionary(definition => definition.Name, definition => definition, Comparer)
            .ToFrozenDictionary(Comparer);
        var orderedTrafficNames = GetOrderedTrafficNames(effectiveNetwork).ToArray();
        var trafficTypeIndexByName = orderedTrafficNames
            .Select((name, index) => new KeyValuePair<string, int>(name, index))
            .ToFrozenDictionary(Comparer);

        var adjacencyBuilders = new List<IndexedGraphArc>[nodesByIndex.Length];
        for (var edgeIndex = 0; edgeIndex < edgesByIndex.Length; edgeIndex++)
        {
            var edge = edgesByIndex[edgeIndex];
            if (!nodeIndexById.TryGetValue(edge.FromNodeId, out var fromIndex) ||
                !nodeIndexById.TryGetValue(edge.ToNodeId, out var toIndex))
            {
                continue;
            }

            AddArc(adjacencyBuilders, fromIndex, new IndexedGraphArc(edgeIndex, fromIndex, toIndex, edge.Time, edge.Cost));
            if (edge.IsBidirectional)
            {
                AddArc(adjacencyBuilders, toIndex, new IndexedGraphArc(edgeIndex, toIndex, fromIndex, edge.Time, edge.Cost));
            }
        }

        var adjacency = new IndexedGraphArc[nodesByIndex.Length][];
        for (var nodeIndex = 0; nodeIndex < adjacency.Length; nodeIndex++)
        {
            adjacency[nodeIndex] = adjacencyBuilders[nodeIndex] is { Count: > 0 } arcs
                ? [.. arcs]
                : [];
        }

        var profileByTraffic = new Dictionary<string, FrozenDictionary<string, NodeTrafficProfile?>>(Comparer);
        var profileByNode = new Dictionary<string, FrozenDictionary<string, NodeTrafficProfile>>(Comparer);
        var permittedSellers = new Dictionary<string, FrozenSet<string>>(Comparer);
        var meetingDemandEligible = new Dictionary<string, FrozenSet<string>>(Comparer);
        foreach (var trafficType in orderedTrafficNames)
        {
            var profiles = new Dictionary<string, NodeTrafficProfile?>(nodesByIndex.Length, Comparer);
            var eligibleNodes = new HashSet<string>(Comparer);
            foreach (var node in nodesByIndex)
            {
                var profile = node.TrafficProfiles.FirstOrDefault(candidate => Comparer.Equals(candidate.TrafficType, trafficType));
                profiles[node.Id] = profile;
                if (SimulationActorSellLocalPermissionResolver.CanReceiveMeetingNodeDemand(effectiveNetwork, node.Id, trafficType))
                {
                    eligibleNodes.Add(node.Id);
                }
            }

            profileByTraffic[trafficType] = profiles.ToFrozenDictionary(Comparer);
            permittedSellers[trafficType] = SimulationActorSellLocalPermissionResolver
                .BuildPermittedSellerNodeSet(effectiveNetwork, trafficType)
                .ToFrozenSet(Comparer);
            meetingDemandEligible[trafficType] = eligibleNodes.ToFrozenSet(Comparer);
        }

        foreach (var node in nodesByIndex)
        {
            var profiles = new Dictionary<string, NodeTrafficProfile>(Comparer);
            foreach (var profile in node.TrafficProfiles)
            {
                if (!string.IsNullOrWhiteSpace(profile.TrafficType))
                {
                    profiles[profile.TrafficType] = profile;
                }
            }

            profileByNode[node.Id] = profiles.ToFrozenDictionary(Comparer);
        }

        var permissionResolver = new EdgeTrafficPermissionResolver();
        var allowedTrafficMaskByEdgeIndex = new ulong[edgesByIndex.Length];
        if (orderedTrafficNames.Length <= 64)
        {
            for (var edgeIndex = 0; edgeIndex < edgesByIndex.Length; edgeIndex++)
            {
                var edge = edgesByIndex[edgeIndex];
                ulong mask = 0;
                for (var trafficIndex = 0; trafficIndex < orderedTrafficNames.Length; trafficIndex++)
                {
                    var trafficType = orderedTrafficNames[trafficIndex];
                    var allowed = permissionResolver.GetAllowedCapacity(edge, permissionResolver.Resolve(effectiveNetwork, edge, trafficType));
                    if (allowed > 0d)
                    {
                        mask |= 1UL << trafficIndex;
                    }
                }

                allowedTrafficMaskByEdgeIndex[edgeIndex] = mask;
            }
        }

        return new CompiledNetworkSimulationContext(
            projectedNetwork,
            effectiveNetwork,
            revision,
            effectivePeriod,
            activeTimelineEventSignature,
            nodesById.ToFrozenDictionary(Comparer),
            edgesById.ToFrozenDictionary(Comparer),
            trafficDefinitionsByName,
            orderedTrafficNames,
            nodesByIndex,
            edgesByIndex,
            nodeIdsByIndex,
            edgeIdsByIndex,
            nodeIndexById.ToFrozenDictionary(Comparer),
            edgeIndexById.ToFrozenDictionary(Comparer),
            trafficTypeIndexByName,
            adjacency,
            baseEdgeCapacity,
            baseNodeCapacity,
            profileByTraffic.ToFrozenDictionary(Comparer),
            profileByNode.ToFrozenDictionary(Comparer),
            permittedSellers.ToFrozenDictionary(Comparer),
            meetingDemandEligible.ToFrozenDictionary(Comparer),
            allowedTrafficMaskByEdgeIndex);
    }

    public bool IsTrafficAllowedOnEdge(int edgeIndex, string trafficType)
    {
        if (TrafficTypeIndexByName.Count > 64 || !TrafficTypeIndexByName.TryGetValue(trafficType, out var trafficIndex))
        {
            return true;
        }

        return (AllowedTrafficMaskByEdgeIndex[edgeIndex] & (1UL << trafficIndex)) != 0;
    }

    private static void AddArc(List<IndexedGraphArc>[] adjacencyBuilders, int fromIndex, IndexedGraphArc arc)
    {
        var arcs = adjacencyBuilders[fromIndex];
        if (arcs is null)
        {
            arcs = [];
            adjacencyBuilders[fromIndex] = arcs;
        }

        arcs.Add(arc);
    }

    private static List<string> GetOrderedTrafficNames(NetworkModel network)
    {
        var definitionsWithOrder = network.TrafficTypes
            .Select((definition, index) => new { Definition = definition, Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.Definition.Name))
            .GroupBy(item => item.Definition.Name, Comparer)
            .Select(group => group.First())
            .OrderBy(item => item.Definition.PerishabilityPeriods.HasValue ? 0 : 1)
            .ThenBy(item => item.Definition.PerishabilityPeriods ?? int.MaxValue)
            .ThenByDescending(item => item.Definition.RouteChoiceSettings?.Priority ?? 0d)
            .ThenBy(item => item.Index)
            .Select(item => item.Definition.Name)
            .ToList();
        var seen = new HashSet<string>(definitionsWithOrder, Comparer);
        foreach (var node in network.Nodes)
        {
            foreach (var profile in node.TrafficProfiles)
            {
                if (!string.IsNullOrWhiteSpace(profile.TrafficType) && seen.Add(profile.TrafficType))
                {
                    definitionsWithOrder.Add(profile.TrafficType);
                }

                foreach (var requirement in profile.InputRequirements)
                {
                    if (!string.IsNullOrWhiteSpace(requirement.TrafficType) && seen.Add(requirement.TrafficType))
                    {
                        definitionsWithOrder.Add(requirement.TrafficType);
                    }
                }
            }
        }

        return definitionsWithOrder;
    }
}

public readonly record struct IndexedGraphArc(int EdgeIndex, int FromNodeIndex, int ToNodeIndex, double Time, double Cost);

internal static class NetworkRevisionHasher
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static long Compute(NetworkModel network)
    {
        // This revision is intentionally conservative: when any simulation-relevant topology, traffic,
        // permission, actor, tax, seed, or timeline setting changes we rebuild instead of risking stale reuse.
        var hash = new HashCode();
        hash.Add(network.Name ?? string.Empty, Comparer);
        hash.Add(network.Description ?? string.Empty, Comparer);
        hash.Add(network.SimulationSeed);
        hash.Add(network.TimelineLoopLength.GetValueOrDefault());
        hash.Add((int)network.DefaultAllocationMode);
        hash.Add((int)network.AgentMode);
        hash.Add(network.LimitMeetingNodeDemandBySellLocalPermission);

        foreach (var traffic in network.TrafficTypes)
        {
            hash.Add(traffic.Name ?? string.Empty, Comparer);
            hash.Add((int)traffic.RoutingPreference);
            hash.Add((int)traffic.AllocationMode);
            hash.Add((int)traffic.RouteChoiceModel);
            hash.Add((int)traffic.FlowSplitPolicy);
            hash.Add(traffic.CapacityBidPerUnit.GetValueOrDefault());
            hash.Add(traffic.DefaultUnitProductionCost);
            hash.Add(traffic.RouteTaxRate);
            hash.Add(traffic.PerishabilityPeriods.GetValueOrDefault());
            if (traffic.RouteChoiceSettings is { } settings)
            {
                hash.Add(settings.MaxCandidateRoutes);
                hash.Add(settings.Priority);
                hash.Add(settings.InformationAccuracy);
                hash.Add(settings.RouteDiversity);
                hash.Add(settings.CongestionSensitivity);
                hash.Add(settings.RerouteThreshold);
                hash.Add(settings.Stickiness);
                hash.Add(settings.IterationCount);
                hash.Add(settings.InternalizeCongestion);
                hash.Add(settings.AdaptiveRoutingEnabled);
            }
        }

        foreach (var node in network.Nodes)
        {
            hash.Add(node.Id ?? string.Empty, Comparer);
            hash.Add(node.Name ?? string.Empty, Comparer);
            hash.Add(node.TranshipmentCapacity.GetValueOrDefault());
            hash.Add(node.X.GetValueOrDefault());
            hash.Add(node.Y.GetValueOrDefault());
            hash.Add(node.IsFacility);
            hash.Add(node.FacilityCapacity.GetValueOrDefault());
            hash.Add(node.ControllingActor ?? string.Empty, Comparer);
            foreach (var profile in node.TrafficProfiles)
            {
                hash.Add(profile.TrafficType ?? string.Empty, Comparer);
                hash.Add(profile.Production);
                hash.Add(profile.Consumption);
                hash.Add(profile.CanTransship);
                hash.Add(profile.IsStore);
                hash.Add(profile.StoreCapacity.GetValueOrDefault());
                hash.Add(profile.ProductionCostPerUnit.GetValueOrDefault());
                hash.Add(profile.ConsumerPremiumPerUnit);
                hash.Add(profile.UnitPrice);
                hash.Add(profile.SalesTaxRate.GetValueOrDefault());
                foreach (var requirement in profile.InputRequirements)
                {
                    hash.Add(requirement.TrafficType ?? string.Empty, Comparer);
                    hash.Add(requirement.InputQuantity);
                    hash.Add(requirement.OutputQuantity);
                    hash.Add(requirement.QuantityPerOutputUnit.GetValueOrDefault());
                }
            }
        }

        foreach (var edge in network.Edges)
        {
            hash.Add(edge.Id ?? string.Empty, Comparer);
            hash.Add(edge.FromNodeId ?? string.Empty, Comparer);
            hash.Add(edge.ToNodeId ?? string.Empty, Comparer);
            hash.Add(edge.IsBidirectional);
            hash.Add(edge.Time);
            hash.Add(edge.Cost);
            hash.Add(edge.Capacity.GetValueOrDefault());
            foreach (var permission in edge.TrafficPermissions)
            {
                hash.Add(permission.TrafficType ?? string.Empty, Comparer);
                hash.Add((int)permission.Mode);
                hash.Add((int)permission.LimitKind);
                hash.Add(permission.LimitValue.GetValueOrDefault());
                hash.Add(permission.IsActive);
            }
        }

        foreach (var timelineEvent in network.TimelineEvents)
        {
            hash.Add(timelineEvent.Name ?? string.Empty, Comparer);
            hash.Add(timelineEvent.StartPeriod.GetValueOrDefault());
            hash.Add(timelineEvent.EndPeriod.GetValueOrDefault());
            foreach (var effect in timelineEvent.Effects)
            {
                hash.Add((int)effect.EffectType);
                hash.Add(effect.NodeId ?? string.Empty, Comparer);
                hash.Add(effect.EdgeId ?? string.Empty, Comparer);
                hash.Add(effect.TrafficType ?? string.Empty, Comparer);
                hash.Add(effect.Multiplier);
            }
        }

        foreach (var permission in network.EdgeTrafficPermissionDefaults)
        {
            hash.Add(permission.TrafficType ?? string.Empty, Comparer);
            hash.Add((int)permission.Mode);
            hash.Add((int)permission.LimitKind);
            hash.Add(permission.LimitValue.GetValueOrDefault());
            hash.Add(permission.IsActive);
        }

        foreach (var rule in network.RouteTaxRules)
        {
            hash.Add(rule.EdgeId ?? string.Empty, Comparer);
            hash.Add(rule.TrafficType ?? string.Empty, Comparer);
            hash.Add(rule.TaxAuthorityActorId ?? string.Empty, Comparer);
            hash.Add(rule.TaxRate);
            hash.Add(rule.IsActive);
        }

        foreach (var actor in network.Actors)
        {
            hash.Add(actor.Id ?? string.Empty, Comparer);
            hash.Add(actor.IsEnabled);
            hash.Add((int)actor.Kind);
            foreach (var nodeId in actor.ControlledNodeIds)
            {
                hash.Add(nodeId ?? string.Empty, Comparer);
            }
        }

        return hash.ToHashCode();
    }
}

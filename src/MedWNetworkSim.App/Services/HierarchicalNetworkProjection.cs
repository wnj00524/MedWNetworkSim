using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

/// <summary>
/// Builds a one-level simulation graph from a parent network with embedded subnetworks.
/// </summary>
public static class HierarchicalNetworkProjection
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static NetworkModel ProjectForSimulation(NetworkModel network)
    {
        ArgumentNullException.ThrowIfNull(network);
        if (network.Subnetworks is null || network.Subnetworks.Count == 0)
        {
            return network;
        }

        var subnetworksById = network.Subnetworks.ToDictionary(subnetwork => subnetwork.Id, subnetwork => subnetwork, Comparer);
        var nodesById = network.Nodes.ToDictionary(node => node.Id, node => node, Comparer);
        var projected = new NetworkModel
        {
            Name = network.Name,
            Description = network.Description,
            TimelineLoopLength = network.TimelineLoopLength,
            DefaultAllocationMode = network.DefaultAllocationMode,
            SimulationSeed = network.SimulationSeed,
            TrafficTypes = MergeTrafficTypes(network),
            TimelineEvents = ProjectTimelineEvents(network, nodesById, subnetworksById),
            Nodes = [],
            Edges = []
        };

        foreach (var node in network.Nodes.Where(node => node.NodeKind != NodeKind.CompositeSubnetwork))
        {
            projected.Nodes.Add(CloneNode(node));
        }

        foreach (var compositeNode in network.Nodes.Where(node => node.NodeKind == NodeKind.CompositeSubnetwork))
        {
            if (string.IsNullOrWhiteSpace(compositeNode.ReferencedSubnetworkId) ||
                !subnetworksById.TryGetValue(compositeNode.ReferencedSubnetworkId, out var subnetwork))
            {
                continue;
            }

            foreach (var childNode in subnetwork.Network.Nodes)
            {
                var projectedChildNode = CloneNode(childNode);
                projectedChildNode.Id = ScopeNodeId(subnetwork.Id, childNode.Id);
                projectedChildNode.Name = $"{compositeNode.Name} / {childNode.Name}";
                projectedChildNode.X = compositeNode.X.HasValue && childNode.X.HasValue ? compositeNode.X + (childNode.X / 8d) : compositeNode.X;
                projectedChildNode.Y = compositeNode.Y.HasValue && childNode.Y.HasValue ? compositeNode.Y + (childNode.Y / 8d) : compositeNode.Y;
                projected.Nodes.Add(projectedChildNode);
            }

            foreach (var childEdge in subnetwork.Network.Edges)
            {
                var projectedChildEdge = CloneEdge(childEdge);
                projectedChildEdge.Id = ScopeEdgeId(subnetwork.Id, childEdge.Id);
                projectedChildEdge.FromNodeId = ScopeNodeId(subnetwork.Id, childEdge.FromNodeId);
                projectedChildEdge.ToNodeId = ScopeNodeId(subnetwork.Id, childEdge.ToNodeId);
                projected.Edges.Add(projectedChildEdge);
            }
        }

        foreach (var edge in network.Edges)
        {
            var projectedEdge = CloneEdge(edge);
            projectedEdge.FromNodeId = ProjectEndpoint(edge.Id, edge.FromNodeId, edge.FromInterfaceNodeId, nodesById, subnetworksById, "source");
            projectedEdge.ToNodeId = ProjectEndpoint(edge.Id, edge.ToNodeId, edge.ToInterfaceNodeId, nodesById, subnetworksById, "target");
            projectedEdge.FromInterfaceNodeId = null;
            projectedEdge.ToInterfaceNodeId = null;
            projected.Edges.Add(projectedEdge);
        }

        return projected;
    }

    private static string ProjectEndpoint(
        string edgeId,
        string nodeId,
        string? interfaceNodeId,
        IReadOnlyDictionary<string, NodeModel> nodesById,
        IReadOnlyDictionary<string, SubnetworkDefinition> subnetworksById,
        string endpointName)
    {
        if (!nodesById.TryGetValue(nodeId, out var node) || node.NodeKind != NodeKind.CompositeSubnetwork)
        {
            return nodeId;
        }

        if (string.IsNullOrWhiteSpace(node.ReferencedSubnetworkId) ||
            !subnetworksById.TryGetValue(node.ReferencedSubnetworkId, out var subnetwork) ||
            string.IsNullOrWhiteSpace(interfaceNodeId))
        {
            throw new InvalidOperationException($"Edge '{edgeId}' cannot project its {endpointName} composite endpoint '{nodeId}' without a valid exposed interface.");
        }

        var interfaceNode = subnetwork.Network.Nodes.FirstOrDefault(childNode => Comparer.Equals(childNode.Id, interfaceNodeId));
        if (interfaceNode is null || !interfaceNode.IsExternalInterface)
        {
            throw new InvalidOperationException($"Edge '{edgeId}' references '{interfaceNodeId}' on composite '{nodeId}', but it is not an exposed child interface.");
        }

        return ScopeNodeId(subnetwork.Id, interfaceNode.Id);
    }

    private static List<TrafficTypeDefinition> MergeTrafficTypes(NetworkModel network)
    {
        var definitions = new Dictionary<string, TrafficTypeDefinition>(Comparer);
        foreach (var definition in network.TrafficTypes.Concat((network.Subnetworks ?? []).SelectMany(subnetwork => subnetwork.Network.TrafficTypes)))
        {
            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                continue;
            }

            definitions.TryAdd(definition.Name, CloneTrafficType(definition));
        }

        return definitions.Values.OrderBy(definition => definition.Name, Comparer).ToList();
    }

    private static List<TimelineEventModel> ProjectTimelineEvents(
        NetworkModel network,
        IReadOnlyDictionary<string, NodeModel> nodesById,
        IReadOnlyDictionary<string, SubnetworkDefinition> subnetworksById)
    {
        var result = network.TimelineEvents.Select(CloneTimelineEvent).ToList();
        foreach (var compositeNode in network.Nodes.Where(node => node.NodeKind == NodeKind.CompositeSubnetwork))
        {
            if (string.IsNullOrWhiteSpace(compositeNode.ReferencedSubnetworkId) ||
                !subnetworksById.TryGetValue(compositeNode.ReferencedSubnetworkId, out var subnetwork))
            {
                continue;
            }

            foreach (var timelineEvent in subnetwork.Network.TimelineEvents)
            {
                var clone = CloneTimelineEvent(timelineEvent);
                clone.Id = ScopeEdgeId(subnetwork.Id, timelineEvent.Id);
                foreach (var effect in clone.Effects)
                {
                    if (!string.IsNullOrWhiteSpace(effect.NodeId))
                    {
                        effect.NodeId = ScopeNodeId(subnetwork.Id, effect.NodeId);
                    }

                    if (!string.IsNullOrWhiteSpace(effect.EdgeId))
                    {
                        effect.EdgeId = ScopeEdgeId(subnetwork.Id, effect.EdgeId);
                    }
                }

                result.Add(clone);
            }
        }

        return result;
    }

    private static NodeModel CloneNode(NodeModel node)
    {
        return new NodeModel
        {
            Id = node.Id,
            Name = node.Name,
            Shape = node.Shape,
            NodeKind = node.NodeKind == NodeKind.CompositeSubnetwork ? NodeKind.Ordinary : node.NodeKind,
            IsExternalInterface = node.IsExternalInterface,
            InterfaceName = node.InterfaceName,
            X = node.X,
            Y = node.Y,
            TranshipmentCapacity = node.TranshipmentCapacity,
            PlaceType = node.PlaceType,
            LoreDescription = node.LoreDescription,
            ControllingActor = node.ControllingActor,
            Tags = node.Tags.ToList(),
            TemplateId = node.TemplateId,
            TrafficProfiles = node.TrafficProfiles.Select(CloneProfile).ToList()
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
            Time = edge.Time,
            Cost = edge.Cost,
            Capacity = edge.Capacity,
            IsBidirectional = edge.IsBidirectional,
            RouteType = edge.RouteType,
            AccessNotes = edge.AccessNotes,
            SeasonalRisk = edge.SeasonalRisk,
            TollNotes = edge.TollNotes,
            SecurityNotes = edge.SecurityNotes
        };
    }

    private static NodeTrafficProfile CloneProfile(NodeTrafficProfile profile)
    {
        return new NodeTrafficProfile
        {
            TrafficType = profile.TrafficType,
            Production = profile.Production,
            Consumption = profile.Consumption,
            ConsumerPremiumPerUnit = profile.ConsumerPremiumPerUnit,
            CanTransship = profile.CanTransship,
            ProductionStartPeriod = profile.ProductionStartPeriod,
            ProductionEndPeriod = profile.ProductionEndPeriod,
            ConsumptionStartPeriod = profile.ConsumptionStartPeriod,
            ConsumptionEndPeriod = profile.ConsumptionEndPeriod,
            ProductionWindows = profile.ProductionWindows.Select(window => new PeriodWindow { StartPeriod = window.StartPeriod, EndPeriod = window.EndPeriod }).ToList(),
            ConsumptionWindows = profile.ConsumptionWindows.Select(window => new PeriodWindow { StartPeriod = window.StartPeriod, EndPeriod = window.EndPeriod }).ToList(),
            InputRequirements = profile.InputRequirements.Select(requirement => new ProductionInputRequirement { TrafficType = requirement.TrafficType, QuantityPerOutputUnit = requirement.QuantityPerOutputUnit }).ToList(),
            IsStore = profile.IsStore,
            StoreCapacity = profile.StoreCapacity
        };
    }

    private static TrafficTypeDefinition CloneTrafficType(TrafficTypeDefinition definition)
    {
        return new TrafficTypeDefinition
        {
            Name = definition.Name,
            Description = definition.Description,
            RoutingPreference = definition.RoutingPreference,
            AllocationMode = definition.AllocationMode,
            RouteChoiceModel = definition.RouteChoiceModel,
            FlowSplitPolicy = definition.FlowSplitPolicy,
            RouteChoiceSettings = definition.RouteChoiceSettings,
            CapacityBidPerUnit = definition.CapacityBidPerUnit
        };
    }

    private static TimelineEventModel CloneTimelineEvent(TimelineEventModel timelineEvent)
    {
        return new TimelineEventModel
        {
            Id = timelineEvent.Id,
            Name = timelineEvent.Name,
            StartPeriod = timelineEvent.StartPeriod,
            EndPeriod = timelineEvent.EndPeriod,
            Effects = timelineEvent.Effects.Select(effect => new TimelineEventEffectModel
            {
                EffectType = effect.EffectType,
                NodeId = effect.NodeId,
                EdgeId = effect.EdgeId,
                TrafficType = effect.TrafficType,
                Multiplier = effect.Multiplier
            }).ToList()
        };
    }

    private static string ScopeNodeId(string subnetworkId, string nodeId)
    {
        return $"sub:{subnetworkId}:{nodeId}";
    }

    private static string ScopeEdgeId(string subnetworkId, string edgeId)
    {
        return $"sub:{subnetworkId}:{edgeId}";
    }
}

using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.Services.Facility;

public sealed class FacilityModeSimulationEngine
{
    private const double Epsilon = 0.000001d;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private readonly NetworkSimulationEngine baseEngine;
    private readonly FacilityAssignmentEngine assignmentEngine = new();

    public FacilityModeSimulationEngine(NetworkSimulationEngine baseEngine)
    {
        this.baseEngine = baseEngine;
    }

    public IReadOnlyList<TrafficSimulationOutcome> Simulate(NetworkModel network)
    {
        ArgumentNullException.ThrowIfNull(network);

        var trafficTypes = network.TrafficTypes
            .Where(traffic => !string.IsNullOrWhiteSpace(traffic.Name))
            .ToList();
        if (trafficTypes.Count == 0)
        {
            return [];
        }

        if (!network.Nodes.Any(node => node.IsFacility))
        {
            var fallback = CloneNetwork(network, facilityModeEnabled: false);
            return baseEngine.Simulate(fallback)
                .Select(outcome => AddNote(outcome, "Facility mode was enabled, but no facility nodes were marked. Ran the standard simulation instead."))
                .ToList();
        }

        var outcomes = new List<TrafficSimulationOutcome>();
        foreach (var traffic in trafficTypes)
        {
            var assignment = assignmentEngine.Assign(network, traffic.Name);
            var totalDemand = GetTotalDemand(network, traffic.Name);

            if (assignment.FacilityByDemandNodeId.Count == 0)
            {
                outcomes.Add(BuildEmptyOutcome(
                    traffic,
                    totalDemand,
                    $"Facility mode found no reachable demand nodes for traffic type '{traffic.Name}'."));
                continue;
            }

            var inboundDemandByFacility = BuildInboundDemandByFacility(network, traffic.Name, assignment);
            var inboundNetwork = BuildInboundNetwork(network, traffic, inboundDemandByFacility);
            var inboundOutcome = baseEngine.Simulate(inboundNetwork).FirstOrDefault();
            var deliveredToFacility = (inboundOutcome?.Allocations ?? [])
                .GroupBy(allocation => allocation.ConsumerNodeId, Comparer)
                .ToDictionary(group => group.Key, group => group.Sum(allocation => allocation.Quantity), Comparer);

            var outboundNetwork = BuildOutboundNetwork(network, traffic, assignment, deliveredToFacility);
            var outboundOutcome = baseEngine.Simulate(outboundNetwork).FirstOrDefault();

            outcomes.Add(CombineFacilityOutcome(
                traffic,
                network,
                assignment,
                inboundOutcome,
                outboundOutcome,
                totalDemand));
        }

        return outcomes;
    }

    private static Dictionary<string, double> BuildInboundDemandByFacility(
        NetworkModel network,
        string trafficType,
        FacilityAssignmentResult assignment)
    {
        var demandByFacility = new Dictionary<string, double>(Comparer);
        foreach (var node in network.Nodes)
        {
            if (!assignment.FacilityByDemandNodeId.TryGetValue(node.Id, out var facilityId))
            {
                continue;
            }

            demandByFacility[facilityId] = demandByFacility.GetValueOrDefault(facilityId) + GetDemand(node, trafficType);
        }

        foreach (var facility in network.Nodes.Where(node => node.IsFacility && node.FacilityCapacity.HasValue))
        {
            if (!demandByFacility.TryGetValue(facility.Id, out var assignedDemand))
            {
                continue;
            }

            demandByFacility[facility.Id] = Math.Min(assignedDemand, Math.Max(0d, facility.FacilityCapacity.GetValueOrDefault()));
        }

        return demandByFacility;
    }

    private static NetworkModel BuildInboundNetwork(
        NetworkModel network,
        TrafficTypeDefinition traffic,
        IReadOnlyDictionary<string, double> inboundDemandByFacility)
    {
        var clone = CloneNetwork(network, facilityModeEnabled: false, traffic.Name);
        foreach (var node in clone.Nodes)
        {
            foreach (var profile in node.TrafficProfiles.Where(profile => Comparer.Equals(profile.TrafficType, traffic.Name)))
            {
                profile.Consumption = inboundDemandByFacility.TryGetValue(node.Id, out var demand) ? demand : 0d;
                profile.CanTransship = true;
            }
        }

        return clone;
    }

    private static NetworkModel BuildOutboundNetwork(
        NetworkModel network,
        TrafficTypeDefinition traffic,
        FacilityAssignmentResult assignment,
        IReadOnlyDictionary<string, double> deliveredToFacility)
    {
        var clone = CloneNetwork(network, facilityModeEnabled: false, traffic.Name);
        var demandByNode = network.Nodes.ToDictionary(node => node.Id, node => GetDemand(node, traffic.Name), Comparer);

        foreach (var node in clone.Nodes)
        {
            foreach (var profile in node.TrafficProfiles.Where(profile => Comparer.Equals(profile.TrafficType, traffic.Name)))
            {
                profile.Production = 0d;
                profile.Consumption = 0d;
                profile.CanTransship = true;
            }
        }

        foreach (var facility in clone.Nodes.Where(node => node.IsFacility))
        {
            var profile = EnsureProfile(facility, traffic.Name);
            profile.Production = deliveredToFacility.GetValueOrDefault(facility.Id);
            profile.Consumption = 0d;
            profile.CanTransship = true;
        }

        foreach (var (demandNodeId, facilityId) in assignment.FacilityByDemandNodeId)
        {
            if (!clone.Nodes.Any(node => Comparer.Equals(node.Id, facilityId)) ||
                !demandByNode.TryGetValue(demandNodeId, out var demand) ||
                demand <= Epsilon)
            {
                continue;
            }

            var node = clone.Nodes.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, demandNodeId));
            if (node is null)
            {
                continue;
            }

            var profile = EnsureProfile(node, traffic.Name);
            profile.Consumption = demand;
            profile.Production = 0d;
            profile.CanTransship = true;
        }

        return clone;
    }

    private static TrafficSimulationOutcome CombineFacilityOutcome(
        TrafficTypeDefinition traffic,
        NetworkModel network,
        FacilityAssignmentResult assignment,
        TrafficSimulationOutcome? inboundOutcome,
        TrafficSimulationOutcome? outboundOutcome,
        double totalDemand)
    {
        var allocations = (inboundOutcome?.Allocations ?? [])
            .Concat(outboundOutcome?.Allocations ?? [])
            .ToList();
        var totalProduction = GetTotalProduction(network, traffic.Name);
        var delivered = outboundOutcome?.TotalDelivered ?? 0d;
        var notes = new List<string>
        {
            $"Facility mode assigned {assignment.FacilityByDemandNodeId.Count} demand node(s) for traffic type '{traffic.Name}'.",
        };

        if (assignment.UnassignedDemandNodeIds.Count > 0)
        {
            notes.Add($"{assignment.UnassignedDemandNodeIds.Count} demand node(s) were outside the facility coverage threshold or unreachable.");
        }

        foreach (var pair in assignment.OverflowDemandByFacilityNodeId.OrderBy(pair => pair.Key, Comparer))
        {
            notes.Add($"Facility '{GetNodeName(network, pair.Key)}' exceeded its facility capacity by {pair.Value:0.##} unit(s).");
        }

        if (inboundOutcome is not null)
        {
            notes.AddRange(inboundOutcome.Notes.Select(note => $"Inbound to facility: {note}"));
        }

        if (outboundOutcome is not null)
        {
            notes.AddRange(outboundOutcome.Notes.Select(note => $"Outbound from facility: {note}"));
        }

        return new TrafficSimulationOutcome
        {
            TrafficType = traffic.Name,
            RoutingPreference = traffic.RoutingPreference,
            AllocationMode = traffic.AllocationMode,
            TotalProduction = totalProduction,
            TotalConsumption = totalDemand,
            TotalDelivered = delivered,
            UnusedSupply = Math.Max(0d, totalProduction - (inboundOutcome?.TotalDelivered ?? 0d)),
            UnmetDemand = Math.Max(0d, totalDemand - delivered),
            Allocations = allocations,
            Notes = notes
        };
    }

    private static TrafficSimulationOutcome BuildEmptyOutcome(TrafficTypeDefinition traffic, double totalDemand, string note) =>
        new()
        {
            TrafficType = traffic.Name,
            RoutingPreference = traffic.RoutingPreference,
            AllocationMode = traffic.AllocationMode,
            TotalProduction = 0d,
            TotalConsumption = totalDemand,
            TotalDelivered = 0d,
            UnusedSupply = 0d,
            UnmetDemand = totalDemand,
            Allocations = [],
            Notes = [note]
        };

    private static TrafficSimulationOutcome AddNote(TrafficSimulationOutcome outcome, string note) =>
        new()
        {
            TrafficType = outcome.TrafficType,
            RoutingPreference = outcome.RoutingPreference,
            AllocationMode = outcome.AllocationMode,
            TotalProduction = outcome.TotalProduction,
            TotalConsumption = outcome.TotalConsumption,
            TotalDelivered = outcome.TotalDelivered,
            UnusedSupply = outcome.UnusedSupply,
            UnmetDemand = outcome.UnmetDemand,
            Allocations = outcome.Allocations,
            Notes = outcome.Notes.Concat([note]).ToList()
        };

    private static NetworkModel CloneNetwork(NetworkModel network, bool facilityModeEnabled, string? onlyTrafficType = null)
    {
        var trafficTypes = string.IsNullOrWhiteSpace(onlyTrafficType)
            ? network.TrafficTypes.Select(CloneTrafficType).ToList()
            : network.TrafficTypes.Where(traffic => Comparer.Equals(traffic.Name, onlyTrafficType)).Select(CloneTrafficType).ToList();

        return new NetworkModel
        {
            Name = network.Name,
            Description = network.Description,
            TimelineLoopLength = network.TimelineLoopLength,
            DefaultAllocationMode = network.DefaultAllocationMode,
            SimulationSeed = network.SimulationSeed,
            FacilityModeEnabled = facilityModeEnabled,
            FacilityCoverageThreshold = network.FacilityCoverageThreshold,
            TrafficTypes = trafficTypes,
            TimelineEvents = network.TimelineEvents.ToList(),
            EdgeTrafficPermissionDefaults = network.EdgeTrafficPermissionDefaults.ToList(),
            Subnetworks = network.Subnetworks?.ToList(),
            Nodes = network.Nodes.Select(CloneNode).ToList(),
            Edges = network.Edges.Select(CloneEdge).ToList()
        };
    }

    private static NodeModel CloneNode(NodeModel node) =>
        new()
        {
            Id = node.Id,
            Name = node.Name,
            Shape = node.Shape,
            NodeKind = node.NodeKind,
            ReferencedSubnetworkId = node.ReferencedSubnetworkId,
            IsExternalInterface = node.IsExternalInterface,
            InterfaceName = node.InterfaceName,
            X = node.X,
            Y = node.Y,
            TranshipmentCapacity = node.TranshipmentCapacity,
            IsFacility = node.IsFacility,
            FacilityCapacity = node.FacilityCapacity,
            PlaceType = node.PlaceType,
            LoreDescription = node.LoreDescription,
            ControllingActor = node.ControllingActor,
            Tags = node.Tags.ToList(),
            TemplateId = node.TemplateId,
            TrafficProfiles = node.TrafficProfiles.Select(CloneProfile).ToList()
        };

    private static EdgeModel CloneEdge(EdgeModel edge) =>
        new()
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
            SecurityNotes = edge.SecurityNotes,
            TrafficPermissions = edge.TrafficPermissions.ToList()
        };

    private static TrafficTypeDefinition CloneTrafficType(TrafficTypeDefinition traffic) =>
        new()
        {
            Name = traffic.Name,
            Description = traffic.Description,
            RoutingPreference = traffic.RoutingPreference,
            AllocationMode = traffic.AllocationMode,
            RouteChoiceModel = traffic.RouteChoiceModel,
            FlowSplitPolicy = traffic.FlowSplitPolicy,
            RouteChoiceSettings = traffic.RouteChoiceSettings,
            CapacityBidPerUnit = traffic.CapacityBidPerUnit,
            PerishabilityPeriods = traffic.PerishabilityPeriods
        };

    private static NodeTrafficProfile CloneProfile(NodeTrafficProfile profile) =>
        new()
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
            ProductionWindows = profile.ProductionWindows.ToList(),
            ConsumptionWindows = profile.ConsumptionWindows.ToList(),
            InputRequirements = profile.InputRequirements.ToList(),
            IsStore = profile.IsStore,
            StoreCapacity = profile.StoreCapacity
        };

    private static NodeTrafficProfile EnsureProfile(NodeModel node, string trafficType)
    {
        var profile = node.TrafficProfiles.FirstOrDefault(profile => Comparer.Equals(profile.TrafficType, trafficType));
        if (profile is not null)
        {
            return profile;
        }

        profile = new NodeTrafficProfile
        {
            TrafficType = trafficType,
            CanTransship = true
        };
        node.TrafficProfiles.Add(profile);
        return profile;
    }

    private static double GetDemand(NodeModel node, string trafficType) =>
        node.TrafficProfiles
            .Where(profile => Comparer.Equals(profile.TrafficType, trafficType))
            .Sum(profile => Math.Max(0d, profile.Consumption));

    private static double GetTotalDemand(NetworkModel network, string trafficType) =>
        network.Nodes.Sum(node => GetDemand(node, trafficType));

    private static double GetTotalProduction(NetworkModel network, string trafficType) =>
        network.Nodes.Sum(node => node.TrafficProfiles
            .Where(profile => Comparer.Equals(profile.TrafficType, trafficType))
            .Sum(profile => Math.Max(0d, profile.Production)));

    private static string GetNodeName(NetworkModel network, string nodeId) =>
        network.Nodes.FirstOrDefault(node => Comparer.Equals(node.Id, nodeId))?.Name ?? nodeId;
}

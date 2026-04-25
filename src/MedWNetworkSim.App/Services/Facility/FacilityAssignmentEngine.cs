using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services.Facility;

public sealed class FacilityAssignmentEngine
{
    private const double Epsilon = 0.000001d;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public FacilityAssignmentResult Assign(NetworkModel network, string trafficType)
    {
        ArgumentNullException.ThrowIfNull(network);

        var threshold = network.FacilityCoverageThreshold.GetValueOrDefault();
        var maxCost = threshold > Epsilon ? threshold : double.PositiveInfinity;
        var nodesById = network.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, node => node, Comparer);
        var facilities = network.Nodes
            .Where(node => node.IsFacility && !string.IsNullOrWhiteSpace(node.Id))
            .OrderBy(node => node.Name, Comparer)
            .ThenBy(node => node.Id, Comparer)
            .ToList();

        if (facilities.Count == 0)
        {
            return new FacilityAssignmentResult(
                trafficType,
                [],
                [],
                [],
                [],
                network.Nodes
                    .Where(node => GetDemand(node, trafficType) > Epsilon)
                    .Select(node => node.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToList());
        }

        var adjacency = BuildAdjacency(network.Edges);
        var facilityDistances = facilities.ToDictionary(
            facility => facility.Id,
            facility => ComputeCostMap(facility.Id, adjacency, maxCost),
            Comparer);

        var assignments = new Dictionary<string, string>(Comparer);
        var assignmentCosts = new Dictionary<string, double>(Comparer);
        var demandByFacility = facilities.ToDictionary(facility => facility.Id, _ => 0d, Comparer);
        var unassignedNodeIds = new List<string>();

        foreach (var node in network.Nodes
                     .Where(node => GetDemand(node, trafficType) > Epsilon)
                     .OrderBy(node => node.Name, Comparer)
                     .ThenBy(node => node.Id, Comparer))
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                continue;
            }

            var bestFacility = facilities
                .Select(facility => new
                {
                    Facility = facility,
                    Cost = facilityDistances[facility.Id].TryGetValue(node.Id, out var cost) ? cost : double.PositiveInfinity
                })
                .Where(candidate => !double.IsPositiveInfinity(candidate.Cost))
                .OrderBy(candidate => candidate.Cost)
                .ThenBy(candidate => candidate.Facility.Name, Comparer)
                .ThenBy(candidate => candidate.Facility.Id, Comparer)
                .FirstOrDefault();

            if (bestFacility is null)
            {
                unassignedNodeIds.Add(node.Id);
                continue;
            }

            assignments[node.Id] = bestFacility.Facility.Id;
            assignmentCosts[node.Id] = bestFacility.Cost;
            demandByFacility[bestFacility.Facility.Id] += GetDemand(node, trafficType);
        }

        var overflowDemandByFacility = new Dictionary<string, double>(Comparer);
        foreach (var facility in facilities.Where(facility => facility.FacilityCapacity.HasValue))
        {
            var capacity = Math.Max(0d, facility.FacilityCapacity.GetValueOrDefault());
            var assigned = demandByFacility.GetValueOrDefault(facility.Id);
            var overflow = assigned - capacity;
            if (overflow > Epsilon)
            {
                overflowDemandByFacility[facility.Id] = overflow;
            }
        }

        return new FacilityAssignmentResult(
            trafficType,
            assignments,
            assignmentCosts,
            demandByFacility,
            overflowDemandByFacility,
            unassignedNodeIds);
    }

    private static double GetDemand(NodeModel node, string trafficType) =>
        node.TrafficProfiles
            .Where(profile => Comparer.Equals(profile.TrafficType, trafficType))
            .Sum(profile => Math.Max(0d, profile.Consumption));

    private static Dictionary<string, List<Segment>> BuildAdjacency(IReadOnlyCollection<EdgeModel> edges)
    {
        var adjacency = new Dictionary<string, List<Segment>>(Comparer);
        foreach (var edge in edges)
        {
            if (string.IsNullOrWhiteSpace(edge.FromNodeId) || string.IsNullOrWhiteSpace(edge.ToNodeId))
            {
                continue;
            }

            var weight = Math.Max(0d, edge.Time);
            AddArc(adjacency, edge.FromNodeId, edge.ToNodeId, weight);
            if (edge.IsBidirectional)
            {
                AddArc(adjacency, edge.ToNodeId, edge.FromNodeId, weight);
            }
        }

        return adjacency;
    }

    private static Dictionary<string, double> ComputeCostMap(
        string originId,
        IReadOnlyDictionary<string, List<Segment>> adjacency,
        double maxCost)
    {
        var bestCostByNode = new Dictionary<string, double>(Comparer)
        {
            [originId] = 0d
        };
        var queue = new PriorityQueue<string, double>();
        queue.Enqueue(originId, 0d);

        while (queue.TryDequeue(out var currentId, out var queuedCost))
        {
            if (!bestCostByNode.TryGetValue(currentId, out var currentCost) || queuedCost > currentCost + Epsilon)
            {
                continue;
            }

            if (currentCost > maxCost || !adjacency.TryGetValue(currentId, out var outgoing))
            {
                continue;
            }

            foreach (var segment in outgoing)
            {
                var nextCost = currentCost + segment.TravelTime;
                if (nextCost > maxCost)
                {
                    continue;
                }

                if (!bestCostByNode.TryGetValue(segment.Target, out var existing) || nextCost < existing)
                {
                    bestCostByNode[segment.Target] = nextCost;
                    queue.Enqueue(segment.Target, nextCost);
                }
            }
        }

        return bestCostByNode;
    }

    private static void AddArc(IDictionary<string, List<Segment>> adjacency, string from, string to, double travelTime)
    {
        if (!adjacency.TryGetValue(from, out var segments))
        {
            segments = [];
            adjacency[from] = segments;
        }

        segments.Add(new Segment(to, travelTime));
    }

    private sealed record Segment(string Target, double TravelTime);
}

public sealed record FacilityAssignmentResult(
    string TrafficType,
    IReadOnlyDictionary<string, string> FacilityByDemandNodeId,
    IReadOnlyDictionary<string, double> CostByDemandNodeId,
    IReadOnlyDictionary<string, double> DemandByFacilityNodeId,
    IReadOnlyDictionary<string, double> OverflowDemandByFacilityNodeId,
    IReadOnlyCollection<string> UnassignedDemandNodeIds);

using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services.Facility;

public sealed class FacilityCoverageColourAllocator
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private static readonly FacilityCoverageStyle[] Palette =
    [
        new("#2563EB", "#DBEAFE", "solid"),
        new("#DC2626", "#FEE2E2", "diagonal"),
        new("#059669", "#D1FAE5", "cross"),
        new("#7C3AED", "#EDE9FE", "dots"),
        new("#EA580C", "#FFEDD5", "stripe"),
        new("#0891B2", "#CFFAFE", "ring"),
        new("#BE123C", "#FFE4E6", "dash"),
        new("#4D7C0F", "#ECFCCB", "grid")
    ];

    public IReadOnlyDictionary<string, FacilityCoverageStyle> Allocate(
        NetworkModel network,
        FacilityAssignmentResult assignment)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(assignment);

        var facilities = assignment.FacilityByDemandNodeId.Values
            .Concat(network.Nodes.Where(node => node.IsFacility).Select(node => node.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(Comparer)
            .OrderBy(id => id, Comparer)
            .ToList();

        var adjacency = BuildFacilityAdjacency(network, assignment, facilities);
        var result = new Dictionary<string, FacilityCoverageStyle>(Comparer);

        foreach (var facilityId in facilities
                     .OrderByDescending(id => adjacency.TryGetValue(id, out var neighbours) ? neighbours.Count : 0)
                     .ThenBy(id => id, Comparer))
        {
            var used = adjacency.TryGetValue(facilityId, out var neighbours)
                ? neighbours
                    .Where(result.ContainsKey)
                    .Select(id => result[id].FillColorHex)
                    .ToHashSet(Comparer)
                : [];

            var style = Palette.FirstOrDefault(candidate => !used.Contains(candidate.FillColorHex))
                ?? Palette[result.Count % Palette.Length];
            result[facilityId] = style with
            {
                FacilityNodeId = facilityId,
                LegendLabel = GetNodeName(network, facilityId)
            };
        }

        return result;
    }

    private static Dictionary<string, HashSet<string>> BuildFacilityAdjacency(
        NetworkModel network,
        FacilityAssignmentResult assignment,
        IReadOnlyCollection<string> facilities)
    {
        var result = facilities.ToDictionary(id => id, _ => new HashSet<string>(Comparer), Comparer);
        foreach (var edge in network.Edges)
        {
            var leftFacility = GetFacilityForNode(edge.FromNodeId, assignment);
            var rightFacility = GetFacilityForNode(edge.ToNodeId, assignment);
            if (string.IsNullOrWhiteSpace(leftFacility) ||
                string.IsNullOrWhiteSpace(rightFacility) ||
                Comparer.Equals(leftFacility, rightFacility))
            {
                continue;
            }

            if (!result.TryGetValue(leftFacility, out var leftNeighbours))
            {
                leftNeighbours = [];
                result[leftFacility] = leftNeighbours;
            }

            if (!result.TryGetValue(rightFacility, out var rightNeighbours))
            {
                rightNeighbours = [];
                result[rightFacility] = rightNeighbours;
            }

            leftNeighbours.Add(rightFacility);
            rightNeighbours.Add(leftFacility);
        }

        return result;
    }

    private static string? GetFacilityForNode(string nodeId, FacilityAssignmentResult assignment)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        return assignment.FacilityByDemandNodeId.TryGetValue(nodeId, out var assignedFacility)
            ? assignedFacility
            : null;
    }

    private static string GetNodeName(NetworkModel network, string nodeId) =>
        network.Nodes.FirstOrDefault(node => Comparer.Equals(node.Id, nodeId))?.Name ?? nodeId;
}

public sealed record FacilityCoverageStyle(
    string FillColorHex,
    string StrokeColorHex,
    string PatternName)
{
    public string FacilityNodeId { get; init; } = string.Empty;
    public string LegendLabel { get; init; } = string.Empty;
}

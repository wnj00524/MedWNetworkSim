using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Import;

public sealed class OsmToSimulationMapper
{
    private const string ImportedTrafficTypeName = "traffic";

    public NetworkModel Map(
        GraphSimplifier.SimplifiedGraph graph,
        string sourceFileName,
        OsmImportSummary? importSummary = null,
        Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        random ??= Random.Shared;

        var orderedNodes = graph.Nodes.Values.OrderBy(node => node.Id).ToList();
        if (orderedNodes.Count == 0)
        {
            throw new InvalidOperationException("The imported OSM graph does not contain any nodes after simplification.");
        }

        var nodeModels = orderedNodes
            .Select(node => new NodeModel
            {
                Id = BuildNodeId(node.Id),
                Name = $"OSM {node.Id}",
                X = node.Longitude,
                Y = -node.Latitude,
                TrafficProfiles = []
            })
            .ToList();

        var edges = graph.Edges
            .Select((edge, index) => new EdgeModel
            {
                Id = $"osm-edge-{index + 1}",
                FromNodeId = BuildNodeId(edge.FromNodeId),
                ToNodeId = BuildNodeId(edge.ToNodeId),
                IsBidirectional = true,
                RouteType = edge.HighwayType,
                Capacity = GetCapacity(edge.HighwayType),
                Time = Math.Max(edge.LengthKilometers, 0.1d),
                Cost = Math.Max(edge.LengthKilometers, 0.1d)
            })
            .ToList();

        ApplyBasicDemandGeneration(nodeModels, random);

        return new NetworkModel
        {
            Name = $"OSM Import - {sourceFileName}",
            Description = BuildDescription(importSummary),
            TrafficTypes =
            [
                new TrafficTypeDefinition
                {
                    Name = ImportedTrafficTypeName,
                    Description = "Automatically created traffic type for OSM imports.",
                    RoutingPreference = RoutingPreference.TotalCost,
                    AllocationMode = AllocationMode.GreedyBestRoute
                }
            ],
            Nodes = nodeModels,
            Edges = edges
        };
    }

    private static string BuildDescription(OsmImportSummary? summary)
    {
        if (summary is null)
        {
            return "Imported from OpenStreetMap and simplified for simulation.";
        }

        return $"Imported from OpenStreetMap and simplified for simulation. " +
               $"Raw nodes: {summary.Parse.RawNodeCount:N0}; raw ways: {summary.Parse.RawWayCount:N0}; retained roads: {summary.Parse.RetainedWayCount:N0}; " +
               $"simplified nodes: {summary.SimplifiedNodeCount:N0}; simplified edges: {summary.SimplifiedEdgeCount:N0}; skipped entities: {summary.Parse.SkippedEntityCount:N0}.";
    }

    private static string BuildNodeId(long osmId) => $"osm-node-{osmId}";

    private static double GetCapacity(string highwayType)
    {
        return highwayType.Trim().ToLowerInvariant() switch
        {
            "motorway" => 100d,
            "primary" => 60d,
            "secondary" => 40d,
            "tertiary" => 25d,
            "residential" => 10d,
            _ => 10d
        };
    }

    private static void ApplyBasicDemandGeneration(IReadOnlyList<NodeModel> nodes, Random random)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        var shuffled = nodes.OrderBy(_ => random.Next()).ToList();
        var producerRatio = random.NextDouble() * 0.10d + 0.10d;
        var consumerRatio = random.NextDouble() * 0.10d + 0.10d;
        var producerCount = Math.Max(1, (int)Math.Round(shuffled.Count * producerRatio, MidpointRounding.AwayFromZero));
        var consumerCount = Math.Max(1, (int)Math.Round(shuffled.Count * consumerRatio, MidpointRounding.AwayFromZero));

        var producerSet = shuffled.Take(Math.Min(producerCount, shuffled.Count)).ToHashSet();
        var consumerSet = shuffled.Skip(Math.Max(0, shuffled.Count - consumerCount)).ToHashSet();

        foreach (var node in shuffled)
        {
            var profile = new NodeTrafficProfile
            {
                TrafficType = ImportedTrafficTypeName,
                CanTransship = true
            };

            if (producerSet.Contains(node))
            {
                profile.Production = random.Next(5, 16);
            }

            if (consumerSet.Contains(node))
            {
                profile.Consumption = random.Next(5, 16);
            }

            if (profile.Production > 0 || profile.Consumption > 0)
            {
                node.TrafficProfiles.Add(profile);
            }
        }
    }
}

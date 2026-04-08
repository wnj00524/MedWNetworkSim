using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

/// <summary>
/// Loads, saves, normalizes, validates, and optionally auto-lays out network files.
/// </summary>
public sealed class NetworkFileService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private readonly JsonSerializerOptions serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Loads a network file from disk and returns a normalized, validated model.
    /// </summary>
    /// <param name="path">The JSON file to load.</param>
    /// <returns>The normalized network model.</returns>
    public NetworkModel Load(string path)
    {
        return LoadJson(File.ReadAllText(path));
    }

    /// <summary>
    /// Loads a network model from JSON text and returns a normalized, validated model.
    /// </summary>
    /// <param name="json">The raw network JSON payload.</param>
    /// <returns>The normalized network model.</returns>
    public NetworkModel LoadJson(string json)
    {
        var model = JsonSerializer.Deserialize<NetworkModel>(json, serializerOptions)
            ?? throw new InvalidOperationException("The selected JSON could not be deserialized into a network.");

        return NormalizeAndValidate(model);
    }

    /// <summary>
    /// Saves a network model to disk after normalizing and validating it.
    /// </summary>
    /// <param name="model">The network model to persist.</param>
    /// <param name="path">The destination JSON file path.</param>
    public void Save(NetworkModel model, string path)
    {
        var normalized = NormalizeAndValidate(model);
        var json = JsonSerializer.Serialize(normalized, serializerOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Recomputes coordinates for every node in the supplied model.
    /// </summary>
    /// <param name="model">The network model to arrange.</param>
    /// <returns>A normalized model with fresh node coordinates.</returns>
    public NetworkModel AutoArrange(NetworkModel model)
    {
        return NormalizeAndValidate(model, forceLayoutAllNodes: true);
    }

    /// <summary>
    /// Normalizes and validates a network model without forcing a full re-layout of every node.
    /// </summary>
    /// <param name="model">The network model to check.</param>
    /// <returns>The normalized network model.</returns>
    public NetworkModel NormalizeAndValidate(NetworkModel model)
    {
        return NormalizeAndValidate(model, forceLayoutAllNodes: false);
    }

    private NetworkModel NormalizeAndValidate(NetworkModel model, bool forceLayoutAllNodes)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Rebuild the model into a predictable, validated shape before either rendering or saving it.
        var normalizedNodes = new List<NodeModel>();
        var nodeIds = new HashSet<string>(Comparer);

        foreach (var node in model.Nodes ?? [])
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                throw new InvalidOperationException("Each node must have a non-empty id.");
            }

            var nodeId = node.Id.Trim();
            if (!nodeIds.Add(nodeId))
            {
                throw new InvalidOperationException($"Duplicate node id '{nodeId}' was found.");
            }

            var transhipmentCapacity = node.TranshipmentCapacity;
            if (transhipmentCapacity.HasValue &&
                (double.IsNaN(transhipmentCapacity.Value) || double.IsInfinity(transhipmentCapacity.Value) || transhipmentCapacity.Value < 0d))
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid transhipmentCapacity value. Use a number >= 0 or omit the property for unlimited transhipment.");
            }

            normalizedNodes.Add(new NodeModel
            {
                Id = nodeId,
                Name = string.IsNullOrWhiteSpace(node.Name) ? nodeId : node.Name.Trim(),
                Shape = node.Shape,
                X = node.X,
                Y = node.Y,
                TranshipmentCapacity = transhipmentCapacity,
                TrafficProfiles = NormalizeProfiles(node.TrafficProfiles, nodeId)
            });
        }

        var normalizedEdges = new List<EdgeModel>();
        var edgeIds = new HashSet<string>(Comparer);

        foreach (var edge in model.Edges ?? [])
        {
            if (string.IsNullOrWhiteSpace(edge.FromNodeId) || string.IsNullOrWhiteSpace(edge.ToNodeId))
            {
                throw new InvalidOperationException("Each edge must have both fromNodeId and toNodeId values.");
            }

            var fromNodeId = edge.FromNodeId.Trim();
            var toNodeId = edge.ToNodeId.Trim();
            var capacity = edge.Capacity;

            if (!nodeIds.Contains(fromNodeId))
            {
                throw new InvalidOperationException($"Edge '{edge.Id}' references missing source node '{fromNodeId}'.");
            }

            if (!nodeIds.Contains(toNodeId))
            {
                throw new InvalidOperationException($"Edge '{edge.Id}' references missing target node '{toNodeId}'.");
            }

            var edgeId = string.IsNullOrWhiteSpace(edge.Id)
                ? $"{fromNodeId}-{toNodeId}-{normalizedEdges.Count + 1}"
                : edge.Id.Trim();

            if (!edgeIds.Add(edgeId))
            {
                throw new InvalidOperationException($"Duplicate edge id '{edgeId}' was found.");
            }

            if (double.IsNaN(edge.Time) || double.IsInfinity(edge.Time) || edge.Time < 0d)
            {
                throw new InvalidOperationException($"Edge '{edgeId}' has an invalid time value. Use a finite number >= 0.");
            }

            if (double.IsNaN(edge.Cost) || double.IsInfinity(edge.Cost) || edge.Cost < 0d)
            {
                throw new InvalidOperationException($"Edge '{edgeId}' has an invalid cost value. Use a finite number >= 0.");
            }

            if (capacity.HasValue && (double.IsNaN(capacity.Value) || double.IsInfinity(capacity.Value) || capacity.Value < 0d))
            {
                throw new InvalidOperationException($"Edge '{edgeId}' has an invalid capacity value. Use a number >= 0 or omit the property for unlimited capacity.");
            }

            normalizedEdges.Add(new EdgeModel
            {
                Id = edgeId,
                FromNodeId = fromNodeId,
                ToNodeId = toNodeId,
                Time = edge.Time,
                Cost = edge.Cost,
                Capacity = capacity,
                IsBidirectional = edge.IsBidirectional
            });
        }

        var trafficDefinitions = NormalizeTrafficDefinitions(model.TrafficTypes, normalizedNodes);
        ApplyAutomaticLayout(normalizedNodes, normalizedEdges, forceLayoutAllNodes);

        return new NetworkModel
        {
            Name = string.IsNullOrWhiteSpace(model.Name) ? "Untitled Network" : model.Name.Trim(),
            Description = model.Description?.Trim() ?? string.Empty,
            Nodes = normalizedNodes,
            Edges = normalizedEdges,
            TrafficTypes = trafficDefinitions
        };
    }

    private static List<NodeTrafficProfile> NormalizeProfiles(IEnumerable<NodeTrafficProfile>? profiles, string nodeId)
    {
        var normalizedProfiles = new Dictionary<string, NodeTrafficProfile>(Comparer);

        foreach (var profile in profiles ?? [])
        {
            if (string.IsNullOrWhiteSpace(profile.TrafficType))
            {
                throw new InvalidOperationException($"Node '{nodeId}' contains a traffic profile with a blank trafficType.");
            }

            if (double.IsNaN(profile.Production) || double.IsInfinity(profile.Production) || profile.Production < 0d)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid production value for traffic '{profile.TrafficType}'. Use a finite number >= 0.");
            }

            if (double.IsNaN(profile.Consumption) || double.IsInfinity(profile.Consumption) || profile.Consumption < 0d)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid consumption value for traffic '{profile.TrafficType}'. Use a finite number >= 0.");
            }

            if (profile.ProductionStartPeriod.HasValue && profile.ProductionStartPeriod.Value < 0)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid productionStartPeriod for traffic '{profile.TrafficType}'. Use an integer >= 0.");
            }

            if (profile.ProductionEndPeriod.HasValue && profile.ProductionEndPeriod.Value < 0)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid productionEndPeriod for traffic '{profile.TrafficType}'. Use an integer >= 0.");
            }

            if (profile.ConsumptionStartPeriod.HasValue && profile.ConsumptionStartPeriod.Value < 0)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid consumptionStartPeriod for traffic '{profile.TrafficType}'. Use an integer >= 0.");
            }

            if (profile.ConsumptionEndPeriod.HasValue && profile.ConsumptionEndPeriod.Value < 0)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid consumptionEndPeriod for traffic '{profile.TrafficType}'. Use an integer >= 0.");
            }

            if (profile.ProductionStartPeriod.HasValue && profile.ProductionEndPeriod.HasValue &&
                profile.ProductionStartPeriod.Value > profile.ProductionEndPeriod.Value)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has a production schedule where start is after end for traffic '{profile.TrafficType}'.");
            }

            if (profile.ConsumptionStartPeriod.HasValue && profile.ConsumptionEndPeriod.HasValue &&
                profile.ConsumptionStartPeriod.Value > profile.ConsumptionEndPeriod.Value)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has a consumption schedule where start is after end for traffic '{profile.TrafficType}'.");
            }

            if (profile.StoreCapacity.HasValue &&
                (double.IsNaN(profile.StoreCapacity.Value) || double.IsInfinity(profile.StoreCapacity.Value) || profile.StoreCapacity.Value < 0d))
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid storeCapacity for traffic '{profile.TrafficType}'. Use a number >= 0 or omit it.");
            }

            var trafficType = profile.TrafficType.Trim();
            if (!normalizedProfiles.TryGetValue(trafficType, out var normalizedProfile))
            {
                normalizedProfile = new NodeTrafficProfile
                {
                    TrafficType = trafficType
                };
                normalizedProfiles[trafficType] = normalizedProfile;
            }

            normalizedProfile.Production += profile.Production;
            normalizedProfile.Consumption += profile.Consumption;
            normalizedProfile.CanTransship |= profile.CanTransship;
            normalizedProfile.ProductionStartPeriod ??= profile.ProductionStartPeriod;
            normalizedProfile.ProductionEndPeriod ??= profile.ProductionEndPeriod;
            normalizedProfile.ConsumptionStartPeriod ??= profile.ConsumptionStartPeriod;
            normalizedProfile.ConsumptionEndPeriod ??= profile.ConsumptionEndPeriod;
            normalizedProfile.IsStore |= profile.IsStore;
            normalizedProfile.StoreCapacity ??= profile.StoreCapacity;
        }

        // Duplicate traffic rows on the same node are collapsed into one persisted profile per traffic type.
        return normalizedProfiles.Values
            .OrderBy(profile => profile.TrafficType, Comparer)
            .ToList();
    }

    private static List<TrafficTypeDefinition> NormalizeTrafficDefinitions(
        IEnumerable<TrafficTypeDefinition>? definitions,
        IEnumerable<NodeModel> nodes)
    {
        var result = new Dictionary<string, TrafficTypeDefinition>(Comparer);

        foreach (var definition in definitions ?? [])
        {
            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                continue;
            }

            var name = definition.Name.Trim();
            var capacityBidPerUnit = definition.CapacityBidPerUnit;
            if (capacityBidPerUnit.HasValue &&
                (double.IsNaN(capacityBidPerUnit.Value) || double.IsInfinity(capacityBidPerUnit.Value) || capacityBidPerUnit.Value < 0d))
            {
                throw new InvalidOperationException($"Traffic type '{name}' has an invalid capacityBidPerUnit. Use a number >= 0 or omit it.");
            }

            result[name] = new TrafficTypeDefinition
            {
                Name = name,
                Description = definition.Description?.Trim() ?? string.Empty,
                RoutingPreference = definition.RoutingPreference,
                CapacityBidPerUnit = capacityBidPerUnit
            };
        }

        // Traffic types referenced by nodes are back-filled even if the file omits an explicit definition.
        foreach (var trafficName in nodes
                     .SelectMany(node => node.TrafficProfiles)
                     .Select(profile => profile.TrafficType)
                     .Distinct(Comparer))
        {
            if (!result.ContainsKey(trafficName))
            {
                result[trafficName] = new TrafficTypeDefinition
                {
                    Name = trafficName,
                    RoutingPreference = RoutingPreference.TotalCost
                };
            }
        }

        return result.Values
            .OrderBy(definition => definition.Name, Comparer)
            .ToList();
    }

    private static void ApplyAutomaticLayout(
        IList<NodeModel> nodes,
        IReadOnlyList<EdgeModel> edges,
        bool forceLayoutAllNodes)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        if (forceLayoutAllNodes)
        {
            // Auto Arrange deliberately relays out every node, even if it already has saved coordinates.
            ApplyRoleBasedLayout(nodes, edges);
            return;
        }

        var nodesMissingCoordinates = nodes
            .Where(node => !node.X.HasValue || !node.Y.HasValue)
            .ToList();

        if (nodesMissingCoordinates.Count == 0)
        {
            return;
        }

        if (nodesMissingCoordinates.Count == nodes.Count)
        {
            ApplyRoleBasedLayout(nodes, edges);
            return;
        }

        ApplySupplementalLayout(nodes, edges, nodesMissingCoordinates);
    }

    private static void ApplyRoleBasedLayout(IList<NodeModel> nodes, IReadOnlyList<EdgeModel> edges)
    {
        // Producers trend left, consumers trend right, and transhipment-capable nodes sit in the middle layer.
        var degreeByNodeId = BuildDegreeLookup(edges);
        var layers = nodes
            .GroupBy(GetLayoutLayer)
            .OrderBy(group => group.Key);

        const double leftMargin = 180d;
        const double topMargin = 160d;
        const double layerSpacing = 360d;
        const double subColumnSpacing = 220d;
        const double rowSpacing = 180d;
        const int rowsPerSubColumn = 5;

        foreach (var layer in layers)
        {
            var orderedNodes = layer
                .OrderByDescending(node => degreeByNodeId.GetValueOrDefault(node.Id, 0))
                .ThenBy(node => node.Name, Comparer)
                .ToList();

            for (var index = 0; index < orderedNodes.Count; index++)
            {
                var subColumn = index / rowsPerSubColumn;
                var row = index % rowsPerSubColumn;

                orderedNodes[index].X = leftMargin + (layer.Key * layerSpacing) + (subColumn * subColumnSpacing);
                orderedNodes[index].Y = topMargin + (row * rowSpacing);
            }
        }
    }

    private static void ApplySupplementalLayout(
        IList<NodeModel> nodes,
        IReadOnlyList<EdgeModel> edges,
        IReadOnlyList<NodeModel> nodesMissingCoordinates)
    {
        // When only some nodes are missing positions, preserve the explicit coordinates and append the rest nearby.
        var explicitNodes = nodes
            .Where(node => node.X.HasValue && node.Y.HasValue)
            .ToList();

        if (explicitNodes.Count == 0)
        {
            ApplyRoleBasedLayout(nodes, edges);
            return;
        }

        var degreeByNodeId = BuildDegreeLookup(edges);
        var orderedMissingNodes = nodesMissingCoordinates
            .OrderBy(GetLayoutLayer)
            .ThenByDescending(node => degreeByNodeId.GetValueOrDefault(node.Id, 0))
            .ThenBy(node => node.Name, Comparer)
            .ToList();

        var startX = explicitNodes.Max(node => node.X ?? 0d) + 260d;
        var startY = Math.Max(140d, explicitNodes.Min(node => node.Y ?? 0d));
        const double columnSpacing = 220d;
        const double rowSpacing = 180d;
        const int rowsPerColumn = 5;

        for (var index = 0; index < orderedMissingNodes.Count; index++)
        {
            var column = index / rowsPerColumn;
            var row = index % rowsPerColumn;
            var generatedX = startX + (column * columnSpacing);
            var generatedY = startY + (row * rowSpacing);

            orderedMissingNodes[index].X ??= generatedX;
            orderedMissingNodes[index].Y ??= generatedY;
        }
    }

    private static Dictionary<string, int> BuildDegreeLookup(IEnumerable<EdgeModel> edges)
    {
        var degreeByNodeId = new Dictionary<string, int>(Comparer);

        foreach (var edge in edges)
        {
            degreeByNodeId[edge.FromNodeId] = degreeByNodeId.GetValueOrDefault(edge.FromNodeId) + 1;
            degreeByNodeId[edge.ToNodeId] = degreeByNodeId.GetValueOrDefault(edge.ToNodeId) + 1;
        }

        return degreeByNodeId;
    }

    private static int GetLayoutLayer(NodeModel node)
    {
        var hasTransshipment = node.TrafficProfiles.Any(profile => profile.CanTransship);
        if (hasTransshipment)
        {
            return 1;
        }

        var totalProduction = node.TrafficProfiles.Sum(profile => profile.Production);
        var totalConsumption = node.TrafficProfiles.Sum(profile => profile.Consumption);

        if (totalProduction > totalConsumption)
        {
            return 0;
        }

        if (totalConsumption > totalProduction)
        {
            return 2;
        }

        return 1;
    }
}

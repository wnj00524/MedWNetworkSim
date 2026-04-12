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
        var trafficTypesWithExplicitFlowSplitPolicy = ReadTrafficTypesWithExplicitFlowSplitPolicy(json);
        var model = JsonSerializer.Deserialize<NetworkModel>(json, serializerOptions)
            ?? throw new InvalidOperationException("The selected JSON could not be deserialized into a network.");

        return NormalizeAndValidate(model, forceLayoutAllNodes: false, trafficTypesWithExplicitFlowSplitPolicy);
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

    private NetworkModel NormalizeAndValidate(
        NetworkModel model,
        bool forceLayoutAllNodes,
        ISet<string>? trafficTypesWithExplicitFlowSplitPolicy = null)
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
                PlaceType = NormalizeOptionalText(node.PlaceType),
                LoreDescription = NormalizeOptionalText(node.LoreDescription),
                ControllingActor = NormalizeOptionalText(node.ControllingActor),
                Tags = NormalizeTags(node.Tags),
                TemplateId = NormalizeOptionalText(node.TemplateId),
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
                IsBidirectional = edge.IsBidirectional,
                RouteType = NormalizeOptionalText(edge.RouteType),
                AccessNotes = NormalizeOptionalText(edge.AccessNotes),
                SeasonalRisk = NormalizeOptionalText(edge.SeasonalRisk),
                TollNotes = NormalizeOptionalText(edge.TollNotes),
                SecurityNotes = NormalizeOptionalText(edge.SecurityNotes)
            });
        }

        var timelineLoopLength = model.TimelineLoopLength;
        if (timelineLoopLength.HasValue && timelineLoopLength.Value < 1)
        {
            timelineLoopLength = null;
        }

        var defaultAllocationMode = model.DefaultAllocationMode;
        var trafficDefinitions = NormalizeTrafficDefinitions(
            model.TrafficTypes,
            normalizedNodes,
            defaultAllocationMode,
            trafficTypesWithExplicitFlowSplitPolicy);
        var timelineEvents = NormalizeTimelineEvents(model.TimelineEvents, normalizedNodes, edgeIds);
        ApplyAutomaticLayout(normalizedNodes, normalizedEdges, forceLayoutAllNodes);

        return new NetworkModel
        {
            Name = string.IsNullOrWhiteSpace(model.Name) ? "Untitled Network" : model.Name.Trim(),
            Description = model.Description?.Trim() ?? string.Empty,
            TimelineLoopLength = timelineLoopLength,
            DefaultAllocationMode = defaultAllocationMode,
            SimulationSeed = model.SimulationSeed,
            Nodes = normalizedNodes,
            Edges = normalizedEdges,
            TrafficTypes = trafficDefinitions,
            TimelineEvents = timelineEvents
        };
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static List<string> NormalizeTags(IEnumerable<string>? tags)
    {
        return (tags ?? [])
            .Select(NormalizeOptionalText)
            .Where(tag => tag is not null)
            .Cast<string>()
            .Distinct(Comparer)
            .OrderBy(tag => tag, Comparer)
            .ToList();
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

            if (double.IsNaN(profile.ConsumerPremiumPerUnit) || double.IsInfinity(profile.ConsumerPremiumPerUnit) || profile.ConsumerPremiumPerUnit < 0d)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid consumerPremiumPerUnit for traffic '{profile.TrafficType}'. Use a finite number >= 0.");
            }

            var productionWindows = NormalizeWindows(
                profile.ProductionWindows,
                profile.ProductionStartPeriod,
                profile.ProductionEndPeriod,
                nodeId,
                profile.TrafficType,
                "production");
            var consumptionWindows = NormalizeWindows(
                profile.ConsumptionWindows,
                profile.ConsumptionStartPeriod,
                profile.ConsumptionEndPeriod,
                nodeId,
                profile.TrafficType,
                "consumption");
            var inputRequirements = NormalizeInputRequirements(profile.InputRequirements, nodeId, profile.TrafficType);

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
            normalizedProfile.ConsumerPremiumPerUnit = Math.Max(normalizedProfile.ConsumerPremiumPerUnit, profile.ConsumerPremiumPerUnit);
            normalizedProfile.CanTransship |= profile.CanTransship;
            MergeWindows(normalizedProfile.ProductionWindows, productionWindows);
            MergeWindows(normalizedProfile.ConsumptionWindows, consumptionWindows);
            MergeInputRequirements(normalizedProfile.InputRequirements, inputRequirements);
            normalizedProfile.IsStore |= profile.IsStore;
            normalizedProfile.StoreCapacity ??= profile.StoreCapacity;
        }

        // Duplicate traffic rows on the same node are collapsed into one persisted profile per traffic type.
        foreach (var profile in normalizedProfiles.Values)
        {
            MirrorLegacyScheduleFields(profile);
        }

        return normalizedProfiles.Values
            .OrderBy(profile => profile.TrafficType, Comparer)
            .ToList();
    }

    private static List<PeriodWindow> NormalizeWindows(
        IEnumerable<PeriodWindow>? windows,
        int? legacyStartPeriod,
        int? legacyEndPeriod,
        string nodeId,
        string trafficType,
        string scheduleKind)
    {
        var normalized = new List<PeriodWindow>();

        foreach (var window in windows ?? [])
        {
            if (window.StartPeriod.HasValue && window.StartPeriod.Value < 0)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid {scheduleKind} window start for traffic '{trafficType}'. Use an integer >= 0.");
            }

            if (window.EndPeriod.HasValue && window.EndPeriod.Value < 0)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid {scheduleKind} window end for traffic '{trafficType}'. Use an integer >= 0.");
            }

            if (window.StartPeriod.HasValue && window.EndPeriod.HasValue && window.StartPeriod.Value > window.EndPeriod.Value)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has a {scheduleKind} schedule where start is after end for traffic '{trafficType}'.");
            }

            normalized.Add(new PeriodWindow
            {
                StartPeriod = window.StartPeriod,
                EndPeriod = window.EndPeriod
            });
        }

        // Old files only have the single start/end pair; promote it into the new list model.
        if (normalized.Count == 0 && (legacyStartPeriod.HasValue || legacyEndPeriod.HasValue))
        {
            if (legacyStartPeriod.HasValue && legacyStartPeriod.Value < 0)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid {scheduleKind}StartPeriod for traffic '{trafficType}'. Use an integer >= 0.");
            }

            if (legacyEndPeriod.HasValue && legacyEndPeriod.Value < 0)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid {scheduleKind}EndPeriod for traffic '{trafficType}'. Use an integer >= 0.");
            }

            if (legacyStartPeriod.HasValue && legacyEndPeriod.HasValue && legacyStartPeriod.Value > legacyEndPeriod.Value)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has a {scheduleKind} schedule where start is after end for traffic '{trafficType}'.");
            }

            normalized.Add(new PeriodWindow
            {
                StartPeriod = legacyStartPeriod,
                EndPeriod = legacyEndPeriod
            });
        }

        return normalized;
    }

    private static List<ProductionInputRequirement> NormalizeInputRequirements(
        IEnumerable<ProductionInputRequirement>? requirements,
        string nodeId,
        string trafficType)
    {
        var merged = new Dictionary<string, double>(Comparer);

        foreach (var requirement in requirements ?? [])
        {
            if (string.IsNullOrWhiteSpace(requirement.TrafficType))
            {
                throw new InvalidOperationException($"Node '{nodeId}' has a blank production input requirement for traffic '{trafficType}'.");
            }

            if (double.IsNaN(requirement.QuantityPerOutputUnit) ||
                double.IsInfinity(requirement.QuantityPerOutputUnit) ||
                requirement.QuantityPerOutputUnit <= 0d)
            {
                throw new InvalidOperationException($"Node '{nodeId}' has an invalid input ratio for traffic '{trafficType}'. Use a finite number > 0.");
            }

            var inputTrafficType = requirement.TrafficType.Trim();
            merged[inputTrafficType] = merged.GetValueOrDefault(inputTrafficType) + requirement.QuantityPerOutputUnit;
        }

        return merged
            .OrderBy(pair => pair.Key, Comparer)
            .Select(pair => new ProductionInputRequirement
            {
                TrafficType = pair.Key,
                QuantityPerOutputUnit = pair.Value
            })
            .ToList();
    }

    private static void MergeWindows(ICollection<PeriodWindow> target, IEnumerable<PeriodWindow> source)
    {
        foreach (var window in source)
        {
            if (target.Any(existing => existing.StartPeriod == window.StartPeriod && existing.EndPeriod == window.EndPeriod))
            {
                continue;
            }

            target.Add(new PeriodWindow
            {
                StartPeriod = window.StartPeriod,
                EndPeriod = window.EndPeriod
            });
        }
    }

    private static void MergeInputRequirements(ICollection<ProductionInputRequirement> target, IEnumerable<ProductionInputRequirement> source)
    {
        foreach (var requirement in source)
        {
            var existing = target.FirstOrDefault(item => Comparer.Equals(item.TrafficType, requirement.TrafficType));
            if (existing is null)
            {
                target.Add(new ProductionInputRequirement
                {
                    TrafficType = requirement.TrafficType,
                    QuantityPerOutputUnit = requirement.QuantityPerOutputUnit
                });
                continue;
            }

            existing.QuantityPerOutputUnit += requirement.QuantityPerOutputUnit;
        }
    }

    private static void MirrorLegacyScheduleFields(NodeTrafficProfile profile)
    {
        var firstProductionWindow = profile.ProductionWindows.FirstOrDefault();
        profile.ProductionStartPeriod = firstProductionWindow?.StartPeriod;
        profile.ProductionEndPeriod = firstProductionWindow?.EndPeriod;

        var firstConsumptionWindow = profile.ConsumptionWindows.FirstOrDefault();
        profile.ConsumptionStartPeriod = firstConsumptionWindow?.StartPeriod;
        profile.ConsumptionEndPeriod = firstConsumptionWindow?.EndPeriod;
    }

    private static List<TrafficTypeDefinition> NormalizeTrafficDefinitions(
        IEnumerable<TrafficTypeDefinition>? definitions,
        IEnumerable<NodeModel> nodes,
        AllocationMode defaultAllocationMode,
        ISet<string>? trafficTypesWithExplicitFlowSplitPolicy)
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
                AllocationMode = definition.AllocationMode,
                RouteChoiceModel = definition.RouteChoiceModel,
                FlowSplitPolicy = NormalizeFlowSplitPolicy(
                    definition.FlowSplitPolicy,
                    definition.AllocationMode,
                    trafficTypesWithExplicitFlowSplitPolicy is null || trafficTypesWithExplicitFlowSplitPolicy.Contains(name)),
                RouteChoiceSettings = NormalizeRouteChoiceSettings(definition.RouteChoiceSettings, name),
                CapacityBidPerUnit = capacityBidPerUnit
            };
        }

        // Traffic types referenced by nodes are back-filled even if the file omits an explicit definition.
        foreach (var trafficName in nodes
                     .SelectMany(node => node.TrafficProfiles)
                     .SelectMany(profile => profile.InputRequirements
                         .Select(requirement => requirement.TrafficType)
                         .Append(profile.TrafficType))
                     .Distinct(Comparer))
        {
            if (!result.ContainsKey(trafficName))
            {
                result[trafficName] = new TrafficTypeDefinition
                {
                    Name = trafficName,
                    RoutingPreference = RoutingPreference.TotalCost,
                    AllocationMode = defaultAllocationMode,
                    FlowSplitPolicy = MapLegacyAllocationMode(defaultAllocationMode)
                };
            }
        }

        return result.Values
            .OrderBy(definition => definition.Name, Comparer)
            .ToList();
    }

    private static List<TimelineEventModel> NormalizeTimelineEvents(
        IEnumerable<TimelineEventModel>? events,
        IReadOnlyList<NodeModel> nodes,
        IReadOnlySet<string> edgeIds)
    {
        var normalized = new List<TimelineEventModel>();
        var seenIds = new HashSet<string>(Comparer);
        var nodeProfilesById = nodes.ToDictionary(
            node => node.Id,
            node => node.TrafficProfiles.Select(profile => profile.TrafficType).ToHashSet(Comparer),
            Comparer);

        foreach (var timelineEvent in events ?? [])
        {
            var eventId = string.IsNullOrWhiteSpace(timelineEvent.Id)
                ? $"event-{normalized.Count + 1}"
                : timelineEvent.Id.Trim();

            if (!seenIds.Add(eventId))
            {
                throw new InvalidOperationException($"Duplicate timeline event id '{eventId}' was found.");
            }

            ValidateEventWindow(timelineEvent, eventId);

            var normalizedEffects = NormalizeTimelineEventEffects(timelineEvent.Effects, eventId, nodeProfilesById, edgeIds);
            normalized.Add(new TimelineEventModel
            {
                Id = eventId,
                Name = string.IsNullOrWhiteSpace(timelineEvent.Name) ? eventId : timelineEvent.Name.Trim(),
                StartPeriod = timelineEvent.StartPeriod,
                EndPeriod = timelineEvent.EndPeriod,
                Effects = normalizedEffects
            });
        }

        return normalized;
    }

    private static void ValidateEventWindow(TimelineEventModel timelineEvent, string eventId)
    {
        if (timelineEvent.StartPeriod.HasValue && timelineEvent.StartPeriod.Value < 0)
        {
            throw new InvalidOperationException($"Timeline event '{eventId}' has an invalid startPeriod. Use an integer >= 0.");
        }

        if (timelineEvent.EndPeriod.HasValue && timelineEvent.EndPeriod.Value < 0)
        {
            throw new InvalidOperationException($"Timeline event '{eventId}' has an invalid endPeriod. Use an integer >= 0.");
        }

        if (timelineEvent.StartPeriod.HasValue &&
            timelineEvent.EndPeriod.HasValue &&
            timelineEvent.StartPeriod.Value > timelineEvent.EndPeriod.Value)
        {
            throw new InvalidOperationException($"Timeline event '{eventId}' has a startPeriod after its endPeriod.");
        }
    }

    private static List<TimelineEventEffectModel> NormalizeTimelineEventEffects(
        IEnumerable<TimelineEventEffectModel>? effects,
        string eventId,
        IReadOnlyDictionary<string, HashSet<string>> nodeProfilesById,
        IReadOnlySet<string> edgeIds)
    {
        var normalized = new List<TimelineEventEffectModel>();

        foreach (var effect in effects ?? [])
        {
            if (double.IsNaN(effect.Multiplier) || double.IsInfinity(effect.Multiplier) || effect.Multiplier < 0d)
            {
                throw new InvalidOperationException($"Timeline event '{eventId}' has an invalid multiplier. Use a finite number >= 0.");
            }

            var nodeId = NormalizeOptionalText(effect.NodeId);
            var edgeId = NormalizeOptionalText(effect.EdgeId);
            var trafficType = NormalizeOptionalText(effect.TrafficType);

            switch (effect.EffectType)
            {
                case TimelineEventEffectType.ProductionMultiplier:
                case TimelineEventEffectType.ConsumptionMultiplier:
                    if (nodeId is null || trafficType is null)
                    {
                        throw new InvalidOperationException($"Timeline event '{eventId}' must specify nodeId and trafficType for {effect.EffectType}.");
                    }

                    if (!nodeProfilesById.TryGetValue(nodeId, out var trafficTypes))
                    {
                        throw new InvalidOperationException($"Timeline event '{eventId}' references missing node '{nodeId}'.");
                    }

                    if (!trafficTypes.Contains(trafficType))
                    {
                        throw new InvalidOperationException($"Timeline event '{eventId}' references missing traffic profile '{trafficType}' on node '{nodeId}'.");
                    }

                    break;

                case TimelineEventEffectType.RouteCostMultiplier:
                    if (edgeId is null)
                    {
                        throw new InvalidOperationException($"Timeline event '{eventId}' must specify edgeId for {effect.EffectType}.");
                    }

                    if (!edgeIds.Contains(edgeId))
                    {
                        throw new InvalidOperationException($"Timeline event '{eventId}' references missing edge '{edgeId}'.");
                    }

                    break;

                default:
                    throw new InvalidOperationException($"Timeline event '{eventId}' has an unsupported effect type '{effect.EffectType}'.");
            }

            normalized.Add(new TimelineEventEffectModel
            {
                EffectType = effect.EffectType,
                NodeId = nodeId,
                EdgeId = edgeId,
                TrafficType = trafficType,
                Multiplier = effect.Multiplier
            });
        }

        return normalized;
    }

    private static ISet<string> ReadTrafficTypesWithExplicitFlowSplitPolicy(string json)
    {
        var result = new HashSet<string>(Comparer);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("trafficTypes", out var trafficTypes) ||
            trafficTypes.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in trafficTypes.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var nameElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(nameElement.GetString()) ||
                !item.TryGetProperty("flowSplitPolicy", out _))
            {
                continue;
            }

            result.Add(nameElement.GetString()!.Trim());
        }

        return result;
    }

    private static FlowSplitPolicy NormalizeFlowSplitPolicy(
        FlowSplitPolicy flowSplitPolicy,
        AllocationMode allocationMode,
        bool hasExplicitFlowSplitPolicy)
    {
        if (!hasExplicitFlowSplitPolicy)
        {
            return MapLegacyAllocationMode(allocationMode);
        }

        return flowSplitPolicy;
    }

    private static FlowSplitPolicy MapLegacyAllocationMode(AllocationMode allocationMode)
    {
        return allocationMode == AllocationMode.ProportionalBranchDemand
            ? FlowSplitPolicy.MultiPath
            : FlowSplitPolicy.SinglePath;
    }

    private static RouteChoiceSettings NormalizeRouteChoiceSettings(RouteChoiceSettings? settings, string trafficType)
    {
        settings ??= new RouteChoiceSettings();

        if (settings.MaxCandidateRoutes < 1)
        {
            throw new InvalidOperationException($"Traffic type '{trafficType}' has an invalid maxCandidateRoutes. Use an integer >= 1.");
        }

        if (settings.IterationCount < 1)
        {
            throw new InvalidOperationException($"Traffic type '{trafficType}' has an invalid iterationCount. Use an integer >= 1.");
        }

        ValidateFiniteNonNegative(settings.Priority, trafficType, "priority");
        ValidateFiniteNonNegative(settings.InformationAccuracy, trafficType, "informationAccuracy");
        ValidateFiniteNonNegative(settings.RouteDiversity, trafficType, "routeDiversity");
        ValidateFiniteNonNegative(settings.CongestionSensitivity, trafficType, "congestionSensitivity");
        ValidateFiniteNonNegative(settings.RerouteThreshold, trafficType, "rerouteThreshold");
        ValidateFiniteNonNegative(settings.Stickiness, trafficType, "stickiness");

        return new RouteChoiceSettings
        {
            MaxCandidateRoutes = settings.MaxCandidateRoutes,
            Priority = settings.Priority,
            InformationAccuracy = Math.Min(1d, settings.InformationAccuracy),
            RouteDiversity = settings.RouteDiversity,
            CongestionSensitivity = settings.CongestionSensitivity,
            RerouteThreshold = settings.RerouteThreshold,
            Stickiness = Math.Min(1d, settings.Stickiness),
            IterationCount = settings.IterationCount,
            InternalizeCongestion = settings.InternalizeCongestion
        };
    }

    private static void ValidateFiniteNonNegative(double value, string trafficType, string propertyName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
        {
            throw new InvalidOperationException($"Traffic type '{trafficType}' has an invalid {propertyName}. Use a finite number >= 0.");
        }
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

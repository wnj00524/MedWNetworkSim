using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services.Facility;
using System.Text.Json;

namespace MedWNetworkSim.App.Services;

/// <summary>
/// The foundational engine for resolving immediate network constraints and executing static, steady-state multi-commodity flow routing.
/// It coordinates paths across complex networks consisting of <see cref="NodeModel"/>s and <see cref="EdgeModel"/>s,
/// respecting capacity, routing costs, traffic permissions, and policy rules.
/// </summary>
public sealed class NetworkSimulationEngine
{
    private readonly INetworkLayerResolver layerResolver = new NetworkLayerResolver();
    private readonly SimulationExecutionCache executionCache = new();
    private readonly TrafficEconomicSettlementService settlementService = new();
    private const double Epsilon = 0.000001d;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Runs the routing simulation for every traffic type present in the network.
    /// </summary>
    /// <param name="network">The network to simulate.</param>
    /// <returns>The per-traffic routing outcomes.</returns>
    public IReadOnlyList<TrafficSimulationOutcome> Simulate(NetworkModel network)
    {
        ArgumentNullException.ThrowIfNull(network);
        network = HierarchicalNetworkProjection.ProjectForSimulation(network);
        network = OrderNetworkForLayerProcessing(network);
        network = Clone(network);
        network = ApplyPolicyRules(network);
        var compiledContext = executionCache.GetStaticContext(network);
        network = compiledContext.EffectiveNetwork;

        if (network.FacilityModeEnabled)
        {
            var facilityOutcomes = new FacilityModeSimulationEngine(this).Simulate(network);
            return settlementService.Settle(network, facilityOutcomes).Outcomes;
        }

        var hasRecipeDependencies = HasStaticRecipeDependencies(network);
        var definitionsByTraffic = network.TrafficTypes
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Name))
            .GroupBy(definition => definition.Name, Comparer)
            .ToDictionary(group => group.Key, group => group.First(), Comparer);
        var contexts = MixedRoutingAllocator.BuildStaticContexts(network, applyLocalAllocations: !hasRecipeDependencies).ToList();
        // Bolt: Replaced LINQ .ToDictionary() with manual foreach to avoid enumerator and delegate allocations
        var remainingCapacityByEdgeId = new Dictionary<string, double>(network.Edges.Count, Comparer);
        var hasFiniteEdges = false;
        foreach (var edge in network.Edges)
        {
            remainingCapacityByEdgeId[edge.Id] = edge.Capacity ?? double.PositiveInfinity;
            if (edge.Capacity.HasValue)
            {
                hasFiniteEdges = true;
            }
        }

        var remainingTranshipmentCapacityByNodeId = new Dictionary<string, double>(network.Nodes.Count, Comparer);
        var hasFiniteNodes = false;
        foreach (var node in network.Nodes)
        {
            remainingTranshipmentCapacityByNodeId[node.Id] = node.TranshipmentCapacity ?? double.PositiveInfinity;
            if (node.TranshipmentCapacity.HasValue)
            {
                hasFiniteNodes = true;
            }
        }

        var hasFiniteCapacities = hasFiniteEdges || hasFiniteNodes;

        if (hasRecipeDependencies)
        {
            // Bolt: Replaced multiple LINQ ToDictionary and Select allocations with a single loop and pre-sized collections
            var contextsByTraffic = new Dictionary<string, RoutingTrafficContext>(contexts.Count, Comparer);
            var sourceUnitCosts = new Dictionary<string, Dictionary<string, double>>(contexts.Count, Comparer);
            var landedUnitCosts = new Dictionary<string, Dictionary<string, double>>(contexts.Count, Comparer);
            var trafficTypes = new List<string>(contexts.Count);

            foreach (var context in contexts)
            {
                contextsByTraffic[context.TrafficType] = context;
                sourceUnitCosts[context.TrafficType] = new Dictionary<string, double>(Comparer);
                landedUnitCosts[context.TrafficType] = new Dictionary<string, double>(Comparer);
                trafficTypes.Add(context.TrafficType);
            }

            var allocationOrder = BuildStaticRecipeCostOrder(network, trafficTypes);

            foreach (var trafficType in allocationOrder)
            {
                if (!contextsByTraffic.TryGetValue(trafficType, out var context))
                {
                    continue;
                }

                SetStaticSourceUnitCosts(context, definitionsByTraffic, sourceUnitCosts, landedUnitCosts);
                MixedRoutingAllocator.ApplyLocalAllocations(context, network, period: 0);
                MixedRoutingAllocator.Allocate(network, [context], remainingCapacityByEdgeId, remainingTranshipmentCapacityByNodeId, compiledContext: compiledContext);
                landedUnitCosts[context.TrafficType] = SummarizeLandedUnitCosts(context.Allocations);
            }
        }
        else
        {
            MixedRoutingAllocator.Allocate(network, contexts, remainingCapacityByEdgeId, remainingTranshipmentCapacityByNodeId, compiledContext: compiledContext);
        }

        AnalyzeContextResults(network, contexts, hasFiniteCapacities);

        var outcomes = BuildOutcomes(contexts);

        return settlementService.Settle(network, outcomes).Outcomes;
    }

    private static void AnalyzeContextResults(NetworkModel network, IReadOnlyList<RoutingTrafficContext> contexts, bool hasFiniteCapacities)
    {
        foreach (var context in contexts)
        {
            AddReachabilityWarnings(network, context);

            // Bolt: Eliminated LINQ .Sum() allocation and delegate overhead by using a standard foreach loop
            var unusedSupply = 0d;
            foreach (var value in context.Supply.Values)
            {
                unusedSupply += Math.Max(0d, value);
            }

            var unmetDemand = 0d;
            foreach (var value in context.Demand.Values)
            {
                unmetDemand += Math.Max(0d, value);
            }

            if (unusedSupply > Epsilon)
            {
                context.Notes.Add($"Unused supply remains after routing: {unusedSupply:0.##} unit(s).");
            }

            if (unmetDemand > Epsilon)
            {
                context.Notes.Add($"Unmet demand remains after routing: {unmetDemand:0.##} unit(s).");
            }

            if (context.NoPermittedPathDemand > Epsilon)
            {
                context.Notes.Add($"No permitted path remained for {context.NoPermittedPathDemand:0.##} unit(s).");
            }

            if (context.PermissionLimitedDemand > Epsilon)
            {
                context.Notes.Add($"Permission limit reached on one or more edges for {context.PermissionLimitedDemand:0.##} unit(s).");
            }

            if (context.CapacityBlockedDemand > Epsilon)
            {
                context.Notes.Add($"General edge or transhipment capacity was exhausted for {context.CapacityBlockedDemand:0.##} unit(s).");
            }

            if (hasFiniteCapacities && (unusedSupply > Epsilon || unmetDemand > Epsilon))
            {
                context.Notes.Add("Shared edge or node transhipment capacity limits may have prevented additional routing.");
            }

            // Bolt: Eliminated LINQ .Sum() allocation and delegate overhead by using a standard foreach loop
            var totalBidCost = 0d;
            foreach (var allocation in context.Allocations)
            {
                totalBidCost += allocation.BidCostPerUnit * allocation.Quantity;
            }

            if (totalBidCost > Epsilon)
            {
                context.Notes.Add($"Capacity bidding added {totalBidCost:0.##} in extra movement cost.");
            }

            if (context.Allocations.Count == 0 && context.TotalProduction > Epsilon && context.TotalConsumption > Epsilon)
            {
                context.Notes.Add("No feasible producer-to-consumer routes were found with the current node roles, edge directions, capacities, and bidding rules.");
            }
        }
    }

    private static List<TrafficSimulationOutcome> BuildOutcomes(IReadOnlyList<RoutingTrafficContext> contexts)
    {
        return contexts
            .Select(context =>
            {
                // Bolt: Replaced multiple LINQ Sums with a single block computing them manually to reduce delegate allocations
                double totalDelivered = 0d;
                foreach (var allocation in context.Allocations)
                {
                    totalDelivered += allocation.Quantity;
                }

                double unusedSupply = 0d;
                foreach (var value in context.Supply.Values)
                {
                    unusedSupply += Math.Max(0d, value);
                }

                double unmetDemand = 0d;
                foreach (var value in context.Demand.Values)
                {
                    unmetDemand += Math.Max(0d, value);
                }

                return new TrafficSimulationOutcome
                {
                    TrafficType = context.TrafficType,
                    RoutingPreference = context.RoutingPreference,
                    AllocationMode = context.AllocationMode,
                    TotalProduction = context.TotalProduction,
                    TotalConsumption = context.TotalConsumption,
                    TotalDelivered = totalDelivered,
                    UnusedSupply = unusedSupply,
                    UnmetDemand = unmetDemand,
                    NoPermittedPathDemand = context.NoPermittedPathDemand,
                    Allocations = context.Allocations.ToList(),
                    Notes = context.Notes.ToList()
                };
            })
            .ToList();
    }

    private static void AddReachabilityWarnings(NetworkModel network, RoutingTrafficContext context)
    {
        // Bolt: Replaced LINQ methods with manual loops to avoid delegate allocations and enumerator overhead
        var producers = new List<string>(context.Supply.Count);
        foreach (var pair in context.Supply)
        {
            if (pair.Value > Epsilon)
            {
                producers.Add(pair.Key);
            }
        }

        var consumers = new List<string>(context.Demand.Count);
        foreach (var pair in context.Demand)
        {
            if (pair.Value > Epsilon)
            {
                consumers.Add(pair.Key);
            }
        }

        if (producers.Count == 0 || consumers.Count == 0)
        {
            return;
        }

        var adjacency = BuildAdjacency(network);
        var permissionResolver = new EdgeTrafficPermissionResolver();

        // Bolt: Hoist dictionary allocation to avoid O(P * C * E) memory bottleneck
        var edgesById = new Dictionary<string, EdgeModel>(network.Edges.Count, Comparer);
        foreach (var edge in network.Edges)
        {
            edgesById[edge.Id] = edge;
        }

        foreach (var producerNodeId in producers)
        {
            var hasReachableConsumer = false;
            foreach (var consumerNodeId in consumers)
            {
                if (HasPermittedPath(network, context, adjacency, permissionResolver, edgesById, producerNodeId, consumerNodeId))
                {
                    hasReachableConsumer = true;
                    break;
                }
            }

            if (!hasReachableConsumer)
            {
                context.Notes.Add(
                    $"Validation warning: producer '{GetNodeLabel(network, producerNodeId)}' has no permitted path to any {context.TrafficType} consumer.");
            }
        }

        foreach (var consumerNodeId in consumers)
        {
            var hasReachableProducer = false;
            foreach (var producerNodeId in producers)
            {
                if (HasPermittedPath(network, context, adjacency, permissionResolver, edgesById, producerNodeId, consumerNodeId))
                {
                    hasReachableProducer = true;
                    break;
                }
            }

            if (!hasReachableProducer)
            {
                context.Notes.Add(
                    $"Validation warning: consumer '{GetNodeLabel(network, consumerNodeId)}' has no permitted path from any {context.TrafficType} producer.");
            }
        }
    }

    private static bool HasPermittedPath(
        NetworkModel network,
        RoutingTrafficContext context,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        EdgeTrafficPermissionResolver permissionResolver,
        IReadOnlyDictionary<string, EdgeModel> edgesById,
        string producerNodeId,
        string consumerNodeId)
    {
        if (Comparer.Equals(producerNodeId, consumerNodeId))
        {
            return true;
        }

        var queue = new Queue<string>();
        var visited = new HashSet<string>(Comparer) { producerNodeId };
        queue.Enqueue(producerNodeId);

        while (queue.Count > 0)
        {
            var currentNodeId = queue.Dequeue();
            if (!adjacency.TryGetValue(currentNodeId, out var arcs))
            {
                continue;
            }

            foreach (var arc in arcs)
            {
                if (!visited.Add(arc.ToNodeId) ||
                    !edgesById.TryGetValue(arc.EdgeId, out var edge) ||
                    permissionResolver.Resolve(network, edge, context.TrafficType).Mode == EdgeTrafficPermissionMode.Blocked ||
                    !CanTraverseNode(arc.ToNodeId, producerNodeId, consumerNodeId, context.ProfilesByNodeId))
                {
                    continue;
                }

                if (Comparer.Equals(arc.ToNodeId, consumerNodeId))
                {
                    return true;
                }

                queue.Enqueue(arc.ToNodeId);
            }
        }

        return false;
    }

    private static string GetNodeLabel(NetworkModel network, string nodeId)
    {
        var node = network.Nodes.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, nodeId));
        return string.IsNullOrWhiteSpace(node?.Name) ? nodeId : node.Name;
    }


    private NetworkModel OrderNetworkForLayerProcessing(NetworkModel network)
    {
        var order = layerResolver.GetSimulationOrder(network)
            .Select((layer, index) => new { layer.Id, index })
            .ToDictionary(item => item.Id, item => item.index);

        return new NetworkModel
        {
            Name = network.Name,
            Description = network.Description,
            TimelineLoopLength = network.TimelineLoopLength,
            DefaultAllocationMode = network.DefaultAllocationMode,
            SimulationSeed = network.SimulationSeed,
            FacilityModeEnabled = network.FacilityModeEnabled,
            LimitMeetingNodeDemandBySellLocalPermission = network.LimitMeetingNodeDemandBySellLocalPermission,
            FacilityCoverageThreshold = network.FacilityCoverageThreshold,
            Layers = network.Layers,
            TrafficTypes = network.TrafficTypes,
            TimelineEvents = network.TimelineEvents,
            ScenarioDefinitions = network.ScenarioDefinitions,
            PolicyRules = network.PolicyRules,
            EdgeTrafficPermissionDefaults = network.EdgeTrafficPermissionDefaults,
            RouteTaxRules = network.RouteTaxRules,
            Subnetworks = network.Subnetworks,
            Nodes = network.Nodes.OrderBy(node => order.GetValueOrDefault(node.LayerId, int.MaxValue)).ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            Edges = network.Edges.OrderBy(edge => order.GetValueOrDefault(edge.LayerId, int.MaxValue)).ThenBy(edge => edge.Id, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static NetworkModel ApplyPolicyRules(NetworkModel network)
    {
        if (network.PolicyRules.Count == 0)
        {
            return network;
        }

        // Bolt: Replaced LINQ chain (.Select, .Where, .Distinct) with standard manual loop
        // to avoid delegate allocations and enumerator overhead during simulation startup.
        var allTrafficTypes = new List<string>();
        var seenTrafficTypes = new HashSet<string>(Comparer);
        foreach (var trafficType in network.TrafficTypes)
        {
            if (!string.IsNullOrWhiteSpace(trafficType.Name) && seenTrafficTypes.Add(trafficType.Name))
            {
                allTrafficTypes.Add(trafficType.Name);
            }
        }

        // Bolt: Replaced LINQ .Where filters with standard loops to avoid nested enumerator
        // allocations and delegate invocations for every edge check.
        foreach (var rule in network.PolicyRules)
        {
            if (!rule.IsEnabled)
            {
                continue;
            }

            foreach (var edge in network.Edges)
            {
                bool matchesEdge = string.IsNullOrWhiteSpace(rule.TargetEdgeId) || Comparer.Equals(edge.Id, rule.TargetEdgeId);
                bool matchesNode = string.IsNullOrWhiteSpace(rule.TargetNodeId) ||
                                   Comparer.Equals(edge.FromNodeId, rule.TargetNodeId) ||
                                   Comparer.Equals(edge.ToNodeId, rule.TargetNodeId);

                if (matchesEdge && matchesNode)
                {
                    ApplyRuleToEdge(edge, rule, allTrafficTypes);
                }
            }
        }

        return network;
    }

    private static void ApplyRuleToEdge(EdgeModel edge, PolicyRuleModel rule, IReadOnlyList<string> allTrafficTypes)
    {
        switch (rule.Effect)
        {
            case PolicyRuleEffect.BlockTraffic:
                ApplyBlockRule(edge, rule, allowOnly: false, allTrafficTypes);
                break;
            case PolicyRuleEffect.AllowOnlyTraffic:
                ApplyBlockRule(edge, rule, allowOnly: true, allTrafficTypes);
                break;
            case PolicyRuleEffect.CostMultiplier:
                edge.Cost *= Math.Max(0d, rule.Value);
                break;
            case PolicyRuleEffect.CapacityMultiplier:
                if (edge.Capacity.HasValue)
                {
                    edge.Capacity *= Math.Max(0d, rule.Value);
                }
                break;
        }
    }

    private static void ApplyBlockRule(EdgeModel edge, PolicyRuleModel rule, bool allowOnly, IReadOnlyList<string> allTrafficTypes)
    {
        if (string.IsNullOrWhiteSpace(rule.TrafficTypeIdOrName))
        {
            edge.Capacity = 0d;
            return;
        }

        if (!allowOnly)
        {
            edge.TrafficPermissions.Add(new EdgeTrafficPermissionRule
            {
                TrafficType = rule.TrafficTypeIdOrName,
                Mode = EdgeTrafficPermissionMode.Blocked
            });
            return;
        }

        foreach (var traffic in allTrafficTypes.Where(traffic => !Comparer.Equals(traffic, rule.TrafficTypeIdOrName)))
        {
            edge.TrafficPermissions.Add(new EdgeTrafficPermissionRule
            {
                TrafficType = traffic,
                Mode = EdgeTrafficPermissionMode.Blocked
            });
        }
    }

    private static NetworkModel Clone(NetworkModel network)
    {
        var json = JsonSerializer.Serialize(network);
        return JsonSerializer.Deserialize<NetworkModel>(json) ?? new NetworkModel();
    }

    private readonly record struct ConsumerCostKey(string TrafficType, string ConsumerNodeId, string ConsumerName);

    /// <summary>
    /// Aggregates route allocations into landed-cost summaries for each consumer node and traffic type.
    /// </summary>
    /// <param name="allocations">The route allocations to aggregate.</param>
    /// <returns>The consumer cost summaries.</returns>
    public IReadOnlyList<ConsumerCostSummary> SummarizeConsumerCosts(IEnumerable<RouteAllocation> allocations)
    {
        var groups = new Dictionary<ConsumerCostKey, List<RouteAllocation>>();
        var orderedKeys = new List<ConsumerCostKey>();
        foreach (var allocation in allocations)
        {
            var key = new ConsumerCostKey(allocation.TrafficType, allocation.ConsumerNodeId, allocation.ConsumerName);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<RouteAllocation>();
                groups.Add(key, list);
                orderedKeys.Add(key);
            }
            list.Add(allocation);
        }

        var summaries = new List<ConsumerCostSummary>(groups.Count);
        foreach (var key in orderedKeys)
        {
            var group = groups[key];
            double localQuantity = 0d;
            double localMovementCost = 0d;
            double importedQuantity = 0d;
            double importedMovementCost = 0d;

            foreach (var allocation in group)
            {
                if (allocation.IsLocalSupply)
                {
                    localQuantity += allocation.Quantity;
                    localMovementCost += allocation.TotalMovementCost;
                }
                else
                {
                    importedQuantity += allocation.Quantity;
                    importedMovementCost += allocation.TotalMovementCost;
                }
            }

            double totalQuantity = localQuantity + importedQuantity;
            double totalMovementCost = localMovementCost + importedMovementCost;

            summaries.Add(new ConsumerCostSummary
            {
                TrafficType = key.TrafficType,
                ConsumerNodeId = key.ConsumerNodeId,
                ConsumerName = key.ConsumerName,
                LocalQuantity = localQuantity,
                LocalUnitCost = localQuantity > Epsilon ? localMovementCost / localQuantity : 0d,
                ImportedQuantity = importedQuantity,
                ImportedUnitCost = importedQuantity > Epsilon ? importedMovementCost / importedQuantity : 0d,
                BlendedUnitCost = totalQuantity > Epsilon ? totalMovementCost / totalQuantity : 0d,
                TotalMovementCost = totalMovementCost
            });
        }

        return summaries
            .OrderBy(summary => summary.TrafficType, Comparer)
            .ThenBy(summary => summary.ConsumerName, Comparer)
            .ToList();
    }

    private static double GetRequiredInputPerOutputUnit(
    ProductionInputRequirement requirement,
    string outputTrafficType)
    {
        var inputQuantity = requirement.InputQuantity;
        var outputQuantity = requirement.OutputQuantity;

        if (inputQuantity <= Epsilon &&
            requirement.QuantityPerOutputUnit.HasValue &&
            requirement.QuantityPerOutputUnit.Value > Epsilon)
        {
            inputQuantity = requirement.QuantityPerOutputUnit.Value;
            outputQuantity = 1d;
        }

        if (double.IsNaN(inputQuantity) || double.IsInfinity(inputQuantity) || inputQuantity <= Epsilon ||
            double.IsNaN(outputQuantity) || double.IsInfinity(outputQuantity) || outputQuantity <= Epsilon)
        {
            throw new InvalidOperationException(
                $"Traffic '{outputTrafficType}' has an invalid production input ratio for precursor '{requirement.TrafficType}'.");
        }

        return inputQuantity / outputQuantity;
    }

    private static bool HasStaticRecipeDependencies(NetworkModel network)
    {
        // Bolt: Replaced LINQ .SelectMany, .Where, and .Any with standard nested foreach loops
        // to avoid delegate allocations and enumerator overhead during simulation setup.
        foreach (var node in network.Nodes)
        {
            foreach (var profile in node.TrafficProfiles)
            {
                if (profile.Production > Epsilon)
                {
                    foreach (var requirement in profile.InputRequirements)
                    {
                        if ((requirement.InputQuantity > Epsilon && requirement.OutputQuantity > Epsilon) ||
                            requirement.QuantityPerOutputUnit.GetValueOrDefault() > Epsilon)
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static List<string> BuildStaticRecipeCostOrder(NetworkModel network, IReadOnlyList<string> trafficTypes)
    {
        // Bolt: Replaced LINQ dictionary initializations with a single standard loop
        // to avoid anonymous type allocations, enumerators, and delegates, reducing initialization overhead.
        var count = trafficTypes.Count;
        var originalIndex = new Dictionary<string, int>(count, Comparer);
        var graph = new Dictionary<string, HashSet<string>>(count, Comparer);
        var indegree = new Dictionary<string, int>(count, Comparer);

        for (int i = 0; i < count; i++)
        {
            var trafficType = trafficTypes[i];
            originalIndex[trafficType] = i;
            graph[trafficType] = new HashSet<string>(Comparer);
            indegree[trafficType] = 0;
        }

        // Bolt: Replaced LINQ .SelectMany and .Where with standard loops
        // to avoid enumerator allocations and delegate overhead during initialization.
        foreach (var node in network.Nodes)
        {
            foreach (var profile in node.TrafficProfiles)
            {
                if (profile.Production <= Epsilon) continue;

                if (!graph.ContainsKey(profile.TrafficType))
                {
                    graph[profile.TrafficType] = [];
                    indegree[profile.TrafficType] = 0;
                    originalIndex[profile.TrafficType] = originalIndex.Count;
                }

                foreach (var requirement in profile.InputRequirements)
                {
                    if (!((requirement.InputQuantity > Epsilon && requirement.OutputQuantity > Epsilon) ||
                          requirement.QuantityPerOutputUnit.GetValueOrDefault() > Epsilon))
                    {
                        continue;
                    }

                    if (!graph.ContainsKey(requirement.TrafficType))
                    {
                        graph[requirement.TrafficType] = [];
                        indegree[requirement.TrafficType] = 0;
                        originalIndex[requirement.TrafficType] = originalIndex.Count;
                    }

                    if (graph[requirement.TrafficType].Add(profile.TrafficType))
                    {
                        indegree[profile.TrafficType]++;
                    }
                }
            }
        }

        var ready = new List<string>();
        foreach (var pair in indegree)
        {
            if (pair.Value == 0)
            {
                ready.Add(pair.Key);
            }
        }
        ready.Sort((a, b) =>
        {
            var idxA = originalIndex.GetValueOrDefault(a, int.MaxValue);
            var idxB = originalIndex.GetValueOrDefault(b, int.MaxValue);
            var cmp = idxA.CompareTo(idxB);
            if (cmp != 0) return cmp;
            return Comparer.Compare(a, b);
        });
        var result = new List<string>(graph.Count);
        Comparison<string> trafficTypeComparison = (a, b) =>
        {
            var idxA = originalIndex.GetValueOrDefault(a, int.MaxValue);
            var idxB = originalIndex.GetValueOrDefault(b, int.MaxValue);
            var cmp = idxA.CompareTo(idxB);
            if (cmp != 0) return cmp;
            return Comparer.Compare(a, b);
        };

        while (ready.Count > 0)
        {
            var trafficType = ready[0];
            ready.RemoveAt(0);
            result.Add(trafficType);

            var dependents = graph[trafficType].ToList();
            dependents.Sort(trafficTypeComparison);

            foreach (var dependent in dependents)
            {
                indegree[dependent]--;
                if (indegree[dependent] == 0)
                {
                    ready.Add(dependent);
                    ready.Sort(trafficTypeComparison);
                }
            }
        }

        if (result.Count != graph.Count)
        {
            var cyclicTraffic = indegree
                .Where(pair => pair.Value > 0)
                .Select(pair => pair.Key)
                .OrderBy(item => item, Comparer);
            throw new InvalidOperationException($"Static inherited recipe cost propagation does not support cyclic recipe dependencies. Cycle includes: {string.Join(", ", cyclicTraffic)}.");
        }

        return result;
    }

    private static void SetStaticSourceUnitCosts(
        RoutingTrafficContext context,
        IReadOnlyDictionary<string, TrafficTypeDefinition> definitionsByTraffic,
        IReadOnlyDictionary<string, Dictionary<string, double>> sourceUnitCosts,
        IReadOnlyDictionary<string, Dictionary<string, double>> landedUnitCosts)
    {
        context.SupplyUnitCosts.Clear();
        definitionsByTraffic.TryGetValue(context.TrafficType, out var definition);
        foreach (var pair in context.Supply)
        {
            var nodeId = pair.Key;
            if (!context.ProfilesByNodeId.TryGetValue(nodeId, out var profile) || profile is null)
            {
                context.SupplyUnitCosts[nodeId] = Math.Max(0d, definition?.DefaultUnitProductionCost ?? 0d);
                continue;
            }

            var sourceUnitCost = CalculateStaticSourceUnitCost(profile, definition, nodeId, landedUnitCosts);
            context.SupplyUnitCosts[nodeId] = sourceUnitCost;
            sourceUnitCosts[context.TrafficType][nodeId] = sourceUnitCost;
        }
    }

    private static double CalculateStaticSourceUnitCost(
    NodeTrafficProfile profile,
    TrafficTypeDefinition? definition,
    string nodeId,
    IReadOnlyDictionary<string, Dictionary<string, double>> landedUnitCosts)
    {
        var sourceUnitCost = ResolveBaseProductionCost(profile, definition);
        // Bolt: Eliminated .Where().ToList() allocation and delegate overhead by using a standard foreach loop
        foreach (var requirement in profile.InputRequirements)
        {
            if (!((requirement.InputQuantity > Epsilon && requirement.OutputQuantity > Epsilon) ||
                  requirement.QuantityPerOutputUnit.GetValueOrDefault() > Epsilon))
            {
                continue;
            }

            var precursorUnitCost = landedUnitCosts.TryGetValue(requirement.TrafficType, out var costsByNode)
                ? costsByNode.GetValueOrDefault(nodeId)
                : 0d;

            sourceUnitCost += precursorUnitCost * GetRequiredInputPerOutputUnit(requirement, profile.TrafficType);
        }

        return sourceUnitCost;
    }

    private static double ResolveBaseProductionCost(NodeTrafficProfile? profile, TrafficTypeDefinition? definition)
    {
        if (profile?.ProductionCostPerUnit is { } profileCost)
        {
            return Math.Max(0d, profileCost);
        }

        return Math.Max(0d, definition?.DefaultUnitProductionCost ?? 0d);
    }

    private static Dictionary<string, double> SummarizeLandedUnitCosts(IEnumerable<RouteAllocation> allocations)
    {
        return allocations
            .GroupBy(allocation => allocation.ConsumerNodeId, Comparer)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    // Bolt: Accumulate quantity and total cost in a single loop to avoid multiple O(N) LINQ enumerations and delegate allocations
                    var quantity = 0d;
                    var totalCost = 0d;
                    foreach (var allocation in group)
                    {
                        quantity += allocation.Quantity;
                        totalCost += allocation.DeliveredCostPerUnit * allocation.Quantity;
                    }

                    return quantity > Epsilon ? totalCost / quantity : 0d;
                },
                Comparer);
    }

    private static TrafficContext BuildContext(NetworkModel network, TrafficTypeDefinition definition)
    {
        // Each traffic type sees its own supply/demand profile, but references the same underlying node set.
        // Bolt: Replaced LINQ ToDictionary, Where, and Sum with a single foreach pass to avoid multiple enumerations and allocations
        var profilesByNodeId = new Dictionary<string, NodeTrafficProfile?>(Comparer);
        var nodesById = new Dictionary<string, NodeModel>(Comparer);
        var supply = new Dictionary<string, double>(Comparer);
        var demand = new Dictionary<string, double>(Comparer);

        var totalProduction = 0d;
        var totalConsumption = 0d;

        foreach (var node in network.Nodes)
        {
            nodesById[node.Id] = node;

            NodeTrafficProfile? matchedProfile = null;
            foreach (var profile in node.TrafficProfiles)
            {
                if (Comparer.Equals(profile.TrafficType, definition.Name))
                {
                    matchedProfile = profile;
                    break;
                }
            }

            profilesByNodeId[node.Id] = matchedProfile;

            if (matchedProfile != null)
            {
                if (matchedProfile.Production > Epsilon)
                {
                    supply[node.Id] = matchedProfile.Production;
                    totalProduction += matchedProfile.Production;
                }

                if (matchedProfile.Consumption > Epsilon)
                {
                    demand[node.Id] = matchedProfile.Consumption;
                    totalConsumption += matchedProfile.Consumption;
                }
            }
        }

        AddImplicitRecipeDemand(network, definition.Name, demand);

        var totalDemand = 0d;
        foreach (var value in demand.Values)
        {
            totalDemand += value;
        }

        return new TrafficContext(
            definition.Name,
            definition.RoutingPreference,
            definition.AllocationMode,
            GetCapacityBidPerUnit(definition),
            nodesById,
            profilesByNodeId,
            supply,
            demand,
            totalProduction,
            totalDemand,
            [],
            []);
    }

    private static void AddImplicitRecipeDemand(
     NetworkModel network,
     string trafficType,
     IDictionary<string, double> demand)
    {
        foreach (var node in network.Nodes)
        {
            var implicitDemand = 0d;
            // Bolt: Replaced nested LINQ .Where and .Sum with standard foreach loops to prevent delegate allocations and GC pressure
            foreach (var profile in node.TrafficProfiles)
            {
                if (profile.Production <= Epsilon)
                {
                    continue;
                }

                foreach (var requirement in profile.InputRequirements)
                {
                    if (Comparer.Equals(requirement.TrafficType, trafficType))
                    {
                        implicitDemand += profile.Production * GetRequiredInputPerOutputUnit(requirement, profile.TrafficType);
                    }
                }
            }

            if (implicitDemand <= Epsilon)
            {
                continue;
            }

            demand[node.Id] = (demand.TryGetValue(node.Id, out var existing) ? existing : 0d) + implicitDemand;
        }
    }

    private static void ApplyLocalAllocations(TrafficContext context)
    {
        foreach (var nodeId in context.Supply.Keys.Intersect(context.Demand.Keys, Comparer).ToList())
        {
            var quantity = Math.Min(context.Supply[nodeId], context.Demand[nodeId]);
            if (quantity <= Epsilon)
            {
                continue;
            }

            var node = context.NodesById[nodeId];
            context.Allocations.Add(new RouteAllocation
            {
                TrafficType = context.TrafficType,
                RoutingPreference = context.RoutingPreference,
                AllocationMode = context.AllocationMode,
                ProducerNodeId = nodeId,
                ProducerName = node.Name,
                ConsumerNodeId = nodeId,
                ConsumerName = node.Name,
                Quantity = quantity,
                IsLocalSupply = true,
                TotalTime = 0d,
                TotalCost = 0d,
                BidCostPerUnit = 0d,
                DeliveredCostPerUnit = 0d,
                TotalMovementCost = 0d,
                TotalScore = 0d,
                PathNodeNames = [node.Name],
                PathNodeIds = [nodeId],
                PathEdgeIds = []
            });

            context.Supply[nodeId] -= quantity;
            context.Demand[nodeId] -= quantity;
        }
    }

    private static void AllocateGreedyBestRoutes(
        IReadOnlyList<TrafficContext> contexts,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        while (true)
        {
            // Capacity bidding is resolved globally: every greedy traffic type competes for the next best route.
            RouteCandidate? nextCandidate = null;
            // Bolt: Replaced LINQ OrderBy/ThenBy with manual O(N) linear scan to avoid O(N log N) sorting overhead and delegate allocations
            foreach (var ctx in contexts)
            {
                var candidates = BuildCandidateRoutes(ctx, adjacency, remainingCapacityByEdgeId, remainingTranshipmentCapacityByNodeId);
                foreach (var candidate in candidates)
                {
                    if (nextCandidate is null)
                    {
                        nextCandidate = candidate;
                        continue;
                    }

                    int cmp = nextCandidate.CapacityBidPerUnit.CompareTo(candidate.CapacityBidPerUnit);
                    if (cmp > 0) continue;
                    if (cmp < 0) { nextCandidate = candidate; continue; }

                    cmp = candidate.TotalScore.CompareTo(nextCandidate.TotalScore);
                    if (cmp > 0) continue;
                    if (cmp < 0) { nextCandidate = candidate; continue; }

                    cmp = candidate.TotalTime.CompareTo(nextCandidate.TotalTime);
                    if (cmp > 0) continue;
                    if (cmp < 0) { nextCandidate = candidate; continue; }

                    cmp = candidate.TransitCostPerUnit.CompareTo(nextCandidate.TransitCostPerUnit);
                    if (cmp > 0) continue;
                    if (cmp < 0) { nextCandidate = candidate; continue; }

                    cmp = Comparer.Compare(candidate.ProducerNodeId, nextCandidate.ProducerNodeId);
                    if (cmp > 0) continue;
                    if (cmp < 0) { nextCandidate = candidate; continue; }

                    cmp = Comparer.Compare(candidate.ConsumerNodeId, nextCandidate.ConsumerNodeId);
                    if (cmp < 0) { nextCandidate = candidate; continue; }
                }
            }

            if (nextCandidate is null)
            {
                break;
            }

            var context = nextCandidate.Context;
            var remainingSupply = context.Supply.TryGetValue(nextCandidate.ProducerNodeId, out var supplyValue) ? supplyValue : 0d;
            var remainingDemand = context.Demand.TryGetValue(nextCandidate.ConsumerNodeId, out var demandValue) ? demandValue : 0d;
            var quantity = Math.Min(remainingSupply, remainingDemand);
            var routeCapacity = GetRouteRemainingCapacity(
                nextCandidate.PathEdgeIds,
                nextCandidate.PathTranshipmentNodeIds,
                remainingCapacityByEdgeId,
                remainingTranshipmentCapacityByNodeId);

            if (!double.IsPositiveInfinity(routeCapacity))
            {
                quantity = Math.Min(quantity, routeCapacity);
            }

            if (quantity <= Epsilon)
            {
                break;
            }

            AddRouteAllocation(
                context,
                nextCandidate.ProducerNodeId,
                nextCandidate.ConsumerNodeId,
                quantity,
                isLocalSupply: false,
                nextCandidate.PathNodeIds,
                nextCandidate.PathEdgeIds,
                nextCandidate.PathTranshipmentNodeIds,
                nextCandidate.TotalTime,
                nextCandidate.TransitCostPerUnit,
                nextCandidate.TotalScore,
                nextCandidate.CapacityBidPerUnit,
                remainingCapacityByEdgeId,
                remainingTranshipmentCapacityByNodeId);

            context.Supply[nextCandidate.ProducerNodeId] -= quantity;
            context.Demand[nextCandidate.ConsumerNodeId] -= quantity;
        }
    }

    private static void AllocateProportionallyByBranchDemand(
        TrafficContext context,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        foreach (var producerNodeId in context.Supply.Keys.OrderBy(nodeId => nodeId, Comparer).ToList())
        {
            while (context.Supply.TryGetValue(producerNodeId, out var supply) && supply > Epsilon)
            {
                var delivered = AllocateProportionallyFromNode(
                    context,
                    producerNodeId,
                    producerNodeId,
                    supply,
                    [producerNodeId],
                    [],
                    [],
                    0d,
                    0d,
                    0d,
                    adjacency,
                    remainingCapacityByEdgeId,
                    remainingTranshipmentCapacityByNodeId);

                if (delivered <= Epsilon)
                {
                    break;
                }

                context.Supply[producerNodeId] -= delivered;
            }
        }
    }

    private static double AllocateProportionallyFromNode(
        TrafficContext context,
        string producerNodeId,
        string currentNodeId,
        double availableSupply,
        IReadOnlyList<string> pathNodeIds,
        IReadOnlyList<string> pathEdgeIds,
        IReadOnlyList<string> pathTranshipmentNodeIds,
        double totalTime,
        double transitCostPerUnit,
        double totalScore,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        var delivered = 0d;
        var supplyAtNode = availableSupply;

        if (context.Demand.TryGetValue(currentNodeId, out var localDemand) && localDemand > Epsilon)
        {
            var localQuantity = Math.Min(supplyAtNode, localDemand);
            var routeCapacity = GetRouteRemainingCapacity(pathEdgeIds, pathTranshipmentNodeIds, remainingCapacityByEdgeId, remainingTranshipmentCapacityByNodeId);
            if (!double.IsPositiveInfinity(routeCapacity))
            {
                localQuantity = Math.Min(localQuantity, routeCapacity);
            }

            if (localQuantity > Epsilon)
            {
                AddRouteAllocation(
                    context,
                    producerNodeId,
                    currentNodeId,
                    localQuantity,
                    isLocalSupply: pathEdgeIds.Count == 0,
                    pathNodeIds,
                    pathEdgeIds,
                    pathTranshipmentNodeIds,
                    totalTime,
                    transitCostPerUnit,
                    totalScore,
                    GetCapacityBidPerUnit(context, currentNodeId),
                    remainingCapacityByEdgeId,
                    remainingTranshipmentCapacityByNodeId);

                context.Demand[currentNodeId] -= localQuantity;
                supplyAtNode -= localQuantity;
                delivered += localQuantity;
            }
        }

        while (supplyAtNode > Epsilon)
        {
            var branches = BuildBranchDemandMap(
                context,
                producerNodeId,
                currentNodeId,
                adjacency,
                remainingCapacityByEdgeId,
                remainingTranshipmentCapacityByNodeId);
            var branchShares = AllocateAcrossBranchRoutes(supplyAtNode, branches);
            if (branchShares.Count == 0)
            {
                break;
            }

            var passDelivered = 0d;
            foreach (var share in branchShares)
            {
                var branch = share.Branch;
                var nextPathNodeIds = pathNodeIds.Concat([branch.ToNodeId]).ToList();
                var nextPathEdgeIds = pathEdgeIds.Concat([branch.EdgeId]).ToList();
                var nextPathTranshipmentNodeIds = pathTranshipmentNodeIds.ToList();
                if (!Comparer.Equals(currentNodeId, producerNodeId))
                {
                    nextPathTranshipmentNodeIds.Add(currentNodeId);
                }

                var branchDelivered = AllocateProportionallyFromNode(
                    context,
                    producerNodeId,
                    branch.ToNodeId,
                    share.Quantity,
                    nextPathNodeIds,
                    nextPathEdgeIds,
                    nextPathTranshipmentNodeIds,
                    totalTime + branch.Time,
                    transitCostPerUnit + branch.Cost,
                    totalScore + Score(branch.Time, branch.Cost, context.RoutingPreference),
                    adjacency,
                    remainingCapacityByEdgeId,
                    remainingTranshipmentCapacityByNodeId);

                passDelivered += branchDelivered;
            }

            if (passDelivered <= Epsilon)
            {
                break;
            }

            supplyAtNode -= passDelivered;
            delivered += passDelivered;
        }

        return delivered;
    }

    private static List<BranchDemand> BuildBranchDemandMap(
        TrafficContext context,
        string producerNodeId,
        string currentNodeId,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        var branchesByKey = new Dictionary<string, BranchDemand>(Comparer);

        // Bolt: Replaced LINQ with foreach to avoid delegate allocations and enumerator overhead
        var targetConsumers = new HashSet<string>(Comparer);
        foreach (var pair in context.Demand)
        {
            if (pair.Value > Epsilon && !Comparer.Equals(pair.Key, currentNodeId))
            {
                targetConsumers.Add(pair.Key);
            }
        }

        if (targetConsumers.Count == 0)
        {
            return [];
        }

        var routes = FindBestRoutes(
            context,
            currentNodeId,
            targetConsumers,
            adjacency,
            remainingCapacityByEdgeId,
            remainingTranshipmentCapacityByNodeId);

        var orderedConsumers = targetConsumers
            .OrderBy(nodeId => context.NodesById[nodeId].Name, Comparer)
            .ThenBy(nodeId => nodeId, Comparer);

        foreach (var consumerNodeId in orderedConsumers)
        {
            if (!routes.TryGetValue(consumerNodeId, out var route) || route.PathEdgeIds.Count == 0 || route.PathNodeIds.Count < 2)
            {
                continue;
            }

            var edgeId = route.PathEdgeIds[0];
            if (!remainingCapacityByEdgeId.TryGetValue(edgeId, out var edgeCapacity) || edgeCapacity <= Epsilon)
            {
                continue;
            }

            var firstHopCapacity = edgeCapacity;
            if (!Comparer.Equals(currentNodeId, producerNodeId) &&
                remainingTranshipmentCapacityByNodeId.TryGetValue(currentNodeId, out var transhipmentCapacity))
            {
                firstHopCapacity = Math.Min(firstHopCapacity, transhipmentCapacity);
            }

            if (firstHopCapacity <= Epsilon)
            {
                continue;
            }

            var toNodeId = route.PathNodeIds[1];
            var key = $"{edgeId}\u001f{toNodeId}";
            if (!branchesByKey.TryGetValue(key, out var branch))
            {
                var arc = adjacency.GetValueOrDefault(currentNodeId)?
                    .FirstOrDefault(item => Comparer.Equals(item.EdgeId, edgeId) && Comparer.Equals(item.ToNodeId, toNodeId));
                if (arc is null)
                {
                    continue;
                }

                branch = new BranchDemand(edgeId, toNodeId, arc.Time, arc.Cost, firstHopCapacity);
                branchesByKey[key] = branch;
            }

            branch.DownstreamDemand += context.Demand[consumerNodeId];
            branch.FirstHopCapacity = Math.Min(branch.FirstHopCapacity, firstHopCapacity);
        }

        return branchesByKey.Values
            .Where(branch => branch.DownstreamDemand > Epsilon && branch.FirstHopCapacity > Epsilon)
            .OrderBy(branch => context.NodesById[branch.ToNodeId].Name, Comparer)
            .ThenBy(branch => branch.ToNodeId, Comparer)
            .ThenBy(branch => branch.EdgeId, Comparer)
            .ToList();
    }

    private static List<BranchShare> AllocateAcrossBranchRoutes(double availableSupply, IReadOnlyList<BranchDemand> branches)
    {
        // Bolt: Pre-sort the states once outside the loop, avoiding redundant sorting and delegate allocation on every cycle
        var states = new List<BranchShareState>(branches.Count);
        foreach (var branch in branches)
        {
            var remainingCapacity = Math.Min(branch.DownstreamDemand, branch.FirstHopCapacity);
            if (remainingCapacity > Epsilon)
            {
                states.Add(new BranchShareState(branch, branch.DownstreamDemand, remainingCapacity));
            }
        }

        states.Sort((a, b) =>
        {
            int cmp = Comparer.Compare(a.Branch.ToNodeId, b.Branch.ToNodeId);
            if (cmp != 0) return cmp;
            return Comparer.Compare(a.Branch.EdgeId, b.Branch.EdgeId);
        });

        var remainingSupply = availableSupply;

        while (remainingSupply > Epsilon)
        {
            var totalDemand = 0d;
            foreach (var state in states)
            {
                totalDemand += state.RemainingDemand;
            }

            if (totalDemand <= Epsilon)
            {
                break;
            }

            var progress = 0d;
            foreach (var state in states)
            {
                if (state.RemainingDemand <= Epsilon)
                {
                    continue;
                }

                var targetShare = remainingSupply * state.RemainingDemand / totalDemand;
                var quantity = Math.Min(targetShare, Math.Min(state.RemainingDemand, state.RemainingCapacity));
                if (quantity <= Epsilon)
                {
                    continue;
                }

                state.Quantity += quantity;
                state.RemainingDemand -= quantity;
                state.RemainingCapacity -= quantity;
                progress += quantity;
            }

            if (progress <= Epsilon)
            {
                break;
            }

            remainingSupply -= progress;
        }

        var results = new List<BranchShare>();
        foreach (var state in states)
        {
            if (state.Quantity > Epsilon)
            {
                results.Add(new BranchShare(state.Branch, state.Quantity));
            }
        }

        return results;
    }

    private static void AddRouteAllocation(
        TrafficContext context,
        string producerNodeId,
        string consumerNodeId,
        double quantity,
        bool isLocalSupply,
        IReadOnlyList<string> pathNodeIds,
        IReadOnlyList<string> pathEdgeIds,
        IReadOnlyList<string> pathTranshipmentNodeIds,
        double totalTime,
        double transitCostPerUnit,
        double totalScore,
        double capacityBidPerUnit,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        var routeCapacity = GetRouteRemainingCapacity(pathEdgeIds, pathTranshipmentNodeIds, remainingCapacityByEdgeId, remainingTranshipmentCapacityByNodeId);
        var bidCostPerUnit = CalculateBidCostPerUnit(
            pathEdgeIds,
            pathTranshipmentNodeIds,
            remainingCapacityByEdgeId,
            remainingTranshipmentCapacityByNodeId,
            capacityBidPerUnit,
            quantity,
            routeCapacity);
        var deliveredCostPerUnit = transitCostPerUnit + bidCostPerUnit;

        context.Allocations.Add(new RouteAllocation
        {
            TrafficType = context.TrafficType,
            RoutingPreference = context.RoutingPreference,
            AllocationMode = context.AllocationMode,
            ProducerNodeId = producerNodeId,
            ProducerName = context.NodesById[producerNodeId].Name,
            ConsumerNodeId = consumerNodeId,
            ConsumerName = context.NodesById[consumerNodeId].Name,
            Quantity = quantity,
            IsLocalSupply = isLocalSupply,
            TotalTime = totalTime,
            TotalCost = transitCostPerUnit,
            BidCostPerUnit = bidCostPerUnit,
            DeliveredCostPerUnit = deliveredCostPerUnit,
            TotalMovementCost = deliveredCostPerUnit * quantity,
            TotalScore = totalScore,
            PathNodeNames = pathNodeIds.Select(nodeId => context.NodesById[nodeId].Name).ToList(),
            PathNodeIds = pathNodeIds.ToList(),
            PathEdgeIds = pathEdgeIds.ToList()
        });

        ReserveCapacity(
            pathEdgeIds,
            pathTranshipmentNodeIds,
            remainingCapacityByEdgeId,
            remainingTranshipmentCapacityByNodeId,
            quantity);
    }

    private static List<RouteCandidate> BuildCandidateRoutes(
        TrafficContext context,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        var routes = new List<RouteCandidate>();

        // Bolt: Replaced LINQ with foreach to prevent delegate allocations in the hot loop
        var activeProducers = new List<string>();
        foreach (var pair in context.Supply)
        {
            if (pair.Value > Epsilon) activeProducers.Add(pair.Key);
        }

        var activeConsumers = new HashSet<string>(Comparer);
        foreach (var pair in context.Demand)
        {
            if (pair.Value > Epsilon) activeConsumers.Add(pair.Key);
        }

        foreach (var producerNodeId in activeProducers)
        {
            // Check if there are any valid consumers left (excluding the producer itself).
            if (activeConsumers.Count == 0 || (activeConsumers.Count == 1 && activeConsumers.Contains(producerNodeId)))
            {
                continue;
            }

            var routesToConsumers = FindBestRoutes(
                context,
                producerNodeId,
                activeConsumers,
                adjacency,
                remainingCapacityByEdgeId,
                remainingTranshipmentCapacityByNodeId);

            routes.AddRange(routesToConsumers.Values);
        }

        return routes;
    }

    private static IReadOnlyDictionary<string, RouteCandidate> FindBestRoutes(
        TrafficContext context,
        string producerNodeId,
        IReadOnlySet<string> consumerNodeIds,
        IReadOnlyDictionary<string, List<GraphArc>> adjacency,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        // A batched Dijkstra pass finds the best currently-feasible routes to all targeted consumers.
        var distances = new Dictionary<string, double>(Comparer)
        {
            [producerNodeId] = 0d
        };

        var previous = new Dictionary<string, PreviousStep>(Comparer);
        var queue = new PriorityQueue<string, double>();
        queue.Enqueue(producerNodeId, 0d);

        var consumersToFind = consumerNodeIds.Count;
        if (consumerNodeIds.Contains(producerNodeId))
        {
            consumersToFind--;
        }

        while (queue.TryDequeue(out var currentNodeId, out var currentDistance))
        {
            if (currentDistance > distances[currentNodeId] + Epsilon)
            {
                continue;
            }

            if (consumerNodeIds.Contains(currentNodeId) && !Comparer.Equals(currentNodeId, producerNodeId))
            {
                consumersToFind--;
            }

            if (consumersToFind <= 0)
            {
                break;
            }

            if (!adjacency.TryGetValue(currentNodeId, out var arcs))
            {
                continue;
            }

            // Before expanding out of the current node (except the origin), check if transshipment is allowed.
            if (!Comparer.Equals(currentNodeId, producerNodeId))
            {
                if (!context.ProfilesByNodeId.TryGetValue(currentNodeId, out var profile) || profile?.CanTransship != true)
                {
                    continue;
                }

                if (remainingTranshipmentCapacityByNodeId.TryGetValue(currentNodeId, out var transhipmentCapacity) && transhipmentCapacity <= Epsilon)
                {
                    continue;
                }
            }

            foreach (var arc in arcs)
            {
                if (!remainingCapacityByEdgeId.TryGetValue(arc.EdgeId, out var remainingCapacity) ||
                    remainingCapacity <= Epsilon)
                {
                    continue;
                }

                var proposedDistance = currentDistance + Score(arc.Time, arc.Cost, context.RoutingPreference);
                if (distances.TryGetValue(arc.ToNodeId, out var existingDistance) &&
                    proposedDistance >= existingDistance - Epsilon)
                {
                    continue;
                }

                distances[arc.ToNodeId] = proposedDistance;
                previous[arc.ToNodeId] = new PreviousStep(currentNodeId, arc);
                queue.Enqueue(arc.ToNodeId, proposedDistance);
            }
        }

        var results = new Dictionary<string, RouteCandidate>(Comparer);

        foreach (var consumerNodeId in consumerNodeIds)
        {
            if (Comparer.Equals(consumerNodeId, producerNodeId) || !distances.ContainsKey(consumerNodeId))
            {
                continue;
            }

            var pathNodeIds = new List<string> { consumerNodeId };
            var pathArcs = new List<GraphArc>();
            var cursor = consumerNodeId;

            while (!Comparer.Equals(cursor, producerNodeId))
            {
                var step = previous[cursor];
                pathNodeIds.Add(step.PreviousNodeId);
                pathArcs.Add(step.Arc);
                cursor = step.PreviousNodeId;
            }

            pathNodeIds.Reverse();
            pathArcs.Reverse();
            var pathTranshipmentNodeIds = GetIntermediateNodeIds(pathNodeIds);

            var pathEdgeIds = new List<string>(pathArcs.Count);
            var totalTime = 0d;
            var totalCost = 0d;
            var totalScore = 0d;
            foreach (var arc in pathArcs)
            {
                pathEdgeIds.Add(arc.EdgeId);
                totalTime += arc.Time;
                totalCost += arc.Cost;
                totalScore += Score(arc.Time, arc.Cost, context.RoutingPreference);
            }

            results[consumerNodeId] = new RouteCandidate(
                context,
                producerNodeId,
                consumerNodeId,
                pathNodeIds,
                pathEdgeIds,
                pathTranshipmentNodeIds,
                totalTime,
                totalCost,
                totalScore,
                GetCapacityBidPerUnit(context, consumerNodeId));
        }

        return results;
    }

    private static bool CanTraverseNode(
        string nodeId,
        string producerNodeId,
        string consumerNodeId,
        IReadOnlyDictionary<string, NodeTrafficProfile?> profilesByNodeId)
    {
        if (Comparer.Equals(nodeId, producerNodeId) || Comparer.Equals(nodeId, consumerNodeId))
        {
            return true;
        }

        // Intermediate nodes must explicitly allow transhipment for the current traffic type.
        return profilesByNodeId.TryGetValue(nodeId, out var profile) && profile?.CanTransship == true;
    }

    private static Dictionary<string, List<GraphArc>> BuildAdjacency(NetworkModel network)
    {
        var adjacency = new Dictionary<string, List<GraphArc>>(Comparer);

        void AddArc(string fromNodeId, string toNodeId, EdgeModel edge)
        {
            if (!adjacency.TryGetValue(fromNodeId, out var arcs))
            {
                arcs = [];
                adjacency[fromNodeId] = arcs;
            }

            arcs.Add(new GraphArc(edge.Id, fromNodeId, toNodeId, edge.Time, edge.Cost));
        }

        foreach (var edge in network.Edges)
        {
            AddArc(edge.FromNodeId, edge.ToNodeId, edge);
            if (edge.IsBidirectional)
            {
                AddArc(edge.ToNodeId, edge.FromNodeId, edge);
            }
        }

        return adjacency;
    }

    private static bool IsIntermediateNode(string nodeId, string producerNodeId, string consumerNodeId)
    {
        return !Comparer.Equals(nodeId, producerNodeId) && !Comparer.Equals(nodeId, consumerNodeId);
    }

    private static IReadOnlyList<string> GetIntermediateNodeIds(IReadOnlyList<string> pathNodeIds)
    {
        if (pathNodeIds.Count <= 2)
        {
            return [];
        }

        var intermediateNodeIds = new List<string>(pathNodeIds.Count - 2);
        for (var index = 1; index < pathNodeIds.Count - 1; index++)
        {
            intermediateNodeIds.Add(pathNodeIds[index]);
        }

        return intermediateNodeIds;
    }

    private static double GetRouteRemainingCapacity(
        IReadOnlyList<string> pathEdgeIds,
        IReadOnlyList<string> pathTranshipmentNodeIds,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId)
    {
        var edgeCapacity = GetPathRemainingCapacity(pathEdgeIds, remainingCapacityByEdgeId);
        var nodeCapacity = GetPathRemainingCapacity(pathTranshipmentNodeIds, remainingTranshipmentCapacityByNodeId);
        return Math.Min(edgeCapacity, nodeCapacity);
    }

    private static double GetPathRemainingCapacity(
        IReadOnlyList<string> pathResourceIds,
        IDictionary<string, double> remainingCapacityById)
    {
        if (pathResourceIds.Count == 0)
        {
            return double.PositiveInfinity;
        }

        double minCapacity = double.PositiveInfinity;
        // Bolt: Replaced LINQ Select/Min with a for loop to avoid delegate and enumerator allocations in hot path
        for (int i = 0; i < pathResourceIds.Count; i++)
        {
            var remainingCapacity = remainingCapacityById.TryGetValue(pathResourceIds[i], out var capacity) ? capacity : 0d;
            if (remainingCapacity < minCapacity)
            {
                minCapacity = remainingCapacity;
            }
        }

        return minCapacity == double.PositiveInfinity ? 0d : minCapacity;
    }

    private static double CalculateBidCostPerUnit(
        IReadOnlyList<string> pathEdgeIds,
        IReadOnlyList<string> pathTranshipmentNodeIds,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId,
        double capacityBidPerUnit,
        double quantity,
        double routeCapacity)
    {
        // Bid cost is only charged when the chosen movement fully consumes one or more finite bottlenecks on the route.
        if (capacityBidPerUnit <= Epsilon ||
            quantity <= Epsilon ||
            double.IsPositiveInfinity(routeCapacity) ||
            quantity < routeCapacity - Epsilon)
        {
            return 0d;
        }

        var bottleneckResourceCount =
            CountBottleneckResources(pathEdgeIds, remainingCapacityByEdgeId, routeCapacity) +
            CountBottleneckResources(pathTranshipmentNodeIds, remainingTranshipmentCapacityByNodeId, routeCapacity);

        return bottleneckResourceCount * capacityBidPerUnit;
    }

    private static int CountBottleneckResources(
        IEnumerable<string> pathResourceIds,
        IDictionary<string, double> remainingCapacityById,
        double routeCapacity)
    {
        int count = 0;
        // Bolt: Replaced LINQ Count with foreach to avoid delegate allocations and closure overhead
        foreach (var resourceId in pathResourceIds)
        {
            if (remainingCapacityById.TryGetValue(resourceId, out var remainingCapacity) &&
                !double.IsPositiveInfinity(remainingCapacity) &&
                remainingCapacity <= routeCapacity + Epsilon)
            {
                count++;
            }
        }
        return count;
    }

    private static void ReserveCapacity(
        IEnumerable<string> pathEdgeIds,
        IEnumerable<string> pathTranshipmentNodeIds,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId,
        double quantity)
    {
        ReserveCapacity(pathEdgeIds, remainingCapacityByEdgeId, quantity);
        ReserveCapacity(pathTranshipmentNodeIds, remainingTranshipmentCapacityByNodeId, quantity);
    }

    private static void ReserveCapacity(
        IEnumerable<string> pathResourceIds,
        IDictionary<string, double> remainingCapacityById,
        double quantity)
    {
        foreach (var resourceId in pathResourceIds)
        {
            if (!remainingCapacityById.TryGetValue(resourceId, out var remainingCapacity) ||
                double.IsPositiveInfinity(remainingCapacity))
            {
                continue;
            }

            remainingCapacityById[resourceId] = Math.Max(0d, remainingCapacity - quantity);
        }
    }

    private static List<string> GetOrderedTrafficNames(NetworkModel network)
    {
        // Bolt: Replaced LINQ queries (.SelectMany, .Select, .Concat, .Where, .Distinct)
        // with standard foreach loops to avoid enumerator and delegate allocations during setup.
        var orderedTrafficNames = new List<string>();
        var seen = new HashSet<string>(Comparer);

        foreach (var definition in network.TrafficTypes)
        {
            if (!string.IsNullOrWhiteSpace(definition.Name) && seen.Add(definition.Name))
            {
                orderedTrafficNames.Add(definition.Name);
            }
        }

        var undeclaredTrafficNames = new HashSet<string>(Comparer);

        foreach (var node in network.Nodes)
        {
            foreach (var profile in node.TrafficProfiles)
            {
                if (!string.IsNullOrWhiteSpace(profile.TrafficType) && !seen.Contains(profile.TrafficType))
                {
                    undeclaredTrafficNames.Add(profile.TrafficType);
                }

                foreach (var requirement in profile.InputRequirements)
                {
                    if (!string.IsNullOrWhiteSpace(requirement.TrafficType) && !seen.Contains(requirement.TrafficType))
                    {
                        undeclaredTrafficNames.Add(requirement.TrafficType);
                    }
                }
            }
        }

        if (undeclaredTrafficNames.Count > 0)
        {
            var undeclaredList = undeclaredTrafficNames.ToList();
            undeclaredList.Sort(Comparer);
            orderedTrafficNames.AddRange(undeclaredList);
        }

        return orderedTrafficNames;
    }

    private static double GetCapacityBidPerUnit(TrafficTypeDefinition definition)
    {
        if (definition.CapacityBidPerUnit.HasValue)
        {
            return Math.Max(0d, definition.CapacityBidPerUnit.Value);
        }

        return definition.RoutingPreference == RoutingPreference.Speed ? 1d : 0d;
    }

    private static double GetCapacityBidPerUnit(TrafficContext context, string consumerNodeId)
    {
        var baseBid = context.CapacityBidPerUnit;
        var consumerPremium = context.ProfilesByNodeId.TryGetValue(consumerNodeId, out var profile)
            ? Math.Max(0d, profile?.ConsumerPremiumPerUnit ?? 0d)
            : 0d;
        return baseBid + consumerPremium;
    }

    private static double Score(double time, double cost, RoutingPreference routingPreference)
    {
        return routingPreference switch
        {
            RoutingPreference.Speed => time,
            RoutingPreference.Cost => cost,
            _ => time + cost
        };
    }
    /// <summary>
    /// Represents the graph arc component.
    /// </summary>

    private sealed record GraphArc(
        string EdgeId,
        string FromNodeId,
        string ToNodeId,
        double Time,
        double Cost);
    /// <summary>
    /// Represents the previous step component.
    /// </summary>

    private sealed record PreviousStep(string PreviousNodeId, GraphArc Arc);
    /// <summary>
    /// Represents the route candidate component.
    /// </summary>

    private sealed record RouteCandidate(
        TrafficContext Context,
        string ProducerNodeId,
        string ConsumerNodeId,
        IReadOnlyList<string> PathNodeIds,
        IReadOnlyList<string> PathEdgeIds,
        IReadOnlyList<string> PathTranshipmentNodeIds,
        double TotalTime,
        double TransitCostPerUnit,
        double TotalScore,
        double CapacityBidPerUnit);
    /// <summary>
    /// Represents the traffic context component.
    /// </summary>

    private sealed record TrafficContext(
        string TrafficType,
        RoutingPreference RoutingPreference,
        AllocationMode AllocationMode,
        double CapacityBidPerUnit,
        IReadOnlyDictionary<string, NodeModel> NodesById,
        IReadOnlyDictionary<string, NodeTrafficProfile?> ProfilesByNodeId,
        IDictionary<string, double> Supply,
        IDictionary<string, double> Demand,
        double TotalProduction,
        double TotalConsumption,
        List<RouteAllocation> Allocations,
        List<string> Notes);
    /// <summary>
    /// Represents the branch demand component.
    /// </summary>

    private sealed class BranchDemand(
        string edgeId,
        string toNodeId,
        double time,
        double cost,
        double firstHopCapacity)
    {
        /// <summary>
        /// Gets or sets the edge id.
        /// </summary>
        public string EdgeId { get; } = edgeId;
        /// <summary>
        /// Gets or sets the to node id.
        /// </summary>

        public string ToNodeId { get; } = toNodeId;
        /// <summary>
        /// Gets or sets the time.
        /// </summary>

        public double Time { get; } = time;
        /// <summary>
        /// Gets or sets the cost.
        /// </summary>

        public double Cost { get; } = cost;
        /// <summary>
        /// Gets or sets the first hop capacity.
        /// </summary>

        public double FirstHopCapacity { get; set; } = firstHopCapacity;
        /// <summary>
        /// Gets or sets the downstream demand.
        /// </summary>

        public double DownstreamDemand { get; set; }
    }
    /// <summary>
    /// Represents the branch share component.
    /// </summary>

    private sealed record BranchShare(BranchDemand Branch, double Quantity);
    /// <summary>
    /// Represents the branch share state component.
    /// </summary>

    private sealed class BranchShareState(BranchDemand branch, double remainingDemand, double remainingCapacity)
    {
        /// <summary>
        /// Gets or sets the branch.
        /// </summary>
        public BranchDemand Branch { get; } = branch;
        /// <summary>
        /// Gets or sets the remaining demand.
        /// </summary>

        public double RemainingDemand { get; set; } = remainingDemand;
        /// <summary>
        /// Gets or sets the remaining capacity.
        /// </summary>

        public double RemainingCapacity { get; set; } = remainingCapacity;
        /// <summary>
        /// Gets or sets the quantity.
        /// </summary>

        public double Quantity { get; set; }
    }
}

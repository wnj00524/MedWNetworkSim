using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public static partial class MixedRoutingAllocator
{
    private static readonly object IndexedRouteCandidateCacheLock = new();
    private static readonly Dictionary<IndexedRouteCacheKey, IndexedCachedRoute[]> IndexedRouteCandidateCache = [];

    private static IReadOnlyList<RouteAllocation> AllocateIndexed(
        NetworkModel network,
        IReadOnlyList<RoutingTrafficContext> contexts,
        IDictionary<string, double> remainingCapacityByEdgeId,
        IDictionary<string, double> remainingTranshipmentCapacityByNodeId,
        IReadOnlyDictionary<EdgeTrafficResourceKey, double>? occupiedEdgeTrafficByKey,
        int period,
        CompiledNetworkSimulationContext compiledContext)
    {
        var indexedContexts = new IndexedRoutingTrafficContext[contexts.Count];
        for (var index = 0; index < contexts.Count; index++)
        {
            indexedContexts[index] = IndexedRoutingTrafficContext.Create(contexts[index], compiledContext);
            contexts[index].Notes.Add(contexts[index].RouteChoiceModel == RouteChoiceModel.SystemOptimal
                ? "System-optimal route choice is active: proposals internalize congestion in a shared capacity pool."
                : "Stochastic user-responsive route choice is active: proposals use seeded probabilistic route selection with congestion perception.");
        }

        var state = IndexedNetworkState.Create(
            compiledContext,
            contexts,
            remainingCapacityByEdgeId,
            remainingTranshipmentCapacityByNodeId,
            occupiedEdgeTrafficByKey);
        var workspace = new IndexedRouteSearchWorkspace(compiledContext.NodesByIndex.Length);
        var maxRounds = 1;
        for (var contextIndex = 0; contextIndex < contexts.Count; contextIndex++)
        {
            maxRounds = Math.Max(maxRounds, Math.Max(1, contexts[contextIndex].RouteChoiceSettings.IterationCount));
        }

        maxRounds *= 64;
        for (var round = 0; round < maxRounds && HasRemainingIndexedTraffic(indexedContexts); round++)
        {
            var proposals = new List<IndexedFlowProposal>();
            for (var contextIndex = 0; contextIndex < indexedContexts.Length; contextIndex++)
            {
                var context = indexedContexts[contextIndex];
                if (!HasRemainingIndexedTraffic(context))
                {
                    continue;
                }

                proposals.AddRange(ProposeIndexedByScore(context, state, compiledContext, workspace, deterministicBest: context.Source.RouteChoiceModel == RouteChoiceModel.SystemOptimal, round));
            }

            if (proposals.Count == 0)
            {
                break;
            }

            var committed = ResolveIndexed(proposals, state);
            if (committed.Count == 0)
            {
                break;
            }

            for (var index = 0; index < committed.Count; index++)
            {
                CommitIndexedFlow(committed[index], period, compiledContext, state);
            }
        }

        for (var contextIndex = 0; contextIndex < contexts.Count; contextIndex++)
        {
            CopyIndexedAdaptiveObservations(indexedContexts[contextIndex], state, compiledContext);
            ClassifyRemainingRestrictionsIndexed(network, indexedContexts[contextIndex], state, compiledContext);
        }

        for (var edgeIndex = 0; edgeIndex < compiledContext.EdgeIdsByIndex.Length; edgeIndex++)
        {
            remainingCapacityByEdgeId[compiledContext.EdgeIdsByIndex[edgeIndex]] = state.RemainingEdgeCapacity[edgeIndex];
        }

        for (var nodeIndex = 0; nodeIndex < compiledContext.NodeIdsByIndex.Length; nodeIndex++)
        {
            remainingTranshipmentCapacityByNodeId[compiledContext.NodeIdsByIndex[nodeIndex]] = state.RemainingNodeCapacity[nodeIndex];
        }

        var allocations = new List<RouteAllocation>();
        for (var contextIndex = 0; contextIndex < contexts.Count; contextIndex++)
        {
            allocations.AddRange(contexts[contextIndex].Allocations);
        }

        return allocations;
    }

    private static List<IndexedFlowProposal> ProposeIndexedByScore(
        IndexedRoutingTrafficContext context,
        IndexedNetworkState state,
        CompiledNetworkSimulationContext compiledContext,
        IndexedRouteSearchWorkspace workspace,
        bool deterministicBest,
        int round)
    {
        var candidates = BuildIndexedCandidateRoutes(context, state, compiledContext, workspace);
        if (candidates.Count == 0)
        {
            return [];
        }

        List<IndexedRouteCandidate> ranked = deterministicBest
            ? SortIndexedCandidatesByScore(candidates)
            : RankIndexedStochastic(context, candidates, compiledContext, round);
        var proposals = new List<IndexedFlowProposal>();

        if (context.Source.FlowSplitPolicy == FlowSplitPolicy.SinglePath)
        {
            var route = ranked[0];
            var quantity = Math.Min(context.Supply[route.ProducerNodeIndex], context.Demand[route.ConsumerNodeIndex]);
            if (quantity > Epsilon)
            {
                proposals.Add(ToIndexedProposal(context, route, quantity));
            }

            return proposals;
        }

        var remainingSupply = (double[])context.Supply.Clone();
        var remainingDemand = (double[])context.Demand.Clone();
        var maxRoutes = Math.Max(1, context.Source.RouteChoiceSettings.MaxCandidateRoutes);
        var rankedLimit = Math.Min(maxRoutes, ranked.Count);
        for (var index = 0; index < rankedLimit; index++)
        {
            var route = ranked[index];
            var available = Math.Min(remainingSupply[route.ProducerNodeIndex], remainingDemand[route.ConsumerNodeIndex]);
            if (available <= Epsilon)
            {
                continue;
            }

            var share = ranked.Count == 1
                ? available
                : deterministicBest
                    ? available / Math.Max(1d, maxRoutes - proposals.Count)
                    : available * Math.Max(0.05d, route.Probability);
            var quantity = Math.Min(available, Math.Max(Epsilon, share));
            proposals.Add(ToIndexedProposal(context, route, quantity));
            remainingSupply[route.ProducerNodeIndex] -= quantity;
            remainingDemand[route.ConsumerNodeIndex] -= quantity;
        }

        return proposals;
    }

    private static List<IndexedRouteCandidate> BuildIndexedCandidateRoutes(
        IndexedRoutingTrafficContext context,
        IndexedNetworkState state,
        CompiledNetworkSimulationContext compiledContext,
        IndexedRouteSearchWorkspace workspace)
    {
        var routes = new List<IndexedRouteCandidate>();
        var maxCandidates = Math.Max(1, context.Source.RouteChoiceSettings.MaxCandidateRoutes);
        for (var producerOffset = 0; producerOffset < context.ActiveSupplyNodeIndexes.Length; producerOffset++)
        {
            var producerNodeIndex = context.ActiveSupplyNodeIndexes[producerOffset];
            if (context.Supply[producerNodeIndex] <= Epsilon)
            {
                continue;
            }

            for (var consumerOffset = 0; consumerOffset < context.ActiveDemandNodeIndexes.Length; consumerOffset++)
            {
                var consumerNodeIndex = context.ActiveDemandNodeIndexes[consumerOffset];
                if (context.Demand[consumerNodeIndex] <= Epsilon ||
                    !context.MeetingDemandEligibleNodeIndexes[consumerNodeIndex] ||
                    producerNodeIndex == consumerNodeIndex)
                {
                    continue;
                }

                var found = FindIndexedCandidateRoutes(context, producerNodeIndex, consumerNodeIndex, state, compiledContext, workspace);
                for (var routeIndex = 0; routeIndex < found.Count; routeIndex++)
                {
                    routes.Add(found[routeIndex]);
                }
            }
        }

        if (routes.Count <= 1)
        {
            return routes;
        }

        routes.Sort(CompareIndexedCandidateScoreThenPath);
        var unique = new List<IndexedRouteCandidate>(Math.Min(routes.Count, maxCandidates));
        for (var index = 0; index < routes.Count && unique.Count < maxCandidates; index++)
        {
            var duplicate = false;
            for (var existing = 0; existing < unique.Count; existing++)
            {
                if (AreSamePath(routes[index].PathEdgeIndexes, unique[existing].PathEdgeIndexes))
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
            {
                unique.Add(routes[index]);
            }
        }

        return unique;
    }

    private static List<IndexedRouteCandidate> FindIndexedCandidateRoutes(
        IndexedRoutingTrafficContext context,
        int producerNodeIndex,
        int consumerNodeIndex,
        IndexedNetworkState state,
        CompiledNetworkSimulationContext compiledContext,
        IndexedRouteSearchWorkspace workspace)
    {
        var result = new List<IndexedRouteCandidate>();
        var cached = GetCachedIndexedRoutes(context, producerNodeIndex, consumerNodeIndex, compiledContext, workspace);
        var maxCandidates = Math.Max(1, context.Source.RouteChoiceSettings.MaxCandidateRoutes);
        for (var index = 0; index < cached.Length && result.Count < maxCandidates; index++)
        {
            if (IsCachedRouteDynamicallyFeasible(cached[index], context, state))
            {
                result.Add(ToIndexedCandidate(context, producerNodeIndex, consumerNodeIndex, cached[index], state, compiledContext));
            }
        }

        return result;
    }

    private static IndexedCachedRoute[] GetCachedIndexedRoutes(
        IndexedRoutingTrafficContext context,
        int producerNodeIndex,
        int consumerNodeIndex,
        CompiledNetworkSimulationContext compiledContext,
        IndexedRouteSearchWorkspace workspace)
    {
        var key = new IndexedRouteCacheKey(
            compiledContext.Revision,
            compiledContext.EffectivePeriod,
            compiledContext.ActiveTimelineEventSignature,
            context.TrafficTypeIndex,
            producerNodeIndex,
            consumerNodeIndex,
            context.Source.RoutingPreference,
            context.Source.RouteChoiceSettings.MaxCandidateRoutes,
            context.Source.RouteChoiceSettings.InternalizeCongestion,
            context.Source.RouteChoiceSettings.AdaptiveRoutingEnabled);
        lock (IndexedRouteCandidateCacheLock)
        {
            if (IndexedRouteCandidateCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var computed = SearchIndexedRoutes(context, producerNodeIndex, consumerNodeIndex, compiledContext, workspace);
        lock (IndexedRouteCandidateCacheLock)
        {
            IndexedRouteCandidateCache[key] = computed;
        }

        return computed;
    }

    private static IndexedCachedRoute[] SearchIndexedRoutes(
        IndexedRoutingTrafficContext context,
        int producerNodeIndex,
        int consumerNodeIndex,
        CompiledNetworkSimulationContext compiledContext,
        IndexedRouteSearchWorkspace workspace)
    {
        workspace.Reset();
        var result = new List<IndexedCachedRoute>();
        var maxCandidates = Math.Max(1, context.Source.RouteChoiceSettings.MaxCandidateRoutes);
        var maxDepth = Math.Max(3, compiledContext.NodesByIndex.Length);
        var expansions = 0;
        var stateId = workspace.AddState(producerNodeIndex, -1, -1, 0d, 0d, 0d, 1);
        workspace.Queue.Enqueue(stateId, 0d);

        var topologyCandidateLimit = Math.Max(maxCandidates, Math.Min(256, maxCandidates * 16));

        while (workspace.Queue.TryDequeue(out var currentStateId, out _) && result.Count < topologyCandidateLimit && expansions < 5000)
        {
            expansions++;
            var current = workspace.States[currentStateId];
            if (current.NodeIndex == consumerNodeIndex)
            {
                result.Add(workspace.ToCachedRoute(currentStateId));
                continue;
            }

            if (current.Depth > maxDepth)
            {
                continue;
            }

            var arcs = compiledContext.AdjacencyByNodeIndex[current.NodeIndex];
            for (var arcIndex = 0; arcIndex < arcs.Length; arcIndex++)
            {
                var arc = arcs[arcIndex];
                if (workspace.PathContainsNode(currentStateId, arc.ToNodeIndex) ||
                    !IsTrafficAllowedOnIndexedEdge(compiledContext, arc.EdgeIndex, context.TrafficTypeIndex, context.Source.TrafficType) ||
                    !CanTraverseIndexedNode(context, arc.ToNodeIndex, producerNodeIndex, consumerNodeIndex))
                {
                    continue;
                }

                var edgeCapacity = compiledContext.BaseEdgeCapacityByIndex[arc.EdgeIndex];
                var used = 0d;
                var alpha = context.Source.RouteChoiceModel == RouteChoiceModel.SystemOptimal && !context.Source.RouteChoiceSettings.InternalizeCongestion
                    ? 0d
                    : context.Source.RouteChoiceSettings.CongestionSensitivity;
                var gamma = alpha;
                var effectiveTime = CongestionCostModel.GetEffectiveTime(arc.Time, used, edgeCapacity, alpha);
                var effectiveCost = CongestionCostModel.GetEffectiveCost(arc.Cost, used, edgeCapacity, gamma);
                if (context.Source.RouteChoiceSettings.AdaptiveRoutingEnabled && Guid.TryParse(compiledContext.EdgeIdsByIndex[arc.EdgeIndex], out var edgeGuid))
                {
                    effectiveCost += AdaptiveRoutingMemory.GetAdaptivePenalty(edgeGuid);
                }

                var score = current.Score + Score(effectiveTime, effectiveCost, context.Source.RoutingPreference);
                var nextStateId = workspace.AddState(
                    arc.ToNodeIndex,
                    arc.EdgeIndex,
                    currentStateId,
                    current.BaseTime + arc.Time,
                    current.BaseCost + arc.Cost,
                    score,
                    current.Depth + 1);
                workspace.Queue.Enqueue(nextStateId, score);
            }
        }

        return [.. result];
    }

    private static bool IsCachedRouteDynamicallyFeasible(IndexedCachedRoute route, IndexedRoutingTrafficContext context, IndexedNetworkState state)
    {
        for (var edgeOffset = 0; edgeOffset < route.PathEdgeIndexes.Length; edgeOffset++)
        {
            var edgeIndex = route.PathEdgeIndexes[edgeOffset];
            if (state.RemainingEdgeCapacity[edgeIndex] <= Epsilon ||
                state.GetRemainingEdgeTrafficCapacity(edgeIndex, context.TrafficTypeIndex) <= Epsilon)
            {
                return false;
            }
        }

        for (var nodeOffset = 1; nodeOffset < route.PathNodeIndexes.Length - 1; nodeOffset++)
        {
            if (state.RemainingNodeCapacity[route.PathNodeIndexes[nodeOffset]] <= Epsilon)
            {
                return false;
            }
        }

        return true;
    }

    private static IndexedRouteCandidate ToIndexedCandidate(
        IndexedRoutingTrafficContext context,
        int producerNodeIndex,
        int consumerNodeIndex,
        IndexedCachedRoute route,
        IndexedNetworkState state,
        CompiledNetworkSimulationContext compiledContext)
    {
        var effectiveTime = 0d;
        var effectiveCost = 0d;
        for (var edgeOffset = 0; edgeOffset < route.PathEdgeIndexes.Length; edgeOffset++)
        {
            var edgeIndex = route.PathEdgeIndexes[edgeOffset];
            effectiveTime += GetEffectiveIndexedArcTime(context, edgeIndex, route.EdgeTimes[edgeOffset], state);
            effectiveCost += GetEffectiveIndexedArcCost(context, edgeIndex, route.EdgeCosts[edgeOffset], state, compiledContext);
        }

        var score = Score(effectiveTime, effectiveCost, context.Source.RoutingPreference);
        var pathKey = BuildPathKey(route.PathEdgeIndexes, compiledContext);
        if (!string.IsNullOrEmpty(context.Source.LastPathKey) && Comparer.Equals(pathKey, context.Source.LastPathKey))
        {
            score *= Math.Max(0d, 1d - context.Source.RouteChoiceSettings.Stickiness);
        }

        return new IndexedRouteCandidate(
            context,
            producerNodeIndex,
            consumerNodeIndex,
            route.PathNodeIndexes,
            route.PathEdgeIndexes,
            route.BaseTime,
            route.BaseCost,
            effectiveTime,
            effectiveCost,
            score,
            pathKey,
            1d);
    }

    private static List<IndexedCommittedFlow> ResolveIndexed(List<IndexedFlowProposal> proposals, IndexedNetworkState state)
    {
        proposals.Sort(CompareIndexedProposalPriority);
        var result = new List<IndexedCommittedFlow>();
        for (var index = 0; index < proposals.Count; index++)
        {
            var proposal = proposals[index];
            if (proposal.Quantity <= Epsilon)
            {
                continue;
            }

            var routeCapacity = GetIndexedRouteRemainingCapacity(proposal.Context.TrafficTypeIndex, proposal.PathEdgeIndexes, proposal.PathTranshipmentNodeIndexes, state);
            var quantity = Math.Min(proposal.Quantity, routeCapacity);
            if (quantity <= Epsilon)
            {
                continue;
            }

            result.Add(new IndexedCommittedFlow(proposal, quantity));
            ReserveIndexedCapacity(proposal.Context.TrafficTypeIndex, proposal.PathEdgeIndexes, proposal.PathTranshipmentNodeIndexes, state, quantity);
        }

        return result;
    }

    private static void CommitIndexedFlow(IndexedCommittedFlow flow, int period, CompiledNetworkSimulationContext compiledContext, IndexedNetworkState state)
    {
        var context = flow.Context.Source;
        var producerNodeId = compiledContext.NodeIdsByIndex[flow.ProducerNodeIndex];
        var consumerNodeId = compiledContext.NodeIdsByIndex[flow.ConsumerNodeIndex];
        var quantity = Math.Min(flow.Quantity, Math.Min(context.Supply.GetValueOrDefault(producerNodeId), context.Demand.GetValueOrDefault(consumerNodeId)));
        if (quantity <= Epsilon)
        {
            return;
        }

        var pathEdgeIds = ToEdgeIds(flow.PathEdgeIndexes, compiledContext);
        var pathTranshipmentNodeIds = ToNodeIds(flow.PathTranshipmentNodeIndexes, compiledContext);
        var pathNodeIds = ToNodeIds(flow.PathNodeIndexes, compiledContext);
        var bidCostPerUnit = CalculateBidCostPerUnit(pathEdgeIds, pathTranshipmentNodeIds, context.CapacityBidPerUnit, quantity, flow.Priority);
        var sourceUnitCostPerUnit = context.SupplyUnitCosts.GetValueOrDefault(producerNodeId);
        var deliveredCostPerUnit = sourceUnitCostPerUnit + flow.EffectiveCost + bidCostPerUnit;
        var pathNodeNames = new List<string>(pathNodeIds.Count);
        for (var index = 0; index < pathNodeIds.Count; index++)
        {
            pathNodeNames.Add(context.NodesById[pathNodeIds[index]].Name);
        }

        context.Allocations.Add(new RouteAllocation
        {
            Period = period,
            TrafficType = context.TrafficType,
            RoutingPreference = context.RoutingPreference,
            AllocationMode = context.AllocationMode,
            ProducerNodeId = producerNodeId,
            ProducerName = context.NodesById[producerNodeId].Name,
            ConsumerNodeId = consumerNodeId,
            ConsumerName = context.NodesById[consumerNodeId].Name,
            Quantity = quantity,
            IsLocalSupply = false,
            TotalTime = flow.EffectiveTime,
            TotalCost = flow.EffectiveCost,
            BidCostPerUnit = bidCostPerUnit,
            SourceUnitCostPerUnit = sourceUnitCostPerUnit,
            DeliveredCostPerUnit = deliveredCostPerUnit,
            TotalMovementCost = deliveredCostPerUnit * quantity,
            TotalScore = flow.Score,
            PathNodeNames = pathNodeNames,
            PathNodeIds = pathNodeIds,
            PathEdgeIds = pathEdgeIds
        });

        context.Supply[producerNodeId] -= quantity;
        context.Demand[consumerNodeId] -= quantity;
        flow.Context.Supply[flow.ProducerNodeIndex] -= quantity;
        flow.Context.Demand[flow.ConsumerNodeIndex] -= quantity;
        context.CommittedSupply[producerNodeId] = context.CommittedSupply.GetValueOrDefault(producerNodeId) + quantity;
        context.CommittedDemand[consumerNodeId] = context.CommittedDemand.GetValueOrDefault(consumerNodeId) + quantity;
        context.LastPathKey = string.Join(">", pathEdgeIds);
    }

    private static double GetIndexedRouteRemainingCapacity(int trafficTypeIndex, int[] pathEdgeIndexes, int[] pathTranshipmentNodeIndexes, IndexedNetworkState state)
    {
        var remaining = double.PositiveInfinity;
        for (var index = 0; index < pathEdgeIndexes.Length; index++)
        {
            var edgeIndex = pathEdgeIndexes[index];
            remaining = Math.Min(remaining, state.RemainingEdgeCapacity[edgeIndex]);
            remaining = Math.Min(remaining, state.GetRemainingEdgeTrafficCapacity(edgeIndex, trafficTypeIndex));
        }

        for (var index = 0; index < pathTranshipmentNodeIndexes.Length; index++)
        {
            remaining = Math.Min(remaining, state.RemainingNodeCapacity[pathTranshipmentNodeIndexes[index]]);
        }

        return remaining;
    }

    private static void ReserveIndexedCapacity(int trafficTypeIndex, int[] pathEdgeIndexes, int[] pathTranshipmentNodeIndexes, IndexedNetworkState state, double quantity)
    {
        for (var index = 0; index < pathEdgeIndexes.Length; index++)
        {
            var edgeIndex = pathEdgeIndexes[index];
            if (!double.IsPositiveInfinity(state.RemainingEdgeCapacity[edgeIndex]))
            {
                state.RemainingEdgeCapacity[edgeIndex] = Math.Max(0d, state.RemainingEdgeCapacity[edgeIndex] - quantity);
            }

            state.EdgeLoad[edgeIndex] += quantity;
            state.ReserveEdgeTrafficCapacity(edgeIndex, trafficTypeIndex, quantity);
        }

        for (var index = 0; index < pathTranshipmentNodeIndexes.Length; index++)
        {
            var nodeIndex = pathTranshipmentNodeIndexes[index];
            if (!double.IsPositiveInfinity(state.RemainingNodeCapacity[nodeIndex]))
            {
                state.RemainingNodeCapacity[nodeIndex] = Math.Max(0d, state.RemainingNodeCapacity[nodeIndex] - quantity);
            }

            state.NodeLoad[nodeIndex] += quantity;
        }
    }

    private static bool HasRemainingIndexedTraffic(IndexedRoutingTrafficContext[] contexts)
    {
        for (var index = 0; index < contexts.Length; index++)
        {
            if (HasRemainingIndexedTraffic(contexts[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRemainingIndexedTraffic(IndexedRoutingTrafficContext context)
    {
        var hasSupply = false;
        for (var index = 0; index < context.ActiveSupplyNodeIndexes.Length; index++)
        {
            if (context.Supply[context.ActiveSupplyNodeIndexes[index]] > Epsilon)
            {
                hasSupply = true;
                break;
            }
        }

        if (!hasSupply)
        {
            return false;
        }

        for (var index = 0; index < context.ActiveDemandNodeIndexes.Length; index++)
        {
            if (context.Demand[context.ActiveDemandNodeIndexes[index]] > Epsilon)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanTraverseIndexedNode(IndexedRoutingTrafficContext context, int nodeIndex, int producerNodeIndex, int consumerNodeIndex)
    {
        return nodeIndex == producerNodeIndex || nodeIndex == consumerNodeIndex || context.CanTransship[nodeIndex];
    }

    private static bool IsTrafficAllowedOnIndexedEdge(CompiledNetworkSimulationContext compiledContext, int edgeIndex, int trafficTypeIndex, string trafficType)
    {
        if (compiledContext.TrafficTypeIndexByName.Count <= 64 && trafficTypeIndex is >= 0 and < 64)
        {
            return (compiledContext.AllowedTrafficMaskByEdgeIndex[edgeIndex] & (1UL << trafficTypeIndex)) != 0;
        }

        return compiledContext.IsTrafficAllowedOnEdge(edgeIndex, trafficType);
    }

    private static double GetEffectiveIndexedArcTime(IndexedRoutingTrafficContext context, int edgeIndex, double baseTime, IndexedNetworkState state)
    {
        var alpha = context.Source.RouteChoiceModel == RouteChoiceModel.SystemOptimal && !context.Source.RouteChoiceSettings.InternalizeCongestion
            ? 0d
            : context.Source.RouteChoiceSettings.CongestionSensitivity;
        return CongestionCostModel.GetEffectiveTime(baseTime, state.EdgeLoad[edgeIndex], state.EdgeCapacity[edgeIndex], alpha);
    }

    private static double GetEffectiveIndexedArcCost(IndexedRoutingTrafficContext context, int edgeIndex, double baseCost, IndexedNetworkState state, CompiledNetworkSimulationContext compiledContext)
    {
        var gamma = context.Source.RouteChoiceModel == RouteChoiceModel.SystemOptimal && !context.Source.RouteChoiceSettings.InternalizeCongestion
            ? 0d
            : context.Source.RouteChoiceSettings.CongestionSensitivity;
        var congestionCost = CongestionCostModel.GetEffectiveCost(baseCost, state.EdgeLoad[edgeIndex], state.EdgeCapacity[edgeIndex], gamma);
        if (!context.Source.RouteChoiceSettings.AdaptiveRoutingEnabled || !Guid.TryParse(compiledContext.EdgeIdsByIndex[edgeIndex], out var edgeId))
        {
            return congestionCost;
        }

        return congestionCost + AdaptiveRoutingMemory.GetAdaptivePenalty(edgeId);
    }

    private static List<IndexedRouteCandidate> SortIndexedCandidatesByScore(List<IndexedRouteCandidate> candidates)
    {
        var ranked = new List<IndexedRouteCandidate>(candidates);
        ranked.Sort(CompareIndexedCandidateScoreThenPath);
        return ranked;
    }

    private static List<IndexedRouteCandidate> RankIndexedStochastic(IndexedRoutingTrafficContext context, IReadOnlyList<IndexedRouteCandidate> candidates, CompiledNetworkSimulationContext compiledContext, int round)
    {
        var rng = new Random(HashCode.Combine(context.Source.Seed, round, StringComparer.OrdinalIgnoreCase.GetHashCode(context.Source.TrafficType)));
        var diversity = Math.Max(0.01d, context.Source.RouteChoiceSettings.RouteDiversity);
        var lambda = 1d / diversity;
        var bestActual = candidates[0].Score;
        for (var index = 1; index < candidates.Count; index++)
        {
            bestActual = Math.Min(bestActual, candidates[index].Score);
        }

        var perceived = new List<IndexedRouteCandidate>(candidates.Count);
        var total = 0d;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var noiseScale = Math.Max(0d, 1d - context.Source.RouteChoiceSettings.InformationAccuracy);
            var noise = (rng.NextDouble() - 0.5d) * noiseScale * Math.Max(1d, candidate.Score);
            var score = candidate.Score + noise;
            if (!string.IsNullOrEmpty(context.Source.LastPathKey) &&
                !Comparer.Equals(candidate.PathKey, context.Source.LastPathKey) &&
                Math.Abs(candidate.Score - bestActual) <= context.Source.RouteChoiceSettings.RerouteThreshold)
            {
                score += context.Source.RouteChoiceSettings.Stickiness * Math.Max(1d, candidate.Score);
            }

            var probability = Math.Exp(-lambda * Math.Max(0d, score));
            total += probability;
            perceived.Add(candidate with { Probability = probability });
        }

        if (total <= Epsilon)
        {
            perceived.Sort(CompareIndexedCandidateScoreThenPath);
            return perceived;
        }

        for (var index = 0; index < perceived.Count; index++)
        {
            perceived[index] = perceived[index] with { Probability = perceived[index].Probability / total };
        }

        if (context.Source.FlowSplitPolicy == FlowSplitPolicy.SinglePath)
        {
            perceived.Sort(CompareIndexedCandidatePathOnly);
            var roll = rng.NextDouble();
            var cumulative = 0d;
            for (var index = 0; index < perceived.Count; index++)
            {
                cumulative += perceived[index].Probability;
                if (roll <= cumulative)
                {
                    return [perceived[index]];
                }
            }
        }

        perceived.Sort(CompareIndexedCandidateProbability);
        return perceived;
    }

    private static IndexedFlowProposal ToIndexedProposal(IndexedRoutingTrafficContext context, IndexedRouteCandidate route, double quantity)
    {
        var transhipmentLength = Math.Max(0, route.PathNodeIndexes.Length - 2);
        var transhipment = new int[transhipmentLength];
        if (transhipmentLength > 0)
        {
            Array.Copy(route.PathNodeIndexes, 1, transhipment, 0, transhipmentLength);
        }

        return new IndexedFlowProposal(
            context,
            route.ProducerNodeIndex,
            route.ConsumerNodeIndex,
            quantity,
            route.PathNodeIndexes,
            route.PathEdgeIndexes,
            transhipment,
            route.BaseTime,
            route.BaseCost,
            route.EffectiveTime,
            route.EffectiveCost,
            route.Score,
            Math.Max(Epsilon, context.Source.RouteChoiceSettings.Priority));
    }

    private static int CompareIndexedProposalPriority(IndexedFlowProposal left, IndexedFlowProposal right)
    {
        var leftPriority = Math.Max(Epsilon, left.Priority) + Math.Max(0d, left.Context.Source.CapacityBidPerUnit);
        var rightPriority = Math.Max(Epsilon, right.Priority) + Math.Max(0d, right.Context.Source.CapacityBidPerUnit);
        var priorityComparison = rightPriority.CompareTo(leftPriority);
        if (priorityComparison != 0)
        {
            return priorityComparison;
        }

        var scoreComparison = left.Score.CompareTo(right.Score);
        if (scoreComparison != 0)
        {
            return scoreComparison;
        }

        return Comparer.Compare(left.Context.Source.TrafficType, right.Context.Source.TrafficType);
    }

    private static int CompareIndexedCandidateScoreThenPath(IndexedRouteCandidate left, IndexedRouteCandidate right)
    {
        var scoreComparison = left.Score.CompareTo(right.Score);
        return scoreComparison != 0 ? scoreComparison : Comparer.Compare(left.PathKey, right.PathKey);
    }

    private static int CompareIndexedCandidatePathOnly(IndexedRouteCandidate left, IndexedRouteCandidate right)
    {
        return Comparer.Compare(left.PathKey, right.PathKey);
    }

    private static int CompareIndexedCandidateProbability(IndexedRouteCandidate left, IndexedRouteCandidate right)
    {
        var probabilityComparison = right.Probability.CompareTo(left.Probability);
        return probabilityComparison != 0 ? probabilityComparison : Comparer.Compare(left.PathKey, right.PathKey);
    }

    private static bool AreSamePath(int[] left, int[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildPathKey(int[] pathEdgeIndexes, CompiledNetworkSimulationContext compiledContext)
    {
        if (pathEdgeIndexes.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(">", ToEdgeIds(pathEdgeIndexes, compiledContext));
    }

    private static List<string> ToEdgeIds(int[] edgeIndexes, CompiledNetworkSimulationContext compiledContext)
    {
        var ids = new List<string>(edgeIndexes.Length);
        for (var index = 0; index < edgeIndexes.Length; index++)
        {
            ids.Add(compiledContext.EdgeIdsByIndex[edgeIndexes[index]]);
        }

        return ids;
    }

    private static List<string> ToNodeIds(int[] nodeIndexes, CompiledNetworkSimulationContext compiledContext)
    {
        var ids = new List<string>(nodeIndexes.Length);
        for (var index = 0; index < nodeIndexes.Length; index++)
        {
            ids.Add(compiledContext.NodeIdsByIndex[nodeIndexes[index]]);
        }

        return ids;
    }

    private static void CopyIndexedAdaptiveObservations(IndexedRoutingTrafficContext context, IndexedNetworkState state, CompiledNetworkSimulationContext compiledContext)
    {
        if (!context.Source.RouteChoiceSettings.AdaptiveRoutingEnabled)
        {
            return;
        }

        for (var edgeIndex = 0; edgeIndex < state.EdgeLoad.Length; edgeIndex++)
        {
            var capacity = state.EdgeCapacity[edgeIndex];
            var util = double.IsPositiveInfinity(capacity) || capacity <= 0d ? 0d : state.EdgeLoad[edgeIndex] / capacity;
            if (Guid.TryParse(compiledContext.EdgeIdsByIndex[edgeIndex], out var edgeId))
            {
                AdaptiveRoutingMemory.RecordObservation(edgeId, observedDelay: state.EdgeLoad[edgeIndex], utilisation: util);
            }
        }
    }

    private static void ClassifyRemainingRestrictionsIndexed(NetworkModel network, IndexedRoutingTrafficContext context, IndexedNetworkState state, CompiledNetworkSimulationContext compiledContext)
    {
        if (!HasRemainingIndexedTraffic(context))
        {
            return;
        }

        for (var producerOffset = 0; producerOffset < context.ActiveSupplyNodeIndexes.Length; producerOffset++)
        {
            var producerNodeIndex = context.ActiveSupplyNodeIndexes[producerOffset];
            if (context.Supply[producerNodeIndex] <= Epsilon)
            {
                continue;
            }

            for (var consumerOffset = 0; consumerOffset < context.ActiveDemandNodeIndexes.Length; consumerOffset++)
            {
                var consumerNodeIndex = context.ActiveDemandNodeIndexes[consumerOffset];
                if (context.Demand[consumerNodeIndex] <= Epsilon || !context.MeetingDemandEligibleNodeIndexes[consumerNodeIndex] || producerNodeIndex == consumerNodeIndex)
                {
                    continue;
                }

                var quantity = Math.Min(context.Supply[producerNodeIndex], context.Demand[consumerNodeIndex]);
                if (quantity <= Epsilon)
                {
                    continue;
                }

                if (!HasAnyIndexedFeasibleRoute(context, producerNodeIndex, consumerNodeIndex, state, compiledContext, RouteConstraintMode.BlockedOnly))
                {
                    context.Source.NoPermittedPathDemand += quantity;
                }
                else if (!HasAnyIndexedFeasibleRoute(context, producerNodeIndex, consumerNodeIndex, state, compiledContext, RouteConstraintMode.PermissionLimited))
                {
                    context.Source.PermissionLimitedDemand += quantity;
                }
                else if (!HasAnyIndexedFeasibleRoute(context, producerNodeIndex, consumerNodeIndex, state, compiledContext, RouteConstraintMode.AllConstraints))
                {
                    context.Source.CapacityBlockedDemand += quantity;
                }
            }
        }
    }

    private static bool HasAnyIndexedFeasibleRoute(
        IndexedRoutingTrafficContext context,
        int producerNodeIndex,
        int consumerNodeIndex,
        IndexedNetworkState state,
        CompiledNetworkSimulationContext compiledContext,
        RouteConstraintMode constraintMode)
    {
        var visited = new bool[compiledContext.NodesByIndex.Length];
        var queue = new Queue<int>();
        visited[producerNodeIndex] = true;
        queue.Enqueue(producerNodeIndex);

        while (queue.Count > 0)
        {
            var currentNodeIndex = queue.Dequeue();
            if (currentNodeIndex == consumerNodeIndex)
            {
                return true;
            }

            var arcs = compiledContext.AdjacencyByNodeIndex[currentNodeIndex];
            for (var index = 0; index < arcs.Length; index++)
            {
                var arc = arcs[index];
                if (!IsTrafficAllowedOnIndexedEdge(compiledContext, arc.EdgeIndex, context.TrafficTypeIndex, context.Source.TrafficType))
                {
                    continue;
                }

                if (constraintMode is RouteConstraintMode.PermissionLimited or RouteConstraintMode.AllConstraints &&
                    state.GetRemainingEdgeTrafficCapacity(arc.EdgeIndex, context.TrafficTypeIndex) <= Epsilon)
                {
                    continue;
                }

                if (constraintMode == RouteConstraintMode.AllConstraints)
                {
                    if (state.RemainingEdgeCapacity[arc.EdgeIndex] <= Epsilon)
                    {
                        continue;
                    }

                    if (arc.ToNodeIndex != producerNodeIndex && arc.ToNodeIndex != consumerNodeIndex && state.RemainingNodeCapacity[arc.ToNodeIndex] <= Epsilon)
                    {
                        continue;
                    }
                }

                if (!CanTraverseIndexedNode(context, arc.ToNodeIndex, producerNodeIndex, consumerNodeIndex) || visited[arc.ToNodeIndex])
                {
                    continue;
                }

                visited[arc.ToNodeIndex] = true;
                queue.Enqueue(arc.ToNodeIndex);
            }
        }

        return false;
    }

    private sealed class IndexedNetworkState
    {
        public double[] RemainingEdgeCapacity { get; private init; } = [];
        public double[] EdgeLoad { get; private init; } = [];
        public double[] RemainingNodeCapacity { get; private init; } = [];
        public double[] NodeLoad { get; private init; } = [];
        public double[] EdgeCapacity { get; private init; } = [];
        public double[] NodeCapacity { get; private init; } = [];
        public double[] RemainingEdgeTrafficCapacity { get; private init; } = [];
        public double[] EdgeTrafficLoad { get; private init; } = [];
        public int TrafficTypeCount { get; private init; }

        public static IndexedNetworkState Create(
            CompiledNetworkSimulationContext compiledContext,
            IReadOnlyList<RoutingTrafficContext> contexts,
            IDictionary<string, double> remainingCapacityByEdgeId,
            IDictionary<string, double> remainingTranshipmentCapacityByNodeId,
            IReadOnlyDictionary<EdgeTrafficResourceKey, double>? occupiedEdgeTrafficByKey)
        {
            var trafficTypeCount = Math.Max(1, compiledContext.TrafficTypeIndexByName.Count);
            var state = new IndexedNetworkState
            {
                RemainingEdgeCapacity = new double[compiledContext.EdgeIdsByIndex.Length],
                EdgeLoad = new double[compiledContext.EdgeIdsByIndex.Length],
                RemainingNodeCapacity = new double[compiledContext.NodeIdsByIndex.Length],
                NodeLoad = new double[compiledContext.NodeIdsByIndex.Length],
                EdgeCapacity = (double[])compiledContext.BaseEdgeCapacityByIndex.Clone(),
                NodeCapacity = (double[])compiledContext.BaseNodeTranshipmentCapacityByIndex.Clone(),
                RemainingEdgeTrafficCapacity = new double[compiledContext.EdgeIdsByIndex.Length * trafficTypeCount],
                EdgeTrafficLoad = new double[compiledContext.EdgeIdsByIndex.Length * trafficTypeCount],
                TrafficTypeCount = trafficTypeCount
            };

            for (var edgeIndex = 0; edgeIndex < compiledContext.EdgeIdsByIndex.Length; edgeIndex++)
            {
                var edgeId = compiledContext.EdgeIdsByIndex[edgeIndex];
                state.RemainingEdgeCapacity[edgeIndex] = remainingCapacityByEdgeId.TryGetValue(edgeId, out var remaining)
                    ? remaining
                    : 0d;
                state.EdgeLoad[edgeIndex] = Math.Max(0d, state.EdgeCapacity[edgeIndex] - state.RemainingEdgeCapacity[edgeIndex]);
            }

            for (var nodeIndex = 0; nodeIndex < compiledContext.NodeIdsByIndex.Length; nodeIndex++)
            {
                var nodeId = compiledContext.NodeIdsByIndex[nodeIndex];
                state.RemainingNodeCapacity[nodeIndex] = remainingTranshipmentCapacityByNodeId.TryGetValue(nodeId, out var remaining)
                    ? remaining
                    : 0d;
                state.NodeLoad[nodeIndex] = Math.Max(0d, state.NodeCapacity[nodeIndex] - state.RemainingNodeCapacity[nodeIndex]);
            }

            var permissionResolver = new EdgeTrafficPermissionResolver();
            var trafficSeen = new HashSet<string>(Comparer);
            for (var contextIndex = 0; contextIndex < contexts.Count; contextIndex++)
            {
                var trafficType = contexts[contextIndex].TrafficType;
                if (!compiledContext.TrafficTypeIndexByName.TryGetValue(trafficType, out var trafficTypeIndex) || !trafficSeen.Add(trafficType))
                {
                    continue;
                }

                for (var edgeIndex = 0; edgeIndex < compiledContext.EdgesByIndex.Length; edgeIndex++)
                {
                    var edge = compiledContext.EdgesByIndex[edgeIndex];
                    var allowed = permissionResolver.GetAllowedCapacity(edge, permissionResolver.Resolve(compiledContext.EffectiveNetwork, edge, trafficType));
                    var key = new EdgeTrafficResourceKey(edge.Id, trafficType);
                    var occupied = occupiedEdgeTrafficByKey?.TryGetValue(key, out var occupiedValue) == true ? occupiedValue : 0d;
                    var remaining = double.IsPositiveInfinity(allowed) ? double.PositiveInfinity : Math.Max(0d, allowed - occupied);
                    var flatIndex = state.GetFlatTrafficIndex(edgeIndex, trafficTypeIndex);
                    state.RemainingEdgeTrafficCapacity[flatIndex] = remaining;
                    state.EdgeTrafficLoad[flatIndex] = double.IsPositiveInfinity(allowed) ? Math.Max(0d, occupied) : Math.Max(0d, allowed - remaining);
                }
            }

            return state;
        }

        public double GetRemainingEdgeTrafficCapacity(int edgeIndex, int trafficTypeIndex)
        {
            return RemainingEdgeTrafficCapacity[GetFlatTrafficIndex(edgeIndex, trafficTypeIndex)];
        }

        public void ReserveEdgeTrafficCapacity(int edgeIndex, int trafficTypeIndex, double quantity)
        {
            var flatIndex = GetFlatTrafficIndex(edgeIndex, trafficTypeIndex);
            if (!double.IsPositiveInfinity(RemainingEdgeTrafficCapacity[flatIndex]))
            {
                RemainingEdgeTrafficCapacity[flatIndex] = Math.Max(0d, RemainingEdgeTrafficCapacity[flatIndex] - quantity);
            }

            EdgeTrafficLoad[flatIndex] += quantity;
        }

        private int GetFlatTrafficIndex(int edgeIndex, int trafficTypeIndex)
        {
            return (edgeIndex * TrafficTypeCount) + trafficTypeIndex;
        }
    }

    private sealed class IndexedRoutingTrafficContext
    {
        public RoutingTrafficContext Source { get; private init; } = null!;
        public int TrafficTypeIndex { get; private init; }
        public double[] Supply { get; private init; } = [];
        public double[] Demand { get; private init; } = [];
        public bool[] CanTransship { get; private init; } = [];
        public bool[] MeetingDemandEligibleNodeIndexes { get; private init; } = [];
        public int[] ActiveSupplyNodeIndexes { get; private init; } = [];
        public int[] ActiveDemandNodeIndexes { get; private init; } = [];

        public static IndexedRoutingTrafficContext Create(RoutingTrafficContext context, CompiledNetworkSimulationContext compiledContext)
        {
            var nodeCount = compiledContext.NodesByIndex.Length;
            var supply = new double[nodeCount];
            var demand = new double[nodeCount];
            var canTransship = new bool[nodeCount];
            var meetingEligible = new bool[nodeCount];
            var supplyIndexes = new List<int>();
            var demandIndexes = new List<int>();
            foreach (var pair in context.Supply)
            {
                if (compiledContext.NodeIndexById.TryGetValue(pair.Key, out var nodeIndex))
                {
                    supply[nodeIndex] = pair.Value;
                    if (pair.Value > Epsilon)
                    {
                        supplyIndexes.Add(nodeIndex);
                    }
                }
            }

            foreach (var pair in context.Demand)
            {
                if (compiledContext.NodeIndexById.TryGetValue(pair.Key, out var nodeIndex))
                {
                    demand[nodeIndex] = pair.Value;
                    if (pair.Value > Epsilon)
                    {
                        demandIndexes.Add(nodeIndex);
                    }
                }
            }

            for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                var nodeId = compiledContext.NodeIdsByIndex[nodeIndex];
                if (context.ProfilesByNodeId.TryGetValue(nodeId, out var profile) && profile?.CanTransship == true)
                {
                    canTransship[nodeIndex] = true;
                }

                if (context.MeetingDemandEligibleNodeIds.Contains(nodeId))
                {
                    meetingEligible[nodeIndex] = true;
                }
            }

            return new IndexedRoutingTrafficContext
            {
                Source = context,
                TrafficTypeIndex = compiledContext.TrafficTypeIndexByName.TryGetValue(context.TrafficType, out var trafficTypeIndex) ? trafficTypeIndex : 0,
                Supply = supply,
                Demand = demand,
                CanTransship = canTransship,
                MeetingDemandEligibleNodeIndexes = meetingEligible,
                ActiveSupplyNodeIndexes = [.. supplyIndexes],
                ActiveDemandNodeIndexes = [.. demandIndexes]
            };
        }
    }

    private sealed class IndexedRouteSearchWorkspace
    {
        public PriorityQueue<int, double> Queue { get; } = new();
        public List<IndexedRouteSearchState> States { get; } = [];

        public IndexedRouteSearchWorkspace(int nodeCount)
        {
            Distance = new double[nodeCount];
            PreviousNode = new int[nodeCount];
            PreviousEdge = new int[nodeCount];
            VisitedGeneration = new int[nodeCount];
            SearchGeneration = 1;
        }

        public double[] Distance { get; }
        public int[] PreviousNode { get; }
        public int[] PreviousEdge { get; }
        public int[] VisitedGeneration { get; }
        public int SearchGeneration { get; private set; }

        public void Reset()
        {
            Queue.Clear();
            States.Clear();
            SearchGeneration++;
            if (SearchGeneration == int.MaxValue)
            {
                Array.Clear(VisitedGeneration);
                SearchGeneration = 1;
            }
        }

        public int AddState(int nodeIndex, int edgeIndex, int previousStateIndex, double baseTime, double baseCost, double score, int depth)
        {
            var id = States.Count;
            States.Add(new IndexedRouteSearchState(nodeIndex, edgeIndex, previousStateIndex, baseTime, baseCost, score, depth));
            return id;
        }

        public bool PathContainsNode(int stateId, int nodeIndex)
        {
            var current = stateId;
            while (current >= 0)
            {
                if (States[current].NodeIndex == nodeIndex)
                {
                    return true;
                }

                current = States[current].PreviousStateIndex;
            }

            return false;
        }

        public IndexedCachedRoute ToCachedRoute(int stateId)
        {
            var depth = States[stateId].Depth;
            var nodeIndexes = new int[depth];
            var edgeIndexes = new int[Math.Max(0, depth - 1)];
            var edgeTimes = new double[edgeIndexes.Length];
            var edgeCosts = new double[edgeIndexes.Length];
            var current = stateId;
            for (var nodeOffset = depth - 1; nodeOffset >= 0; nodeOffset--)
            {
                var state = States[current];
                nodeIndexes[nodeOffset] = state.NodeIndex;
                if (nodeOffset > 0)
                {
                    edgeIndexes[nodeOffset - 1] = state.EdgeIndex;
                }

                current = state.PreviousStateIndex;
            }

            // The cached route stores immutable topology. Times/costs are filled by the caller from states.
            current = stateId;
            for (var edgeOffset = edgeIndexes.Length - 1; edgeOffset >= 0; edgeOffset--)
            {
                var state = States[current];
                edgeTimes[edgeOffset] = state.BaseTime - (state.PreviousStateIndex >= 0 ? States[state.PreviousStateIndex].BaseTime : 0d);
                edgeCosts[edgeOffset] = state.BaseCost - (state.PreviousStateIndex >= 0 ? States[state.PreviousStateIndex].BaseCost : 0d);
                current = state.PreviousStateIndex;
            }

            return new IndexedCachedRoute(nodeIndexes, edgeIndexes, edgeTimes, edgeCosts, States[stateId].BaseTime, States[stateId].BaseCost);
        }
    }

    private sealed record IndexedFlowProposal(
        IndexedRoutingTrafficContext Context,
        int ProducerNodeIndex,
        int ConsumerNodeIndex,
        double Quantity,
        int[] PathNodeIndexes,
        int[] PathEdgeIndexes,
        int[] PathTranshipmentNodeIndexes,
        double BaseTime,
        double BaseCost,
        double EffectiveTime,
        double EffectiveCost,
        double Score,
        double Priority);

    private sealed record IndexedCommittedFlow(IndexedFlowProposal Proposal, double Quantity)
    {
        public IndexedRoutingTrafficContext Context => Proposal.Context;
        public int ProducerNodeIndex => Proposal.ProducerNodeIndex;
        public int ConsumerNodeIndex => Proposal.ConsumerNodeIndex;
        public int[] PathNodeIndexes => Proposal.PathNodeIndexes;
        public int[] PathEdgeIndexes => Proposal.PathEdgeIndexes;
        public int[] PathTranshipmentNodeIndexes => Proposal.PathTranshipmentNodeIndexes;
        public double BaseTime => Proposal.BaseTime;
        public double BaseCost => Proposal.BaseCost;
        public double EffectiveTime => Proposal.EffectiveTime;
        public double EffectiveCost => Proposal.EffectiveCost;
        public double Score => Proposal.Score;
        public double Priority => Proposal.Priority;
    }

    private sealed record IndexedRouteCandidate(
        IndexedRoutingTrafficContext Context,
        int ProducerNodeIndex,
        int ConsumerNodeIndex,
        int[] PathNodeIndexes,
        int[] PathEdgeIndexes,
        double BaseTime,
        double BaseCost,
        double EffectiveTime,
        double EffectiveCost,
        double Score,
        string PathKey,
        double Probability);

    private readonly record struct IndexedRouteSearchState(
        int NodeIndex,
        int EdgeIndex,
        int PreviousStateIndex,
        double BaseTime,
        double BaseCost,
        double Score,
        int Depth);

    private sealed record IndexedCachedRoute(
        int[] PathNodeIndexes,
        int[] PathEdgeIndexes,
        double[] EdgeTimes,
        double[] EdgeCosts,
        double BaseTime,
        double BaseCost);

    private readonly record struct IndexedRouteCacheKey(
        long Revision,
        int EffectivePeriod,
        int ActiveTimelineEventSignature,
        int TrafficTypeIndex,
        int ProducerNodeIndex,
        int ConsumerNodeIndex,
        RoutingPreference RoutingPreference,
        int MaxCandidateRoutes,
        bool InternalizeCongestion,
        bool AdaptiveRoutingEnabled);
}

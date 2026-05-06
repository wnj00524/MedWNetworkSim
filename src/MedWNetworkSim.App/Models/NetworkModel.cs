using System.Collections.Generic;
using System.Text.Json.Serialization;
using MedWNetworkSim.App.Agents;

namespace MedWNetworkSim.App.Models;

/// <summary>
/// Represents a complete persisted network, including traffic-type definitions, nodes, and edges.
/// </summary>
public sealed class NetworkModel
{
    /// <summary>
    /// Gets or sets the display name of the network.
    /// </summary>
    public string Name { get; set; } = "Untitled Network";

    /// <summary>
    /// Gets or sets an optional free-text description shown in the editor.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional timeline loop length. Values below 1 disable looping.
    /// </summary>
    public int? TimelineLoopLength { get; set; }

    /// <summary>
    /// Gets or sets the allocation mode used when creating new traffic types.
    /// </summary>
    public AllocationMode DefaultAllocationMode { get; set; } = AllocationMode.GreedyBestRoute;

    /// <summary>
    /// Gets or sets the deterministic seed used for stochastic route choice.
    /// </summary>
    public int SimulationSeed { get; set; } = 12345;

    /// <summary>
    /// Gets or sets a value indicating whether simulations should use facility catchments.
    /// When enabled, demand is assigned to the nearest reachable facility before routing.
    /// </summary>
    public bool FacilityModeEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether graph layout should follow geographic map coordinates for geo-anchored nodes.
    /// </summary>
    public bool LockLayoutToMap { get; set; }

    /// <summary>
    /// Gets or sets the optional maximum travel-time budget for facility catchments.
    /// Null or non-positive values mean unlimited reachability.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FacilityCoverageThreshold { get; set; }


    /// <summary>
    /// Gets or sets the declared simulation layers used to process different network concerns.
    /// </summary>
    public List<NetworkLayerModel> Layers { get; set; } = [];

    /// <summary>
    /// Gets or sets the saved scenario definitions available for what-if analysis runs.
    /// </summary>
    public List<ScenarioDefinitionModel> ScenarioDefinitions { get; set; } = [];

    /// <summary>
    /// Gets or sets deterministic policy rules that can block traffic or adjust route costs/capacity.
    /// </summary>
    public List<PolicyRuleModel> PolicyRules { get; set; } = [];

    /// <summary>
    /// Gets or sets the declared traffic types that can move through the network.
    /// </summary>
    public List<TrafficTypeDefinition> TrafficTypes { get; set; } = [];

    /// <summary>
    /// Gets or sets optional timeline events that temporarily adjust simulation inputs.
    /// </summary>
    public List<TimelineEventModel> TimelineEvents { get; set; } = [];

    /// <summary>
    /// Gets or sets the default edge traffic permission applied to new edges for each traffic type.
    /// </summary>
    public List<EdgeTrafficPermissionRule> EdgeTrafficPermissionDefaults { get; set; } = [];

    /// <summary>
    /// Gets or sets actor-owned route tax rules applied during economic settlement.
    /// </summary>
    public List<RouteTaxRule> RouteTaxRules { get; set; } = [];

    /// <summary>
    /// Gets or sets embedded child networks that can be placed as composite nodes in this network.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SubnetworkDefinition>? Subnetworks { get; set; }

    /// <summary>
    /// Gets or sets the nodes that can produce, consume, or transship traffic.
    /// </summary>
    public List<NodeModel> Nodes { get; set; } = [];

    /// <summary>
    /// Gets or sets the edges that connect nodes and define time, cost, and optional capacity.
    /// </summary>
    public List<EdgeModel> Edges { get; set; } = [];

    /// <summary>
    /// Gets or sets autonomous actor configuration persisted with the network.
    /// </summary>
    public List<SimulationActorState> Actors { get; set; } = [];

    /// <summary>
    /// Gets or sets optional persisted actor decision history.
    /// </summary>
    public List<SimulationActorDecision> ActorDecisions { get; set; } = [];

    /// <summary>
    /// Gets or sets optional persisted actor metrics by tick.
    /// </summary>
    public List<SimulationActorMetrics> ActorMetrics { get; set; } = [];

    /// <summary>
    /// Gets or sets optional persisted actor action outcomes.
    /// </summary>
    public List<SimulationActorActionOutcome> ActorActionOutcomes { get; set; } = [];

    /// <summary>
    /// Gets or sets optional persisted detailed actor action logs.
    /// </summary>
    public List<AgentActionLogEntry> AgentActionLogs { get; set; } = [];

    /// <summary>
    /// Gets or sets the network snapshot captured before agent actions first mutated the network.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NetworkModel? PreAgentMutationNetwork { get; set; }

    /// <summary>
    /// Gets or sets the persisted actor simulation tick.
    /// </summary>
    public int ActorTick { get; set; }
}

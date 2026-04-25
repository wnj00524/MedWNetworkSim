using System.Collections.Generic;
using System.Text.Json.Serialization;

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
    /// Gets or sets the optional maximum travel-time budget for facility catchments.
    /// Null or non-positive values mean unlimited reachability.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FacilityCoverageThreshold { get; set; }

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
}

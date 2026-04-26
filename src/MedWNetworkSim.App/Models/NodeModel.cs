using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MedWNetworkSim.App.Models;

/// <summary>
/// Represents a node in the network graph.
/// </summary>
public sealed class NodeModel
{
    /// <summary>
    /// Gets or sets the unique identifier used by edges to reference this node.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user-facing name shown on the canvas and in editors.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the visual shape used to represent this node on the canvas.
    /// </summary>
    public NodeVisualShape Shape { get; set; } = NodeVisualShape.Square;

    /// <summary>
    /// Gets or sets whether this node is an ordinary graph node or a parent-side composite subnetwork instance.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public NodeKind NodeKind { get; set; }

    /// <summary>
    /// Gets or sets the embedded subnetwork id represented by this composite node.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReferencedSubnetworkId { get; set; }

    /// <summary>
    /// Gets or sets whether this child-network node can be used as a parent-facing interface.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsExternalInterface { get; set; }

    /// <summary>
    /// Gets or sets an optional display name for an exposed child-network interface.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InterfaceName { get; set; }

    /// <summary>
    /// Gets or sets the layer identifier this node belongs to.
    /// </summary>
    public Guid LayerId { get; set; }

    /// <summary>
    /// Gets or sets the optional horizontal canvas position of the node center.
    /// </summary>
    public double? X { get; set; }

    /// <summary>
    /// Gets or sets the optional vertical canvas position of the node center.
    /// </summary>
    public double? Y { get; set; }

    /// <summary>
    /// Gets or sets the optional shared capacity limit for using this node as an intermediate transhipment point.
    /// </summary>
    public double? TranshipmentCapacity { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this node acts as a facility origin in facility mode.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsFacility { get; set; }

    /// <summary>
    /// Gets or sets the optional total demand this facility can serve across its catchment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FacilityCapacity { get; set; }

    /// <summary>
    /// Gets or sets the optional worldbuilder place category for this node.
    /// </summary>
    public string? PlaceType { get; set; }

    /// <summary>
    /// Gets or sets optional lore text for this place.
    /// </summary>
    public string? LoreDescription { get; set; }

    /// <summary>
    /// Gets or sets the optional actor currently controlling this place.
    /// </summary>
    public string? ControllingActor { get; set; }

    /// <summary>
    /// Gets or sets optional worldbuilder tags for this place.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the optional source template identifier for this place.
    /// </summary>
    public string? TemplateId { get; set; }

    /// <summary>
    /// Gets or sets the per-traffic roles and quantities for this node.
    /// </summary>
    public List<NodeTrafficProfile> TrafficProfiles { get; set; } = [];

    [JsonIgnore]
    public bool IsCompositeSubnetwork => NodeKind == NodeKind.CompositeSubnetwork;
}

using System;
using System.Text.Json.Serialization;

namespace MedWNetworkSim.App.Models;

/// <summary>
/// Represents a connection between two nodes, including routing attributes and optional capacity.
/// </summary>
public sealed class EdgeModel
{
    /// <summary>
    /// Gets or sets the unique identifier for the edge.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source node identifier.
    /// </summary>
    public string FromNodeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the exposed child interface used when the source endpoint is a composite subnetwork node.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FromInterfaceNodeId { get; set; }

    /// <summary>
    /// Gets or sets the destination node identifier.
    /// </summary>
    public string ToNodeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the exposed child interface used when the target endpoint is a composite subnetwork node.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToInterfaceNodeId { get; set; }

    /// <summary>
    /// Gets or sets the layer identifier this edge belongs to.
    /// </summary>
    public Guid LayerId { get; set; }

    /// <summary>
    /// Gets or sets the time cost used when traffic prioritizes speed or total cost.
    /// </summary>
    public double Time { get; set; }

    /// <summary>
    /// Gets or sets the monetary or general routing cost used when traffic prioritizes cost or total cost.
    /// </summary>
    public double Cost { get; set; }

    /// <summary>
    /// Gets or sets the optional shared capacity of the edge. Null means unlimited capacity.
    /// </summary>
    public double? Capacity { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether traffic can travel in both directions on this edge.
    /// </summary>
    public bool IsBidirectional { get; set; } = true;

    /// <summary>
    /// Gets or sets the optional worldbuilder route category for this edge.
    /// </summary>
    public string? RouteType { get; set; }

    /// <summary>
    /// Gets or sets optional notes about who can use this route.
    /// </summary>
    public string? AccessNotes { get; set; }

    /// <summary>
    /// Gets or sets optional notes about seasonal hazards on this route.
    /// </summary>
    public string? SeasonalRisk { get; set; }

    /// <summary>
    /// Gets or sets optional toll or fee notes for this route.
    /// </summary>
    public string? TollNotes { get; set; }

    /// <summary>
    /// Gets or sets optional security notes for this route.
    /// </summary>
    public string? SecurityNotes { get; set; }

    /// <summary>
    /// Gets or sets optional traffic-specific permission overrides for this edge.
    /// </summary>
    public List<EdgeTrafficPermissionRule> TrafficPermissions { get; set; } = [];
}

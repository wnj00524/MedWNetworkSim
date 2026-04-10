using System.Collections.Generic;

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
    /// Gets or sets the declared traffic types that can move through the network.
    /// </summary>
    public List<TrafficTypeDefinition> TrafficTypes { get; set; } = [];

    /// <summary>
    /// Gets or sets the nodes that can produce, consume, or transship traffic.
    /// </summary>
    public List<NodeModel> Nodes { get; set; } = [];

    /// <summary>
    /// Gets or sets the edges that connect nodes and define time, cost, and optional capacity.
    /// </summary>
    public List<EdgeModel> Edges { get; set; } = [];
}

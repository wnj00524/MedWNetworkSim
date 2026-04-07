using System.Collections.Generic;

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
    /// Gets or sets the optional horizontal canvas position of the node center.
    /// </summary>
    public double? X { get; set; }

    /// <summary>
    /// Gets or sets the optional vertical canvas position of the node center.
    /// </summary>
    public double? Y { get; set; }

    /// <summary>
    /// Gets or sets the per-traffic roles and quantities for this node.
    /// </summary>
    public List<NodeTrafficProfile> TrafficProfiles { get; set; } = [];
}

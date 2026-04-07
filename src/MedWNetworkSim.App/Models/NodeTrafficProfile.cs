namespace MedWNetworkSim.App.Models;

/// <summary>
/// Describes how a single node participates in one traffic type.
/// </summary>
public sealed class NodeTrafficProfile
{
    /// <summary>
    /// Gets or sets the traffic type this profile applies to.
    /// </summary>
    public string TrafficType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the amount of this traffic type produced at the node.
    /// </summary>
    public double Production { get; set; }

    /// <summary>
    /// Gets or sets the amount of this traffic type consumed at the node.
    /// </summary>
    public double Consumption { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this node may be used as an intermediate transhipment point.
    /// </summary>
    public bool CanTransship { get; set; }
}

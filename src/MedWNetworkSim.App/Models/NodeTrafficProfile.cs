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

    /// <summary>
    /// Gets or sets the first period in which production is active. Null means active from the beginning.
    /// </summary>
    public int? ProductionStartPeriod { get; set; }

    /// <summary>
    /// Gets or sets the last period in which production is active. Null means no upper bound.
    /// </summary>
    public int? ProductionEndPeriod { get; set; }

    /// <summary>
    /// Gets or sets the first period in which consumption is active. Null means active from the beginning.
    /// </summary>
    public int? ConsumptionStartPeriod { get; set; }

    /// <summary>
    /// Gets or sets the last period in which consumption is active. Null means no upper bound.
    /// </summary>
    public int? ConsumptionEndPeriod { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this traffic profile stores received traffic in inventory instead of destroying it.
    /// </summary>
    public bool IsStore { get; set; }

    /// <summary>
    /// Gets or sets the optional maximum amount of this traffic type that can be stored at the node.
    /// </summary>
    public double? StoreCapacity { get; set; }
}

namespace MedWNetworkSim.App.Services;

/// <summary>
/// Summarizes the delivered movement cost seen by a consumer node for one traffic type.
/// </summary>
public sealed class ConsumerCostSummary
{
    /// <summary>
    /// Gets the traffic type summarized by this row.
    /// </summary>
    public string TrafficType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the consumer node identifier.
    /// </summary>
    public string ConsumerNodeId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the consumer node name.
    /// </summary>
    public string ConsumerName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the quantity satisfied by same-node local production.
    /// </summary>
    public double LocalQuantity { get; init; }

    /// <summary>
    /// Gets the average movement cost per unit for locally satisfied demand.
    /// </summary>
    public double LocalUnitCost { get; init; }

    /// <summary>
    /// Gets the quantity satisfied by imported flow from other nodes.
    /// </summary>
    public double ImportedQuantity { get; init; }

    /// <summary>
    /// Gets the average movement cost per unit for imported flow.
    /// </summary>
    public double ImportedUnitCost { get; init; }

    /// <summary>
    /// Gets the average movement cost per unit across local and imported supply together.
    /// </summary>
    public double BlendedUnitCost { get; init; }

    /// <summary>
    /// Gets the total movement cost accumulated by all delivered supply in this summary row.
    /// </summary>
    public double TotalMovementCost { get; init; }
}

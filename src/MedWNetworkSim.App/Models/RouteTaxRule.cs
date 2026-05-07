namespace MedWNetworkSim.App.Models;

/// <summary>
/// Stores an actor-owned tax rule for traffic moving over a specific route edge.
/// </summary>
public sealed class RouteTaxRule
{
    /// <summary>
    /// Gets or sets the edge id.
    /// </summary>
    public string EdgeId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the traffic type.
    /// </summary>

    public string TrafficType { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the tax rate.
    /// </summary>

    public double TaxRate { get; set; }
    /// <summary>
    /// Gets or sets the tax authority actor id.
    /// </summary>

    public string TaxAuthorityActorId { get; set; } = string.Empty;
    /// <summary>
    /// Gets a value indicating whether is active is enabled or active.
    /// </summary>

    public bool IsActive { get; set; } = true;
}

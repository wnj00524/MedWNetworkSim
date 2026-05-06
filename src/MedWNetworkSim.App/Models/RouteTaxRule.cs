namespace MedWNetworkSim.App.Models;

/// <summary>
/// Stores an actor-owned tax rule for traffic moving over a specific route edge.
/// </summary>
public sealed class RouteTaxRule
{
    public string EdgeId { get; set; } = string.Empty;

    public string TrafficType { get; set; } = string.Empty;

    public double TaxRate { get; set; }

    public string TaxAuthorityActorId { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

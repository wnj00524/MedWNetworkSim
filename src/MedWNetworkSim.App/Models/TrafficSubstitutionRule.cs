namespace MedWNetworkSim.App.Models;
/// <summary>
/// Represents the traffic substitution rule component.
/// </summary>

public sealed class TrafficSubstitutionRule
{
    /// <summary>
    /// Gets or sets the required traffic type id or name.
    /// </summary>
    public string RequiredTrafficTypeIdOrName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the substitute traffic type id or name.
    /// </summary>

    public string SubstituteTrafficTypeIdOrName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the conversion ratio.
    /// </summary>

    public double ConversionRatio { get; set; } = 1.0;
    /// <summary>
    /// Gets or sets the penalty cost.
    /// </summary>

    public double PenaltyCost { get; set; }
}

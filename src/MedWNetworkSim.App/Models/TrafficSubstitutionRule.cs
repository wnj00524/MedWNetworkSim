namespace MedWNetworkSim.App.Models;

public sealed class TrafficSubstitutionRule
{
    public string RequiredTrafficTypeIdOrName { get; set; } = string.Empty;

    public string SubstituteTrafficTypeIdOrName { get; set; } = string.Empty;

    public double ConversionRatio { get; set; } = 1.0;

    public double PenaltyCost { get; set; }
}

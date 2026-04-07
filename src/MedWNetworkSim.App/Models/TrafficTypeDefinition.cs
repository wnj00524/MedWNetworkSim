namespace MedWNetworkSim.App.Models;

public sealed class TrafficTypeDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public RoutingPreference RoutingPreference { get; set; } = RoutingPreference.TotalCost;
}

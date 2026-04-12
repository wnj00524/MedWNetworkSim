using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Templates;

public sealed record PlaceTemplate(
    string Id,
    string DisplayName,
    string NamePrefix,
    string PlaceType,
    string LoreDescription,
    double? TranshipmentCapacity,
    IReadOnlyList<string> Tags,
    IReadOnlyList<PlaceTemplateTrafficProfile> TrafficProfiles);

public sealed record PlaceTemplateTrafficProfile(
    string TrafficType,
    double Production = 0d,
    double Consumption = 0d,
    bool CanTransship = false,
    bool IsStore = false,
    double? StoreCapacity = null)
{
    public NodeTrafficProfile ToNodeTrafficProfile()
    {
        return new NodeTrafficProfile
        {
            TrafficType = TrafficType,
            Production = Production,
            Consumption = Consumption,
            CanTransship = CanTransship,
            IsStore = IsStore,
            StoreCapacity = StoreCapacity
        };
    }
}

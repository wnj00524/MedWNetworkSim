namespace MedWNetworkSim.App.Presets;

public static class DemographicDemandPresetCatalog
{
    public static IReadOnlyList<DemographicDemandPreset> Presets { get; } =
    [
        new(
            "hamlet",
            "Hamlet",
            "A small rural population with basic food and household needs.",
            [
                new("Grain", 10d, StoreCapacity: 18d),
                new("Tools", 2d),
                new("Salt", 1d)
            ]),
        new(
            "village",
            "Village",
            "A larger rural settlement with food, market, and maintenance demand.",
            [
                new("Grain", 20d, StoreCapacity: 35d, CanTransship: true),
                new("Flour", 8d, StoreCapacity: 16d),
                new("Tools", 4d),
                new("Salt", 2d),
                new("Ale", 4d)
            ]),
        new(
            "town-ward",
            "Town Ward",
            "A dense urban ward with broad household and workshop demand.",
            [
                new("Grain", 35d, StoreCapacity: 55d, CanTransship: true),
                new("Flour", 18d, StoreCapacity: 30d),
                new("Ale", 12d),
                new("Cloth", 10d),
                new("Tools", 8d),
                new("Charcoal", 6d)
            ]),
        new(
            "garrison",
            "Garrison",
            "A military household with food, arms, maintenance, and medical demand.",
            [
                new("Grain", 24d, StoreCapacity: 60d),
                new("Arms", 10d, StoreCapacity: 24d, ConsumerPremiumPerUnit: 0.8d),
                new("Tools", 8d, StoreCapacity: 18d),
                new("Remedies", 6d, ConsumerPremiumPerUnit: 0.6d),
                new("Ale", 6d)
            ]),
        new(
            "monastic-house",
            "Monastic House",
            "A religious household with steady food needs and specialist supplies.",
            [
                new("Grain", 14d, StoreCapacity: 32d),
                new("Flour", 6d, StoreCapacity: 14d),
                new("Ale", 4d),
                new("Herbs", 4d, StoreCapacity: 10d),
                new("Manuscripts", 2d, ConsumerPremiumPerUnit: 0.5d)
            ])
    ];
}

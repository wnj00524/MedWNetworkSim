namespace MedWNetworkSim.App.Templates;

public static class PlaceTemplateCatalog
{
    public static IReadOnlyList<PlaceTemplate> Templates { get; } =
    [
        new(
            "settlement",
            "Settlement",
            "Settlement",
            "Settlement",
            "A population center with everyday needs and local exchange.",
            20d,
            ["settlement", "community", "hub"],
            [
                new("Food", Consumption: 18d, CanTransship: true, IsStore: true, StoreCapacity: 35d),
                new("Tools", Consumption: 4d),
                new("Local Goods", Production: 6d, CanTransship: true)
            ]),
        new(
            "resource-site",
            "Resource Site",
            "Resource Site",
            "Resource Site",
            "A place that supplies one or more raw materials into the wider system.",
            null,
            ["resource", "source"],
            [
                new("Raw Material", Production: 24d),
                new("Tools", Consumption: 3d)
            ]),
        new(
            "transit-hub",
            "Transit Hub",
            "Transit Hub",
            "Transit Hub",
            "A transfer point where routes meet and flows can be redirected.",
            60d,
            ["relay", "hub", "infrastructure"],
            [
                new("Food", CanTransship: true, IsStore: true, StoreCapacity: 40d),
                new("Tools", CanTransship: true, IsStore: true, StoreCapacity: 20d),
                new("Raw Material", CanTransship: true)
            ]),
        new(
            "workshop",
            "Workshop",
            "Workshop",
            "Workshop",
            "A production site that turns inputs into useful local output.",
            12d,
            ["production", "craft", "processing"],
            [
                new("Raw Material", Consumption: 12d, CanTransship: true),
                new("Tools", Production: 10d, IsStore: true, StoreCapacity: 18d)
            ]),
        new(
            "storage-depot",
            "Storage Depot",
            "Depot",
            "Storage Depot",
            "A stockpile location that buffers supply before it moves onward.",
            35d,
            ["stockpile", "logistics", "reserve"],
            [
                new("Food", CanTransship: true, IsStore: true, StoreCapacity: 80d),
                new("Tools", CanTransship: true, IsStore: true, StoreCapacity: 30d)
            ]),
        new(
            "farm",
            "Farm",
            "Farmstead",
            "Farm",
            "Fields and barns that keep nearby settlements fed.",
            null,
            ["rural", "food", "estate"],
            [
                new("Grain", Production: 30d),
                new("Livestock", Production: 8d),
                new("Tools", Consumption: 3d)
            ]),
        new(
            "village",
            "Village",
            "Village",
            "Village",
            "A small settlement that turns local supplies into everyday demand.",
            12d,
            ["settlement", "rural", "local-market"],
            [
                new("Grain", Consumption: 18d, CanTransship: true, IsStore: true, StoreCapacity: 30d),
                new("Tools", Consumption: 4d)
            ]),
        new(
            "market-town",
            "Market Town",
            "Market Town",
            "Market Town",
            "A busy exchange point where regional goods gather before moving onward.",
            45d,
            ["settlement", "trade", "hub"],
            [
                new("Grain", Consumption: 25d, CanTransship: true, IsStore: true, StoreCapacity: 70d),
                new("Tools", Consumption: 10d, CanTransship: true, IsStore: true, StoreCapacity: 25d),
                new("Cloth", Production: 12d, Consumption: 6d, CanTransship: true)
            ]),
        new(
            "monastery",
            "Monastery",
            "Monastery",
            "Monastery",
            "A secluded religious house with steady needs and specialist output.",
            null,
            ["religious", "remote", "scholarly"],
            [
                new("Grain", Consumption: 10d),
                new("Manuscripts", Production: 4d),
                new("Herbs", Production: 6d, IsStore: true, StoreCapacity: 12d)
            ]),
        new(
            "castle",
            "Castle",
            "Castle",
            "Castle",
            "A fortified seat that consumes supplies and anchors control of nearby routes.",
            25d,
            ["fortified", "military", "authority"],
            [
                new("Grain", Consumption: 22d, CanTransship: true, IsStore: true, StoreCapacity: 60d),
                new("Arms", Consumption: 8d, IsStore: true, StoreCapacity: 20d),
                new("Stone", Consumption: 6d)
            ]),
        new(
            "wharf",
            "Wharf",
            "Wharf",
            "Wharf",
            "A waterside loading point that turns local roads and water routes into one network.",
            60d,
            ["waterway", "trade", "hub"],
            [
                new("Fish", Production: 14d, CanTransship: true, IsStore: true, StoreCapacity: 24d),
                new("Timber", Consumption: 8d, CanTransship: true),
                new("Grain", CanTransship: true, IsStore: true, StoreCapacity: 45d)
            ]),
        new(
            "mill",
            "Mill",
            "Mill",
            "Mill",
            "A processing site that converts grain into flour for nearby needs.",
            10d,
            ["production", "water", "food"],
            [
                new("Grain", Consumption: 20d, CanTransship: true),
                new("Flour", Production: 16d, IsStore: true, StoreCapacity: 25d)
            ]),
        new(
            "smithy",
            "Smithy",
            "Smithy",
            "Smithy",
            "A workshop that turns raw supply into tools and repair capacity.",
            null,
            ["craft", "production", "tools"],
            [
                new("Iron", Consumption: 12d),
                new("Charcoal", Consumption: 6d),
                new("Tools", Production: 10d, IsStore: true, StoreCapacity: 18d)
            ])
    ];
}

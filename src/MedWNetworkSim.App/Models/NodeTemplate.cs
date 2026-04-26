namespace MedWNetworkSim.App.Models;

public sealed class NodeTemplate
{
    public string Name { get; set; } = string.Empty;

    public string NodeType { get; set; } = string.Empty;

    public Dictionary<string, double> DefaultProduction { get; set; } = [];

    public Dictionary<string, double> DefaultConsumption { get; set; } = [];

    public Dictionary<string, object> Properties { get; set; } = [];

    public static IReadOnlyList<NodeTemplate> BuiltIn { get; } =
    [
        new() { Name = "Factory", NodeType = "Factory" },
        new() { Name = "Warehouse", NodeType = "Warehouse" },
        new() { Name = "Port", NodeType = "Port" },
        new() { Name = "Consumer", NodeType = "Consumer" },
        new() { Name = "Junction", NodeType = "Junction" }
    ];
}

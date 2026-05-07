namespace MedWNetworkSim.App.Models;
/// <summary>
/// Represents the node template component.
/// </summary>

public sealed class NodeTemplate
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the node type.
    /// </summary>

    public string NodeType { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the default production.
    /// </summary>

    public Dictionary<string, double> DefaultProduction { get; set; } = [];
    /// <summary>
    /// Gets or sets the default consumption.
    /// </summary>

    public Dictionary<string, double> DefaultConsumption { get; set; } = [];
    /// <summary>
    /// Gets or sets the properties.
    /// </summary>

    public Dictionary<string, object> Properties { get; set; } = [];
    /// <summary>
    /// Gets the collection of built in associated with this entity.
    /// </summary>

    public static IReadOnlyList<NodeTemplate> BuiltIn { get; } =
    [
        new() { Name = "Factory", NodeType = "Factory" },
        new() { Name = "Warehouse", NodeType = "Warehouse" },
        new() { Name = "Port", NodeType = "Port" },
        new() { Name = "Consumer", NodeType = "Consumer" },
        new() { Name = "Junction", NodeType = "Junction" }
    ];
}

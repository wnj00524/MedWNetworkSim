using System.Text.Json.Serialization;

namespace MedWNetworkSim.App.Models;

/// <summary>
/// Declares which child interface node a parent composite node exposes as a connection point.
/// </summary>
public sealed class SubnetworkInterfaceBinding
{
    /// <summary>
    /// Gets or sets the parent composite node id.
    /// </summary>
    public string ParentCompositeNodeId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the child subnetwork id.
    /// </summary>

    public string ChildSubnetworkId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the child interface node id.
    /// </summary>

    public string ChildInterfaceNodeId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the display name.
    /// </summary>

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }
    /// <summary>
    /// Gets the collection of allowed traffic types associated with this entity.
    /// </summary>

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AllowedTrafficTypes { get; set; }
    /// <summary>
    /// Gets or sets the direction hint.
    /// </summary>

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DirectionHint { get; set; }
}

using System.Text.Json.Serialization;

namespace MedWNetworkSim.App.Models;

/// <summary>
/// Embeds a reusable child network that can be placed on a parent canvas as a composite node.
/// </summary>
public sealed class SubnetworkDefinition
{
    /// <summary>
    /// Gets or sets the unique identifier for this instance.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the display name.
    /// </summary>

    public string DisplayName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the network.
    /// </summary>

    public NetworkModel Network { get; set; } = new();
    /// <summary>
    /// Gets the collection of interface bindings associated with this entity.
    /// </summary>

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SubnetworkInterfaceBinding>? InterfaceBindings { get; set; }
}

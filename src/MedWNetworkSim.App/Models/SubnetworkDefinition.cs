using System.Text.Json.Serialization;

namespace MedWNetworkSim.App.Models;

/// <summary>
/// Embeds a reusable child network that can be placed on a parent canvas as a composite node.
/// </summary>
public sealed class SubnetworkDefinition
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public NetworkModel Network { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SubnetworkInterfaceBinding>? InterfaceBindings { get; set; }
}

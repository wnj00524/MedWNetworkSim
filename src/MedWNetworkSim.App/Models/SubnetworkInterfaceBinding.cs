using System.Text.Json.Serialization;

namespace MedWNetworkSim.App.Models;

/// <summary>
/// Declares which child interface node a parent composite node exposes as a connection point.
/// </summary>
public sealed class SubnetworkInterfaceBinding
{
    public string ParentCompositeNodeId { get; set; } = string.Empty;

    public string ChildSubnetworkId { get; set; } = string.Empty;

    public string ChildInterfaceNodeId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AllowedTrafficTypes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DirectionHint { get; set; }
}

using System.Text.Json.Serialization;

namespace MedWNetworkSim.App.Models;

/// <summary>
/// Stores a traffic-specific permission rule for an edge, either as a network default or an edge-level override.
/// </summary>
public sealed class EdgeTrafficPermissionRule
{
    /// <summary>
    /// Gets or sets the traffic type affected by this rule.
    /// </summary>
    public string TrafficType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the rule permits, blocks, or limits the traffic type.
    /// </summary>
    public EdgeTrafficPermissionMode Mode { get; set; } = EdgeTrafficPermissionMode.Permitted;

    /// <summary>
    /// Gets or sets how the limit is measured when <see cref="Mode"/> is <see cref="EdgeTrafficPermissionMode.Limited"/>.
    /// </summary>
    public EdgeTrafficLimitKind LimitKind { get; set; } = EdgeTrafficLimitKind.AbsoluteUnits;

    /// <summary>
    /// Gets or sets the numeric limit value when <see cref="Mode"/> is <see cref="EdgeTrafficPermissionMode.Limited"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LimitValue { get; set; }

    /// <summary>
    /// Gets or sets whether this edge-level rule overrides the network default.
    /// Network defaults should leave this true, while edge rows can set it false to fall back to the network.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

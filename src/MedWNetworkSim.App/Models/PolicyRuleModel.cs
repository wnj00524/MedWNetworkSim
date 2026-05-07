namespace MedWNetworkSim.App.Models;
/// <summary>
/// Specifies the policy rule effect.
/// </summary>

public enum PolicyRuleEffect
{
    BlockTraffic,
    AllowOnlyTraffic,
    CostMultiplier,
    CapacityMultiplier
}
/// <summary>
/// Represents a data model for policy rule entities within the simulation.
/// </summary>

public sealed class PolicyRuleModel
{
    /// <summary>
    /// Gets or sets the unique identifier for this instance.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the name.
    /// </summary>

    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the effect.
    /// </summary>

    public PolicyRuleEffect Effect { get; set; }
    /// <summary>
    /// Gets or sets the traffic type id or name.
    /// </summary>

    public string? TrafficTypeIdOrName { get; set; }
    /// <summary>
    /// Gets or sets the target node id.
    /// </summary>

    public string? TargetNodeId { get; set; }
    /// <summary>
    /// Gets or sets the target edge id.
    /// </summary>

    public string? TargetEdgeId { get; set; }
    /// <summary>
    /// Gets or sets the value.
    /// </summary>

    public double Value { get; set; } = 1d;
    /// <summary>
    /// Gets a value indicating whether is enabled is enabled or active.
    /// </summary>

    public bool IsEnabled { get; set; } = true;
}

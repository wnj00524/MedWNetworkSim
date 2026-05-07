namespace MedWNetworkSim.App.Agents;
/// <summary>
/// Represents the simulation actor permission component.
/// </summary>

public sealed class SimulationActorPermission
{
    /// <summary>
    /// Gets or sets the action kind.
    /// </summary>
    public SimulationActorActionKind ActionKind { get; set; }
    /// <summary>
    /// Gets or sets the traffic type.
    /// </summary>
    public string? TrafficType { get; set; }
    /// <summary>
    /// Gets or sets the node id.
    /// </summary>
    public string? NodeId { get; set; }
    /// <summary>
    /// Gets or sets the edge id.
    /// </summary>
    public string? EdgeId { get; set; }
    /// <summary>
    /// Gets a value indicating whether is allowed is enabled or active.
    /// </summary>
    public bool IsAllowed { get; set; } = true;
}

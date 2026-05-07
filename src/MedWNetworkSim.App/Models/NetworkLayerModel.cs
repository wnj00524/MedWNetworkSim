namespace MedWNetworkSim.App.Models;

/// <summary>
/// Defines a distinct structural or operational layer within the overarching simulation graph.
/// Layers segregate the network into different modalities or operational partitions (e.g., Physical infrastructure, Service flow, Information flow)
/// and establish isolated spaces within which specific traffic types operate and interact according to defined sub-networks.
/// </summary>
public sealed class NetworkLayerModel
{
    /// <summary>
    /// Gets or sets the unique identifier for this instance.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the type.
    /// </summary>

    public NetworkLayerType Type { get; set; } = NetworkLayerType.Physical;
    /// <summary>
    /// Gets or sets the name.
    /// </summary>

    public string Name { get; set; } = "Physical";
    /// <summary>
    /// Gets or sets the order.
    /// </summary>

    public int Order { get; set; }
    /// <summary>
    /// Gets a value indicating whether is visible is enabled or active.
    /// </summary>

    public bool IsVisible { get; set; } = true;
    /// <summary>
    /// Gets a value indicating whether is locked is enabled or active.
    /// </summary>

    public bool IsLocked { get; set; }
}

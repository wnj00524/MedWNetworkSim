namespace MedWNetworkSim.App.Models;

/// <summary>
/// Describes one additive event overlay effect applied while a timeline event is active.
/// </summary>
public sealed class TimelineEventEffectModel
{
    /// <summary>
    /// Gets or sets the kind of input adjustment this effect applies.
    /// </summary>
    public TimelineEventEffectType EffectType { get; set; }

    /// <summary>
    /// Gets or sets the optional target node for production or consumption effects.
    /// </summary>
    public string? NodeId { get; set; }

    /// <summary>
    /// Gets or sets the optional target edge for route cost effects.
    /// </summary>
    public string? EdgeId { get; set; }

    /// <summary>
    /// Gets or sets the optional target traffic type for production or consumption effects.
    /// </summary>
    public string? TrafficType { get; set; }

    /// <summary>
    /// Gets or sets the finite non-negative multiplier applied to the target input.
    /// </summary>
    public double Multiplier { get; set; } = 1d;
}

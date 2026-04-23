namespace MedWNetworkSim.App.Models;

/// <summary>
/// Describes an additive timeline event whose effects are applied to simulation inputs for an active period window.
/// </summary>
public sealed class TimelineEventModel
{
    /// <summary>
    /// Gets or sets the optional stable identifier for this event.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the event.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the first active effective period. Null means active from the beginning.
    /// </summary>
    public int? StartPeriod { get; set; }

    /// <summary>
    /// Gets or sets the last active effective period. Null means no upper bound.
    /// </summary>
    public int? EndPeriod { get; set; }

    /// <summary>
    /// Gets or sets the input adjustments applied while the event is active.
    /// </summary>
    public List<TimelineEventEffectModel> Effects { get; set; } = [];
}

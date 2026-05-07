namespace MedWNetworkSim.App.Models;

/// <summary>
/// Configures route choice, congestion perception, and priority for a traffic type.
/// </summary>
public sealed class RouteChoiceSettings
{
    /// <summary>
    /// Gets or sets the max candidate routes.
    /// </summary>
    public int MaxCandidateRoutes { get; set; } = 3;
    /// <summary>
    /// Gets or sets the priority.
    /// </summary>

    public double Priority { get; set; } = 1d;
    /// <summary>
    /// Gets or sets the information accuracy.
    /// </summary>

    public double InformationAccuracy { get; set; } = 1d;
    /// <summary>
    /// Gets or sets the route diversity.
    /// </summary>

    public double RouteDiversity { get; set; } = 0.25d;
    /// <summary>
    /// Gets or sets the congestion sensitivity.
    /// </summary>

    public double CongestionSensitivity { get; set; } = 1d;
    /// <summary>
    /// Gets or sets the reroute threshold.
    /// </summary>

    public double RerouteThreshold { get; set; } = 0.1d;
    /// <summary>
    /// Gets or sets the stickiness.
    /// </summary>

    public double Stickiness { get; set; } = 0.3d;
    /// <summary>
    /// Gets or sets the iteration count.
    /// </summary>

    public int IterationCount { get; set; } = 4;
    /// <summary>
    /// Gets a value indicating whether internalize congestion is enabled or active.
    /// </summary>

    public bool InternalizeCongestion { get; set; } = true;
    /// <summary>
    /// Gets a value indicating whether adaptive routing enabled is enabled or active.
    /// </summary>

    public bool AdaptiveRoutingEnabled { get; set; }
}

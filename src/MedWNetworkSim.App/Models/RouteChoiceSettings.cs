namespace MedWNetworkSim.App.Models;

/// <summary>
/// Configures route choice, congestion perception, and priority for a traffic type.
/// </summary>
public sealed class RouteChoiceSettings
{
    public int MaxCandidateRoutes { get; set; } = 3;

    public double Priority { get; set; } = 1d;

    public double InformationAccuracy { get; set; } = 1d;

    public double RouteDiversity { get; set; } = 0.25d;

    public double CongestionSensitivity { get; set; } = 1d;

    public double RerouteThreshold { get; set; } = 0.1d;

    public double Stickiness { get; set; } = 0.3d;

    public int IterationCount { get; set; } = 4;

    public bool InternalizeCongestion { get; set; } = true;
}

namespace MedWNetworkSim.App.Models;

/// <summary>
/// Describes the first-pass simulation input a timeline event can adjust.
/// </summary>
public enum TimelineEventEffectType
{
    ProductionMultiplier,
    ConsumptionMultiplier,
    RouteCostMultiplier
}

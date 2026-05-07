using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.Models;
/// <summary>
/// Represents the simulation result component.
/// </summary>

public sealed class SimulationResult
{
    /// <summary>
    /// Gets the collection of outcomes associated with this entity.
    /// </summary>
    public IReadOnlyList<TrafficSimulationOutcome> Outcomes { get; set; } = [];

    public IReadOnlyList<TemporalNetworkSimulationEngine.TemporalSimulationStepResult> Steps { get; set; } = [];
    /// <summary>
    /// Gets or sets the total throughput.
    /// </summary>

    public double TotalThroughput => Outcomes.Sum(outcome => outcome.TotalDelivered);
    /// <summary>
    /// Gets or sets the total unmet demand.
    /// </summary>

    public double TotalUnmetDemand => Outcomes.Sum(outcome => outcome.UnmetDemand);
    /// <summary>
    /// Gets or sets the total cost.
    /// </summary>

    public double TotalCost => Outcomes.Sum(outcome => outcome.Allocations.Sum(allocation => allocation.TotalCost));
}

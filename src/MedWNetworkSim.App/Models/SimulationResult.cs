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

    public double TotalThroughput
    {
        get
        {
            double sum = 0;
            foreach (var outcome in Outcomes)
            {
                sum += outcome.TotalDelivered;
            }
            return sum;
        }
    }

    /// <summary>
    /// Gets or sets the total unmet demand.
    /// </summary>

    public double TotalUnmetDemand
    {
        get
        {
            double sum = 0;
            foreach (var outcome in Outcomes)
            {
                sum += outcome.UnmetDemand;
            }
            return sum;
        }
    }

    /// <summary>
    /// Gets or sets the total cost.
    /// </summary>

    public double TotalCost
    {
        get
        {
            double sum = 0;
            foreach (var outcome in Outcomes)
            {
                foreach (var allocation in outcome.Allocations)
                {
                    sum += allocation.TotalCost;
                }
            }
            return sum;
        }
    }
}

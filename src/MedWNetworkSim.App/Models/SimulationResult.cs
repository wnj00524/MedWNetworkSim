namespace MedWNetworkSim.App.Models;

public sealed class SimulationResult
{
    public IReadOnlyList<TrafficSimulationOutcome> Outcomes { get; set; } = [];

    public IReadOnlyList<TemporalNetworkSimulationEngine.TemporalSimulationStepResult> Steps { get; set; } = [];

    public double TotalThroughput => Outcomes.Sum(outcome => outcome.TotalDelivered);

    public double TotalUnmetDemand => Outcomes.Sum(outcome => outcome.UnmetDemand);

    public double TotalCost => Outcomes.Sum(outcome => outcome.Allocations.Sum(allocation => allocation.TotalCost));
}

using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public interface IScenarioValidationService
{
    IReadOnlyList<string> ValidateEvent(ScenarioEventModel evt);
}

public sealed class ScenarioValidationService : IScenarioValidationService
{
    public IReadOnlyList<string> ValidateEvent(ScenarioEventModel evt)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(evt.Name))
        {
            errors.Add("Enter a name for this scenario event.");
        }

        if (evt.Time < 0d)
        {
            errors.Add("Start time must be 0 or greater.");
        }

        if (evt.EndTime.HasValue && evt.EndTime.Value < evt.Time)
        {
            errors.Add("End time must be after start time.");
        }

        if (evt.Kind == ScenarioEventKind.NodeFailure && (evt.TargetKind != ScenarioTargetKind.Node || evt.TargetId is null))
        {
            errors.Add("Choose a node for this node failure.");
        }

        if ((evt.Kind == ScenarioEventKind.EdgeClosure || evt.Kind == ScenarioEventKind.EdgeCostChange) && (evt.TargetKind != ScenarioTargetKind.Edge || evt.TargetId is null))
        {
            errors.Add(evt.Kind == ScenarioEventKind.EdgeClosure
                ? "Choose an edge for this edge closure."
                : "Choose an edge for this edge cost change.");
        }

        if (evt.Kind == ScenarioEventKind.DemandSpike && (evt.TargetKind != ScenarioTargetKind.Node || evt.TargetId is null || string.IsNullOrWhiteSpace(evt.TrafficTypeIdOrName)))
        {
            errors.Add("Choose a node for this demand spike.");
        }

        return errors;
    }
}

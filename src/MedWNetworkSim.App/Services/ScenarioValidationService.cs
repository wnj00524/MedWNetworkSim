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
            errors.Add("Start time must be zero or greater.");
        }

        if (evt.EndTime.HasValue && evt.EndTime.Value < evt.Time)
        {
            errors.Add("End time must be after start time.");
        }

        if (evt.Kind == ScenarioEventKind.NodeFailure && (evt.TargetKind != ScenarioTargetKind.Node || evt.TargetId is null))
        {
            errors.Add("Choose a node for this event.");
        }

        if ((evt.Kind == ScenarioEventKind.EdgeClosure || evt.Kind == ScenarioEventKind.EdgeCostChange) && (evt.TargetKind != ScenarioTargetKind.Edge || evt.TargetId is null))
        {
            errors.Add(evt.Kind == ScenarioEventKind.EdgeClosure
                ? "Choose an edge for this event."
                : "Choose an edge for this event.");
        }

        if (evt.Kind == ScenarioEventKind.DemandSpike && (evt.TargetKind != ScenarioTargetKind.Node || evt.TargetId is null || string.IsNullOrWhiteSpace(evt.TrafficTypeIdOrName)))
        {
            if (evt.TargetKind != ScenarioTargetKind.Node || evt.TargetId is null)
            {
                errors.Add("Choose a node for this event.");
            }

            if (string.IsNullOrWhiteSpace(evt.TrafficTypeIdOrName))
            {
                errors.Add("Choose a traffic type for this demand spike.");
            }
        }

        return errors;
    }
}

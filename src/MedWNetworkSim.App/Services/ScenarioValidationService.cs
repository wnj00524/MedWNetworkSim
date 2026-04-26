using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public interface IScenarioValidationService
{
    IReadOnlyList<string> ValidateScenario(ScenarioDefinitionModel scenario);
    IReadOnlyList<string> ValidateEvent(ScenarioEventModel evt);
}

public sealed class ScenarioValidationService : IScenarioValidationService
{
    public IReadOnlyList<string> ValidateScenario(ScenarioDefinitionModel scenario)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(scenario.Name))
        {
            errors.Add("Enter a scenario name.");
        }

        if (scenario.StartTime < 0d)
        {
            errors.Add("Start time must be zero or greater.");
        }

        if (scenario.EndTime <= scenario.StartTime)
        {
            errors.Add("End time must be after start time.");
        }

        if (scenario.DeltaTime <= 0d)
        {
            errors.Add("Step size must be greater than zero.");
        }

        return errors;
    }

    public IReadOnlyList<string> ValidateEvent(ScenarioEventModel evt)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(evt.Name))
        {
            errors.Add("Enter an event name.");
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

        if (evt.Kind is ScenarioEventKind.DemandSpike or ScenarioEventKind.ProductionMultiplier or ScenarioEventKind.ConsumptionMultiplier &&
            (evt.TargetKind != ScenarioTargetKind.Node || evt.TargetId is null || string.IsNullOrWhiteSpace(evt.TrafficTypeIdOrName)))
        {
            if (evt.TargetKind != ScenarioTargetKind.Node || evt.TargetId is null)
            {
                errors.Add("Choose a node for this event.");
            }

            if (string.IsNullOrWhiteSpace(evt.TrafficTypeIdOrName))
            {
                errors.Add("Choose a traffic type for this event.");
            }
        }

        if (evt.Kind == ScenarioEventKind.RouteCostMultiplier && string.IsNullOrWhiteSpace(evt.TrafficTypeIdOrName))
        {
            // Optional traffic type for route cost multiplier.
        }

        if (evt.Kind is ScenarioEventKind.EdgeCostChange or ScenarioEventKind.DemandSpike or ScenarioEventKind.ProductionMultiplier or ScenarioEventKind.ConsumptionMultiplier or ScenarioEventKind.RouteCostMultiplier)
        {
            if (evt.Value <= 0d)
            {
                errors.Add("Value must be greater than zero.");
            }
        }

        return errors;
    }
}

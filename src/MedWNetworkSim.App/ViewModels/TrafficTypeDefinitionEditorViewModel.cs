using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class TrafficTypeDefinitionEditorViewModel : ObservableObject
{
    private string name;
    private string description;
    private RoutingPreference routingPreference;
    private AllocationMode allocationMode;
    private double? capacityBidPerUnit;

    public TrafficTypeDefinitionEditorViewModel(TrafficTypeDefinition definition)
    {
        name = definition.Name;
        description = definition.Description;
        routingPreference = definition.RoutingPreference;
        allocationMode = definition.AllocationMode;
        capacityBidPerUnit = definition.CapacityBidPerUnit;
    }

    public event EventHandler<ValueChangedEventArgs<string>>? NameChanged;

    public string Name
    {
        get => name;
        set
        {
            var oldValue = name;
            if (!SetProperty(ref name, value))
            {
                return;
            }

            NameChanged?.Invoke(this, new ValueChangedEventArgs<string>(oldValue, value));
        }
    }

    public string Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }

    public RoutingPreference RoutingPreference
    {
        get => routingPreference;
        set => SetProperty(ref routingPreference, value);
    }

    public AllocationMode AllocationMode
    {
        get => allocationMode;
        set
        {
            if (SetProperty(ref allocationMode, value))
            {
                OnPropertyChanged(nameof(AllocationModeLabel));
                OnPropertyChanged(nameof(AllocationModeHelpText));
            }
        }
    }

    public string AllocationModeLabel => GetAllocationModeLabel(AllocationMode);

    public string AllocationModeHelpText => GetAllocationModeHelpText(AllocationMode);

    public double? CapacityBidPerUnit
    {
        get => capacityBidPerUnit;
        set => SetProperty(ref capacityBidPerUnit, value);
    }

    public TrafficTypeDefinition ToModel()
    {
        return new TrafficTypeDefinition
        {
            Name = Name,
            Description = Description,
            RoutingPreference = RoutingPreference,
            AllocationMode = AllocationMode,
            CapacityBidPerUnit = CapacityBidPerUnit
        };
    }

    public static string GetAllocationModeLabel(AllocationMode allocationMode)
    {
        return allocationMode switch
        {
            AllocationMode.ProportionalBranchDemand => "Split by downstream demand",
            _ => "Greedy best route"
        };
    }

    public static string GetAllocationModeHelpText(AllocationMode allocationMode)
    {
        return allocationMode switch
        {
            AllocationMode.ProportionalBranchDemand => "Split by downstream demand: divides flow across branches in proportion to the total reachable demand beyond each branch.",
            _ => "Greedy best route: sends flow to the current best destination route first."
        };
    }
}

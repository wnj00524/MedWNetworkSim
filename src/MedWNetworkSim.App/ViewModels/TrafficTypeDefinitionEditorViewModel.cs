using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class TrafficTypeDefinitionEditorViewModel : ObservableObject
{
    private string name;
    private string description;
    private RoutingPreference routingPreference;
    private double? capacityBidPerUnit;

    public TrafficTypeDefinitionEditorViewModel(TrafficTypeDefinition definition)
    {
        name = definition.Name;
        description = definition.Description;
        routingPreference = definition.RoutingPreference;
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
            CapacityBidPerUnit = CapacityBidPerUnit
        };
    }
}

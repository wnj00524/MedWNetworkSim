using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class ProductionInputRequirementViewModel : ObservableObject
{
    private string trafficType;
    private double quantityPerOutputUnit;

    public ProductionInputRequirementViewModel(ProductionInputRequirement requirement)
    {
        trafficType = requirement.TrafficType;
        quantityPerOutputUnit = requirement.QuantityPerOutputUnit;
    }

    public string TrafficType
    {
        get => trafficType;
        set => SetProperty(ref trafficType, value);
    }

    public double QuantityPerOutputUnit
    {
        get => quantityPerOutputUnit;
        set => SetProperty(ref quantityPerOutputUnit, value);
    }

    public ProductionInputRequirement ToModel()
    {
        return new ProductionInputRequirement
        {
            TrafficType = TrafficType,
            QuantityPerOutputUnit = QuantityPerOutputUnit
        };
    }
}

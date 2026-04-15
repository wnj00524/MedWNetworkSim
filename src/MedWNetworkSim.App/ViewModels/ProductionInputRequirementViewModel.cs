using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class ProductionInputRequirementViewModel : ObservableObject
{
    private string trafficType;
    private double inputQuantity;
    private double outputQuantity;

    public ProductionInputRequirementViewModel(ProductionInputRequirement requirement)
    {
        trafficType = requirement.TrafficType;
        inputQuantity = requirement.InputQuantity > 0d
            ? requirement.InputQuantity
            : requirement.QuantityPerOutputUnit.GetValueOrDefault(1d);

        outputQuantity = requirement.OutputQuantity > 0d
            ? requirement.OutputQuantity
            : 1d;
    }

    public string TrafficType
    {
        get => trafficType;
        set => SetProperty(ref trafficType, value);
    }

    public double InputQuantity
    {
        get => inputQuantity;
        set => SetProperty(ref inputQuantity, value);
    }

    public double OutputQuantity
    {
        get => outputQuantity;
        set => SetProperty(ref outputQuantity, value);
    }

    public ProductionInputRequirement ToModel()
    {
        return new ProductionInputRequirement
        {
            TrafficType = TrafficType,
            InputQuantity = InputQuantity,
            OutputQuantity = OutputQuantity
        };
    }
}
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.ViewModels;

public sealed class RouteAllocationRowViewModel(RouteAllocation allocation) : ObservableObject
{
    public int Period { get; } = allocation.Period;

    public string TrafficType { get; } = allocation.TrafficType;

    public RoutingPreference RoutingPreference { get; } = allocation.RoutingPreference;

    public AllocationMode AllocationMode { get; } = allocation.AllocationMode;

    public string RoutingPreferenceLabel => RoutingPreference switch
    {
        RoutingPreference.Speed => "Speed",
        RoutingPreference.Cost => "Cost",
        _ => "Total cost"
    };

    public string AllocationModeLabel => TrafficTypeDefinitionEditorViewModel.GetAllocationModeLabel(AllocationMode);

    public string ProducerName { get; } = allocation.ProducerName;

    public string ConsumerName { get; } = allocation.ConsumerName;

    public double Quantity { get; } = allocation.Quantity;

    public string SourceLabel { get; } = allocation.IsLocalSupply ? "Local" : "Imported";

    public double TotalTime { get; } = allocation.TotalTime;

    public double TransitCostPerUnit { get; } = allocation.TotalCost;

    public double BidCostPerUnit { get; } = allocation.BidCostPerUnit;

    public double DeliveredCostPerUnit { get; } = allocation.DeliveredCostPerUnit;

    public double TotalMovementCost { get; } = allocation.TotalMovementCost;

    public double TotalScore { get; } = allocation.TotalScore;

    public string PathDescription { get; } = string.Join(" -> ", allocation.PathNodeNames);
}

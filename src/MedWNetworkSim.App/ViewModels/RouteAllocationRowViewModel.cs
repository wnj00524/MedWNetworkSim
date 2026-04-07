using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.ViewModels;

public sealed class RouteAllocationRowViewModel(RouteAllocation allocation) : ObservableObject
{
    public string TrafficType { get; } = allocation.TrafficType;

    public RoutingPreference RoutingPreference { get; } = allocation.RoutingPreference;

    public string RoutingPreferenceLabel => RoutingPreference switch
    {
        RoutingPreference.Speed => "Speed",
        RoutingPreference.Cost => "Cost",
        _ => "Total cost"
    };

    public string ProducerName { get; } = allocation.ProducerName;

    public string ConsumerName { get; } = allocation.ConsumerName;

    public double Quantity { get; } = allocation.Quantity;

    public double TotalTime { get; } = allocation.TotalTime;

    public double TotalCost { get; } = allocation.TotalCost;

    public double TotalScore { get; } = allocation.TotalScore;

    public string PathDescription { get; } = string.Join(" -> ", allocation.PathNodeNames);
}

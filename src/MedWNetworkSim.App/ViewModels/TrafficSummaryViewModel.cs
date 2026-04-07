using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.ViewModels;

public sealed class TrafficSummaryViewModel : ObservableObject
{
    private double delivered;
    private double unmetDemand;
    private double unusedSupply;
    private string notesSummary = "Run the simulation to see routed movement.";

    public TrafficSummaryViewModel(
        string name,
        RoutingPreference routingPreference,
        double totalProduction,
        double totalConsumption,
        int producerCount,
        int consumerCount,
        int transshipmentCount)
    {
        Name = name;
        RoutingPreference = routingPreference;
        TotalProduction = totalProduction;
        TotalConsumption = totalConsumption;
        ProducerCount = producerCount;
        ConsumerCount = consumerCount;
        TransshipmentCount = transshipmentCount;
    }

    public string Name { get; }

    public RoutingPreference RoutingPreference { get; }

    public double TotalProduction { get; }

    public double TotalConsumption { get; }

    public int ProducerCount { get; }

    public int ConsumerCount { get; }

    public int TransshipmentCount { get; }

    public double Delivered
    {
        get => delivered;
        private set
        {
            if (SetProperty(ref delivered, value))
            {
                OnPropertyChanged(nameof(OutcomeSummary));
            }
        }
    }

    public double UnmetDemand
    {
        get => unmetDemand;
        private set
        {
            if (SetProperty(ref unmetDemand, value))
            {
                OnPropertyChanged(nameof(OutcomeSummary));
            }
        }
    }

    public double UnusedSupply
    {
        get => unusedSupply;
        private set
        {
            if (SetProperty(ref unusedSupply, value))
            {
                OnPropertyChanged(nameof(OutcomeSummary));
            }
        }
    }

    public string NotesSummary
    {
        get => notesSummary;
        private set => SetProperty(ref notesSummary, value);
    }

    public string RoutingPreferenceLabel => RoutingPreference switch
    {
        RoutingPreference.Speed => "Fastest path",
        RoutingPreference.Cost => "Lowest cost",
        _ => "Lowest total cost"
    };

    public string SupplyDemandSummary =>
        $"P {TotalProduction:0.##} in {ProducerCount} producer(s)  |  C {TotalConsumption:0.##} in {ConsumerCount} consumer(s)  |  T {TransshipmentCount}";

    public string OutcomeSummary =>
        $"Delivered {Delivered:0.##}  |  Unmet {UnmetDemand:0.##}  |  Unused {UnusedSupply:0.##}";

    public void ApplyOutcome(TrafficSimulationOutcome outcome)
    {
        Delivered = outcome.TotalDelivered;
        UnmetDemand = outcome.UnmetDemand;
        UnusedSupply = outcome.UnusedSupply;
        NotesSummary = outcome.Notes.Count == 0
            ? "All routed demand was satisfied."
            : string.Join(" ", outcome.Notes);
    }

    public void ClearOutcome()
    {
        Delivered = 0d;
        UnmetDemand = 0d;
        UnusedSupply = 0d;
        NotesSummary = "Run the simulation to see routed movement.";
    }
}

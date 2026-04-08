using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class NodeTrafficProfileViewModel : ObservableObject, NodeTrafficRoleCatalog.NodeTrafficProfileViewModelAdapter
{
    private string trafficType;
    private double production;
    private double consumption;
    private bool canTransship;
    private int? productionStartPeriod;
    private int? productionEndPeriod;
    private int? consumptionStartPeriod;
    private int? consumptionEndPeriod;
    private bool isStore;
    private double? storeCapacity;

    public NodeTrafficProfileViewModel(NodeTrafficProfile profile)
    {
        trafficType = profile.TrafficType;
        production = profile.Production;
        consumption = profile.Consumption;
        canTransship = profile.CanTransship;
        productionStartPeriod = profile.ProductionStartPeriod;
        productionEndPeriod = profile.ProductionEndPeriod;
        consumptionStartPeriod = profile.ConsumptionStartPeriod;
        consumptionEndPeriod = profile.ConsumptionEndPeriod;
        isStore = profile.IsStore;
        storeCapacity = profile.StoreCapacity;
    }

    public string TrafficType
    {
        get => trafficType;
        set
        {
            if (!SetProperty(ref trafficType, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectionLabel));
            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public double Production
    {
        get => production;
        set
        {
            if (!SetProperty(ref production, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsProducer));
            OnPropertyChanged(nameof(SelectedRoleName));
            OnPropertyChanged(nameof(SelectionLabel));
            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public double Consumption
    {
        get => consumption;
        set
        {
            if (!SetProperty(ref consumption, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsConsumer));
            OnPropertyChanged(nameof(SelectedRoleName));
            OnPropertyChanged(nameof(SelectionLabel));
            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public bool IsProducer
    {
        get => Production > 0;
        set
        {
            if (value == IsProducer)
            {
                return;
            }

            Production = value ? Math.Max(Production, 1d) : 0d;
        }
    }

    public bool IsConsumer
    {
        get => Consumption > 0;
        set
        {
            if (value == IsConsumer)
            {
                return;
            }

            Consumption = value ? Math.Max(Consumption, 1d) : 0d;
        }
    }

    public bool CanTransship
    {
        get => canTransship;
        set
        {
            if (!SetProperty(ref canTransship, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedRoleName));
            OnPropertyChanged(nameof(SelectionLabel));
            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public int? ProductionStartPeriod
    {
        get => productionStartPeriod;
        set
        {
            if (!SetProperty(ref productionStartPeriod, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ProductionScheduleLabel));
            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public int? ProductionEndPeriod
    {
        get => productionEndPeriod;
        set
        {
            if (!SetProperty(ref productionEndPeriod, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ProductionScheduleLabel));
            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public int? ConsumptionStartPeriod
    {
        get => consumptionStartPeriod;
        set
        {
            if (!SetProperty(ref consumptionStartPeriod, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ConsumptionScheduleLabel));
            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public int? ConsumptionEndPeriod
    {
        get => consumptionEndPeriod;
        set
        {
            if (!SetProperty(ref consumptionEndPeriod, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ConsumptionScheduleLabel));
            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public bool IsStore
    {
        get => isStore;
        set
        {
            if (!SetProperty(ref isStore, value))
            {
                return;
            }

            if (!isStore)
            {
                StoreCapacity = null;
            }

            OnPropertyChanged(nameof(SelectionLabel));
            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public double? StoreCapacity
    {
        get => storeCapacity;
        set
        {
            if (!SetProperty(ref storeCapacity, value))
            {
                return;
            }

            OnPropertyChanged(nameof(StoreCapacityLabel));
            OnPropertyChanged(nameof(RoleSummary));
        }
    }

    public IReadOnlyList<string> RoleOptions => NodeTrafficRoleCatalog.RoleOptions;

    public string SelectedRoleName
    {
        get => NodeTrafficRoleCatalog.GetRoleName(IsProducer, IsConsumer, CanTransship);
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            NodeTrafficRoleCatalog.ApplyRoleSelection(this, value);

            OnPropertyChanged(nameof(SelectedRoleName));
        }
    }

    public string RoleSummary
    {
        get
        {
            var parts = new List<string>();

            if (Production > 0)
            {
                parts.Add($"P {Production:0.##}");
            }

            if (CanTransship)
            {
                parts.Add("T");
            }

            if (Consumption > 0)
            {
                parts.Add($"C {Consumption:0.##}");
            }

            if (IsStore)
            {
                parts.Add(StoreCapacity.HasValue
                    ? $"Store {StoreCapacity.Value:0.##}"
                    : "Store");
            }

            if (Production > 0)
            {
                parts.Add($"P@{ProductionScheduleLabel}");
            }

            if (Consumption > 0 || IsStore)
            {
                parts.Add($"C@{ConsumptionScheduleLabel}");
            }

            return parts.Count == 0 ? "No traffic role" : string.Join("  ", parts);
        }
    }

    public string SelectionLabel => $"{TrafficType} | {SelectedRoleName}";

    public string ProductionScheduleLabel => FormatSchedule(ProductionStartPeriod, ProductionEndPeriod);

    public string ConsumptionScheduleLabel => FormatSchedule(ConsumptionStartPeriod, ConsumptionEndPeriod);

    public string StoreCapacityLabel => !IsStore
        ? "Not a store"
        : StoreCapacity.HasValue
            ? $"Store cap {StoreCapacity.Value:0.##}"
            : "Store cap inf";

    private static string FormatSchedule(int? startPeriod, int? endPeriod)
    {
        var startLabel = startPeriod?.ToString() ?? "0";
        var endLabel = endPeriod?.ToString() ?? "inf";
        return $"{startLabel}-{endLabel}";
    }
}

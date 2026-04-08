using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class NodeTrafficProfileViewModel : ObservableObject, NodeTrafficRoleCatalog.NodeTrafficProfileViewModelAdapter
{
    private string trafficType;
    private double production;
    private double consumption;
    private bool canTransship;

    public NodeTrafficProfileViewModel(NodeTrafficProfile profile)
    {
        trafficType = profile.TrafficType;
        production = profile.Production;
        consumption = profile.Consumption;
        canTransship = profile.CanTransship;
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

            return parts.Count == 0 ? "No traffic role" : string.Join("  ", parts);
        }
    }

    public string SelectionLabel => $"{TrafficType} | {SelectedRoleName}";
}

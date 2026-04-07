using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class NodeTrafficProfileViewModel : ObservableObject
{
    private const string NoTrafficRole = "No Traffic Role";
    private const string ProducerRole = "Producer";
    private const string ConsumerRole = "Consumer";
    private const string TransshipRole = "Transship";
    private const string ProducerConsumerRole = "Producer + Consumer";
    private const string ProducerTransshipRole = "Producer + Transship";
    private const string ConsumerTransshipRole = "Consumer + Transship";
    private const string ProducerConsumerTransshipRole = "Producer + Consumer + Transship";

    private static readonly IReadOnlyList<string> roleOptions =
    [
        NoTrafficRole,
        ProducerRole,
        ConsumerRole,
        TransshipRole,
        ProducerConsumerRole,
        ProducerTransshipRole,
        ConsumerTransshipRole,
        ProducerConsumerTransshipRole
    ];

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

    public IReadOnlyList<string> RoleOptions => roleOptions;

    public string SelectedRoleName
    {
        get
        {
            return (IsProducer, IsConsumer, CanTransship) switch
            {
                (false, false, false) => NoTrafficRole,
                (true, false, false) => ProducerRole,
                (false, true, false) => ConsumerRole,
                (false, false, true) => TransshipRole,
                (true, true, false) => ProducerConsumerRole,
                (true, false, true) => ProducerTransshipRole,
                (false, true, true) => ConsumerTransshipRole,
                (true, true, true) => ProducerConsumerTransshipRole
            };
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            switch (value)
            {
                case ProducerRole:
                    ApplyRoleSelection(isProducer: true, isConsumer: false, canTransship: false);
                    break;
                case ConsumerRole:
                    ApplyRoleSelection(isProducer: false, isConsumer: true, canTransship: false);
                    break;
                case TransshipRole:
                    ApplyRoleSelection(isProducer: false, isConsumer: false, canTransship: true);
                    break;
                case ProducerConsumerRole:
                    ApplyRoleSelection(isProducer: true, isConsumer: true, canTransship: false);
                    break;
                case ProducerTransshipRole:
                    ApplyRoleSelection(isProducer: true, isConsumer: false, canTransship: true);
                    break;
                case ConsumerTransshipRole:
                    ApplyRoleSelection(isProducer: false, isConsumer: true, canTransship: true);
                    break;
                case ProducerConsumerTransshipRole:
                    ApplyRoleSelection(isProducer: true, isConsumer: true, canTransship: true);
                    break;
                default:
                    ApplyRoleSelection(isProducer: false, isConsumer: false, canTransship: false);
                    break;
            }

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

    private void ApplyRoleSelection(bool isProducer, bool isConsumer, bool canTransship)
    {
        Production = isProducer ? Math.Max(Production, 1d) : 0d;
        Consumption = isConsumer ? Math.Max(Consumption, 1d) : 0d;
        CanTransship = canTransship;
    }
}

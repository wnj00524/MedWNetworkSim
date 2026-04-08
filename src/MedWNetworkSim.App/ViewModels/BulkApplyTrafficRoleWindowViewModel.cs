using System.Collections.ObjectModel;
using System.Globalization;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class BulkApplyTrafficRoleWindowViewModel : ObservableObject
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private string trafficTypeText;
    private string selectedRoleName;
    private string productionAmountText;
    private string consumptionAmountText;
    private bool applyTranshipmentCapacity;
    private string transhipmentCapacityText;

    public BulkApplyTrafficRoleWindowViewModel(
        IEnumerable<string> trafficTypeNames,
        string? initialTrafficType,
        string? initialRoleName,
        double? initialProductionAmount,
        double? initialConsumptionAmount,
        double? initialTranshipmentCapacity)
    {
        ArgumentNullException.ThrowIfNull(trafficTypeNames);

        foreach (var trafficTypeName in trafficTypeNames
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(Comparer)
                     .OrderBy(name => name, Comparer))
        {
            TrafficTypeOptions.Add(trafficTypeName);
        }

        trafficTypeText = string.IsNullOrWhiteSpace(initialTrafficType)
            ? TrafficTypeOptions.FirstOrDefault() ?? string.Empty
            : initialTrafficType.Trim();
        selectedRoleName = string.IsNullOrWhiteSpace(initialRoleName)
            ? NodeTrafficRoleCatalog.ProducerRole
            : initialRoleName.Trim();
        productionAmountText = FormatAmount(initialProductionAmount ?? 1d);
        consumptionAmountText = FormatAmount(initialConsumptionAmount ?? 1d);
        applyTranshipmentCapacity = false;
        transhipmentCapacityText = initialTranshipmentCapacity.HasValue
            ? FormatAmount(initialTranshipmentCapacity.Value)
            : string.Empty;
    }

    public ObservableCollection<string> TrafficTypeOptions { get; } = [];

    public IReadOnlyList<string> RoleOptions => NodeTrafficRoleCatalog.RoleOptions;

    public string TrafficTypeText
    {
        get => trafficTypeText;
        set
        {
            if (!SetProperty(ref trafficTypeText, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanApply));
        }
    }

    public string SelectedRoleName
    {
        get => selectedRoleName;
        set
        {
            var normalizedValue = string.IsNullOrWhiteSpace(value)
                ? NodeTrafficRoleCatalog.NoTrafficRole
                : value.Trim();

            if (!SetProperty(ref selectedRoleName, normalizedValue))
            {
                return;
            }

            if (!IsTransshipRoleSelected && ApplyTranshipmentCapacity)
            {
                ApplyTranshipmentCapacity = false;
            }

            OnPropertyChanged(nameof(IsProducerRoleSelected));
            OnPropertyChanged(nameof(IsConsumerRoleSelected));
            OnPropertyChanged(nameof(IsTransshipRoleSelected));
            OnPropertyChanged(nameof(CanEditTranshipmentCapacity));
            OnPropertyChanged(nameof(ApplySummary));
        }
    }

    public string ProductionAmountText
    {
        get => productionAmountText;
        set => SetProperty(ref productionAmountText, value);
    }

    public string ConsumptionAmountText
    {
        get => consumptionAmountText;
        set => SetProperty(ref consumptionAmountText, value);
    }

    public bool ApplyTranshipmentCapacity
    {
        get => applyTranshipmentCapacity;
        set
        {
            if (!SetProperty(ref applyTranshipmentCapacity, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanEditTranshipmentCapacity));
            OnPropertyChanged(nameof(ApplySummary));
        }
    }

    public string TranshipmentCapacityText
    {
        get => transhipmentCapacityText;
        set => SetProperty(ref transhipmentCapacityText, value);
    }

    public bool IsProducerRoleSelected => TryGetRoleFlags(out var flags) && flags.IsProducer;

    public bool IsConsumerRoleSelected => TryGetRoleFlags(out var flags) && flags.IsConsumer;

    public bool IsTransshipRoleSelected => TryGetRoleFlags(out var flags) && flags.CanTransship;

    public bool CanEditTranshipmentCapacity => IsTransshipRoleSelected && ApplyTranshipmentCapacity;

    public bool CanApply => !string.IsNullOrWhiteSpace(TrafficTypeText);

    public string ApplySummary
    {
        get
        {
            if (!TryGetRoleFlags(out var flags) || (!flags.IsProducer && !flags.IsConsumer && !flags.CanTransship))
            {
                return "Choosing No Traffic Role removes the selected traffic type from every node.";
            }

            if (flags.CanTransship && ApplyTranshipmentCapacity)
            {
                return "This updates or creates the selected traffic-role entry on every node and also updates each node's shared transhipment capacity.";
            }

            return "This updates or creates the selected traffic-role entry on every node. Shared transhipment capacity is left alone unless you explicitly opt in below.";
        }
    }

    public BulkApplyTrafficRoleOptions BuildOptions()
    {
        var normalizedTrafficType = TrafficTypeText?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTrafficType))
        {
            throw new InvalidOperationException("Choose or type a traffic type before applying a role to all nodes.");
        }

        var normalizedRoleName = string.IsNullOrWhiteSpace(SelectedRoleName)
            ? NodeTrafficRoleCatalog.NoTrafficRole
            : SelectedRoleName.Trim();

        if (!NodeTrafficRoleCatalog.TryParseFlags(normalizedRoleName, out var flags))
        {
            throw new InvalidOperationException("Choose a valid role to apply.");
        }

        var productionAmount = flags.IsProducer
            ? ParseRequiredPositiveAmount(ProductionAmountText, "Production amount")
            : 0d;
        var consumptionAmount = flags.IsConsumer
            ? ParseRequiredPositiveAmount(ConsumptionAmountText, "Consumption amount")
            : 0d;
        var applyTranshipmentCapacity = flags.CanTransship && ApplyTranshipmentCapacity;
        var transhipmentCapacity = applyTranshipmentCapacity
            ? ParseOptionalNonNegativeAmount(TranshipmentCapacityText, "Transhipment capacity")
            : null;

        return new BulkApplyTrafficRoleOptions(
            normalizedTrafficType,
            normalizedRoleName,
            productionAmount,
            consumptionAmount,
            applyTranshipmentCapacity,
            transhipmentCapacity);
    }

    private bool TryGetRoleFlags(out NodeTrafficRoleCatalog.NodeTrafficRoleFlags flags)
    {
        return NodeTrafficRoleCatalog.TryParseFlags(SelectedRoleName, out flags);
    }

    private static string FormatAmount(double amount)
    {
        return amount.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static double ParseRequiredPositiveAmount(string? text, string fieldName)
    {
        var value = ParseOptionalNonNegativeAmount(text, fieldName);
        if (!value.HasValue || value.Value <= 0d)
        {
            throw new InvalidOperationException($"{fieldName} must be a number greater than 0.");
        }

        return value.Value;
    }

    private static double? ParseOptionalNonNegativeAmount(string? text, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var invariantValue))
        {
            if (invariantValue < 0d)
            {
                throw new InvalidOperationException($"{fieldName} must be a number >= 0 when provided.");
            }

            return invariantValue;
        }

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var currentCultureValue))
        {
            if (currentCultureValue < 0d)
            {
                throw new InvalidOperationException($"{fieldName} must be a number >= 0 when provided.");
            }

            return currentCultureValue;
        }

        throw new InvalidOperationException($"{fieldName} must be a valid number.");
    }
}

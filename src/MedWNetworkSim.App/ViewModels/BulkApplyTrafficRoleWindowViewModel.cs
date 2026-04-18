using System.Collections.ObjectModel;
using System.Globalization;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class BulkApplyTrafficRoleWindowViewModel : ObservableObject
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private string scopeSummary;
    private bool applyPlaceType;
    private string placeTypeText;
    private bool applyTrafficRole;
    private string trafficTypeText;
    private string selectedRoleName;
    private string productionAmountText;
    private string consumptionAmountText;
    private bool applyTranshipmentCapacity;
    private string transhipmentCapacityText;

    public BulkApplyTrafficRoleWindowViewModel(
        IEnumerable<string> trafficTypeNames,
        string scopeSummary,
        string? initialPlaceType,
        string? initialTrafficType,
        string? initialRoleName,
        double? initialProductionAmount,
        double? initialConsumptionAmount,
        double? initialTranshipmentCapacity)
    {
        ArgumentNullException.ThrowIfNull(trafficTypeNames);

        this.scopeSummary = scopeSummary;

        foreach (var trafficTypeName in trafficTypeNames
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(Comparer)
                     .OrderBy(name => name, Comparer))
        {
            TrafficTypeOptions.Add(trafficTypeName);
        }

        placeTypeText = initialPlaceType?.Trim() ?? string.Empty;
        applyPlaceType = false;
        applyTrafficRole = true;
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

    public string ScopeSummary
    {
        get => scopeSummary;
        set => SetProperty(ref scopeSummary, value);
    }

    public bool ApplyPlaceType
    {
        get => applyPlaceType;
        set
        {
            if (!SetProperty(ref applyPlaceType, value))
            {
                return;
            }

            RaiseStatePropertiesChanged();
        }
    }

    public string PlaceTypeText
    {
        get => placeTypeText;
        set
        {
            if (!SetProperty(ref placeTypeText, value))
            {
                return;
            }

            RaiseStatePropertiesChanged();
        }
    }

    public bool ApplyTrafficRole
    {
        get => applyTrafficRole;
        set
        {
            if (!SetProperty(ref applyTrafficRole, value))
            {
                return;
            }

            RaiseStatePropertiesChanged();
        }
    }

    public string TrafficTypeText
    {
        get => trafficTypeText;
        set
        {
            if (!SetProperty(ref trafficTypeText, value))
            {
                return;
            }

            RaiseStatePropertiesChanged();
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

            RaiseStatePropertiesChanged();
        }
    }

    public string ProductionAmountText
    {
        get => productionAmountText;
        set
        {
            if (!SetProperty(ref productionAmountText, value))
            {
                return;
            }

            RaiseStatePropertiesChanged();
        }
    }

    public string ConsumptionAmountText
    {
        get => consumptionAmountText;
        set
        {
            if (!SetProperty(ref consumptionAmountText, value))
            {
                return;
            }

            RaiseStatePropertiesChanged();
        }
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

            RaiseStatePropertiesChanged();
        }
    }

    public string TranshipmentCapacityText
    {
        get => transhipmentCapacityText;
        set
        {
            if (!SetProperty(ref transhipmentCapacityText, value))
            {
                return;
            }

            RaiseStatePropertiesChanged();
        }
    }

    public bool IsProducerRoleSelected => TryGetRoleFlags(out var flags) && flags.IsProducer;

    public bool IsConsumerRoleSelected => TryGetRoleFlags(out var flags) && flags.IsConsumer;

    public bool IsTransshipRoleSelected => TryGetRoleFlags(out var flags) && flags.CanTransship;

    public bool HasAnyChangeSelected => ApplyPlaceType || ApplyTrafficRole || ApplyTranshipmentCapacity;

    public bool CanApply => HasAnyChangeSelected && string.IsNullOrWhiteSpace(ValidationText);

    public string ChangePreview
    {
        get
        {
            var parts = new List<string>();

            if (ApplyPlaceType)
            {
                parts.Add(string.IsNullOrWhiteSpace(PlaceTypeText)
                    ? "Clear place type"
                    : $"Set place type to '{PlaceTypeText.Trim()}'");
            }

            if (ApplyTrafficRole)
            {
                if (!TryGetRoleFlags(out var flags) || (!flags.IsProducer && !flags.IsConsumer && !flags.CanTransship))
                {
                    parts.Add($"Remove traffic type '{TrafficTypeText.Trim()}'");
                }
                else
                {
                    parts.Add($"Apply role '{SelectedRoleName}' for traffic type '{TrafficTypeText.Trim()}'");
                }
            }

            if (ApplyTranshipmentCapacity)
            {
                parts.Add(string.IsNullOrWhiteSpace(TranshipmentCapacityText)
                    ? "Clear shared transhipment capacity"
                    : $"Set shared transhipment capacity to {TranshipmentCapacityText.Trim()}");
            }

            return parts.Count == 0
                ? "Choose one or more shared edits before applying."
                : string.Join(". ", parts) + ".";
        }
    }

    public string ValidationText
    {
        get
        {
            if (!HasAnyChangeSelected)
            {
                return "Choose at least one shared edit before applying.";
            }

            if (ApplyTrafficRole)
            {
                if (string.IsNullOrWhiteSpace(TrafficTypeText))
                {
                    return "Traffic type is required when traffic role changes are enabled.";
                }

                if (!TryGetRoleFlags(out var flags))
                {
                    return "Choose a valid traffic role.";
                }

                if (flags.IsProducer)
                {
                    ParseRequiredPositiveAmount(ProductionAmountText, "Production amount");
                }

                if (flags.IsConsumer)
                {
                    ParseRequiredPositiveAmount(ConsumptionAmountText, "Consumption amount");
                }
            }

            if (ApplyTranshipmentCapacity)
            {
                ParseOptionalNonNegativeAmount(TranshipmentCapacityText, "Shared transhipment capacity");
            }

            return string.Empty;
        }
    }

    public BulkApplyTrafficRoleOptions BuildOptions()
    {
        var validationText = ValidationText;
        if (!string.IsNullOrWhiteSpace(validationText))
        {
            throw new InvalidOperationException(validationText);
        }

        double? productionAmount = null;
        double? consumptionAmount = null;
        if (ApplyTrafficRole && TryGetRoleFlags(out var roleFlags))
        {
            productionAmount = roleFlags.IsProducer
                ? ParseRequiredPositiveAmount(ProductionAmountText, "Production amount")
                : null;
            consumptionAmount = roleFlags.IsConsumer
                ? ParseRequiredPositiveAmount(ConsumptionAmountText, "Consumption amount")
                : null;
        }

        return new BulkApplyTrafficRoleOptions(
            ApplyPlaceType,
            string.IsNullOrWhiteSpace(PlaceTypeText) ? null : PlaceTypeText.Trim(),
            ApplyTrafficRole,
            string.IsNullOrWhiteSpace(TrafficTypeText) ? null : TrafficTypeText.Trim(),
            SelectedRoleName,
            productionAmount,
            consumptionAmount,
            ApplyTranshipmentCapacity,
            ApplyTranshipmentCapacity
                ? ParseOptionalNonNegativeAmount(TranshipmentCapacityText, "Shared transhipment capacity")
                : null);
    }

    private void RaiseStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsProducerRoleSelected));
        OnPropertyChanged(nameof(IsConsumerRoleSelected));
        OnPropertyChanged(nameof(IsTransshipRoleSelected));
        OnPropertyChanged(nameof(HasAnyChangeSelected));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ChangePreview));
        OnPropertyChanged(nameof(ValidationText));
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
                throw new InvalidOperationException($"{fieldName} must be a number greater than or equal to 0.");
            }

            return invariantValue;
        }

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var currentCultureValue))
        {
            if (currentCultureValue < 0d)
            {
                throw new InvalidOperationException($"{fieldName} must be a number greater than or equal to 0.");
            }

            return currentCultureValue;
        }

        throw new InvalidOperationException($"{fieldName} must be a valid number.");
    }
}

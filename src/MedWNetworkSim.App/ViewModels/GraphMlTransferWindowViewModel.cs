using System.Collections.ObjectModel;
using System.Globalization;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.ViewModels;

public sealed class GraphMlTransferWindowViewModel : ObservableObject
{
    public const string NoDefaultTrafficTypeOption = "(None)";

    private string defaultTrafficTypeText = NoDefaultTrafficTypeOption;
    private string selectedRoleName = NodeTrafficRoleCatalog.NoTrafficRole;
    private string defaultCapacityText = string.Empty;

    public GraphMlTransferWindowViewModel(
        IEnumerable<string> trafficTypeNames,
        string suggestedExportFileName,
        bool canExport)
    {
        ArgumentNullException.ThrowIfNull(trafficTypeNames);

        TrafficTypeOptions.Add(NoDefaultTrafficTypeOption);
        foreach (var trafficTypeName in trafficTypeNames)
        {
            if (string.IsNullOrWhiteSpace(trafficTypeName) ||
                TrafficTypeOptions.Any(option => string.Equals(option, trafficTypeName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            TrafficTypeOptions.Add(trafficTypeName);
        }

        SuggestedExportFileName = suggestedExportFileName;
        CanExport = canExport;
    }

    public ObservableCollection<string> TrafficTypeOptions { get; } = [];

    public IReadOnlyList<string> RoleOptions => NodeTrafficRoleCatalog.RoleOptions;

    public string SuggestedExportFileName { get; }

    public bool CanExport { get; }

    public string DefaultTrafficTypeText
    {
        get => defaultTrafficTypeText;
        set => SetProperty(ref defaultTrafficTypeText, value);
    }

    public string SelectedRoleName
    {
        get => selectedRoleName;
        set => SetProperty(ref selectedRoleName, value);
    }

    public string DefaultCapacityText
    {
        get => defaultCapacityText;
        set => SetProperty(ref defaultCapacityText, value);
    }

    public GraphMlTransferOptions BuildTransferOptions()
    {
        var normalizedTrafficType = NormalizeTrafficType(DefaultTrafficTypeText);
        var normalizedRole = string.IsNullOrWhiteSpace(SelectedRoleName)
            ? NodeTrafficRoleCatalog.NoTrafficRole
            : SelectedRoleName.Trim();
        var normalizedCapacity = ParseCapacity(DefaultCapacityText);

        if (normalizedCapacity.HasValue && normalizedCapacity.Value <= 0d)
        {
            throw new InvalidOperationException("Default node capacity must be greater than 0 when provided.");
        }

        return new GraphMlTransferOptions(normalizedTrafficType, normalizedRole, normalizedCapacity);
    }

    private static string? NormalizeTrafficType(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return string.Equals(trimmed, NoDefaultTrafficTypeOption, StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }

    private static double? ParseCapacity(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var invariantValue))
        {
            return invariantValue;
        }

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var currentCultureValue))
        {
            return currentCultureValue;
        }

        throw new InvalidOperationException("Default node capacity must be a valid number or left blank.");
    }
}

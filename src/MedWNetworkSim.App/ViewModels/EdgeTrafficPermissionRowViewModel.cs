using System.Windows;
using System.Globalization;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.ViewModels;

public sealed class EdgeTrafficPermissionRowViewModel : ObservableObject
{
    private string trafficType;
    private bool isOverrideActive;
    private EdgeTrafficPermissionMode mode;
    private EdgeTrafficLimitKind limitKind;
    private string limitValueText;
    private string effectiveSummary;
    private double? edgeCapacity;

    public EdgeTrafficPermissionRowViewModel(
        string trafficType,
        bool supportsOverrideToggle,
        EdgeTrafficPermissionRule? rule = null)
    {
        this.trafficType = trafficType;
        SupportsOverrideToggle = supportsOverrideToggle;
        isOverrideActive = !supportsOverrideToggle || rule?.IsActive != false;
        mode = rule?.Mode ?? EdgeTrafficPermissionMode.Permitted;
        limitKind = rule?.LimitKind ?? EdgeTrafficLimitKind.AbsoluteUnits;
        limitValueText = FormatLimitValue(rule?.LimitValue);
        effectiveSummary = EdgeTrafficPermissionResolver.FormatSummary(mode, limitKind, ParseLimitValue());
    }

    public event EventHandler? DefinitionChanged;

    public IReadOnlyList<SelectionOptionViewModel<EdgeTrafficPermissionMode>> PermissionModes { get; } =
    [
        new(EdgeTrafficPermissionMode.Permitted, "Permitted"),
        new(EdgeTrafficPermissionMode.Blocked, "Blocked"),
        new(EdgeTrafficPermissionMode.Limited, "Limited")
    ];

    public IReadOnlyList<SelectionOptionViewModel<EdgeTrafficLimitKind>> LimitKinds { get; } =
    [
        new(EdgeTrafficLimitKind.AbsoluteUnits, "Absolute units"),
        new(EdgeTrafficLimitKind.PercentOfEdgeCapacity, "% of edge capacity")
    ];

    public bool SupportsOverrideToggle { get; }

    public string TrafficType
    {
        get => trafficType;
        private set => SetProperty(ref trafficType, value);
    }

    public bool IsOverrideActive
    {
        get => isOverrideActive;
        set
        {
            if (!SetProperty(ref isOverrideActive, value))
            {
                return;
            }

            RaiseStateChanged();
        }
    }

    public EdgeTrafficPermissionMode Mode
    {
        get => mode;
        set
        {
            if (!SetProperty(ref mode, value))
            {
                return;
            }

            if (mode != EdgeTrafficPermissionMode.Limited)
            {
                LimitValueText = string.Empty;
            }

            RaiseStateChanged();
        }
    }

    public EdgeTrafficLimitKind LimitKind
    {
        get => limitKind;
        set
        {
            if (!SetProperty(ref limitKind, value))
            {
                return;
            }

            RaiseStateChanged();
        }
    }

    public string LimitValueText
    {
        get => limitValueText;
        set
        {
            if (!SetProperty(ref limitValueText, value))
            {
                return;
            }

            RaiseStateChanged();
        }
    }

    public string OverrideChoiceText => IsOverrideActive ? "Override default" : "Use network default";

    public string SelectedModeLabel => PermissionModes.First(option => option.Value == Mode).Label;

    public string SelectedLimitKindLabel => LimitKinds.First(option => option.Value == LimitKind).Label;

    public Visibility OverrideEditorsVisibility => !SupportsOverrideToggle || IsOverrideActive
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility LimitEditorsVisibility => (!SupportsOverrideToggle || IsOverrideActive) && Mode == EdgeTrafficPermissionMode.Limited
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ValidationMessage
    {
        get
        {
            if (SupportsOverrideToggle && !IsOverrideActive)
            {
                return string.Empty;
            }

            if (Mode != EdgeTrafficPermissionMode.Limited)
            {
                return string.Empty;
            }

            var limitValue = ParseLimitValue();
            if (!limitValue.HasValue)
            {
                return LimitKind == EdgeTrafficLimitKind.PercentOfEdgeCapacity
                    ? "Enter a percentage from 0 to 100."
                    : "Enter a limit of 0 or more.";
            }

            if (LimitKind == EdgeTrafficLimitKind.PercentOfEdgeCapacity)
            {
                if (limitValue.Value < 0d || limitValue.Value > 100d)
                {
                    return "Enter a percentage from 0 to 100.";
                }

                if (edgeCapacity is null)
                {
                    return "Set an edge capacity before using a percentage limit.";
                }

                return string.Empty;
            }

            return limitValue.Value < 0d ? "Enter a limit of 0 or more." : string.Empty;
        }
    }

    public Visibility ValidationVisibility => string.IsNullOrWhiteSpace(ValidationMessage)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string EffectiveSummary
    {
        get => effectiveSummary;
        private set => SetProperty(ref effectiveSummary, value);
    }

    public void RenameTrafficType(string nextTrafficType)
    {
        TrafficType = nextTrafficType;
    }

    public void SetEdgeCapacity(double? capacity)
    {
        edgeCapacity = capacity;
        RaiseValidationOnly();
    }

    public void SetEffectiveSummary(string summary)
    {
        EffectiveSummary = summary;
    }

    public EdgeTrafficPermissionRule ToModel()
    {
        return new EdgeTrafficPermissionRule
        {
            TrafficType = TrafficType,
            IsActive = !SupportsOverrideToggle || IsOverrideActive,
            Mode = Mode,
            LimitKind = LimitKind,
            LimitValue = Mode == EdgeTrafficPermissionMode.Limited ? ParseLimitValue() : null
        };
    }

    private double? ParseLimitValue()
    {
        if (string.IsNullOrWhiteSpace(LimitValueText))
        {
            return null;
        }

        var rawValue = LimitValueText.Trim();
        if (rawValue.EndsWith('%'))
        {
            rawValue = rawValue[..^1].TrimEnd();
        }

        if (double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var parsedCurrentCulture))
        {
            return parsedCurrentCulture;
        }

        return double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedInvariantCulture)
            ? parsedInvariantCulture
            : null;
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(OverrideChoiceText));
        OnPropertyChanged(nameof(SelectedModeLabel));
        OnPropertyChanged(nameof(SelectedLimitKindLabel));
        OnPropertyChanged(nameof(OverrideEditorsVisibility));
        OnPropertyChanged(nameof(LimitEditorsVisibility));
        RaiseValidationOnly();
        DefinitionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseValidationOnly()
    {
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(ValidationVisibility));
    }

    private static string FormatLimitValue(double? limitValue)
    {
        return !limitValue.HasValue || double.IsNaN(limitValue.Value) || double.IsInfinity(limitValue.Value)
            ? string.Empty
            : limitValue.Value.ToString("0.##");
    }
}

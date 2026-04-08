using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class NodeViewModel : ObservableObject
{
    public const double DefaultWidth = 176d;
    public const double DefaultHeight = 154d;

    private const double UtilizationTrackWidth = 92d;
    private const double Epsilon = 0.000001d;

    private static readonly Brush DefaultNodeBorder = CreateFrozenBrush("#FFC7B27C");
    private static readonly Brush IdleBrush = CreateFrozenBrush("#FFD7C7B1");
    private static readonly Brush LowUsageBrush = CreateFrozenBrush("#FF9BAA76");
    private static readonly Brush MediumUsageBrush = CreateFrozenBrush("#FFC48B4B");
    private static readonly Brush HighUsageBrush = CreateFrozenBrush("#FFC56245");

    private string id;
    private string name;
    private double x;
    private double y;
    private double? transhipmentCapacity;
    private bool hasSimulationDetails;
    private double routedOutboundQuantity;
    private double transhipmentQuantity;
    private double routedInboundQuantity;
    private double localQuantity;
    private double transhipmentUtilizationRatio;
    private double availableSupplyQuantity;
    private double demandBacklogQuantity;
    private double storeInventoryQuantity;
    private bool hasTimelineDetails;
    private Brush nodeBorderDisplayBrush = DefaultNodeBorder;
    private Brush simulationBrush = IdleBrush;

    public NodeViewModel(NodeModel model)
    {
        id = model.Id;
        name = model.Name;
        x = model.X ?? 0d;
        y = model.Y ?? 0d;
        transhipmentCapacity = model.TranshipmentCapacity;
        TrafficProfiles = new ObservableCollection<NodeTrafficProfileViewModel>(
            model.TrafficProfiles.Select(profile => new NodeTrafficProfileViewModel(profile)));
        TrafficProfiles.CollectionChanged += HandleTrafficProfilesChanged;

        // Bubble traffic-profile edits up as node-definition changes so the rest of the UI can refresh once.
        foreach (var profile in TrafficProfiles)
        {
            profile.PropertyChanged += HandleTrafficProfilePropertyChanged;
        }
    }

    public event EventHandler? PositionChanged;

    public event EventHandler? DefinitionChanged;

    public event EventHandler<ValueChangedEventArgs<string>>? IdChanged;

    public string Id
    {
        get => id;
        set
        {
            var oldValue = id;
            if (!SetProperty(ref id, value))
            {
                return;
            }

            IdChanged?.Invoke(this, new ValueChangedEventArgs<string>(oldValue, value));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Name
    {
        get => name;
        set
        {
            if (!SetProperty(ref name, value))
            {
                return;
            }

            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Width => DefaultWidth;

    public double Height => DefaultHeight;

    public double X
    {
        get => x;
        set
        {
            if (!SetProperty(ref x, value))
            {
                return;
            }

            OnPropertyChanged(nameof(Left));
            OnPropertyChanged(nameof(CenterX));
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Y
    {
        get => y;
        set
        {
            if (!SetProperty(ref y, value))
            {
                return;
            }

            OnPropertyChanged(nameof(Top));
            OnPropertyChanged(nameof(CenterY));
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Left => X - (Width / 2d);

    public double Top => Y - (Height / 2d);

    public double CenterX => X;

    public double CenterY => Y;

    public double? TranshipmentCapacity
    {
        get => transhipmentCapacity;
        set
        {
            if (!SetProperty(ref transhipmentCapacity, value))
            {
                return;
            }

            OnPropertyChanged(nameof(TranshipmentCapacityLabel));
            OnPropertyChanged(nameof(FullTrafficSummary));
            RefreshSimulationDerivedState();
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ObservableCollection<NodeTrafficProfileViewModel> TrafficProfiles { get; }

    public string TrafficProfileCountLabel => TrafficProfiles.Count switch
    {
        1 => "1 traffic type",
        _ => $"{TrafficProfiles.Count} traffic types"
    };

    public string TranshipmentCapacityLabel => TranshipmentCapacity.HasValue
        ? $"trans cap {TranshipmentCapacity.Value:0.##}"
        : "trans cap inf";

    public string FlowSummaryLabel
    {
        get
        {
            if (!HasSimulationDetails && !HasTimelineDetails)
            {
                return string.Empty;
            }

            var parts = new List<string>();

            if (routedOutboundQuantity > Epsilon)
            {
                parts.Add($"out {routedOutboundQuantity:0.##}");
            }

            if (transhipmentQuantity > Epsilon)
            {
                parts.Add($"thru {transhipmentQuantity:0.##}");
            }

            if (routedInboundQuantity > Epsilon)
            {
                parts.Add($"in {routedInboundQuantity:0.##}");
            }

            if (localQuantity > Epsilon)
            {
                parts.Add($"local {localQuantity:0.##}");
            }

            return parts.Count == 0 ? "No routed flow." : string.Join("  ", parts);
        }
    }

    public string TimelineSummaryLabel
    {
        get
        {
            if (!HasTimelineDetails)
            {
                return string.Empty;
            }

            var parts = new List<string>();

            if (availableSupplyQuantity > Epsilon)
            {
                parts.Add($"ready {availableSupplyQuantity:0.##}");
            }

            if (demandBacklogQuantity > Epsilon)
            {
                parts.Add($"need {demandBacklogQuantity:0.##}");
            }

            if (storeInventoryQuantity > Epsilon)
            {
                parts.Add($"stored {storeInventoryQuantity:0.##}");
            }

            return parts.Count == 0 ? "No waiting stock or demand." : string.Join("  ", parts);
        }
    }

    public string TranshipmentUsageLabel
    {
        get
        {
            if (!HasTranshipmentUsageDetails)
            {
                return string.Empty;
            }

            if (TranshipmentCapacity.HasValue)
            {
                return $"trans used {transhipmentQuantity:0.##} / {TranshipmentCapacity.Value:0.##} ({transhipmentUtilizationRatio:0%})";
            }

            return $"trans used {transhipmentQuantity:0.##} | cap inf";
        }
    }

    public Visibility SimulationPanelVisibility => HasSimulationDetails ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TimelinePanelVisibility => HasTimelineDetails ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TranshipmentUsageVisibility => HasTranshipmentUsageDetails ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TranshipmentUsageBarVisibility => HasTranshipmentUsageDetails && TranshipmentCapacity.HasValue
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double TranshipmentUtilizationBarWidth => TranshipmentUsageBarVisibility == Visibility.Visible
        ? UtilizationTrackWidth * transhipmentUtilizationRatio
        : 0d;

    public Brush NodeBorderDisplayBrush => nodeBorderDisplayBrush;

    public Brush SimulationBrush => simulationBrush;

    public bool HasSimulationDetails => hasSimulationDetails;

    public bool HasTimelineDetails => hasTimelineDetails;

    public string FullTrafficSummary
    {
        get
        {
            var lines = new List<string>
            {
                $"Transhipment Capacity: {(TranshipmentCapacity.HasValue ? TranshipmentCapacity.Value.ToString("0.##") : "Unlimited")}"
            };

            if (HasSimulationDetails)
            {
                lines.Add($"Flow: {FlowSummaryLabel}");
            }

            if (HasTimelineDetails)
            {
                lines.Add($"Timeline: {TimelineSummaryLabel}");
            }

            if (HasTranshipmentUsageDetails)
            {
                lines.Add($"Utilization: {TranshipmentUsageLabel}");
            }

            lines.AddRange(TrafficProfiles.Select(profile => $"{profile.TrafficType}: {profile.RoleSummary}"));
            return string.Join(Environment.NewLine, lines);
        }
    }

    public void AddTrafficProfile(NodeTrafficProfileViewModel profile)
    {
        TrafficProfiles.Add(profile);
    }

    public void RemoveTrafficProfile(NodeTrafficProfileViewModel profile)
    {
        TrafficProfiles.Remove(profile);
    }

    public void MoveBy(double deltaX, double deltaY)
    {
        // Keep the node on the positive canvas while preserving drag semantics from the node center.
        X = Math.Max(Width / 2d, X + deltaX);
        Y = Math.Max(Height / 2d, Y + deltaY);
    }

    public void ApplySimulationVisuals(
        double outboundQuantity,
        double transhipmentFlowQuantity,
        double inboundQuantity,
        double localMatchedQuantity,
        bool hasSimulationSnapshot)
    {
        routedOutboundQuantity = Math.Max(0d, outboundQuantity);
        transhipmentQuantity = Math.Max(0d, transhipmentFlowQuantity);
        routedInboundQuantity = Math.Max(0d, inboundQuantity);
        localQuantity = Math.Max(0d, localMatchedQuantity);
        hasSimulationDetails = hasSimulationSnapshot &&
            (TranshipmentCapacity.HasValue ||
             routedOutboundQuantity > Epsilon ||
             transhipmentQuantity > Epsilon ||
             routedInboundQuantity > Epsilon ||
             localQuantity > Epsilon);
        RefreshSimulationDerivedState();
    }

    public void ClearSimulationVisuals()
    {
        routedOutboundQuantity = 0d;
        transhipmentQuantity = 0d;
        routedInboundQuantity = 0d;
        localQuantity = 0d;
        hasSimulationDetails = false;
        RefreshSimulationDerivedState();
    }

    public void ApplyTimelineVisuals(double availableSupply, double demandBacklog, double storeInventory)
    {
        availableSupplyQuantity = Math.Max(0d, availableSupply);
        demandBacklogQuantity = Math.Max(0d, demandBacklog);
        storeInventoryQuantity = Math.Max(0d, storeInventory);
        hasTimelineDetails = availableSupplyQuantity > Epsilon ||
            demandBacklogQuantity > Epsilon ||
            storeInventoryQuantity > Epsilon;
        RefreshSimulationDerivedState();
    }

    public void ClearTimelineVisuals()
    {
        availableSupplyQuantity = 0d;
        demandBacklogQuantity = 0d;
        storeInventoryQuantity = 0d;
        hasTimelineDetails = false;
        RefreshSimulationDerivedState();
    }

    public NodeModel ToModel()
    {
        return new NodeModel
        {
            Id = Id,
            Name = Name,
            X = X,
            Y = Y,
            TranshipmentCapacity = TranshipmentCapacity,
            TrafficProfiles = TrafficProfiles
                .Select(profile => new NodeTrafficProfile
                {
                    TrafficType = profile.TrafficType,
                    Production = profile.Production,
                    Consumption = profile.Consumption,
                    CanTransship = profile.CanTransship,
                    ProductionStartPeriod = profile.ProductionStartPeriod,
                    ProductionEndPeriod = profile.ProductionEndPeriod,
                    ConsumptionStartPeriod = profile.ConsumptionStartPeriod,
                    ConsumptionEndPeriod = profile.ConsumptionEndPeriod,
                    IsStore = profile.IsStore,
                    StoreCapacity = profile.StoreCapacity
                })
                .ToList()
        };
    }

    private bool HasTranshipmentUsageDetails => HasSimulationDetails && (TranshipmentCapacity.HasValue || transhipmentQuantity > Epsilon);

    private void HandleTrafficProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var profile in e.NewItems?.OfType<NodeTrafficProfileViewModel>() ?? [])
        {
            profile.PropertyChanged += HandleTrafficProfilePropertyChanged;
        }

        foreach (var profile in e.OldItems?.OfType<NodeTrafficProfileViewModel>() ?? [])
        {
            profile.PropertyChanged -= HandleTrafficProfilePropertyChanged;
        }

        OnPropertyChanged(nameof(TrafficProfileCountLabel));
        OnPropertyChanged(nameof(FullTrafficSummary));
        DefinitionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleTrafficProfilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FullTrafficSummary));
        DefinitionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshSimulationDerivedState()
    {
        transhipmentUtilizationRatio = TranshipmentCapacity.HasValue && TranshipmentCapacity.Value > Epsilon
            ? Math.Min(1d, transhipmentQuantity / TranshipmentCapacity.Value)
            : 0d;
        simulationBrush = PickUsageBrush(
            TranshipmentCapacity.HasValue ? transhipmentUtilizationRatio : transhipmentQuantity > Epsilon ? 1d : 0d,
            transhipmentQuantity > Epsilon || routedOutboundQuantity > Epsilon || routedInboundQuantity > Epsilon || localQuantity > Epsilon);
        nodeBorderDisplayBrush = HasSimulationDetails
            ? simulationBrush
            : DefaultNodeBorder;

        OnPropertyChanged(nameof(FlowSummaryLabel));
        OnPropertyChanged(nameof(TranshipmentUsageLabel));
        OnPropertyChanged(nameof(SimulationPanelVisibility));
        OnPropertyChanged(nameof(TimelinePanelVisibility));
        OnPropertyChanged(nameof(TranshipmentUsageVisibility));
        OnPropertyChanged(nameof(TranshipmentUsageBarVisibility));
        OnPropertyChanged(nameof(TranshipmentUtilizationBarWidth));
        OnPropertyChanged(nameof(NodeBorderDisplayBrush));
        OnPropertyChanged(nameof(SimulationBrush));
        OnPropertyChanged(nameof(HasSimulationDetails));
        OnPropertyChanged(nameof(TimelineSummaryLabel));
        OnPropertyChanged(nameof(FullTrafficSummary));
    }

    private static Brush PickUsageBrush(double ratio, bool hasFlow)
    {
        if (!hasFlow)
        {
            return IdleBrush;
        }

        if (ratio >= 0.8d)
        {
            return HighUsageBrush;
        }

        if (ratio >= 0.45d)
        {
            return MediumUsageBrush;
        }

        return LowUsageBrush;
    }

    private static Brush CreateFrozenBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}

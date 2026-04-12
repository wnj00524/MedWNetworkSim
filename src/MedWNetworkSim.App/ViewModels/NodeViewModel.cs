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

    private static readonly Brush IdleBrush = CreateFrozenBrush("#FFD7C7B1");
    private static readonly Brush LowUsageBrush = CreateFrozenBrush("#FF9BAA76");
    private static readonly Brush MediumUsageBrush = CreateFrozenBrush("#FFC48B4B");
    private static readonly Brush HighUsageBrush = CreateFrozenBrush("#FFC56245");
    private static Brush DefaultNodeBorder => GetThemeBrush("NodeBorderBrush", "#FFC7B27C");

    private string id;
    private string name;
    private NodeVisualShape shape;
    private double x;
    private double y;
    private double? transhipmentCapacity;
    private string? placeType;
    private string? loreDescription;
    private string? controllingActor;
    private string? templateId;
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
        shape = model.Shape;
        x = model.X ?? 0d;
        y = model.Y ?? 0d;
        transhipmentCapacity = model.TranshipmentCapacity;
        placeType = model.PlaceType;
        loreDescription = model.LoreDescription;
        controllingActor = model.ControllingActor;
        templateId = model.TemplateId;
        Tags = new ObservableCollection<string>(model.Tags ?? []);
        Tags.CollectionChanged += HandleTagsChanged;
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

            RaiseWorldbuilderSummaryPropertiesChanged();
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public NodeVisualShape Shape
    {
        get => shape;
        set
        {
            if (!SetProperty(ref shape, value))
            {
                return;
            }

            OnPropertyChanged(nameof(NodeCornerRadius));
            OnPropertyChanged(nameof(SquareNodeVisibility));
            OnPropertyChanged(nameof(CircularNodeVisibility));
            OnPropertyChanged(nameof(OutlineShapeVisibility));
            OnPropertyChanged(nameof(ShapeWatermarkVisibility));
            OnPropertyChanged(nameof(ShapeIconGeometry));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Width => DefaultWidth;

    public double Height => DefaultHeight;

    public IReadOnlyList<NodeVisualShape> ShapeOptions { get; } = Enum.GetValues<NodeVisualShape>();

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

    public CornerRadius NodeCornerRadius => new(8d);

    public Visibility SquareNodeVisibility => Shape == NodeVisualShape.Square
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility CircularNodeVisibility => Shape == NodeVisualShape.Circle
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility OutlineShapeVisibility => Shape is NodeVisualShape.Person or NodeVisualShape.Car or NodeVisualShape.Building
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ShapeWatermarkVisibility => Shape is NodeVisualShape.Person or NodeVisualShape.Car or NodeVisualShape.Building
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string ShapeIconGeometry => Shape switch
    {
        NodeVisualShape.Circle => "M 50,8 A 42,42 0 1 1 49.99,8 Z",
        NodeVisualShape.Person => "M50,10 A12,12 0 1 1 49.99,10 Z M26,42 C26,31 74,31 74,42 L74,68 26,68 Z M20,86 C20,72 80,72 80,86 L80,92 20,92 Z",
        NodeVisualShape.Car => "M20,58 L30,34 70,34 80,58 88,58 88,74 80,74 A8,8 0 1 1 64,74 L36,74 A8,8 0 1 1 20,74 12,74 12,58 Z M28,42 L72,42 78,54 22,54 Z",
        NodeVisualShape.Building => "M18,92 L18,36 42,20 42,32 58,32 58,12 82,12 82,92 Z M28,42 L36,42 36,50 28,50 Z M46,42 L54,42 54,50 46,50 Z M64,42 L72,42 72,50 64,50 Z M28,58 L36,58 36,66 28,66 Z M46,58 L54,58 54,66 46,66 Z M64,58 L72,58 72,66 64,66 Z M46,76 L54,76 54,92 46,92 Z",
        _ => "M12,12 H88 V88 H12 Z"
    };

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
            RaiseWorldbuilderSummaryPropertiesChanged();
            RefreshSimulationDerivedState();
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ObservableCollection<NodeTrafficProfileViewModel> TrafficProfiles { get; }

    public string? PlaceType
    {
        get => placeType;
        set
        {
            if (!SetProperty(ref placeType, value))
            {
                return;
            }

            RaiseWorldbuilderSummaryPropertiesChanged();
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? LoreDescription
    {
        get => loreDescription;
        set
        {
            if (!SetProperty(ref loreDescription, value))
            {
                return;
            }

            RaiseWorldbuilderSummaryPropertiesChanged();
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? ControllingActor
    {
        get => controllingActor;
        set
        {
            if (!SetProperty(ref controllingActor, value))
            {
                return;
            }

            RaiseWorldbuilderSummaryPropertiesChanged();
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ObservableCollection<string> Tags { get; }

    public string TagsText
    {
        get => string.Join(", ", Tags);
        set
        {
            var nextTags = SplitTags(value);
            if (Tags.SequenceEqual(nextTags, StringComparer.Ordinal))
            {
                return;
            }

            Tags.Clear();
            foreach (var tag in nextTags)
            {
                Tags.Add(tag);
            }
        }
    }

    public string? TemplateId
    {
        get => templateId;
        set
        {
            if (!SetProperty(ref templateId, value))
            {
                return;
            }

            RaiseWorldbuilderSummaryPropertiesChanged();
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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

    public string WorldbuilderSummary
    {
        get
        {
            var identity = GetPlaceIdentity();
            var parts = new List<string>();
            var producedGoods = GetProducedGoods();
            var consumedGoods = GetConsumedGoods();
            var storedGoods = GetStoredGoods();

            if (producedGoods.Count > 0)
            {
                parts.Add($"supplies {FormatList(producedGoods)}");
            }

            if (consumedGoods.Count > 0)
            {
                parts.Add($"needs {FormatList(consumedGoods)}");
            }

            if (storedGoods.Count > 0)
            {
                parts.Add($"stockpiles {FormatList(storedGoods)}");
            }

            if (CanTransship)
            {
                parts.Add(TranshipmentCapacity.HasValue
                    ? $"moves goods onward through a transhipment capacity of {TranshipmentCapacity.Value:0.##}"
                    : "moves goods onward without a fixed transhipment limit");
            }

            var activity = parts.Count == 0
                ? "has no configured production, need, stockpile, or transhipment role yet"
                : string.Join("; ", parts);
            var owner = string.IsNullOrWhiteSpace(ControllingActor)
                ? string.Empty
                : $" Controlled by {ControllingActor.Trim()}.";
            var lore = string.IsNullOrWhiteSpace(LoreDescription)
                ? string.Empty
                : $" {LoreDescription.Trim()}";

            return $"{identity} {activity}.{owner}{lore}".Trim();
        }
    }

    public string WorldbuilderImportanceSummary
    {
        get
        {
            var reasons = new List<string>();

            if (GetProducedGoods().Count > 0)
            {
                reasons.Add("a source of supply");
            }

            if (GetConsumedGoods().Count > 0)
            {
                reasons.Add("a demand center");
            }

            if (GetStoredGoods().Count > 0)
            {
                reasons.Add("a stockpile");
            }

            if (CanTransship)
            {
                reasons.Add("a routing hub");
            }

            if (!string.IsNullOrWhiteSpace(ControllingActor))
            {
                reasons.Add($"controlled by {ControllingActor.Trim()}");
            }

            var tags = Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (tags.Count > 0)
            {
                reasons.Add($"tagged {FormatList(tags)}");
            }

            return reasons.Count == 0
                ? "Importance is not established yet; add roles or worldbuilder metadata to explain its place in the world."
                : $"Important because it is {FormatList(reasons)}.";
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
            Shape = Shape,
            X = X,
            Y = Y,
            TranshipmentCapacity = TranshipmentCapacity,
            PlaceType = PlaceType,
            LoreDescription = LoreDescription,
            ControllingActor = ControllingActor,
            Tags = Tags.ToList(),
            TemplateId = TemplateId,
            TrafficProfiles = TrafficProfiles
                .Select(profile => new NodeTrafficProfile
                {
                    TrafficType = profile.TrafficType,
                    Production = profile.Production,
                    Consumption = profile.Consumption,
                    ConsumerPremiumPerUnit = profile.ConsumerPremiumPerUnit,
                    CanTransship = profile.CanTransship,
                    ProductionStartPeriod = profile.ProductionStartPeriod,
                    ProductionEndPeriod = profile.ProductionEndPeriod,
                    ConsumptionStartPeriod = profile.ConsumptionStartPeriod,
                    ConsumptionEndPeriod = profile.ConsumptionEndPeriod,
                    ProductionWindows = profile.ProductionWindows.Select(window => window.ToModel()).ToList(),
                    ConsumptionWindows = profile.ConsumptionWindows.Select(window => window.ToModel()).ToList(),
                    InputRequirements = profile.InputRequirements.Select(requirement => requirement.ToModel()).ToList(),
                    IsStore = profile.IsStore,
                    StoreCapacity = profile.StoreCapacity
                })
                .ToList()
        };
    }

    private bool HasTranshipmentUsageDetails => HasSimulationDetails && (TranshipmentCapacity.HasValue || transhipmentQuantity > Epsilon);

    private void HandleTagsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TagsText));
        RaiseWorldbuilderSummaryPropertiesChanged();
        DefinitionChanged?.Invoke(this, EventArgs.Empty);
    }

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
        RaiseWorldbuilderSummaryPropertiesChanged();
        DefinitionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleTrafficProfilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FullTrafficSummary));
        RaiseWorldbuilderSummaryPropertiesChanged();
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
            : GetThemeBrush("NodeBorderBrush", "#FFC7B27C");

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

    private void RaiseWorldbuilderSummaryPropertiesChanged()
    {
        OnPropertyChanged(nameof(WorldbuilderSummary));
        OnPropertyChanged(nameof(WorldbuilderImportanceSummary));
    }

    private string GetPlaceIdentity()
    {
        return string.IsNullOrWhiteSpace(PlaceType)
            ? Name
            : $"{Name} is a {PlaceType.Trim()} that";
    }

    private IReadOnlyList<string> GetProducedGoods()
    {
        return GetTrafficTypes(profile => profile.Production > Epsilon);
    }

    private IReadOnlyList<string> GetConsumedGoods()
    {
        return GetTrafficTypes(profile => profile.Consumption > Epsilon);
    }

    private IReadOnlyList<string> GetStoredGoods()
    {
        return GetTrafficTypes(profile => profile.IsStore);
    }

    private bool CanTransship => TrafficProfiles.Any(profile => profile.CanTransship);

    private IReadOnlyList<string> GetTrafficTypes(Func<NodeTrafficProfileViewModel, bool> predicate)
    {
        return TrafficProfiles
            .Where(predicate)
            .Select(profile => profile.TrafficType)
            .Where(trafficType => !string.IsNullOrWhiteSpace(trafficType))
            .Select(trafficType => trafficType.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(trafficType => trafficType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatList(IReadOnlyList<string> items)
    {
        return items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}"
        };
    }

    private static IReadOnlyList<string> SplitTags(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static Brush GetThemeBrush(string resourceKey, string fallbackHex)
    {
        return Application.Current?.TryFindResource(resourceKey) as Brush ?? CreateFrozenBrush(fallbackHex);
    }
}

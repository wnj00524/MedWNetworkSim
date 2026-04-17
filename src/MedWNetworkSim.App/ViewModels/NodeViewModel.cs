using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.ViewModels;

public sealed class NodeViewModel : ObservableObject
{
    private static readonly StringComparer TrafficComparer = StringComparer.OrdinalIgnoreCase;

    public const double DefaultWidth = 168d;
    public const double DefaultHeight = 112d;

    private const double UtilizationTrackWidth = 92d;
    private const double DemandMeterTrackWidth = 116d;
    private const double UrgentBacklogThreshold = 5d;
    private const double Epsilon = 0.000001d;

    private static readonly Brush IdleBrush = CreateFrozenBrush("#FFD7C7B1");
    private static readonly Brush LowUsageBrush = CreateFrozenBrush("#FF9BAA76");
    private static readonly Brush MediumUsageBrush = CreateFrozenBrush("#FFC48B4B");
    private static readonly Brush HighUsageBrush = CreateFrozenBrush("#FFC56245");
    private static readonly Brush UrgentBacklogBorderBrush = CreateFrozenBrush("#FFDA8A34");
    private static Brush DefaultNodeBorder => GetThemeBrush("NodeBorderBrush", "#FFC7B27C");

    private string id;
    private string name;
    private NodeVisualShape shape;
    private NodeKind nodeKind;
    private string? referencedSubnetworkId;
    private bool isExternalInterface;
    private string? interfaceName;
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
    private double pressureScore;
    private string pressureTopCause = string.Empty;
    private bool hasTimelineDetails;
    private double deliveredDemandQuantity;
    private string producedTrafficDetails = "none visible";
    private string transhippedTrafficDetails = "none visible";
    private string storedTrafficDetails = "none visible";
    private readonly ObservableCollection<NodeDemandBadgeViewModel> demandBadges = [];
    private Brush nodeBorderDisplayBrush = DefaultNodeBorder;
    private Brush simulationBrush = IdleBrush;
    private bool isSelected;
    private bool isKeyboardFocused;

    public NodeViewModel(NodeModel model)
    {
        id = model.Id;
        name = model.Name;
        shape = model.Shape;
        nodeKind = model.NodeKind;
        referencedSubnetworkId = model.ReferencedSubnetworkId;
        isExternalInterface = model.IsExternalInterface;
        interfaceName = model.InterfaceName;
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

    public IReadOnlyList<NodeKind> NodeKindOptions { get; } = Enum.GetValues<NodeKind>();

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

    public NodeKind NodeKind
    {
        get => nodeKind;
        set
        {
            if (!SetProperty(ref nodeKind, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsCompositeSubnetwork));
            OnPropertyChanged(nameof(CompositeSummaryLabel));
            RaiseWorldbuilderSummaryPropertiesChanged();
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsCompositeSubnetwork => NodeKind == NodeKind.CompositeSubnetwork;

    public string? ReferencedSubnetworkId
    {
        get => referencedSubnetworkId;
        set
        {
            if (!SetProperty(ref referencedSubnetworkId, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CompositeSummaryLabel));
            RaiseWorldbuilderSummaryPropertiesChanged();
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsExternalInterface
    {
        get => isExternalInterface;
        set
        {
            if (!SetProperty(ref isExternalInterface, value))
            {
                return;
            }

            OnPropertyChanged(nameof(InterfaceSummaryLabel));
            RaiseWorldbuilderSummaryPropertiesChanged();
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? InterfaceName
    {
        get => interfaceName;
        set
        {
            if (!SetProperty(ref interfaceName, value))
            {
                return;
            }

            OnPropertyChanged(nameof(InterfaceSummaryLabel));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string CompositeSummaryLabel => IsCompositeSubnetwork
        ? $"Composite: {(string.IsNullOrWhiteSpace(ReferencedSubnetworkId) ? "choose a subnetwork" : ReferencedSubnetworkId)}"
        : string.Empty;

    public string InterfaceSummaryLabel => IsExternalInterface
        ? $"Interface: {(string.IsNullOrWhiteSpace(InterfaceName) ? Id : InterfaceName)}"
        : string.Empty;

    public ObservableCollection<NodeTrafficProfileViewModel> TrafficProfiles { get; }
    public ObservableCollection<NodeDemandBadgeViewModel> DemandBadges => demandBadges;

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
        1 => "1 flow",
        _ => $"{TrafficProfiles.Count} flows"
    };

    public string PlaceTypeLabel => string.IsNullOrWhiteSpace(PlaceType)
        ? GetPrimaryRoleLabel()
        : PlaceType.Trim();

    public IReadOnlyList<string> RoleBadges
    {
        get
        {
            var badges = new List<string>();

            if (TrafficProfiles.Any(profile => profile.Production > Epsilon))
            {
                badges.Add("Source");
            }

            if (TrafficProfiles.Any(profile => profile.Consumption > Epsilon))
            {
                badges.Add("Need");
            }

            if (TrafficProfiles.Any(profile => profile.IsStore))
            {
                badges.Add("Stockpile");
            }

            if (CanTransship)
            {
                badges.Add("Relay");
            }

            if (IsExternalInterface)
            {
                badges.Add("Interface");
            }

            if (IsCompositeSubnetwork)
            {
                badges.Add("Composite");
            }

            return badges.Count == 0 ? ["Draft"] : badges;
        }
    }

    public string CompactFlowSummary
    {
        get
        {
            var produced = GetProducedGoods();
            var consumed = GetConsumedGoods();
            var stored = GetStoredGoods();
            var parts = new List<string>();

            if (produced.Count > 0)
            {
                parts.Add($"makes {FormatCompactList(produced)}");
            }

            if (consumed.Count > 0)
            {
                parts.Add($"needs {FormatCompactList(consumed)}");
            }

            if (stored.Count > 0)
            {
                parts.Add($"stores {FormatCompactList(stored)}");
            }

            if (parts.Count == 0 && CanTransship)
            {
                parts.Add("moves flows onward");
            }

            if (IsCompositeSubnetwork)
            {
                parts.Add("hosts a child network");
            }

            if (IsExternalInterface)
            {
                parts.Add("opens to a parent network");
            }

            return parts.Count == 0 ? "Add flows to define its role" : string.Join(" | ", parts);
        }
    }

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

            if (pressureScore > Epsilon)
            {
                parts.Add($"pressure {pressureScore:0.##}");
            }

            return parts.Count == 0 ? "No waiting stock or demand." : string.Join("  ", parts);
        }
    }

    public string PressureSummaryLabel => pressureScore <= Epsilon
        ? "No pressure detected."
        : $"pressure {pressureScore:0.##}" +
          (string.IsNullOrWhiteSpace(pressureTopCause) ? string.Empty : $" | top cause {pressureTopCause}");

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
    public Visibility DemandBadgeVisibility => demandBadges.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DemandMeterVisibility => HasTimelineDetails && (deliveredDemandQuantity > Epsilon || demandBacklogQuantity > Epsilon)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility TranshipmentUsageVisibility => HasTranshipmentUsageDetails ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TranshipmentUsageBarVisibility => HasTranshipmentUsageDetails && TranshipmentCapacity.HasValue
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double TranshipmentUtilizationBarWidth => TranshipmentUsageBarVisibility == Visibility.Visible
        ? UtilizationTrackWidth * transhipmentUtilizationRatio
        : 0d;
    public double DemandMeterFillWidth => DemandMeterVisibility == Visibility.Visible
        ? DemandMeterTrackWidth * DemandMeterRatio
        : 0d;
    public double DemandMeterTrackWidthValue => DemandMeterTrackWidth;
    public double DemandMeterRatio
    {
        get
        {
            var totalRequired = deliveredDemandQuantity + demandBacklogQuantity;
            if (totalRequired <= Epsilon)
            {
                return 0d;
            }

            return Math.Clamp(deliveredDemandQuantity / totalRequired, 0d, 1d);
        }
    }
    public bool HasUrgentBacklog => demandBacklogQuantity > UrgentBacklogThreshold;
    public string DemandMeterSummary => $"Demand fulfilled {deliveredDemandQuantity:0.##} / {(deliveredDemandQuantity + demandBacklogQuantity):0.##}";
    public string DemandTooltipText
    {
        get
        {
            if (demandBadges.Count == 0)
            {
                return "Node requires no backlog goods.";
            }

            var lines = new List<string> { "Node requires:" };
            lines.AddRange(demandBadges.Select(badge => $"- {badge.TrafficType}: {badge.QuantityLabel} units"));
            return string.Join(Environment.NewLine, lines);
        }
    }

    public Brush NodeBorderDisplayBrush => nodeBorderDisplayBrush;

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public bool IsKeyboardFocused
    {
        get => isKeyboardFocused;
        set => SetProperty(ref isKeyboardFocused, value);
    }

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

            if (IsCompositeSubnetwork)
            {
                lines.Add(CompositeSummaryLabel);
            }

            if (IsExternalInterface)
            {
                lines.Add(InterfaceSummaryLabel);
            }

            if (HasSimulationDetails)
            {
                lines.Add($"Flow: {FlowSummaryLabel}");
            }

            lines.Add($"Produced traffic: {producedTrafficDetails}");
            lines.Add($"Transhipped traffic: {transhippedTrafficDetails}");
            lines.Add($"Stored traffic: {storedTrafficDetails}");

            if (HasTimelineDetails)
            {
                lines.Add($"Timeline: {TimelineSummaryLabel}");

                if (demandBadges.Count > 0)
                {
                    lines.Add("Demand backlog by traffic type:");
                    lines.AddRange(demandBadges.Select(badge => $"- {badge.TrafficType}: {badge.QuantityLabel} units"));
                }
            }

            if (pressureScore > Epsilon)
            {
                lines.Add($"Pressure: {PressureSummaryLabel}");
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

            if (IsCompositeSubnetwork)
            {
                parts.Add($"represents the child network {ReferencedSubnetworkId ?? "(unassigned)"}");
            }

            if (IsExternalInterface)
            {
                parts.Add($"is exposed as interface {InterfaceName ?? Id}");
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

            if (IsCompositeSubnetwork)
            {
                reasons.Add("a composite child network");
            }

            if (IsExternalInterface)
            {
                reasons.Add("a parent-facing interface");
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
        X += deltaX;
        Y += deltaY;
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

    public void ApplyTooltipTrafficDetails(
        IReadOnlyList<KeyValuePair<string, double>> producedByTraffic,
        IReadOnlyList<KeyValuePair<string, double>> storedByTraffic,
        IReadOnlyList<KeyValuePair<string, double>> transhippedByTraffic)
    {
        producedTrafficDetails = FormatTrafficDetails(producedByTraffic);
        storedTrafficDetails = FormatTrafficDetails(storedByTraffic);
        transhippedTrafficDetails = FormatTrafficDetails(transhippedByTraffic);
        OnPropertyChanged(nameof(FullTrafficSummary));
    }

    public void ApplyTimelineVisuals(double availableSupply, double demandBacklog, double storeInventory)
    {
        ApplyTimelineVisuals(availableSupply, demandBacklog, storeInventory, 0d, [], null);
    }

    public void ApplyTimelineVisuals(
        double availableSupply,
        double demandBacklog,
        double storeInventory,
        double deliveredDemand,
        IReadOnlyList<KeyValuePair<string, double>> backlogByTraffic,
        TemporalNetworkSimulationEngine.NodePressureSnapshot? pressure)
    {
        availableSupplyQuantity = Math.Max(0d, availableSupply);
        demandBacklogQuantity = Math.Max(0d, demandBacklog);
        storeInventoryQuantity = Math.Max(0d, storeInventory);
        deliveredDemandQuantity = Math.Max(0d, deliveredDemand);
        pressureScore = pressure?.Score > Epsilon ? pressure.Value.Score : 0d;
        pressureTopCause = pressure is { TopCause: { Length: > 0 } } ? pressure.Value.TopCause : string.Empty;
        hasTimelineDetails = availableSupplyQuantity > Epsilon ||
            demandBacklogQuantity > Epsilon ||
            storeInventoryQuantity > Epsilon ||
            pressureScore > Epsilon;
        UpdateDemandBadges(backlogByTraffic);
        RefreshSimulationDerivedState();
    }

    public void ClearTimelineVisuals()
    {
        availableSupplyQuantity = 0d;
        demandBacklogQuantity = 0d;
        storeInventoryQuantity = 0d;
        deliveredDemandQuantity = 0d;
        pressureScore = 0d;
        pressureTopCause = string.Empty;
        hasTimelineDetails = false;
        demandBadges.Clear();
        RefreshSimulationDerivedState();
    }

    public NodeModel ToModel()
    {
        return new NodeModel
        {
            Id = Id,
            Name = Name,
            Shape = Shape,
            NodeKind = NodeKind,
            ReferencedSubnetworkId = ReferencedSubnetworkId,
            IsExternalInterface = IsExternalInterface,
            InterfaceName = InterfaceName,
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
            : HasUrgentBacklog
                ? UrgentBacklogBorderBrush
            : GetThemeBrush("NodeBorderBrush", "#FFC7B27C");

        OnPropertyChanged(nameof(FlowSummaryLabel));
        OnPropertyChanged(nameof(TranshipmentUsageLabel));
        OnPropertyChanged(nameof(SimulationPanelVisibility));
        OnPropertyChanged(nameof(TimelinePanelVisibility));
        OnPropertyChanged(nameof(DemandBadgeVisibility));
        OnPropertyChanged(nameof(DemandMeterVisibility));
        OnPropertyChanged(nameof(DemandMeterFillWidth));
        OnPropertyChanged(nameof(DemandMeterTrackWidthValue));
        OnPropertyChanged(nameof(DemandMeterRatio));
        OnPropertyChanged(nameof(DemandMeterSummary));
        OnPropertyChanged(nameof(DemandTooltipText));
        OnPropertyChanged(nameof(HasUrgentBacklog));
        OnPropertyChanged(nameof(TranshipmentUsageVisibility));
        OnPropertyChanged(nameof(TranshipmentUsageBarVisibility));
        OnPropertyChanged(nameof(TranshipmentUtilizationBarWidth));
        OnPropertyChanged(nameof(NodeBorderDisplayBrush));
        OnPropertyChanged(nameof(SimulationBrush));
        OnPropertyChanged(nameof(HasSimulationDetails));
        OnPropertyChanged(nameof(TimelineSummaryLabel));
        OnPropertyChanged(nameof(PressureSummaryLabel));
        OnPropertyChanged(nameof(FullTrafficSummary));
    }

    private void UpdateDemandBadges(IReadOnlyList<KeyValuePair<string, double>> backlogByTraffic)
    {
        demandBadges.Clear();

        foreach (var pair in backlogByTraffic
                     .Where(item => item.Value > Epsilon)
                     .OrderByDescending(item => item.Value)
                     .ThenBy(item => item.Key, TrafficComparer))
        {
            demandBadges.Add(new NodeDemandBadgeViewModel(
                pair.Key,
                pair.Value,
                GetDemandIcon(pair.Key),
                GetDemandBrush(pair.Key)));
        }
    }

    private static Brush GetDemandBrush(string trafficType)
    {
        var color = trafficType.Trim().ToLowerInvariant() switch
        {
            "spice" => "#FFD45B5B",
            "water" => "#FF4A8EDC",
            "herbs" => "#FF5DAA5B",
            _ => "#FF8B7E6A"
        };

        return CreateFrozenBrush(color);
    }

    private static string GetDemandIcon(string trafficType)
    {
        return trafficType.Trim().ToLowerInvariant() switch
        {
            "spice" => "🧂",
            "water" => "💧",
            "herbs" => "🌿",
            _ => "◉"
        };
    }

    private void RaiseWorldbuilderSummaryPropertiesChanged()
    {
        OnPropertyChanged(nameof(WorldbuilderSummary));
        OnPropertyChanged(nameof(WorldbuilderImportanceSummary));
        OnPropertyChanged(nameof(PlaceTypeLabel));
        OnPropertyChanged(nameof(RoleBadges));
        OnPropertyChanged(nameof(CompactFlowSummary));
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

    private string GetPrimaryRoleLabel()
    {
        if (CanTransship)
        {
            return "Relay";
        }

        if (TrafficProfiles.Any(profile => profile.Production > Epsilon))
        {
            return "Source";
        }

        if (TrafficProfiles.Any(profile => profile.Consumption > Epsilon))
        {
            return "Need";
        }

        if (TrafficProfiles.Any(profile => profile.IsStore))
        {
            return "Stockpile";
        }

        return "Place";
    }

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

    private static string FormatCompactList(IReadOnlyList<string> items)
    {
        return items.Count switch
        {
            0 => string.Empty,
            <= 2 => string.Join(", ", items),
            _ => $"{items[0]}, {items[1]} +{items.Count - 2}"
        };
    }

    private static string FormatTrafficDetails(IReadOnlyList<KeyValuePair<string, double>> values)
    {
        return values.Count == 0
            ? "none visible"
            : string.Join(", ", values.Select(value => $"{value.Key} {value.Value:0.##}"));
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

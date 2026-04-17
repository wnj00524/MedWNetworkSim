using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Presets;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.App.Templates;

namespace MedWNetworkSim.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private const double Epsilon = 0.000001d;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private static readonly BundledScenarioOption BundledSampleScenario = new(
        "Technical Sample",
        "sample-network.json",
        "MedWNetworkSim.App.Samples.sample-network.json",
        "Technical",
        "A neutral baseline network for checking simulation mechanics.");

    private static readonly IReadOnlyList<BundledScenarioOption> WorldbuilderScenarioCatalog =
    [
        new(
            "Village and Manor",
            "village-and-manor.json",
            "MedWNetworkSim.App.Samples.village-and-manor.json",
            "Medieval",
            "A small estate supply pattern."),
        new(
            "Market Town and Hinterland",
            "market-town-and-hinterland.json",
            "MedWNetworkSim.App.Samples.market-town-and-hinterland.json",
            "Medieval",
            "A regional hub drawing on surrounding producers."),
        new(
            "River Port Chain",
            "river-port-chain.json",
            "MedWNetworkSim.App.Samples.river-port-chain.json",
            "Medieval",
            "A route-chain example with port relay behavior."),
        new(
            "Fortress Supply Network",
            "fortress-supply-network.json",
            "MedWNetworkSim.App.Samples.fortress-supply-network.json",
            "Medieval",
            "A fortified demand center and supply support pattern.")
    ];

    private readonly NetworkFileService fileService = new();
    private readonly GraphMlFileService graphMlFileService = new();
    private readonly ReportExportService reportExportService = new();
    private readonly EdgeTrafficPermissionResolver edgeTrafficPermissionResolver = new();
    private readonly NetworkSimulationEngine simulationEngine = new();
    private readonly TemporalNetworkSimulationEngine temporalSimulationEngine = new();
    private readonly List<RouteAllocation> allAllocationModels = [];
    private readonly List<RouteAllocationRowViewModel> allAllocations = [];
    private readonly List<ConsumerCostSummaryRowViewModel> allConsumerCostSummaries = [];
    private readonly List<PerishingEventRowViewModel> allPerishingEvents = [];

    private bool isNormalizingEdgeInterfaces;

    private string activeFileLabel = "Blank world";
    private string? currentFilePath;
    private string networkName = "Untitled World";
    private string networkDescription = "Sketch places, routes, and flows first; use advanced controls when you need simulation detail.";
    private AllocationMode defaultAllocationMode = AllocationMode.GreedyBestRoute;
    private int? timelineLoopLength;
    private int simulationSeed = 12345;
    private string statusMessage = "Start a blank world, open a saved world, or load an example to explore the system.";
    private AppTheme selectedTheme = AppTheme.Classic;
    private TrafficSummaryViewModel? selectedTraffic;
    private NodeViewModel? selectedNode;
    private NodeTrafficProfileViewModel? selectedNodeTrafficProfile;
    private EdgeViewModel? selectedEdge;
    private TrafficTypeDefinitionEditorViewModel? selectedTrafficDefinition;
    private PlaceTemplate? selectedPlaceTemplate = PlaceTemplateCatalog.Templates.FirstOrDefault();
    private DemographicDemandPreset? selectedDemographicDemandPreset = DemographicDemandPresetCatalog.Presets.FirstOrDefault();
    private BundledScenarioOption? selectedWorldbuilderScenario = WorldbuilderScenarioCatalog.FirstOrDefault();
    private bool isNormalizingNodeTrafficProfiles;
    private bool isAdjustingTrafficDefinitionNames;
    private bool isBulkUpdatingTrafficProfiles;
    private bool isBulkUpdatingTrafficDefinitions;
    private bool hasSimulationSnapshot;
    private TemporalNetworkSimulationEngine.TemporalSimulationState? temporalSimulationState;
    private TemporalNetworkSimulationEngine.TemporalSimulationStepResult? lastTimelineStepResult;
    private int currentPeriod;
    private bool hasTimelineSnapshot;
    private double workspaceMinX;
    private double workspaceMinY;
    private double workspaceWidth = 1600d;
    private double workspaceHeight = 1000d;
    private bool hasNetwork;
    private bool hasUnsavedChanges;
    private bool isReplacingNetwork;
    private bool isWorldbuilderMode = true;
    private bool isCanvasOnlyMode;
    private bool isLayersPanelOpen;
    private bool isLegendPanelOpen;
    private bool suppressInspectorAutoOpen;
    private string graphKeyboardHint = "Press F6 to move into the canvas workspace.";
    private string focusedEdgeStatus = "No route focused.";

    public MainWindowViewModel()
    {
         InspectorPanel = new InspectorPanelViewModel(
        GetCompositeInterfaceSummaries,
        GetCompositeInterfaceSummary);

    AppThemeManager.ApplyTheme(selectedTheme);
    Terminology.IsWorldbuilderMode = isWorldbuilderMode;
    LayersPanel.LayersChanged += HandleLayersChanged;
    ReportsDrawer.RouteSelected += HandleReportRouteSelected;
    CreateNewNetwork();
    }

    public ObservableCollection<NodeViewModel> Nodes { get; } = [];

    public ObservableCollection<EdgeViewModel> Edges { get; } = [];

    public ObservableCollection<TrafficTypeDefinitionEditorViewModel> TrafficDefinitions { get; } = [];

    public ObservableCollection<TrafficSummaryViewModel> TrafficTypes { get; } = [];

    public ObservableCollection<SubnetworkDefinition> Subnetworks { get; } = [];

    public ObservableCollection<string> NodeIdOptions { get; } = [];

    public ObservableCollection<string> SubnetworkIdOptions { get; } = [];

    public ObservableCollection<string> TrafficTypeNameOptions { get; } = [];

    public ObservableCollection<EdgeTrafficPermissionRowViewModel> EdgeTrafficPermissionDefaults { get; } = [];

    public ObservableCollection<RouteAllocationRowViewModel> VisibleAllocations { get; } = [];

    public ObservableCollection<ConsumerCostSummaryRowViewModel> VisibleConsumerCostSummaries { get; } = [];

    public ObservableCollection<PerishingEventRowViewModel> VisiblePerishingEvents { get; } = [];

    public CanvasViewModel Canvas { get; } = new();

    public LayersPanelViewModel LayersPanel { get; } = new();

    public InspectorPanelViewModel InspectorPanel { get; }

    public ReportsDrawerViewModel ReportsDrawer { get; } = new();

    public TimelineToolbarViewModel TimelineToolbar { get; } = new();

    public UiTerminologyViewModel Terminology { get; } = new();

    public string WindowTitle => HasNetwork
        ? $"World Systems Builder - {NetworkName}{(HasUnsavedChanges ? " *" : string.Empty)}"
        : "World Systems Builder";

    public Array RoutingPreferences { get; } = Enum.GetValues(typeof(RoutingPreference));

    public IReadOnlyList<AllocationModeOptionViewModel> AllocationModeOptions { get; } =
    [
        new(
            AllocationMode.GreedyBestRoute,
            TrafficTypeDefinitionEditorViewModel.GetAllocationModeLabel(AllocationMode.GreedyBestRoute),
            TrafficTypeDefinitionEditorViewModel.GetAllocationModeHelpText(AllocationMode.GreedyBestRoute)),
        new(
            AllocationMode.ProportionalBranchDemand,
            TrafficTypeDefinitionEditorViewModel.GetAllocationModeLabel(AllocationMode.ProportionalBranchDemand),
            TrafficTypeDefinitionEditorViewModel.GetAllocationModeHelpText(AllocationMode.ProportionalBranchDemand))
    ];

    public Array ThemeOptions { get; } = Enum.GetValues(typeof(AppTheme));

    public IReadOnlyList<PlaceTemplate> PlaceTemplateOptions { get; } =
        PlaceTemplateCatalog.Templates;

    public IReadOnlyList<DemographicDemandPreset> DemographicDemandPresetOptions { get; } =
        DemographicDemandPresetCatalog.Presets;

    public IReadOnlyList<BundledScenarioOption> WorldbuilderScenarioOptions { get; } =
        WorldbuilderScenarioCatalog;

    public string ActiveWorldLabel => HasNetwork
        ? $"{NetworkName} | {ActiveFileLabel}"
        : "No world loaded";

    public string? CurrentFilePath
    {
        get => currentFilePath;
        private set
        {
            if (SetProperty(ref currentFilePath, value))
            {
                OnPropertyChanged(nameof(HasActiveFilePath));
            }
        }
    }

    public bool HasActiveFilePath => !string.IsNullOrWhiteSpace(CurrentFilePath);

    public bool HasUnsavedChanges
    {
        get => hasUnsavedChanges;
        private set
        {
            if (SetProperty(ref hasUnsavedChanges, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(ActiveWorldLabel));
            }
        }
    }

    public PlaceTemplate? SelectedPlaceTemplate
    {
        get => selectedPlaceTemplate;
        set => SetProperty(ref selectedPlaceTemplate, value);
    }

    public DemographicDemandPreset? SelectedDemographicDemandPreset
    {
        get => selectedDemographicDemandPreset;
        set
        {
            if (!SetProperty(ref selectedDemographicDemandPreset, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedDemographicDemandPresetDescription));
            OnPropertyChanged(nameof(SelectedDemographicDemandPresetMappingSummary));
        }
    }

    public string SelectedDemographicDemandPresetDescription =>
        SelectedDemographicDemandPreset?.Description ?? "Choose a preset to add baseline demand rows.";

    public string SelectedDemographicDemandPresetMappingSummary =>
        SelectedDemographicDemandPreset?.MappingSummary ?? string.Empty;

    public BundledScenarioOption? SelectedWorldbuilderScenario
    {
        get => selectedWorldbuilderScenario;
        set => SetProperty(ref selectedWorldbuilderScenario, value);
    }

    private void NormalizeEdgeInterfaceBindings(EdgeViewModel edge)
{
    if (edge is null)
    {
        return;
    }

    isNormalizingEdgeInterfaces = true;

    try
    {
        edge.FromInterfaceNodeId = NormalizeEdgeEndpointInterface(edge.FromNodeId, edge.FromInterfaceNodeId);
        edge.ToInterfaceNodeId = NormalizeEdgeEndpointInterface(edge.ToNodeId, edge.ToInterfaceNodeId);
    }
    finally
    {
        isNormalizingEdgeInterfaces = false;
    }
}

private string? NormalizeEdgeEndpointInterface(string? nodeId, string? currentInterfaceNodeId)
{
    var options = GetInterfaceNodeOptionsForEdgeEndpoint(nodeId);
    if (options.Count == 0)
    {
        // Non-composite endpoint, or composite with no exposed interfaces.
        return null;
    }

    if (!string.IsNullOrWhiteSpace(currentInterfaceNodeId))
    {
        var exactMatch = options.FirstOrDefault(option => Comparer.Equals(option, currentInterfaceNodeId));
        if (exactMatch is not null)
        {
            // Preserve the existing valid choice and normalize its casing/text.
            return exactMatch;
        }
    }

    // Auto-pick only when the composite has exactly one exposed interface.
    return options.Count == 1 ? options[0] : null;
}

    public bool IsWorldbuilderMode
    {
        get => isWorldbuilderMode;
        set
        {
            if (!SetProperty(ref isWorldbuilderMode, value))
            {
                return;
            }

            Terminology.IsWorldbuilderMode = value;
            OnPropertyChanged(nameof(SelectedNodeRoleOptions));
            OnPropertyChanged(nameof(SelectedNodeRoleName));
            OnPropertyChanged(nameof(SelectedNodeTrafficRoleHeadline));
        }
    }

    public string ActiveFileLabel
    {
        get => activeFileLabel;
        private set
        {
            if (SetProperty(ref activeFileLabel, value))
            {
                OnPropertyChanged(nameof(ActiveWorldLabel));
            }
        }
    }

    public AppTheme SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (!SetProperty(ref selectedTheme, value))
            {
                return;
            }

            AppThemeManager.ApplyTheme(value);
        }
    }

    public string NetworkName
    {
        get => networkName;
        set
        {
            if (SetProperty(ref networkName, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(ActiveWorldLabel));
                OnPropertyChanged(nameof(SuggestedFileName));
                OnPropertyChanged(nameof(SuggestedGraphMlFileName));
                MarkDirty("Updated the world name.");
            }
        }
    }

    public string NetworkDescription
    {
        get => networkDescription;
        set
        {
            if (SetProperty(ref networkDescription, value))
            {
                MarkDirty("Updated the world description.");
            }
        }
    }

    public AllocationMode DefaultAllocationMode
    {
        get => defaultAllocationMode;
        set
        {
            if (SetProperty(ref defaultAllocationMode, value))
            {
                OnPropertyChanged(nameof(DefaultAllocationModeLabel));
                OnPropertyChanged(nameof(DefaultAllocationModeHelpText));
                MarkDirty("Updated the default allocation mode.");
            }
        }
    }

    public string DefaultAllocationModeLabel => TrafficTypeDefinitionEditorViewModel.GetAllocationModeLabel(DefaultAllocationMode);

    public string DefaultAllocationModeHelpText => TrafficTypeDefinitionEditorViewModel.GetAllocationModeHelpText(DefaultAllocationMode);

    public int? TimelineLoopLength
    {
        get => timelineLoopLength;
        set
        {
            var normalized = value is < 1 ? null : value;
            if (SetProperty(ref timelineLoopLength, normalized))
            {
                OnPropertyChanged(nameof(IsTimelineLoopEnabled));
                OnPropertyChanged(nameof(TimelineHeadline));
                InvalidateSimulationResults("Updated timeline loop settings.");
                MarkDirty("Updated timeline loop settings.");
            }
        }
    }

    public bool IsTimelineLoopEnabled
    {
        get => TimelineLoopLength.HasValue;
        set
        {
            if (value == IsTimelineLoopEnabled)
            {
                return;
            }

            TimelineLoopLength = value ? 12 : null;
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string GraphKeyboardHint
    {
        get => graphKeyboardHint;
        set => SetProperty(ref graphKeyboardHint, value);
    }

    public string FocusedEdgeStatus
    {
        get => focusedEdgeStatus;
        set => SetProperty(ref focusedEdgeStatus, value);
    }

    public bool HasNetwork
    {
        get => hasNetwork;
        private set
        {
            if (SetProperty(ref hasNetwork, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(ActiveWorldLabel));
            }
        }
    }

    public bool IsCanvasOnlyMode
    {
        get => isCanvasOnlyMode;
        private set
        {
            if (SetProperty(ref isCanvasOnlyMode, value))
            {
                OnPropertyChanged(nameof(CanvasOnlyButtonLabel));
                OnPropertyChanged(nameof(RightRailColumnWidth));
                OnPropertyChanged(nameof(RightRailSpacerColumnWidth));
                OnPropertyChanged(nameof(RightRailVisibility));
                OnPropertyChanged(nameof(BottomWorkspaceVisibility));
                OnPropertyChanged(nameof(OptionalSidePanelVisibility));
                OnPropertyChanged(nameof(OptionalSidePanelColumnWidth));
                OnPropertyChanged(nameof(OptionalSidePanelSpacerColumnWidth));
                RaiseOptionalSurfacePropertiesChanged();
            }
        }
    }

    public string CanvasOnlyButtonLabel => IsCanvasOnlyMode ? "Exit Canvas Only" : "Canvas Only";

    public int CurrentPeriod => currentPeriod;

    public string TimelineHeadline => hasTimelineSnapshot
        ? TimelineLoopLength.HasValue
            ? $"Timeline period {CurrentPeriod} (cycle {TemporalNetworkSimulationEngine.GetEffectivePeriod(CurrentPeriod, TimelineLoopLength)} of {TimelineLoopLength})"
            : $"Timeline period {CurrentPeriod}"
        : "Timeline not started";

    public GridLength RightRailColumnWidth => OptionalSidePanelColumnWidth;

    public GridLength OptionalSidePanelColumnWidth => IsCanvasOnlyMode || !IsOptionalSidePanelOpen
        ? new GridLength(0d)
        : new GridLength(300d);

    public GridLength RightRailSpacerColumnWidth => OptionalSidePanelSpacerColumnWidth;

    public GridLength OptionalSidePanelSpacerColumnWidth => IsCanvasOnlyMode || !IsOptionalSidePanelOpen
        ? new GridLength(0d)
        : new GridLength(18d);

    public Visibility RightRailVisibility => OptionalSidePanelVisibility;

    public Visibility OptionalSidePanelVisibility => IsCanvasOnlyMode || !IsOptionalSidePanelOpen
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility BottomWorkspaceVisibility => IsCanvasOnlyMode || !ReportsDrawer.IsOpen
        ? Visibility.Collapsed
        : Visibility.Visible;

    public bool IsOptionalSidePanelOpen => IsLayersPanelOpen || InspectorPanel.IsOpen || IsLegendPanelOpen;

    public bool IsLayersPanelOpen
    {
        get => isLayersPanelOpen;
        set
        {
            if (SetProperty(ref isLayersPanelOpen, value))
            {
                RaiseOptionalSurfacePropertiesChanged();
            }
        }
    }

    public bool IsLegendPanelOpen
    {
        get => isLegendPanelOpen;
        set
        {
            if (SetProperty(ref isLegendPanelOpen, value))
            {
                RaiseOptionalSurfacePropertiesChanged();
            }
        }
    }

    public Visibility LayersPanelVisibility => IsLayersPanelOpen ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LegendPanelVisibility => IsLegendPanelOpen ? Visibility.Visible : Visibility.Collapsed;

    public string RoutesTabHeader => $"Routes ({VisibleAllocations.Count})";

    public string ConsumerCostsTabHeader => $"Consumer Costs ({VisibleConsumerCostSummaries.Count})";

    public string PerishingTabHeader => $"Perishing ({VisiblePerishingEvents.Count})";

    public bool HasReportSnapshot => hasSimulationSnapshot || hasTimelineSnapshot;

    public string RoutesEmptyText => !HasReportSnapshot
        ? "No results yet. Run a simulation or advance the timeline to populate routes."
        : "No data for current layer/filter.";

    public string ConsumerCostsEmptyText => !HasReportSnapshot
        ? "No results yet. Run a simulation to populate consumer costs."
        : "No data for current layer/filter.";

    public string PerishingEmptyText => !hasTimelineSnapshot
        ? "No timeline results yet. Advance the timeline to detect where traffic perishes."
        : "No perishing detected for current layer/filter.";

    public Visibility RoutesEmptyStateVisibility => VisibleAllocations.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility RoutesGridVisibility => VisibleAllocations.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility ConsumerCostsEmptyStateVisibility => VisibleConsumerCostSummaries.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ConsumerCostsGridVisibility => VisibleConsumerCostSummaries.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility PerishingEmptyStateVisibility => VisiblePerishingEvents.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PerishingGridVisibility => VisiblePerishingEvents.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public int NodeCount => Nodes.Count;

    public int EdgeCount => Edges.Count;

    public int TrafficTypeCount => TrafficDefinitions.Count;

    public void ToggleCanvasOnlyMode()
    {
        IsCanvasOnlyMode = !IsCanvasOnlyMode;
    }

    public void ToggleLayersPanel()
    {
        IsLayersPanelOpen = !IsLayersPanelOpen;
    }

    public void ToggleInspectorPanel()
    {
        if (InspectorPanel.IsOpen)
        {
            suppressInspectorAutoOpen = true;
            InspectorPanel.IsOpen = false;
        }
        else
        {
            suppressInspectorAutoOpen = false;
            InspectorPanel.IsOpen = true;
        }

        RaiseOptionalSurfacePropertiesChanged();
    }

    public void ToggleReportsDrawer()
    {
        ReportsDrawer.IsOpen = !ReportsDrawer.IsOpen;
        if (!ReportsDrawer.IsOpen)
        {
            ReportsDrawer.SelectedReportRow = null;
            Canvas.ClearRouteHighlight();
            ApplyRouteHighlights();
        }

        OnPropertyChanged(nameof(BottomWorkspaceVisibility));
    }

    public void ToggleLegendPanel()
    {
        IsLegendPanelOpen = !IsLegendPanelOpen;
    }

    public double WorkspaceMinX
    {
        get => workspaceMinX;
        private set
        {
            if (SetProperty(ref workspaceMinX, value))
            {
                OnPropertyChanged(nameof(WorkspaceTranslateX));
            }
        }
    }

    public double WorkspaceMinY
    {
        get => workspaceMinY;
        private set
        {
            if (SetProperty(ref workspaceMinY, value))
            {
                OnPropertyChanged(nameof(WorkspaceTranslateY));
            }
        }
    }

    public double WorkspaceTranslateX => -WorkspaceMinX;

    public double WorkspaceTranslateY => -WorkspaceMinY;

    public double WorkspaceWidth
    {
        get => workspaceWidth;
        private set => SetProperty(ref workspaceWidth, value);
    }

    public double WorkspaceHeight
    {
        get => workspaceHeight;
        private set => SetProperty(ref workspaceHeight, value);
    }

    public TrafficSummaryViewModel? SelectedTraffic
    {
        get => selectedTraffic;
        set
        {
            if (!SetProperty(ref selectedTraffic, value))
            {
                return;
            }

            OnPropertyChanged(nameof(VisibleAllocationHeadline));
            OnPropertyChanged(nameof(VisibleConsumerCostHeadline));
            if (value is not null)
            {
                InspectorPanel.InspectTraffic(value, ShouldOpenInspectorForSelection());
                RaiseOptionalSurfacePropertiesChanged();
            }
        }
    }

    public NodeViewModel? SelectedNode
    {
        get => selectedNode;
        set
        {
            if (ReferenceEquals(selectedNode, value))
            {
                return;
            }

            if (selectedNode is not null)
            {
                selectedNode.IsSelected = false;
            }

            SetProperty(ref selectedNode, value);

            if (value is not null)
            {
                value.IsSelected = true;
            }

            SelectedNodeTrafficProfile = value?.TrafficProfiles.FirstOrDefault();
            if (value is not null)
            {
                InspectorPanel.InspectNode(value, ShouldOpenInspectorForSelection());
                RaiseOptionalSurfacePropertiesChanged();
            }

            OnPropertyChanged(nameof(SelectedNodeTrafficRoleHeadline));
            OnPropertyChanged(nameof(SelectedNodeShapeOptions));
            OnPropertyChanged(nameof(SelectedNodeShape));
        }
    }

    public NodeTrafficProfileViewModel? SelectedNodeTrafficProfile
    {
        get => selectedNodeTrafficProfile;
        set
        {
            if (ReferenceEquals(selectedNodeTrafficProfile, value))
            {
                return;
            }

            if (selectedNodeTrafficProfile is not null)
            {
                selectedNodeTrafficProfile.PropertyChanged -= HandleSelectedNodeTrafficProfilePropertyChanged;
            }

            if (!SetProperty(ref selectedNodeTrafficProfile, value))
            {
                return;
            }

            if (selectedNodeTrafficProfile is not null)
            {
                selectedNodeTrafficProfile.PropertyChanged += HandleSelectedNodeTrafficProfilePropertyChanged;
            }

            // The node editor binds through these proxy properties so profile swaps do not confuse nested WPF bindings.
            RaiseSelectedNodeTrafficEditorPropertiesChanged();
        }
    }

    public string SelectedNodeTrafficRoleHeadline => SelectedNode is null
        ? $"{Terminology.TrafficType} Roles"
        : $"{Terminology.TrafficType} Roles For {SelectedNode.Name}";

    public IReadOnlyList<string> SelectedNodeRoleOptions => SelectedNodeTrafficProfile?.RoleOptions
        .Select(Terminology.ToDisplayRoleName)
        .ToList() ?? [];

    public string? SelectedNodeTrafficType
    {
        get => SelectedNodeTrafficProfile?.TrafficType;
        set
        {
            if (SelectedNodeTrafficProfile is null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (Comparer.Equals(SelectedNodeTrafficProfile.TrafficType, value))
            {
                return;
            }

            SelectedNodeTrafficProfile.TrafficType = value;
        }
    }

    public string? SelectedNodeRoleName
    {
        get => SelectedNodeTrafficProfile is null
            ? null
            : Terminology.ToDisplayRoleName(SelectedNodeTrafficProfile.SelectedRoleName);
        set
        {
            if (SelectedNodeTrafficProfile is null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var internalRoleName = Terminology.ToInternalRoleName(value);
            if (string.Equals(SelectedNodeTrafficProfile.SelectedRoleName, internalRoleName, StringComparison.Ordinal))
            {
                return;
            }

            SelectedNodeTrafficProfile.SelectedRoleName = internalRoleName;
        }
    }

    public bool IsSelectedNodeProducer => SelectedNodeTrafficProfile?.IsProducer ?? false;

    public bool IsSelectedNodeConsumer => SelectedNodeTrafficProfile?.IsConsumer ?? false;

    public double SelectedNodeProduction
    {
        get => SelectedNodeTrafficProfile?.Production ?? 0d;
        set
        {
            if (SelectedNodeTrafficProfile is null)
            {
                return;
            }

            if (Math.Abs(SelectedNodeTrafficProfile.Production - value) < 0.000001d)
            {
                return;
            }

            SelectedNodeTrafficProfile.Production = value;
        }
    }

    public double SelectedNodeConsumption
    {
        get => SelectedNodeTrafficProfile?.Consumption ?? 0d;
        set
        {
            if (SelectedNodeTrafficProfile is null)
            {
                return;
            }

            if (Math.Abs(SelectedNodeTrafficProfile.Consumption - value) < 0.000001d)
            {
                return;
            }

            SelectedNodeTrafficProfile.Consumption = value;
        }
    }

    public double SelectedNodeConsumerPremiumPerUnit
    {
        get => SelectedNodeTrafficProfile?.ConsumerPremiumPerUnit ?? 0d;
        set
        {
            if (SelectedNodeTrafficProfile is null)
            {
                return;
            }

            if (Math.Abs(SelectedNodeTrafficProfile.ConsumerPremiumPerUnit - value) < 0.000001d)
            {
                return;
            }

            SelectedNodeTrafficProfile.ConsumerPremiumPerUnit = value;
        }
    }

    public int? SelectedNodeProductionStartPeriod
    {
        get => SelectedNodeTrafficProfile?.ProductionStartPeriod;
        set
        {
            if (SelectedNodeTrafficProfile is null)
            {
                return;
            }

            if (SelectedNodeTrafficProfile.ProductionStartPeriod == value)
            {
                return;
            }

            SelectedNodeTrafficProfile.ProductionStartPeriod = value;
        }
    }

    public int? SelectedNodeProductionEndPeriod
    {
        get => SelectedNodeTrafficProfile?.ProductionEndPeriod;
        set
        {
            if (SelectedNodeTrafficProfile is null)
            {
                return;
            }

            if (SelectedNodeTrafficProfile.ProductionEndPeriod == value)
            {
                return;
            }

            SelectedNodeTrafficProfile.ProductionEndPeriod = value;
        }
    }

    public int? SelectedNodeConsumptionStartPeriod
    {
        get => SelectedNodeTrafficProfile?.ConsumptionStartPeriod;
        set
        {
            if (SelectedNodeTrafficProfile is null)
            {
                return;
            }

            if (SelectedNodeTrafficProfile.ConsumptionStartPeriod == value)
            {
                return;
            }

            SelectedNodeTrafficProfile.ConsumptionStartPeriod = value;
        }
    }

    public int? SelectedNodeConsumptionEndPeriod
    {
        get => SelectedNodeTrafficProfile?.ConsumptionEndPeriod;
        set
        {
            if (SelectedNodeTrafficProfile is null)
            {
                return;
            }

            if (SelectedNodeTrafficProfile.ConsumptionEndPeriod == value)
            {
                return;
            }

            SelectedNodeTrafficProfile.ConsumptionEndPeriod = value;
        }
    }

    public bool IsSelectedNodeStore
    {
        get => SelectedNodeTrafficProfile?.IsStore ?? false;
        set
        {
            if (SelectedNodeTrafficProfile is null || SelectedNodeTrafficProfile.IsStore == value)
            {
                return;
            }

            SelectedNodeTrafficProfile.IsStore = value;
        }
    }

    public double? SelectedNodeStoreCapacity
    {
        get => SelectedNodeTrafficProfile?.StoreCapacity;
        set
        {
            if (SelectedNodeTrafficProfile is null || SelectedNodeTrafficProfile.StoreCapacity == value)
            {
                return;
            }

            SelectedNodeTrafficProfile.StoreCapacity = value;
        }
    }

    public IReadOnlyList<NodeVisualShape> SelectedNodeShapeOptions => SelectedNode?.ShapeOptions ?? [];

    public IReadOnlyList<NodeKind> SelectedNodeKindOptions => SelectedNode?.NodeKindOptions ?? [];

    public NodeVisualShape SelectedNodeShape
    {
        get => SelectedNode?.Shape ?? NodeVisualShape.Square;
        set
        {
            if (SelectedNode is null || SelectedNode.Shape == value)
            {
                return;
            }

            SelectedNode.Shape = value;
        }
    }

    public NodeKind SelectedNodeKind
    {
        get => SelectedNode?.NodeKind ?? NodeKind.Ordinary;
        set
        {
            if (SelectedNode is null || SelectedNode.NodeKind == value)
            {
                return;
            }

            SelectedNode.NodeKind = value;
        }
    }

    public string SelectedNodeTrafficSelectionLabel => SelectedNodeTrafficProfile?.SelectionLabel ?? "No traffic role selected";

    public string SelectedNodeTrafficRoleSummary => SelectedNodeTrafficProfile?.RoleSummary ?? "Choose or add a traffic role entry.";

    public EdgeViewModel? SelectedEdge
    {
        get => selectedEdge;
        set
        {
            if (ReferenceEquals(selectedEdge, value))
            {
                return;
            }

            if (selectedEdge is not null)
            {
                selectedEdge.IsSelected = false;
            }

            SetProperty(ref selectedEdge, value);

            if (value is not null)
            {
                value.IsSelected = true;
            }

            if (value is not null)
            {
                InspectorPanel.InspectEdge(value, ShouldOpenInspectorForSelection());
                RaiseOptionalSurfacePropertiesChanged();
            }
        }
    }

    public void SetStatusMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        StatusMessage = message.Trim();
    }

    public TrafficTypeDefinitionEditorViewModel? SelectedTrafficDefinition
    {
        get => selectedTrafficDefinition;
        set => SetProperty(ref selectedTrafficDefinition, value);
    }

    public string VisibleAllocationHeadline =>
        $"{VisibleAllocations.Count} routed movement(s) for {LayersPanel.SelectedDisplayMode}";

    public string VisibleConsumerCostHeadline =>
        $"{VisibleConsumerCostSummaries.Count} consumer cost row(s) for {LayersPanel.SelectedDisplayMode}";

    public string VisiblePerishingHeadline => hasTimelineSnapshot
        ? $"{VisiblePerishingEvents.Count} perishing row(s), {VisiblePerishingEvents.Sum(item => item.Quantity):0.##} unit(s) expired in visible layers."
        : "Advance the timeline to report perishing hotspots.";

    public string SuggestedFileName
    {
        get
        {
            var baseName = Regex.Replace(NetworkName, @"[^\w\-]+", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "network";
            }

            return $"{baseName}.json";
        }
    }

    public string SuggestedGraphMlFileName
    {
        get
        {
            var baseName = Regex.Replace(NetworkName, @"[^\w\-]+", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "network";
            }

            return $"{baseName}.graphml";
        }
    }

    public string SuggestedReportFileName
    {
        get
        {
            var baseName = Regex.Replace(NetworkName, @"[^\w\-]+", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "network";
            }

            return $"{baseName}-report.html";
        }
    }

    public string SuggestedReportFilePath
    {
        get
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documentsPath, SuggestedReportFileName);
        }
    }

    public void CreateNewNetwork()
    {
        LoadNetwork(
            new NetworkModel
            {
                Name = "Untitled World",
                Description = "Sketch places, routes, and flows first; add simulation detail when the world needs it."
            },
            null,
            "Created a blank world. Add places, connect routes, then define flows.");
    }

    public void LoadFromFile(string path)
    {
        var network = fileService.Load(path);
        LoadNetwork(network, path, $"Loaded network file '{Path.GetFileName(path)}'.");
    }

    public void LoadFromGraphMl(string path, GraphMlTransferOptions options)
    {
        var network = graphMlFileService.Load(path, options);
        LoadNetwork(network, null, $"Imported GraphML file '{Path.GetFileName(path)}'.");
        ActiveFileLabel = Path.GetFileName(path);
    }

    public void SaveToFile(string path)
    {
        var network = BuildValidatedNetwork();
        fileService.Save(network, path);
        MarkClean(path);
        StatusMessage = $"Saved the current network to '{Path.GetFileName(path)}'.";
    }

    public void SaveToGraphMl(string path, GraphMlTransferOptions options)
    {
        var network = BuildValidatedNetwork();
        graphMlFileService.Save(network, path, options);
        ActiveFileLabel = path;
        StatusMessage = $"Exported the current network to GraphML file '{Path.GetFileName(path)}'.";
    }

    public void MarkDirty(string statusMessage)
    {
        if (isReplacingNetwork)
        {
            return;
        }

        if (HasNetwork)
        {
            HasUnsavedChanges = true;
        }

        StatusMessage = statusMessage;
    }

    private void MarkClean(string? savedPath)
    {
        CurrentFilePath = string.IsNullOrWhiteSpace(savedPath) ? null : savedPath;
        ActiveFileLabel = CurrentFilePath ?? "Unsaved world";
        HasUnsavedChanges = false;
    }

    public void ExportCurrentReport(string path, ReportExportFormat format)
    {
        var network = BuildValidatedNetwork();
        var outcomes = simulationEngine.Simulate(network);
        var consumerCosts = simulationEngine.SummarizeConsumerCosts(outcomes);
        reportExportService.SaveCurrentReport(network, outcomes, consumerCosts, path, format);
        StatusMessage = $"Exported the current report to '{Path.GetFileName(path)}'.";
    }

    public void ExportTimelineReport(string path, int periods, ReportExportFormat format)
    {
        var network = BuildValidatedNetwork();
        var state = temporalSimulationEngine.Initialize(network);
        var results = new List<TemporalNetworkSimulationEngine.TemporalSimulationStepResult>(periods);

        for (var period = 0; period < periods; period++)
        {
            results.Add(temporalSimulationEngine.Advance(network, state));
        }

        reportExportService.SaveTimelineReport(network, results, path, format);
        StatusMessage = $"Exported the timeline report for {periods} period(s) to '{Path.GetFileName(path)}'.";
    }

    public void LoadBundledSample()
    {
        LoadBundledScenario(BundledSampleScenario, "Loaded the bundled sample network.");
    }

    public void LoadSelectedWorldbuilderScenario()
    {
        var scenario = SelectedWorldbuilderScenario ?? WorldbuilderScenarioCatalog.FirstOrDefault();
        if (scenario is null)
        {
            throw new InvalidOperationException("No worldbuilder scenarios are available.");
        }

        LoadBundledScenario(scenario, $"Loaded the {scenario.DisplayName} worldbuilder scenario.");
    }

    private void LoadBundledScenario(BundledScenarioOption scenario, string successMessage)
    {
        var samplePath = Path.Combine(AppContext.BaseDirectory, "Samples", scenario.FileName);
        if (File.Exists(samplePath))
        {
            var fileNetwork = fileService.Load(samplePath);
            LoadNetwork(fileNetwork, null, successMessage);
            ActiveFileLabel = scenario.DisplayName;
            return;
        }

        using var stream = typeof(MainWindowViewModel).Assembly.GetManifestResourceStream(scenario.ResourceName);
        if (stream is null)
        {
            throw new FileNotFoundException($"The bundled scenario '{scenario.DisplayName}' was not found.", samplePath);
        }

        using var reader = new StreamReader(stream);
        var resourceNetwork = fileService.LoadJson(reader.ReadToEnd());
        LoadNetwork(resourceNetwork, null, successMessage);
        ActiveFileLabel = scenario.DisplayName;
    }

    public void RunSimulation()
    {
        if (!HasNetwork)
        {
            return;
        }

        var current = BuildValidatedNetwork();
        var outcomes = simulationEngine.Simulate(current);
        var outcomesByTraffic = outcomes.ToDictionary(outcome => outcome.TrafficType, outcome => outcome, Comparer);

        foreach (var traffic in TrafficTypes)
        {
            traffic.ClearOutcome();
            if (outcomesByTraffic.TryGetValue(traffic.Name, out var outcome))
            {
                traffic.ApplyOutcome(outcome);
            }
        }

        hasSimulationSnapshot = true;
        allAllocationModels.Clear();
        allAllocationModels.AddRange(outcomes.SelectMany(outcome => outcome.Allocations));
        allAllocations.Clear();
        allAllocations.AddRange(allAllocationModels.Select(allocation => new RouteAllocationRowViewModel(allocation)));
        allConsumerCostSummaries.Clear();
        allConsumerCostSummaries.AddRange(
            simulationEngine.SummarizeConsumerCosts(outcomes)
                .Select(summary => new ConsumerCostSummaryRowViewModel(summary)));
        allPerishingEvents.Clear();
        VisiblePerishingEvents.Clear();
        RefreshVisibleAllocations();
        RefreshVisibleConsumerCostSummaries();
        RefreshVisiblePerishingEvents();
        RefreshFlowVisuals();
        RaiseReportStatePropertiesChanged();

        var totalDelivered = outcomes.Sum(outcome => outcome.TotalDelivered);
        StatusMessage = $"Simulation complete. Routed {allAllocations.Count} movement(s) delivering {totalDelivered:0.##} unit(s).";
    }

    public void ResetTimeline()
    {
        temporalSimulationState = null;
        lastTimelineStepResult = null;
        currentPeriod = 0;
        hasTimelineSnapshot = false;
        allPerishingEvents.Clear();
        VisiblePerishingEvents.Clear();
        OnPropertyChanged(nameof(CurrentPeriod));
        OnPropertyChanged(nameof(TimelineHeadline));
        OnPropertyChanged(nameof(VisiblePerishingHeadline));
        TimelineToolbar.CurrentPeriod = CurrentPeriod;
        TimelineToolbar.Headline = TimelineHeadline;
        ClearTimelineVisuals();
        RaiseReportStatePropertiesChanged();
        StatusMessage = "Reset the time-step simulation.";
    }

    public void AdvanceTimeline()
    {
        if (!HasNetwork)
        {
            return;
        }

        var current = BuildValidatedNetwork();
        temporalSimulationState ??= temporalSimulationEngine.Initialize(current);
        var stepResult = temporalSimulationEngine.Advance(current, temporalSimulationState);
        lastTimelineStepResult = stepResult;
        currentPeriod = stepResult.Period;
        hasTimelineSnapshot = true;
        OnPropertyChanged(nameof(CurrentPeriod));
        OnPropertyChanged(nameof(TimelineHeadline));
        Canvas.CurrentPeriod = CurrentPeriod;
        TimelineToolbar.CurrentPeriod = CurrentPeriod;
        TimelineToolbar.Headline = TimelineHeadline;

        allAllocationModels.Clear();
        allAllocationModels.AddRange(stepResult.Allocations);
        allAllocations.Clear();
        allAllocations.AddRange(stepResult.Allocations.Select(allocation => new RouteAllocationRowViewModel(allocation)));
        allConsumerCostSummaries.Clear();
        allConsumerCostSummaries.AddRange(
            simulationEngine.SummarizeConsumerCosts(stepResult.Allocations)
                .Select(summary => new ConsumerCostSummaryRowViewModel(summary)));
        allPerishingEvents.Clear();
        allPerishingEvents.AddRange(BuildPerishingEventRows(stepResult));
        RefreshVisibleAllocations();
        RefreshVisibleConsumerCostSummaries();
        RefreshVisiblePerishingEvents();
        ApplyTimelineVisuals(stepResult);
        StatusMessage = $"Advanced the timeline to period {currentPeriod}. {stepResult.InFlightMovementCount} movement(s) remain in flight.";
    }

    public void AutoArrangeNodes()
    {
        if (!HasNetwork)
        {
            return;
        }

        var current = BuildValidatedNetwork();
        var arranged = fileService.AutoArrange(current);
        var arrangedNodesById = arranged.Nodes.ToDictionary(node => node.Id, Comparer);

        // Keep the existing node view models and only update coordinates so in-memory edits are preserved.
        foreach (var node in Nodes)
        {
            if (!arrangedNodesById.TryGetValue(node.Id, out var arrangedNode))
            {
                continue;
            }

            if (arrangedNode.X.HasValue)
            {
                node.X = arrangedNode.X.Value;
            }

            if (arrangedNode.Y.HasValue)
            {
                node.Y = arrangedNode.Y.Value;
            }
        }

        RecalculateWorkspace();
        MarkDirty("Auto-arranged all node positions.");
        StatusMessage = "Auto-arranged all node positions.";
    }

    public void AddTrafficDefinition()
    {
        EnsureNetworkExists();

        CreateTrafficDefinition();
        RefreshDerivedStateAfterStructureChange("Added a new traffic type.");
    }

    public void RemoveSelectedTrafficDefinition()
    {
        if (SelectedTrafficDefinition is null)
        {
            return;
        }

        UnregisterTrafficDefinition(SelectedTrafficDefinition);
        TrafficDefinitions.Remove(SelectedTrafficDefinition);
        SelectedTrafficDefinition = null;
        RefreshDerivedStateAfterStructureChange("Removed the selected traffic type definition.");
    }

    public void ApplyDefaultAllocationModeToAllTrafficDefinitions()
    {
        EnsureNetworkExists();

        var definitions = TrafficDefinitions.ToList();
        if (definitions.Count == 0)
        {
            StatusMessage = "No traffic types exist yet. New traffic types will use the selected default allocation mode.";
            return;
        }

        isBulkUpdatingTrafficDefinitions = true;
        try
        {
            foreach (var definition in definitions)
            {
                definition.AllocationMode = DefaultAllocationMode;
            }
        }
        finally
        {
            isBulkUpdatingTrafficDefinitions = false;
        }

        RefreshDerivedStateAfterStructureChange($"Applied {DefaultAllocationModeLabel.ToLowerInvariant()} to all {definitions.Count} traffic type(s).");
    }

    public void AddNode()
    {
        AddNodeAt(null, null);
    }

    public void AddSubnetworkFromFile(string path)
    {
        EnsureNetworkExists();
        var child = fileService.Load(path);
        var baseId = Regex.Replace(Path.GetFileNameWithoutExtension(path), @"[^\w\-]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = "subnetwork";
        }

        var subnetworkId = GetNextUniqueName(baseId, Subnetworks.Select(subnetwork => subnetwork.Id));
        var subnetwork = new SubnetworkDefinition
        {
            Id = subnetworkId,
            DisplayName = child.Name,
            Network = child
        };
        Subnetworks.Add(subnetwork);

        var node = AddCompositeSubnetworkNode(subnetwork);
        var interfaceCount = child.Nodes.Count(childNode => childNode.IsExternalInterface);
        var statusMessage = $"Embedded subnetwork '{child.Name}' as '{subnetworkId}' and placed composite node '{node.Name}'.";
        if (interfaceCount == 0)
        {
            statusMessage += " The child network currently exposes no external interface nodes.";
        }

        RefreshDerivedStateAfterStructureChange(statusMessage);
    }

    public void AddNodeAt(double? x, double? y)
    {
        AddNodeAt(null, x, y);
    }

    public void AddNodeFromSelectedTemplate()
    {
        AddNodeFromSelectedTemplateAt(null, null);
    }

    public void AddNodeFromSelectedTemplateAt(double? x, double? y)
    {
        var template = SelectedPlaceTemplate ?? PlaceTemplateCatalog.Templates.FirstOrDefault();
        AddNodeAt(template, x, y);
    }

    public void ApplySelectedDemographicDemandPresetToSelectedNode()
    {
        if (SelectedNode is null)
        {
            throw new InvalidOperationException("Select a node before applying a demographic demand preset.");
        }

        var preset = SelectedDemographicDemandPreset ?? DemographicDemandPresetCatalog.Presets.FirstOrDefault();
        if (preset is null)
        {
            throw new InvalidOperationException("No demographic demand presets are available.");
        }

        ApplyDemographicDemandPreset(SelectedNode, preset);
    }

    private void AddNodeAt(PlaceTemplate? template, double? x, double? y)
    {
        EnsureNetworkExists();

        var nodeIndex = Nodes.Count + 1;
        var nodeModel = template is null
            ? CreateDefaultNodeModel(nodeIndex, x, y)
            : CreateTemplatedNodeModel(template, nodeIndex, x, y);
        var node = new NodeViewModel(nodeModel);
        var initialProfile = node.TrafficProfiles.FirstOrDefault();

        RegisterNode(node);
        SelectedNode = node;
        SelectedNodeTrafficProfile = initialProfile;
        RefreshDerivedStateAfterStructureChange(
            template is null
                ? "Added a new node."
                : $"Added a new {template.DisplayName.ToLowerInvariant()} place from template.");
    }

    private NodeViewModel AddCompositeSubnetworkNode(SubnetworkDefinition subnetwork)
    {
        var nodeModel = CreateCompositeSubnetworkNodeModel(subnetwork, Nodes.Count + 1);
        var node = new NodeViewModel(nodeModel);

        RegisterNode(node);
        SelectedNode = node;
        SelectedNodeTrafficProfile = node.TrafficProfiles.FirstOrDefault();
        return node;
    }

    private void ApplyDemographicDemandPreset(NodeViewModel node, DemographicDemandPreset preset)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(preset);

        isBulkUpdatingTrafficProfiles = true;

        try
        {
            foreach (var row in preset.TrafficRows)
            {
                EnsureTrafficDefinition(row.TrafficType);

                var profile = node.TrafficProfiles
                    .FirstOrDefault(item => Comparer.Equals(item.TrafficType, row.TrafficType));
                if (profile is null)
                {
                    profile = new NodeTrafficProfileViewModel(new NodeTrafficProfile
                    {
                        TrafficType = row.TrafficType
                    });
                    node.AddTrafficProfile(profile);
                }

                profile.Consumption = Math.Max(profile.Consumption, row.Consumption);
                profile.ConsumerPremiumPerUnit = Math.Max(profile.ConsumerPremiumPerUnit, row.ConsumerPremiumPerUnit);
                profile.CanTransship |= row.CanTransship;
                if (row.StoreCapacity.HasValue)
                {
                    profile.IsStore = true;
                    profile.StoreCapacity = profile.StoreCapacity.HasValue
                        ? Math.Max(profile.StoreCapacity.Value, row.StoreCapacity.Value)
                        : row.StoreCapacity;
                }
            }

            NormalizeNodeTrafficProfiles(node);
        }
        finally
        {
            isBulkUpdatingTrafficProfiles = false;
        }

        SelectedNodeTrafficProfile = node.TrafficProfiles.FirstOrDefault();
        RefreshDerivedStateAfterStructureChange($"Applied the {preset.DisplayName} demographic demand preset to '{node.Name}'.");
    }

    public void RemoveSelectedNode()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var node = SelectedNode;
        var edgesToRemove = Edges
            .Where(edge => Comparer.Equals(edge.FromNodeId, node.Id) || Comparer.Equals(edge.ToNodeId, node.Id))
            .ToList();

        foreach (var edge in edgesToRemove)
        {
            UnregisterEdge(edge);
            Edges.Remove(edge);
        }

        UnregisterNode(node);
        Nodes.Remove(node);
        SelectedNode = null;
        SelectedNodeTrafficProfile = null;
        RefreshDerivedStateAfterStructureChange("Removed the selected node and any connected edges.");
    }

    public void AddTrafficProfileToSelectedNode()
    {
        if (SelectedNode is null)
        {
            throw new InvalidOperationException("Select a node before adding a traffic profile.");
        }

        var primaryTrafficDefinition = EnsurePrimaryTrafficDefinition();
        var trafficName = TrafficDefinitions
            .Select(definition => definition.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .FirstOrDefault(name => SelectedNode.TrafficProfiles.All(profile => !Comparer.Equals(profile.TrafficType, name)))
            ?? primaryTrafficDefinition.Name;

        var createdTrafficDefinition = false;
        if (SelectedNode.TrafficProfiles.Any(profile => Comparer.Equals(profile.TrafficType, trafficName)))
        {
            trafficName = CreateTrafficDefinition().Name;
            createdTrafficDefinition = true;
        }

        var profile = new NodeTrafficProfileViewModel(new NodeTrafficProfile
        {
            TrafficType = trafficName
        });

        SelectedNode.AddTrafficProfile(profile);
        SelectedNodeTrafficProfile = profile;
        RefreshDerivedStateAfterStructureChange(
            createdTrafficDefinition
                ? "Added a traffic profile to the selected node and created a new traffic type for it."
                : "Added a traffic profile to the selected node.");
    }

    public void RemoveSelectedTrafficProfileFromNode()
    {
        if (SelectedNode is null || SelectedNodeTrafficProfile is null)
        {
            return;
        }

        SelectedNode.RemoveTrafficProfile(SelectedNodeTrafficProfile);
        SelectedNodeTrafficProfile = SelectedNode.TrafficProfiles.FirstOrDefault();
        RefreshDerivedStateAfterStructureChange("Removed the selected traffic profile.");
    }

    public void AddProductionWindowToSelectedProfile()
    {
        if (SelectedNodeTrafficProfile is null)
        {
            return;
        }

        SelectedNodeTrafficProfile.ProductionWindows.Add(new PeriodWindowViewModel(new PeriodWindow()));
    }

    public void RemoveProductionWindowFromSelectedProfile(PeriodWindowViewModel window)
    {
        if (SelectedNodeTrafficProfile is null)
        {
            return;
        }

        SelectedNodeTrafficProfile.ProductionWindows.Remove(window);
    }

    public void AddConsumptionWindowToSelectedProfile()
    {
        if (SelectedNodeTrafficProfile is null)
        {
            return;
        }

        SelectedNodeTrafficProfile.ConsumptionWindows.Add(new PeriodWindowViewModel(new PeriodWindow()));
    }

    public void RemoveConsumptionWindowFromSelectedProfile(PeriodWindowViewModel window)
    {
        if (SelectedNodeTrafficProfile is null)
        {
            return;
        }

        SelectedNodeTrafficProfile.ConsumptionWindows.Remove(window);
    }

    public void AddInputRequirementToSelectedProfile()
    {
        if (SelectedNodeTrafficProfile is null)
        {
            return;
        }

        var defaultTrafficType = TrafficTypeNameOptions.FirstOrDefault(name => !Comparer.Equals(name, SelectedNodeTrafficProfile.TrafficType))
            ?? TrafficTypeNameOptions.FirstOrDefault()
            ?? string.Empty;

        SelectedNodeTrafficProfile.InputRequirements.Add(new ProductionInputRequirementViewModel(new ProductionInputRequirement
        {
            TrafficType = defaultTrafficType,
            InputQuantity = 1d,
            OutputQuantity = 1d
        }));
    }

    public void RemoveInputRequirementFromSelectedProfile(ProductionInputRequirementViewModel requirement)
    {
        if (SelectedNodeTrafficProfile is null)
        {
            return;
        }

        SelectedNodeTrafficProfile.InputRequirements.Remove(requirement);
    }

    public void AddEdge()
    {
         EnsureNetworkExists();

    if (Nodes.Count < 2)
    {
        throw new InvalidOperationException("Add at least two nodes before creating an edge.");
    }

    var edge = new EdgeViewModel(
        new EdgeModel
        {
            Id = GetNextUniqueName("E", Edges.Select(item => item.Id)),
            FromNodeId = Nodes[0].Id,
            ToNodeId = Nodes[1].Id,
            Time = 1d,
            Cost = 1d,
            IsBidirectional = true
        },
        Nodes[0],
        Nodes[1]);

    NormalizeEdgeInterfaceBindings(edge);

    RegisterEdge(edge);
    SelectedEdge = edge;
    RefreshDerivedStateAfterStructureChange("Added a new edge.");
    }

    public void AddEdgeBetween(NodeViewModel fromNode, NodeViewModel toNode)
    {
        AddEdgeBetween(fromNode, toNode, isBidirectional: true);
    }

    public void AddEdgeBetween(NodeViewModel fromNode, NodeViewModel toNode, bool isBidirectional)
    {
        ArgumentNullException.ThrowIfNull(fromNode);
    ArgumentNullException.ThrowIfNull(toNode);

    EnsureNetworkExists();

    if (Comparer.Equals(fromNode.Id, toNode.Id))
    {
        throw new InvalidOperationException("Choose two different nodes to create an edge.");
    }

    var edge = new EdgeViewModel(
        new EdgeModel
        {
            Id = GetNextUniqueName("E", Edges.Select(item => item.Id)),
            FromNodeId = fromNode.Id,
            ToNodeId = toNode.Id,
            Time = 1d,
            Cost = 1d,
            IsBidirectional = isBidirectional
        },
        fromNode,
        toNode);

    NormalizeEdgeInterfaceBindings(edge);

    RegisterEdge(edge);
    SelectedEdge = edge;
    var edgeDirectionLabel = isBidirectional ? "bidirectional" : "one-way";
    RefreshDerivedStateAfterStructureChange($"Added a {edgeDirectionLabel} edge from '{fromNode.Name}' to '{toNode.Name}'.");
    }

    public void RemoveSelectedEdge()
    {
        if (SelectedEdge is null)
        {
            return;
        }

        var edge = SelectedEdge;
        UnregisterEdge(edge);
        Edges.Remove(edge);
        SelectedEdge = null;
        RefreshDerivedStateAfterStructureChange("Removed the selected edge.");
    }

    public IReadOnlyList<string> GetCompositeInterfaceSummaries(string? parentCompositeNodeId)
{
    if (string.IsNullOrWhiteSpace(parentCompositeNodeId))
    {
        return [];
    }

    var parentNode = Nodes.FirstOrDefault(item => Comparer.Equals(item.Id, parentCompositeNodeId));
    if (parentNode is null || !parentNode.IsCompositeSubnetwork || string.IsNullOrWhiteSpace(parentNode.ReferencedSubnetworkId))
    {
        return [];
    }

    var subnetwork = Subnetworks.FirstOrDefault(item => Comparer.Equals(item.Id, parentNode.ReferencedSubnetworkId));
    if (subnetwork?.Network?.Nodes is null)
    {
        return [];
    }

    return subnetwork.Network.Nodes
        .Where(childNode => childNode.IsExternalInterface)
        .OrderBy(childNode => GetInterfaceDisplayName(childNode), Comparer)
        .Select(BuildCompositeInterfaceSummaryLine)
        .ToList();
}

public string? GetCompositeInterfaceSummary(string? parentCompositeNodeId, string? childInterfaceNodeId)
{
    if (string.IsNullOrWhiteSpace(parentCompositeNodeId) || string.IsNullOrWhiteSpace(childInterfaceNodeId))
    {
        return null;
    }

    var parentNode = Nodes.FirstOrDefault(item => Comparer.Equals(item.Id, parentCompositeNodeId));
    if (parentNode is null || !parentNode.IsCompositeSubnetwork || string.IsNullOrWhiteSpace(parentNode.ReferencedSubnetworkId))
    {
        return null;
    }

    var subnetwork = Subnetworks.FirstOrDefault(item => Comparer.Equals(item.Id, parentNode.ReferencedSubnetworkId));
    var childNode = subnetwork?.Network?.Nodes?.FirstOrDefault(node =>
        Comparer.Equals(node.Id, childInterfaceNodeId) && node.IsExternalInterface);

    return childNode is null
        ? null
        : BuildCompositeInterfaceSummaryLine(childNode);
}

private string BuildCompositeInterfaceSummaryLine(NodeModel childNode)
{
    var displayName = GetInterfaceDisplayName(childNode);
    var parts = new List<string>();

    var produced = childNode.TrafficProfiles
        .Where(profile => profile.Production > Epsilon)
        .Select(profile => profile.TrafficType)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(Comparer)
        .OrderBy(name => name, Comparer)
        .ToList();

    var consumed = childNode.TrafficProfiles
        .Where(profile => profile.Consumption > Epsilon)
        .Select(profile => profile.TrafficType)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(Comparer)
        .OrderBy(name => name, Comparer)
        .ToList();

    var stored = childNode.TrafficProfiles
        .Where(profile => profile.IsStore)
        .Select(profile => profile.TrafficType)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(Comparer)
        .OrderBy(name => name, Comparer)
        .ToList();

    var relays = childNode.TrafficProfiles.Any(profile => profile.CanTransship);

    if (produced.Count > 0)
    {
        parts.Add($"produces {FormatInterfaceTrafficList(produced)}");
    }

    if (consumed.Count > 0)
    {
        parts.Add($"needs {FormatInterfaceTrafficList(consumed)}");
    }

    if (stored.Count > 0)
    {
        parts.Add($"stores {FormatInterfaceTrafficList(stored)}");
    }

    if (relays)
    {
        parts.Add("relays");
    }

    if (parts.Count == 0)
    {
        parts.Add("no production, need, storage, or relay role configured");
    }

    return $"{childNode.Id} ({displayName}): {string.Join("; ", parts)}";
}

private static string GetInterfaceDisplayName(NodeModel childNode)
{
    return string.IsNullOrWhiteSpace(childNode.InterfaceName)
        ? childNode.Id
        : childNode.InterfaceName.Trim();
}

private static string FormatInterfaceTrafficList(IReadOnlyList<string> items)
{
    return items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}"
    };
}

    public IReadOnlyList<string> GetInterfaceNodeOptionsForEdgeEndpoint(string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return [];
        }

        var node = Nodes.FirstOrDefault(item => Comparer.Equals(item.Id, nodeId));
        if (node is null || node.NodeKind != NodeKind.CompositeSubnetwork || string.IsNullOrWhiteSpace(node.ReferencedSubnetworkId))
        {
            return [];
        }

        var subnetwork = Subnetworks.FirstOrDefault(item => Comparer.Equals(item.Id, node.ReferencedSubnetworkId));
        if (subnetwork is null)
        {
            return [];
        }

        return subnetwork.Network.Nodes
            .Where(childNode => childNode.IsExternalInterface)
            .Select(childNode => childNode.Id)
            .OrderBy(id => id, Comparer)
            .ToList();
    }

    public void MoveNode(NodeViewModel node, double deltaX, double deltaY)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.MoveBy(deltaX, deltaY);
        RecalculateWorkspace();
        MarkDirty("Moved a node.");
    }

    public IReadOnlyList<string> GetAvailableTrafficTypeNames()
    {
        return TrafficDefinitions
            .Select(definition => definition.Name)
            .Concat(Nodes.SelectMany(node => node.TrafficProfiles).Select(profile => profile.TrafficType))
            .Concat(Nodes.SelectMany(node => node.TrafficProfiles).SelectMany(profile => profile.InputRequirements).Select(requirement => requirement.TrafficType))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(Comparer)
            .OrderBy(name => name, Comparer)
            .ToList();
    }

    public void ApplyTrafficRoleToAllNodes(BulkApplyTrafficRoleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        EnsureNetworkExists();
        if (Nodes.Count == 0)
        {
            throw new InvalidOperationException("Add at least one node before applying a traffic role to all nodes.");
        }

        var normalizedTrafficType = options.TrafficType.Trim();
        var normalizedRoleName = string.IsNullOrWhiteSpace(options.RoleName)
            ? NodeTrafficRoleCatalog.NoTrafficRole
            : options.RoleName.Trim();
        var hasRoleFlags = NodeTrafficRoleCatalog.TryParseFlags(normalizedRoleName, out var roleFlags);
        var clearsTrafficRole = !hasRoleFlags || (!roleFlags.IsProducer && !roleFlags.IsConsumer && !roleFlags.CanTransship);

        if (!clearsTrafficRole)
        {
            EnsureTrafficDefinition(normalizedTrafficType);
        }

        isBulkUpdatingTrafficProfiles = true;

        try
        {
            foreach (var node in Nodes)
            {
                var matchingProfiles = node.TrafficProfiles
                    .Where(profile => Comparer.Equals(profile.TrafficType, normalizedTrafficType))
                    .ToList();

                if (clearsTrafficRole)
                {
                    foreach (var matchingProfile in matchingProfiles)
                    {
                        if (ReferenceEquals(SelectedNode, node) && ReferenceEquals(SelectedNodeTrafficProfile, matchingProfile))
                        {
                            SelectedNodeTrafficProfile = null;
                        }

                        node.RemoveTrafficProfile(matchingProfile);
                    }

                    continue;
                }

                var profile = matchingProfiles.FirstOrDefault();
                if (profile is null)
                {
                    profile = new NodeTrafficProfileViewModel(new NodeTrafficProfile
                    {
                        TrafficType = normalizedTrafficType
                    });
                    node.AddTrafficProfile(profile);
                }

                profile.TrafficType = normalizedTrafficType;
                profile.Production = roleFlags.IsProducer ? options.ProductionAmount : 0d;
                profile.Consumption = roleFlags.IsConsumer ? options.ConsumptionAmount : 0d;
                profile.CanTransship = roleFlags.CanTransship;

                if (options.ApplyTranshipmentCapacity && roleFlags.CanTransship)
                {
                    node.TranshipmentCapacity = options.TranshipmentCapacity;
                }

                NormalizeNodeTrafficProfiles(node);
            }
        }
        finally
        {
            isBulkUpdatingTrafficProfiles = false;
        }

        if (SelectedNode is not null)
        {
            SelectedNodeTrafficProfile = SelectedNode.TrafficProfiles
                .FirstOrDefault(profile => Comparer.Equals(profile.TrafficType, normalizedTrafficType))
                ?? SelectedNode.TrafficProfiles.FirstOrDefault();
        }

        var statusMessage = clearsTrafficRole
            ? $"Removed traffic type '{normalizedTrafficType}' from all {Nodes.Count} node(s)."
            : $"Applied role '{normalizedRoleName}' for traffic type '{normalizedTrafficType}' to all {Nodes.Count} node(s).";
        RefreshDerivedStateAfterStructureChange(statusMessage);
    }

    private void EnsureNetworkExists()
    {
        if (!HasNetwork)
        {
            CreateNewNetwork();
        }
    }

    private TrafficTypeDefinitionEditorViewModel EnsurePrimaryTrafficDefinition()
    {
        var existingDefinition = TrafficDefinitions.FirstOrDefault(definition => !string.IsNullOrWhiteSpace(definition.Name));
        if (existingDefinition is not null)
        {
            return existingDefinition;
        }

        return CreateTrafficDefinition();
    }

    private NodeModel CreateDefaultNodeModel(int nodeIndex, double? x, double? y)
    {
        var primaryTrafficDefinition = EnsurePrimaryTrafficDefinition();
        return new NodeModel
        {
            Id = GetNextUniqueName("N", Nodes.Select(item => item.Id)),
            Name = $"Node {nodeIndex}",
            X = x ?? 220d + ((nodeIndex - 1) % 4 * 220d),
            Y = y ?? 180d + ((nodeIndex - 1) / 4 * 170d),
            TrafficProfiles =
            [
                new NodeTrafficProfile
                {
                    TrafficType = primaryTrafficDefinition.Name
                }
            ]
        };
    }

    private NodeModel CreateTemplatedNodeModel(PlaceTemplate template, int nodeIndex, double? x, double? y)
    {
        foreach (var profile in template.TrafficProfiles)
        {
            EnsureTrafficDefinition(profile.TrafficType);
        }

        return new NodeModel
        {
            Id = GetNextUniqueName("N", Nodes.Select(item => item.Id)),
            Name = GetNextUniqueName($"{template.NamePrefix} ", Nodes.Select(item => item.Name)),
            X = x ?? 220d + ((nodeIndex - 1) % 4 * 220d),
            Y = y ?? 180d + ((nodeIndex - 1) / 4 * 170d),
            TranshipmentCapacity = template.TranshipmentCapacity,
            PlaceType = template.PlaceType,
            LoreDescription = template.LoreDescription,
            Tags = template.Tags.ToList(),
            TemplateId = template.Id,
            TrafficProfiles = template.TrafficProfiles
                .Select(profile => profile.ToNodeTrafficProfile())
                .ToList()
        };
    }

    private NodeModel CreateCompositeSubnetworkNodeModel(SubnetworkDefinition subnetwork, int nodeIndex)
    {
        var baseName = string.IsNullOrWhiteSpace(subnetwork.DisplayName)
            ? subnetwork.Id
            : subnetwork.DisplayName.Trim();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "Subnetwork";
        }

        return new NodeModel
        {
            Id = GetNextUniqueName("N", Nodes.Select(item => item.Id)),
            Name = GetNextUniqueName(baseName, Nodes.Select(item => item.Name)),
            Shape = NodeVisualShape.Building,
            NodeKind = NodeKind.CompositeSubnetwork,
            ReferencedSubnetworkId = subnetwork.Id,
            X = 220d + ((nodeIndex - 1) % 4 * 220d),
            Y = 180d + ((nodeIndex - 1) / 4 * 170d),
            TrafficProfiles = []
        };
    }

    private TrafficTypeDefinitionEditorViewModel EnsureTrafficDefinition(string trafficTypeName)
    {
        var normalizedName = trafficTypeName.Trim();
        var existingDefinition = TrafficDefinitions.FirstOrDefault(definition => Comparer.Equals(definition.Name, normalizedName));
        if (existingDefinition is not null)
        {
            return existingDefinition;
        }

        var definition = new TrafficTypeDefinitionEditorViewModel(new TrafficTypeDefinition
        {
            Name = normalizedName,
            RoutingPreference = RoutingPreference.TotalCost,
            AllocationMode = DefaultAllocationMode
        });

        RegisterTrafficDefinition(definition);
        return definition;
    }

    private void LoadBundledSampleIfAvailable()
    {
        try
        {
            LoadBundledSample();
        }
        catch
        {
            CreateNewNetwork();
        }
    }

    private void LoadNetwork(NetworkModel network, string? activeFilePath, string successMessage)
    {
        isReplacingNetwork = true;

        try
        {
            foreach (var node in Nodes.ToList())
            {
                UnregisterNode(node);
            }

            foreach (var edge in Edges.ToList())
            {
                UnregisterEdge(edge);
            }

            foreach (var definition in TrafficDefinitions.ToList())
            {
                UnregisterTrafficDefinition(definition);
            }

            Nodes.Clear();
            Edges.Clear();
            TrafficDefinitions.Clear();
            TrafficTypes.Clear();
            Subnetworks.Clear();
            NodeIdOptions.Clear();
            SubnetworkIdOptions.Clear();
            TrafficTypeNameOptions.Clear();
            EdgeTrafficPermissionDefaults.Clear();
            VisibleAllocations.Clear();
            VisibleConsumerCostSummaries.Clear();
            allAllocationModels.Clear();
            allAllocations.Clear();
            allConsumerCostSummaries.Clear();
            hasSimulationSnapshot = false;
            SelectedTraffic = null;
            SelectedNode = null;
            SelectedNodeTrafficProfile = null;
            SelectedEdge = null;
            SelectedTrafficDefinition = null;

            foreach (var definition in BuildDefinitionEditors(network))
            {
                RegisterTrafficDefinition(definition);
            }

            foreach (var subnetwork in network.Subnetworks ?? [])
            {
                Subnetworks.Add(subnetwork);
            }

            foreach (var nodeModel in network.Nodes)
            {
                RegisterNode(new NodeViewModel(nodeModel));
            }

            RefreshNodeIdOptions();
            RefreshSubnetworkIdOptions();

            var nodeMap = CreateNodeMap();
            foreach (var edgeModel in network.Edges)
            {
                nodeMap.TryGetValue(edgeModel.FromNodeId, out var sourceNode);
                nodeMap.TryGetValue(edgeModel.ToNodeId, out var targetNode);
                RegisterEdge(new EdgeViewModel(edgeModel, sourceNode, targetNode));
            }

            ReplaceEdgeTrafficPermissionDefaults(network.EdgeTrafficPermissionDefaults);

            NetworkName = network.Name;
            NetworkDescription = string.IsNullOrWhiteSpace(network.Description)
                ? string.Empty
                : network.Description;
            DefaultAllocationMode = network.DefaultAllocationMode;
            simulationSeed = network.SimulationSeed;
            timelineLoopLength = network.TimelineLoopLength is > 0 ? network.TimelineLoopLength : null;
            OnPropertyChanged(nameof(TimelineLoopLength));
            OnPropertyChanged(nameof(IsTimelineLoopEnabled));
            HasNetwork = true;
            temporalSimulationState = null;
            lastTimelineStepResult = null;
            currentPeriod = 0;
            hasTimelineSnapshot = false;
            RefreshNodeIdOptions();
            RefreshTrafficTypeNameOptions();
            RefreshEdgeTrafficPermissionEditors();
            RefreshTrafficSummariesFromCurrentState();
            LayersPanel.SyncTrafficTypes(TrafficTypes.Select(traffic => traffic.Name));
            RecalculateWorkspace();
            ClearFlowVisuals();
            ClearTimelineVisuals();
            RefreshCounts();
            OnPropertyChanged(nameof(CurrentPeriod));
            OnPropertyChanged(nameof(TimelineHeadline));
            MarkClean(activeFilePath);
            StatusMessage = successMessage;
        }
        finally
        {
            isReplacingNetwork = false;
        }
    }

    private IReadOnlyList<TrafficTypeDefinitionEditorViewModel> BuildDefinitionEditors(NetworkModel network)
    {
        var definitionsByName = network.TrafficTypes
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Name))
            .GroupBy(definition => definition.Name, Comparer)
            .ToDictionary(group => group.Key, group => group.First(), Comparer);
        var orderedTrafficNames = network.TrafficTypes
            .Select(definition => definition.Name)
            .Concat(network.Nodes.SelectMany(node => node.TrafficProfiles).Select(profile => profile.TrafficType))
            .Concat(network.Nodes.SelectMany(node => node.TrafficProfiles).SelectMany(profile => profile.InputRequirements).Select(requirement => requirement.TrafficType))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(Comparer)
            .ToList();

        return orderedTrafficNames
            .Select(name =>
            {
                definitionsByName.TryGetValue(name, out var definition);
                return new TrafficTypeDefinitionEditorViewModel(definition ?? new TrafficTypeDefinition
                {
                    Name = name,
                    RoutingPreference = RoutingPreference.TotalCost,
                    AllocationMode = DefaultAllocationMode
                });
            })
            .ToList();
    }

    private NetworkModel BuildValidatedNetwork()
    {
        return fileService.NormalizeAndValidate(BuildNetworkSnapshot());
    }

    private NetworkModel BuildNetworkSnapshot()
    {
        return new NetworkModel
        {
            Name = NetworkName,
            Description = NetworkDescription,
            TimelineLoopLength = TimelineLoopLength,
            DefaultAllocationMode = DefaultAllocationMode,
            SimulationSeed = simulationSeed,
            TrafficTypes = TrafficDefinitions.Select(definition => definition.ToModel()).ToList(),
            EdgeTrafficPermissionDefaults = EdgeTrafficPermissionDefaults.Select(permission => permission.ToModel()).ToList(),
            Subnetworks = Subnetworks.Count == 0 ? null : Subnetworks.ToList(),
            Nodes = Nodes.Select(node => node.ToModel()).ToList(),
            Edges = Edges.Select(edge => edge.ToModel()).ToList()
        };
    }

    private void ReplaceEdgeTrafficPermissionDefaults(IEnumerable<EdgeTrafficPermissionRule> rules)
    {
        foreach (var row in EdgeTrafficPermissionDefaults)
        {
            row.DefinitionChanged -= HandleEdgeTrafficPermissionDefaultChanged;
        }

        EdgeTrafficPermissionDefaults.Clear();

        foreach (var rule in rules)
        {
            var row = new EdgeTrafficPermissionRowViewModel(rule.TrafficType, supportsOverrideToggle: false, rule);
            row.DefinitionChanged += HandleEdgeTrafficPermissionDefaultChanged;
            EdgeTrafficPermissionDefaults.Add(row);
        }
    }

    private void RefreshEdgeTrafficPermissionEditors(string? renamedTrafficType = null, string? replacementTrafficType = null)
    {
        var trafficTypes = GetAvailableTrafficTypeNames()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(Comparer)
            .OrderBy(name => name, Comparer)
            .ToList();

        var defaultRows = BuildPermissionRows(
            EdgeTrafficPermissionDefaults,
            trafficTypes,
            supportsOverrideToggle: false,
            renamedTrafficType,
            replacementTrafficType);
        ReplaceEdgeTrafficPermissionDefaults(defaultRows.Select(row => row.ToModel()));

        foreach (var edge in Edges)
        {
            var rows = BuildPermissionRows(
                edge.TrafficPermissions,
                trafficTypes,
                supportsOverrideToggle: true,
                renamedTrafficType,
                replacementTrafficType);
            edge.SynchronizeTrafficPermissions(rows);
        }

        UpdateEffectiveEdgeTrafficPermissionSummaries();
    }

    private void UpdateEffectiveEdgeTrafficPermissionSummaries()
    {
        var previewNetwork = new NetworkModel
        {
            EdgeTrafficPermissionDefaults = EdgeTrafficPermissionDefaults.Select(row => row.ToModel()).ToList(),
            Edges = Edges.Select(edge => edge.ToModel()).ToList()
        };

        foreach (var row in EdgeTrafficPermissionDefaults)
        {
            row.SetEffectiveSummary(EdgeTrafficPermissionResolver.FormatSummary(row.Mode, row.LimitKind, row.ToModel().LimitValue));
        }

        var previewEdgesById = previewNetwork.Edges
            .Where(edge => !string.IsNullOrWhiteSpace(edge.Id))
            .ToDictionary(edge => edge.Id, edge => edge, Comparer);

        foreach (var edge in Edges)
        {
            foreach (var row in edge.TrafficPermissions)
            {
                row.SetEdgeCapacity(edge.Capacity);
                if (!previewEdgesById.TryGetValue(edge.Id, out var edgeModel))
                {
                    row.SetEffectiveSummary("Effective: Permitted");
                    continue;
                }

                var effective = edgeTrafficPermissionResolver.Resolve(previewNetwork, edgeModel, row.TrafficType);
                row.SetEffectiveSummary(effective.Summary);
            }
        }
    }

    private static List<EdgeTrafficPermissionRowViewModel> BuildPermissionRows(
        IEnumerable<EdgeTrafficPermissionRowViewModel> existingRows,
        IReadOnlyList<string> trafficTypes,
        bool supportsOverrideToggle,
        string? renamedTrafficType,
        string? replacementTrafficType)
    {
        var rowsByTraffic = existingRows
            .GroupBy(row => row.TrafficType, Comparer)
            .ToDictionary(group => group.Key, group => group.First(), Comparer);

        if (!string.IsNullOrWhiteSpace(renamedTrafficType) &&
            !string.IsNullOrWhiteSpace(replacementTrafficType) &&
            rowsByTraffic.TryGetValue(renamedTrafficType, out var renamedRow))
        {
            rowsByTraffic.Remove(renamedTrafficType);
            renamedRow.RenameTrafficType(replacementTrafficType);
            rowsByTraffic[replacementTrafficType] = renamedRow;
        }

        var result = new List<EdgeTrafficPermissionRowViewModel>(trafficTypes.Count);
        foreach (var trafficType in trafficTypes)
        {
            if (rowsByTraffic.TryGetValue(trafficType, out var existing))
            {
                existing.RenameTrafficType(trafficType);
                result.Add(existing);
                continue;
            }

            result.Add(new EdgeTrafficPermissionRowViewModel(
                trafficType,
                supportsOverrideToggle,
                new EdgeTrafficPermissionRule
                {
                    TrafficType = trafficType,
                    IsActive = !supportsOverrideToggle,
                    Mode = EdgeTrafficPermissionMode.Permitted,
                    LimitKind = EdgeTrafficLimitKind.AbsoluteUnits
                }));
        }

        return result;
    }

    private void RegisterNode(NodeViewModel node)
    {
        node.PositionChanged += HandleNodePositionChanged;
        node.DefinitionChanged += HandleNodeDefinitionChanged;
        node.IdChanged += HandleNodeIdChanged;
        Nodes.Add(node);
    }

    private void UnregisterNode(NodeViewModel node)
    {
        node.PositionChanged -= HandleNodePositionChanged;
        node.DefinitionChanged -= HandleNodeDefinitionChanged;
        node.IdChanged -= HandleNodeIdChanged;
    }

    private void RegisterEdge(EdgeViewModel edge)
    {
          NormalizeEdgeInterfaceBindings(edge);
    edge.DefinitionChanged += HandleEdgeDefinitionChanged;
    Edges.Add(edge);
    }

    private void UnregisterEdge(EdgeViewModel edge)
    {
        edge.DefinitionChanged -= HandleEdgeDefinitionChanged;
    }

    private void RegisterTrafficDefinition(TrafficTypeDefinitionEditorViewModel definition)
    {
        definition.PropertyChanged += HandleTrafficDefinitionPropertyChanged;
        definition.NameChanged += HandleTrafficDefinitionNameChanged;
        TrafficDefinitions.Add(definition);
    }

    private void UnregisterTrafficDefinition(TrafficTypeDefinitionEditorViewModel definition)
    {
        definition.PropertyChanged -= HandleTrafficDefinitionPropertyChanged;
        definition.NameChanged -= HandleTrafficDefinitionNameChanged;
    }

    private void HandleNodePositionChanged(object? sender, EventArgs e)
    {
        RecalculateWorkspace();
    }

    private void HandleNodeDefinitionChanged(object? sender, EventArgs e)
    {
        if (isNormalizingNodeTrafficProfiles || isBulkUpdatingTrafficProfiles)
        {
            return;
        }

        if (!isNormalizingNodeTrafficProfiles && sender is NodeViewModel node)
        {
            // Editing can temporarily create duplicate traffic rows; fold them back into one profile per traffic type.
            NormalizeNodeTrafficProfiles(node);
        }

        OnPropertyChanged(nameof(SelectedNodeTrafficRoleHeadline));
        RefreshDerivedStateAfterStructureChange("Updated node data.");
    }

    private void HandleNodeIdChanged(object? sender, ValueChangedEventArgs<string> e)
    {
        foreach (var edge in Edges)
        {
            if (Comparer.Equals(edge.FromNodeId, e.OldValue))
            {
                edge.FromNodeId = e.NewValue;
            }

            if (Comparer.Equals(edge.ToNodeId, e.OldValue))
            {
                edge.ToNodeId = e.NewValue;
            }
        }

        RefreshDerivedStateAfterStructureChange("Updated node identifiers and any connected edges.");
    }

    private void HandleEdgeDefinitionChanged(object? sender, EventArgs e)
    {
            if (isNormalizingEdgeInterfaces)
    {
        return;
    }

    if (sender is EdgeViewModel edge)
    {
        NormalizeEdgeInterfaceBindings(edge);
    }

    RefreshDerivedStateAfterEdgeChange("Updated edge data.");
    }

    private void HandleSelectedNodeTrafficProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseSelectedNodeTrafficEditorPropertiesChanged();
    }

    private void HandleTrafficDefinitionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrafficTypeDefinitionEditorViewModel.Name))
        {
            return;
        }

        if (e.PropertyName is nameof(TrafficTypeDefinitionEditorViewModel.AllocationModeLabel) or
            nameof(TrafficTypeDefinitionEditorViewModel.AllocationModeHelpText))
        {
            return;
        }

        if (isBulkUpdatingTrafficDefinitions)
        {
            return;
        }

        RefreshDerivedStateAfterStructureChange("Updated traffic type settings.");
    }

    private void HandleTrafficDefinitionNameChanged(object? sender, ValueChangedEventArgs<string> e)
    {
        if (isAdjustingTrafficDefinitionNames || sender is not TrafficTypeDefinitionEditorViewModel definition)
        {
            return;
        }

        var oldValue = e.OldValue?.Trim() ?? string.Empty;
        var requestedName = definition.Name;
        var normalizedName = requestedName.Trim();

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            RestoreTrafficDefinitionName(definition, oldValue);
            StatusMessage = "Traffic type names cannot be blank.";
            return;
        }

        if (TrafficDefinitions.Any(other => !ReferenceEquals(other, definition) && Comparer.Equals(other.Name, normalizedName)))
        {
            RestoreTrafficDefinitionName(definition, oldValue);
            StatusMessage = $"Traffic type '{normalizedName}' already exists.";
            return;
        }

        if (!string.Equals(requestedName, normalizedName, StringComparison.Ordinal))
        {
            RestoreTrafficDefinitionName(definition, normalizedName);
        }

        if (string.Equals(oldValue, normalizedName, StringComparison.Ordinal))
        {
            return;
        }

        isBulkUpdatingTrafficProfiles = true;

        try
        {
            foreach (var node in Nodes)
            {
                foreach (var profile in node.TrafficProfiles
                             .Where(profile => Comparer.Equals(profile.TrafficType, oldValue))
                             .ToList())
                {
                    profile.TrafficType = normalizedName;
                }

                NormalizeNodeTrafficProfiles(node);
            }
        }
        finally
        {
            isBulkUpdatingTrafficProfiles = false;
        }

        RefreshEdgeTrafficPermissionEditors(oldValue, normalizedName);
        RefreshDerivedStateAfterStructureChange("Renamed a traffic type and updated matching node profiles.");
    }

    private void RefreshDerivedStateAfterStructureChange(string message)
    {
        // Centralize all the "network shape changed" refresh work so the UI stays consistent after edits.
        RefreshNodeIdOptions();
        RefreshSubnetworkIdOptions();
        RefreshTrafficTypeNameOptions();
        RefreshEdgeBindings();
        RefreshEdgeTrafficPermissionEditors();
        RefreshTrafficSummariesFromCurrentState();
        RecalculateWorkspace();
        RefreshCounts();
        InvalidateSimulationResults(message);
        MarkDirty(message);
    }

    private void RefreshDerivedStateAfterEdgeChange(string message)
    {
        // Edge edits do not change the available node/traffic dropdown options, so avoid rebuilding them mid-edit.
        RefreshEdgeBindings();
        UpdateEffectiveEdgeTrafficPermissionSummaries();
        RecalculateWorkspace();
        InvalidateSimulationResults(message);
        MarkDirty(message);
    }

    private void HandleEdgeTrafficPermissionDefaultChanged(object? sender, EventArgs e)
    {
        UpdateEffectiveEdgeTrafficPermissionSummaries();
        InvalidateSimulationResults("Updated edge traffic defaults.");
        MarkDirty("Updated edge traffic defaults.");
    }

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(EdgeCount));
        OnPropertyChanged(nameof(TrafficTypeCount));
    }

    private void RefreshNodeIdOptions()
    {
        SynchronizeCollection(NodeIdOptions, Nodes.Select(node => node.Id).OrderBy(id => id, Comparer));
    }

    private void RefreshSubnetworkIdOptions()
    {
        SynchronizeCollection(SubnetworkIdOptions, Subnetworks.Select(subnetwork => subnetwork.Id).OrderBy(id => id, Comparer));
    }

    private void RefreshTrafficTypeNameOptions()
    {
        var selectedTrafficType = SelectedNodeTrafficProfile?.TrafficType;
        var trafficTypeNames = TrafficDefinitions
            .Select(definition => definition.Name)
            .Concat(Nodes.SelectMany(node => node.TrafficProfiles).Select(profile => profile.TrafficType))
            .Concat(Nodes.SelectMany(node => node.TrafficProfiles).SelectMany(profile => profile.InputRequirements).Select(requirement => requirement.TrafficType))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(Comparer)
            .OrderBy(name => name, Comparer);

        SynchronizeCollection(TrafficTypeNameOptions, trafficTypeNames);

        if (SelectedNodeTrafficProfile is null || string.IsNullOrWhiteSpace(selectedTrafficType))
        {
            return;
        }

        var matchedTrafficType = TrafficTypeNameOptions
            .FirstOrDefault(option => Comparer.Equals(option, selectedTrafficType));

        if (matchedTrafficType is null)
        {
            return;
        }

        if (!string.Equals(SelectedNodeTrafficProfile.TrafficType, matchedTrafficType, StringComparison.Ordinal))
        {
            // Snap the profile value to the exact option text so WPF can restore the combo box selection.
            SelectedNodeTrafficProfile.TrafficType = matchedTrafficType;
            return;
        }

        // Re-announce the current selection after rebuilding the options collection so WPF restores the combo value.
        OnPropertyChanged(nameof(SelectedNodeTrafficType));
    }

    private void RefreshEdgeBindings()
    {
        var nodeMap = CreateNodeMap();
        foreach (var edge in Edges)
        {
            edge.ResolveNodes(nodeMap);
        }
    }

    private Dictionary<string, NodeViewModel> CreateNodeMap()
    {
        var nodeMap = new Dictionary<string, NodeViewModel>(Comparer);

        foreach (var node in Nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Id) && !nodeMap.ContainsKey(node.Id))
            {
                nodeMap[node.Id] = node;
            }
        }

        return nodeMap;
    }

    private void RefreshTrafficSummariesFromCurrentState()
    {
        TrafficTypes.Clear();

        var definitionsByTraffic = TrafficDefinitions
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Name))
            .GroupBy(definition => definition.Name, Comparer)
            .ToDictionary(group => group.Key, group => group.First(), Comparer);

        foreach (var trafficName in GetOrderedTrafficNames())
        {
            var profiles = Nodes
                .Select(node => node.TrafficProfiles.FirstOrDefault(profile => Comparer.Equals(profile.TrafficType, trafficName)))
                .Where(profile => profile is not null)
                .Cast<NodeTrafficProfileViewModel>()
                .ToList();

            var definition = definitionsByTraffic.GetValueOrDefault(trafficName)
                ?? new TrafficTypeDefinitionEditorViewModel(new TrafficTypeDefinition
                {
                    Name = trafficName,
                    RoutingPreference = RoutingPreference.TotalCost
                });

            TrafficTypes.Add(new TrafficSummaryViewModel(
                trafficName,
                definition.RoutingPreference,
                definition.AllocationMode,
                profiles.Sum(profile => profile.Production),
                profiles.Sum(profile => profile.Consumption),
                profiles.Count(profile => profile.Production > 0),
                profiles.Count(profile => profile.Consumption > 0),
                profiles.Count(profile => profile.CanTransship)));
        }

        LayersPanel.SyncTrafficTypes(TrafficTypes.Select(traffic => traffic.Name));
    }

    private IEnumerable<string> GetOrderedTrafficNames()
    {
        var orderedNames = new List<string>();
        var seen = new HashSet<string>(Comparer);

        foreach (var definition in TrafficDefinitions)
        {
            if (!string.IsNullOrWhiteSpace(definition.Name) && seen.Add(definition.Name))
            {
                orderedNames.Add(definition.Name);
            }
        }

        var profileNames = Nodes
            .SelectMany(node => node.TrafficProfiles)
            .Select(profile => profile.TrafficType)
            .Where(name => !string.IsNullOrWhiteSpace(name) && seen.Add(name));

        orderedNames.AddRange(profileNames);
        return orderedNames;
    }

    private void NormalizeNodeTrafficProfiles(NodeViewModel node)
    {
        isNormalizingNodeTrafficProfiles = true;

        try
        {
            foreach (var duplicateGroup in node.TrafficProfiles
                         .Where(profile => !string.IsNullOrWhiteSpace(profile.TrafficType))
                         .GroupBy(profile => profile.TrafficType.Trim(), Comparer)
                         .Where(group => group.Count() > 1)
                         .ToList())
            {
                var primaryProfile = duplicateGroup.First();

                foreach (var duplicateProfile in duplicateGroup.Skip(1).ToList())
                {
                    primaryProfile.Production += duplicateProfile.Production;
                    primaryProfile.Consumption += duplicateProfile.Consumption;
                    primaryProfile.CanTransship |= duplicateProfile.CanTransship;
                    foreach (var window in duplicateProfile.ProductionWindows)
                    {
                        primaryProfile.ProductionWindows.Add(new PeriodWindowViewModel(window.ToModel()));
                    }

                    foreach (var window in duplicateProfile.ConsumptionWindows)
                    {
                        primaryProfile.ConsumptionWindows.Add(new PeriodWindowViewModel(window.ToModel()));
                    }

                    foreach (var requirement in duplicateProfile.InputRequirements)
                    {
                        primaryProfile.InputRequirements.Add(new ProductionInputRequirementViewModel(requirement.ToModel()));
                    }

                    if (ReferenceEquals(SelectedNodeTrafficProfile, duplicateProfile))
                    {
                        SelectedNodeTrafficProfile = primaryProfile;
                    }

                    node.RemoveTrafficProfile(duplicateProfile);
                }
            }
        }
        finally
        {
            isNormalizingNodeTrafficProfiles = false;
        }
    }

    private void RecalculateWorkspace()
    {
        const double defaultWidth = 1600d;
        const double defaultHeight = 1000d;
        const double minimumWidth = 1400d;
        const double minimumHeight = 900d;
        const double padding = 200d;

        if (Nodes.Count == 0)
        {
            WorkspaceMinX = 0d;
            WorkspaceMinY = 0d;
            WorkspaceWidth = defaultWidth;
            WorkspaceHeight = defaultHeight;
            return;
        }

        var minLeft = Nodes.Min(node => node.Left);
        var minTop = Nodes.Min(node => node.Top);
        var maxRight = Nodes.Max(node => node.Left + node.Width);
        var maxBottom = Nodes.Max(node => node.Top + node.Height);

        WorkspaceMinX = minLeft - padding;
        WorkspaceMinY = minTop - padding;
        WorkspaceWidth = Math.Max(minimumWidth, (maxRight - minLeft) + (padding * 2d));
        WorkspaceHeight = Math.Max(minimumHeight, (maxBottom - minTop) + (padding * 2d));
    }

    private void InvalidateSimulationResults(string message)
    {
        hasSimulationSnapshot = false;
        hasTimelineSnapshot = false;
        temporalSimulationState = null;
        lastTimelineStepResult = null;
        currentPeriod = 0;
        allAllocationModels.Clear();
        allAllocations.Clear();
        allConsumerCostSummaries.Clear();
        allPerishingEvents.Clear();
        VisibleAllocations.Clear();
        VisibleConsumerCostSummaries.Clear();
        VisiblePerishingEvents.Clear();
        ClearFlowVisuals();
        ClearTimelineVisuals();
        Canvas.ClearRouteHighlight();

        foreach (var traffic in TrafficTypes)
        {
            traffic.ClearOutcome();
        }

        OnPropertyChanged(nameof(CurrentPeriod));
        OnPropertyChanged(nameof(TimelineHeadline));
        TimelineToolbar.CurrentPeriod = CurrentPeriod;
        TimelineToolbar.Headline = TimelineHeadline;
        OnPropertyChanged(nameof(VisibleAllocationHeadline));
        OnPropertyChanged(nameof(VisibleConsumerCostHeadline));
        OnPropertyChanged(nameof(VisiblePerishingHeadline));
        RaiseReportStatePropertiesChanged();
        StatusMessage = message;
    }

    private void RefreshVisibleAllocations()
    {
        VisibleAllocations.Clear();

        var source = allAllocations.Where(allocation => LayersPanel.ShouldIncludeTraffic(allocation.TrafficType));

        foreach (var allocation in source)
        {
            VisibleAllocations.Add(allocation);
        }

        OnPropertyChanged(nameof(VisibleAllocationHeadline));
        RaiseReportStatePropertiesChanged();
    }

    private void RefreshFlowVisuals()
    {
        if (!hasSimulationSnapshot)
        {
            if (hasTimelineSnapshot && lastTimelineStepResult is not null)
            {
                ApplyTimelineVisuals(lastTimelineStepResult);
                return;
            }

            ClearFlowVisuals();
            return;
        }

        var filteredAllocations = allAllocationModels
            .Where(allocation => LayersPanel.ShouldIncludeTraffic(allocation.TrafficType))
            .ToList();
        var edgeMap = Edges
            .Where(edge => !string.IsNullOrWhiteSpace(edge.Id))
            .GroupBy(edge => edge.Id, Comparer)
            .ToDictionary(group => group.Key, group => group.First(), Comparer);
        var edgeVisualsById = new Dictionary<string, EdgeFlowVisualSummary>(Comparer);
        var nodeVisualsById = new Dictionary<string, NodeFlowVisualSummary>(Comparer);
        var edgeTrafficById = new Dictionary<string, EdgeTrafficBreakdown>(Comparer);
        var producedTrafficByNodeId = new Dictionary<string, Dictionary<string, double>>(Comparer);
        var transhippedTrafficByNodeId = new Dictionary<string, Dictionary<string, double>>(Comparer);

        foreach (var allocation in filteredAllocations)
        {
            AddTrafficQuantityByNode(producedTrafficByNodeId, allocation.ProducerNodeId, allocation.TrafficType, allocation.Quantity);

            if (allocation.IsLocalSupply)
            {
                AddNodeLocalQuantity(allocation.ConsumerNodeId, allocation.Quantity, nodeVisualsById);
                continue;
            }

            AddNodeOutboundQuantity(allocation.ProducerNodeId, allocation.Quantity, nodeVisualsById);
            AddNodeInboundQuantity(allocation.ConsumerNodeId, allocation.Quantity, nodeVisualsById);

            foreach (var transhipmentNodeId in allocation.PathNodeIds.Skip(1).Take(Math.Max(0, allocation.PathNodeIds.Count - 2)))
            {
                AddNodeTranshipmentQuantity(transhipmentNodeId, allocation.Quantity, nodeVisualsById);
                AddTrafficQuantityByNode(transhippedTrafficByNodeId, transhipmentNodeId, allocation.TrafficType, allocation.Quantity);
            }

            for (var index = 0; index < allocation.PathEdgeIds.Count; index++)
            {
                var edgeId = allocation.PathEdgeIds[index];
                if (string.IsNullOrWhiteSpace(edgeId))
                {
                    continue;
                }

                var fromNodeId = index < allocation.PathNodeIds.Count ? allocation.PathNodeIds[index] : string.Empty;
                var toNodeId = index + 1 < allocation.PathNodeIds.Count ? allocation.PathNodeIds[index + 1] : string.Empty;
                var existingEdgeSummary = edgeVisualsById.GetValueOrDefault(edgeId, EdgeFlowVisualSummary.Empty);

                if (edgeMap.TryGetValue(edgeId, out var edge) &&
                    Comparer.Equals(fromNodeId, edge.FromNodeId) &&
                    Comparer.Equals(toNodeId, edge.ToNodeId))
                {
                    AddEdgeTrafficQuantity(edgeTrafficById, edgeId, allocation.TrafficType, allocation.Quantity, isForward: true);
                    edgeVisualsById[edgeId] = existingEdgeSummary with
                    {
                        ForwardQuantity = existingEdgeSummary.ForwardQuantity + allocation.Quantity
                    };
                }
                else
                {
                    AddEdgeTrafficQuantity(edgeTrafficById, edgeId, allocation.TrafficType, allocation.Quantity, isForward: false);
                    edgeVisualsById[edgeId] = existingEdgeSummary with
                    {
                        ReverseQuantity = existingEdgeSummary.ReverseQuantity + allocation.Quantity
                    };
                }
            }
        }

        var maxEdgeFlowQuantity = edgeVisualsById.Count == 0
            ? 0d
            : edgeVisualsById.Values.Max(summary => summary.TotalQuantity);

        foreach (var edge in Edges)
        {
            var summary = string.IsNullOrWhiteSpace(edge.Id)
                ? EdgeFlowVisualSummary.Empty
                : edgeVisualsById.GetValueOrDefault(edge.Id, EdgeFlowVisualSummary.Empty);
            edge.ApplySimulationVisuals(summary.ForwardQuantity, summary.ReverseQuantity, maxEdgeFlowQuantity, hasSimulationSnapshot);
            var trafficBreakdown = string.IsNullOrWhiteSpace(edge.Id)
                ? EdgeTrafficBreakdown.Empty
                : edgeTrafficById.GetValueOrDefault(edge.Id, EdgeTrafficBreakdown.Empty);
            edge.ApplyTrafficDetails(
                ToOrderedTrafficPairs(trafficBreakdown.ForwardByTraffic),
                ToOrderedTrafficPairs(trafficBreakdown.ReverseByTraffic));
        }

        ApplyRouteHighlights();

        foreach (var node in Nodes)
        {
            var summary = string.IsNullOrWhiteSpace(node.Id)
                ? NodeFlowVisualSummary.Empty
                : nodeVisualsById.GetValueOrDefault(node.Id, NodeFlowVisualSummary.Empty);
            node.ApplySimulationVisuals(
                summary.OutboundQuantity,
                summary.TranshipmentQuantity,
                summary.InboundQuantity,
                summary.LocalQuantity,
                hasSimulationSnapshot);
            node.ApplyTooltipTrafficDetails(
                ToOrderedTrafficPairs(producedTrafficByNodeId.GetValueOrDefault(node.Id)),
                [],
                ToOrderedTrafficPairs(transhippedTrafficByNodeId.GetValueOrDefault(node.Id)));
        }
    }

    private void ApplyTimelineVisuals(TemporalNetworkSimulationEngine.TemporalSimulationStepResult stepResult)
    {
        var edgeTrafficById = new Dictionary<string, EdgeTrafficBreakdown>(Comparer);
        var producedTrafficByNodeId = new Dictionary<string, Dictionary<string, double>>(Comparer);
        var transhippedTrafficByNodeId = new Dictionary<string, Dictionary<string, double>>(Comparer);
        var storedTrafficByNodeId = new Dictionary<string, Dictionary<string, double>>(Comparer);
        BuildTrafficTooltipBreakdowns(
            stepResult.Allocations,
            Edges,
            edgeTrafficById,
            producedTrafficByNodeId,
            transhippedTrafficByNodeId);

        foreach (var state in stepResult.NodeStates)
        {
            AddTrafficQuantityByNode(
                storedTrafficByNodeId,
                state.Key.NodeId,
                state.Key.TrafficType,
                state.Value.StoreInventory);
        }

        var maxEdgeFlowQuantity = stepResult.EdgeFlows.Count == 0
            ? 0d
            : stepResult.EdgeFlows.Values.Max(summary => summary.ForwardQuantity + summary.ReverseQuantity);

        foreach (var edge in Edges)
        {
            var summary = string.IsNullOrWhiteSpace(edge.Id)
                ? TemporalNetworkSimulationEngine.EdgeFlowVisualSummary.Empty
                : stepResult.EdgeFlows.GetValueOrDefault(edge.Id, TemporalNetworkSimulationEngine.EdgeFlowVisualSummary.Empty);
            edge.ApplySimulationVisuals(summary.ForwardQuantity, summary.ReverseQuantity, maxEdgeFlowQuantity, hasSimulationSnapshot: true);
            var trafficBreakdown = string.IsNullOrWhiteSpace(edge.Id)
                ? EdgeTrafficBreakdown.Empty
                : edgeTrafficById.GetValueOrDefault(edge.Id, EdgeTrafficBreakdown.Empty);
            edge.ApplyTrafficDetails(
                ToOrderedTrafficPairs(trafficBreakdown.ForwardByTraffic),
                ToOrderedTrafficPairs(trafficBreakdown.ReverseByTraffic));
            edge.ApplyTimelinePressure(
                string.IsNullOrWhiteSpace(edge.Id)
                    ? null
                    : stepResult.EdgePressureById.GetValueOrDefault(edge.Id));
        }

        ApplyRouteHighlights();

        foreach (var node in Nodes)
        {
            var flowSummary = string.IsNullOrWhiteSpace(node.Id)
                ? TemporalNetworkSimulationEngine.NodeFlowVisualSummary.Empty
                : stepResult.NodeFlows.GetValueOrDefault(node.Id, TemporalNetworkSimulationEngine.NodeFlowVisualSummary.Empty);

            var trafficStates = stepResult.NodeStates
                .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id))
                .Select(pair => pair.Value)
                .ToList();
            var backlogByTraffic = stepResult.NodeStates
                .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id) && pair.Value.DemandBacklog > 0d)
                .GroupBy(pair => pair.Key.TrafficType, pair => pair.Value.DemandBacklog, Comparer)
                .Select(group => new KeyValuePair<string, double>(group.Key, group.Sum()))
                .ToList();
            var deliveredDemand = stepResult.Allocations
                .Where(allocation => Comparer.Equals(allocation.ConsumerNodeId, node.Id))
                .Sum(allocation => allocation.Quantity);

            node.ApplySimulationVisuals(
                flowSummary.OutboundQuantity,
                0d,
                flowSummary.InboundQuantity,
                0d,
                hasSimulationSnapshot: true);
            node.ApplyTimelineVisuals(
                trafficStates.Sum(item => item.AvailableSupply),
                trafficStates.Sum(item => item.DemandBacklog),
                trafficStates.Sum(item => item.StoreInventory),
                deliveredDemand,
                backlogByTraffic,
                string.IsNullOrWhiteSpace(node.Id)
                    ? null
                    : stepResult.NodePressureById.GetValueOrDefault(node.Id));
            node.ApplyTooltipTrafficDetails(
                ToOrderedTrafficPairs(producedTrafficByNodeId.GetValueOrDefault(node.Id)),
                ToOrderedTrafficPairs(storedTrafficByNodeId.GetValueOrDefault(node.Id)),
                ToOrderedTrafficPairs(transhippedTrafficByNodeId.GetValueOrDefault(node.Id)));
        }
    }

    private void ClearFlowVisuals()
    {
        foreach (var edge in Edges)
        {
            edge.ClearSimulationVisuals();
            edge.ApplyTrafficDetails([], []);
            edge.ApplyRouteHighlight(false);
            edge.ClearTimelinePressure();
        }

        foreach (var node in Nodes)
        {
            node.ClearSimulationVisuals();
            node.ApplyTooltipTrafficDetails([], [], []);
        }
    }

    private void ClearTimelineVisuals()
    {
        foreach (var edge in Edges)
        {
            edge.ClearSimulationVisuals();
            edge.ApplyTrafficDetails([], []);
            edge.ApplyRouteHighlight(false);
            edge.ClearTimelinePressure();
        }

        foreach (var node in Nodes)
        {
            node.ClearSimulationVisuals();
            node.ClearTimelineVisuals();
            node.ApplyTooltipTrafficDetails([], [], []);
        }
    }

    private void RefreshVisibleConsumerCostSummaries()
    {
        VisibleConsumerCostSummaries.Clear();

        var source = allConsumerCostSummaries.Where(summary => LayersPanel.ShouldIncludeTraffic(summary.TrafficType));

        foreach (var summary in source)
        {
            VisibleConsumerCostSummaries.Add(summary);
        }

        OnPropertyChanged(nameof(VisibleConsumerCostHeadline));
        RaiseReportStatePropertiesChanged();
    }

    private void RefreshVisiblePerishingEvents()
    {
        VisiblePerishingEvents.Clear();

        var source = allPerishingEvents.Where(item => LayersPanel.ShouldIncludeTraffic(item.TrafficType));

        foreach (var row in source)
        {
            VisiblePerishingEvents.Add(row);
        }

        OnPropertyChanged(nameof(VisiblePerishingHeadline));
        RaiseReportStatePropertiesChanged();
    }

    private void HandleLayersChanged(object? sender, EventArgs e)
    {
        RefreshVisibleAllocations();
        RefreshVisibleConsumerCostSummaries();
        RefreshVisiblePerishingEvents();
        RefreshFlowVisuals();
    }

    private void HandleReportRouteSelected(object? sender, RouteAllocationRowViewModel route)
    {
        Canvas.HighlightRoute(route);
        LayersPanel.HighlightRouteTraffic(route.TrafficType);
        InspectorPanel.InspectRoute(route, ShouldOpenInspectorForSelection());
        ApplyRouteHighlights();
        RaiseOptionalSurfacePropertiesChanged();
    }

    private bool ShouldOpenInspectorForSelection()
    {
        return InspectorPanel.IsOpen || !suppressInspectorAutoOpen;
    }

    private void ApplyRouteHighlights()
    {
        var highlightedEdgeIds = LayersPanel.ShowRouteHighlights
            ? new HashSet<string>(Canvas.HighlightedRouteEdgeIds, Comparer)
            : new HashSet<string>(Comparer);

        foreach (var edge in Edges)
        {
            edge.ApplyRouteHighlight(
                !string.IsNullOrWhiteSpace(edge.Id) &&
                highlightedEdgeIds.Contains(edge.Id));
        }
    }

    private void RaiseOptionalSurfacePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsOptionalSidePanelOpen));
        OnPropertyChanged(nameof(OptionalSidePanelVisibility));
        OnPropertyChanged(nameof(OptionalSidePanelColumnWidth));
        OnPropertyChanged(nameof(OptionalSidePanelSpacerColumnWidth));
        OnPropertyChanged(nameof(RightRailVisibility));
        OnPropertyChanged(nameof(RightRailColumnWidth));
        OnPropertyChanged(nameof(RightRailSpacerColumnWidth));
        OnPropertyChanged(nameof(BottomWorkspaceVisibility));
        OnPropertyChanged(nameof(LayersPanelVisibility));
        OnPropertyChanged(nameof(LegendPanelVisibility));
    }

    private void RaiseReportStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(HasReportSnapshot));
        OnPropertyChanged(nameof(RoutesTabHeader));
        OnPropertyChanged(nameof(ConsumerCostsTabHeader));
        OnPropertyChanged(nameof(PerishingTabHeader));
        OnPropertyChanged(nameof(RoutesEmptyText));
        OnPropertyChanged(nameof(ConsumerCostsEmptyText));
        OnPropertyChanged(nameof(PerishingEmptyText));
        OnPropertyChanged(nameof(RoutesEmptyStateVisibility));
        OnPropertyChanged(nameof(RoutesGridVisibility));
        OnPropertyChanged(nameof(ConsumerCostsEmptyStateVisibility));
        OnPropertyChanged(nameof(ConsumerCostsGridVisibility));
        OnPropertyChanged(nameof(PerishingEmptyStateVisibility));
        OnPropertyChanged(nameof(PerishingGridVisibility));
    }

    private IReadOnlyList<PerishingEventRowViewModel> BuildPerishingEventRows(
        TemporalNetworkSimulationEngine.TemporalSimulationStepResult stepResult)
    {
        var nodeNamesById = Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, node => node.Name, Comparer);
        var edgeLabelsById = Edges
            .Where(edge => !string.IsNullOrWhiteSpace(edge.Id))
            .ToDictionary(
                edge => edge.Id,
                edge =>
                {
                    var from = nodeNamesById.GetValueOrDefault(edge.FromNodeId, edge.FromNodeId);
                    var to = nodeNamesById.GetValueOrDefault(edge.ToNodeId, edge.ToNodeId);
                    return $"{from} → {to}";
                },
                Comparer);

        return stepResult.PressureEvents
            .Where(item => PerishingEventRowViewModel.IsPerishingCause(item.Cause))
            .GroupBy(
                item => (item.Period, item.TrafficType, item.IsEdge, item.EntityId, item.Cause))
            .Select(group =>
            {
                var sample = group.First();
                var locationType = sample.IsEdge ? "Edge" : "Node";
                var locationLabel = sample.IsEdge
                    ? edgeLabelsById.GetValueOrDefault(sample.EntityId, sample.EntityId)
                    : nodeNamesById.GetValueOrDefault(sample.EntityId, sample.EntityId);
                var detail = group
                    .Select(item => item.Detail)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault() ?? string.Empty;

                return new PerishingEventRowViewModel(
                    sample.Period,
                    sample.TrafficType,
                    locationType,
                    sample.EntityId,
                    locationLabel,
                    sample.Cause.ToString(),
                    group.Sum(item => item.Quantity),
                    group.Sum(item => item.WeightedImpact),
                    detail);
            })
            .OrderByDescending(item => item.Quantity)
            .ThenBy(item => item.TrafficType, Comparer)
            .ThenBy(item => item.LocationLabel, Comparer)
            .ToList();
    }

    private static void AddNodeOutboundQuantity(string nodeId, double quantity, IDictionary<string, NodeFlowVisualSummary> nodeVisualsById)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || quantity <= Epsilon)
        {
            return;
        }

        var existingSummary = nodeVisualsById.TryGetValue(nodeId, out var summary)
            ? summary
            : NodeFlowVisualSummary.Empty;
        nodeVisualsById[nodeId] = existingSummary with
        {
            OutboundQuantity = existingSummary.OutboundQuantity + quantity
        };
    }

    private static void AddNodeInboundQuantity(string nodeId, double quantity, IDictionary<string, NodeFlowVisualSummary> nodeVisualsById)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || quantity <= Epsilon)
        {
            return;
        }

        var existingSummary = nodeVisualsById.TryGetValue(nodeId, out var summary)
            ? summary
            : NodeFlowVisualSummary.Empty;
        nodeVisualsById[nodeId] = existingSummary with
        {
            InboundQuantity = existingSummary.InboundQuantity + quantity
        };
    }

    private static void AddNodeTranshipmentQuantity(string nodeId, double quantity, IDictionary<string, NodeFlowVisualSummary> nodeVisualsById)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || quantity <= Epsilon)
        {
            return;
        }

        var existingSummary = nodeVisualsById.TryGetValue(nodeId, out var summary)
            ? summary
            : NodeFlowVisualSummary.Empty;
        nodeVisualsById[nodeId] = existingSummary with
        {
            TranshipmentQuantity = existingSummary.TranshipmentQuantity + quantity
        };
    }

    private static void AddNodeLocalQuantity(string nodeId, double quantity, IDictionary<string, NodeFlowVisualSummary> nodeVisualsById)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || quantity <= Epsilon)
        {
            return;
        }

        var existingSummary = nodeVisualsById.TryGetValue(nodeId, out var summary)
            ? summary
            : NodeFlowVisualSummary.Empty;
        nodeVisualsById[nodeId] = existingSummary with
        {
            LocalQuantity = existingSummary.LocalQuantity + quantity
        };
    }

    private static void BuildTrafficTooltipBreakdowns(
        IEnumerable<RouteAllocation> allocations,
        IEnumerable<EdgeViewModel> edges,
        IDictionary<string, EdgeTrafficBreakdown> edgeTrafficById,
        IDictionary<string, Dictionary<string, double>> producedTrafficByNodeId,
        IDictionary<string, Dictionary<string, double>> transhippedTrafficByNodeId)
    {
        var edgeMap = edges
            .Where(edge => !string.IsNullOrWhiteSpace(edge.Id))
            .GroupBy(edge => edge.Id, Comparer)
            .ToDictionary(group => group.Key, group => group.First(), Comparer);

        foreach (var allocation in allocations.Where(allocation => allocation.Quantity > Epsilon))
        {
            AddTrafficQuantityByNode(producedTrafficByNodeId, allocation.ProducerNodeId, allocation.TrafficType, allocation.Quantity);

            if (allocation.IsLocalSupply)
            {
                continue;
            }

            foreach (var transhipmentNodeId in allocation.PathNodeIds.Skip(1).Take(Math.Max(0, allocation.PathNodeIds.Count - 2)))
            {
                AddTrafficQuantityByNode(transhippedTrafficByNodeId, transhipmentNodeId, allocation.TrafficType, allocation.Quantity);
            }

            for (var index = 0; index < allocation.PathEdgeIds.Count; index++)
            {
                var edgeId = allocation.PathEdgeIds[index];
                if (string.IsNullOrWhiteSpace(edgeId))
                {
                    continue;
                }

                var fromNodeId = index < allocation.PathNodeIds.Count ? allocation.PathNodeIds[index] : string.Empty;
                var toNodeId = index + 1 < allocation.PathNodeIds.Count ? allocation.PathNodeIds[index + 1] : string.Empty;
                var isForward = edgeMap.TryGetValue(edgeId, out var edge) &&
                    Comparer.Equals(fromNodeId, edge.FromNodeId) &&
                    Comparer.Equals(toNodeId, edge.ToNodeId);
                AddEdgeTrafficQuantity(edgeTrafficById, edgeId, allocation.TrafficType, allocation.Quantity, isForward);
            }
        }
    }

    private static void AddEdgeTrafficQuantity(
        IDictionary<string, EdgeTrafficBreakdown> edgeTrafficById,
        string edgeId,
        string trafficType,
        double quantity,
        bool isForward)
    {
        if (string.IsNullOrWhiteSpace(edgeId) || string.IsNullOrWhiteSpace(trafficType) || quantity <= Epsilon)
        {
            return;
        }

        var existing = edgeTrafficById.TryGetValue(edgeId, out var current)
            ? current
            : EdgeTrafficBreakdown.Empty;
        if (isForward)
        {
            AddTrafficQuantity(existing.ForwardByTraffic, trafficType, quantity);
        }
        else
        {
            AddTrafficQuantity(existing.ReverseByTraffic, trafficType, quantity);
        }

        edgeTrafficById[edgeId] = existing;
    }

    private static void AddTrafficQuantityByNode(
        IDictionary<string, Dictionary<string, double>> trafficByNodeId,
        string nodeId,
        string trafficType,
        double quantity)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(trafficType) || quantity <= Epsilon)
        {
            return;
        }

        if (!trafficByNodeId.TryGetValue(nodeId, out var nodeBreakdown))
        {
            nodeBreakdown = new Dictionary<string, double>(Comparer);
            trafficByNodeId[nodeId] = nodeBreakdown;
        }

        AddTrafficQuantity(nodeBreakdown, trafficType, quantity);
    }

    private static void AddTrafficQuantity(IDictionary<string, double> breakdown, string trafficType, double quantity)
    {
        if (quantity <= Epsilon)
        {
            return;
        }

        var currentQuantity = breakdown.TryGetValue(trafficType, out var existingQuantity)
            ? existingQuantity
            : 0d;
        breakdown[trafficType] = currentQuantity + quantity;
    }

    private static IReadOnlyList<KeyValuePair<string, double>> ToOrderedTrafficPairs(IReadOnlyDictionary<string, double>? breakdown)
    {
        if (breakdown is null || breakdown.Count == 0)
        {
            return [];
        }

        return breakdown
            .Where(pair => pair.Value > Epsilon)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, Comparer)
            .Select(pair => new KeyValuePair<string, double>(pair.Key, pair.Value))
            .ToList();
    }

    private static void SynchronizeCollection(ObservableCollection<string> target, IEnumerable<string> values)
    {
        var nextValues = values.ToList();
        var sharedCount = Math.Min(target.Count, nextValues.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            if (!string.Equals(target[index], nextValues[index], StringComparison.Ordinal))
            {
                target[index] = nextValues[index];
            }
        }

        while (target.Count > nextValues.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (var index = sharedCount; index < nextValues.Count; index++)
        {
            target.Add(nextValues[index]);
        }
    }

    private static string GetNextUniqueName(string prefix, IEnumerable<string> existingNames)
    {
        var existing = new HashSet<string>(existingNames.Where(name => !string.IsNullOrWhiteSpace(name)), Comparer);
        var index = 1;

        while (true)
        {
            var candidate = $"{prefix}{index}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    private TrafficTypeDefinitionEditorViewModel CreateTrafficDefinition()
    {
        var definition = new TrafficTypeDefinitionEditorViewModel(new TrafficTypeDefinition
        {
            Name = GetNextUniqueName("Traffic", TrafficDefinitions.Select(item => item.Name)),
            RoutingPreference = RoutingPreference.TotalCost,
            AllocationMode = DefaultAllocationMode
        });

        RegisterTrafficDefinition(definition);
        SelectedTrafficDefinition = definition;
        return definition;
    }

    private void RestoreTrafficDefinitionName(TrafficTypeDefinitionEditorViewModel definition, string name)
    {
        isAdjustingTrafficDefinitionNames = true;

        try
        {
            definition.Name = name;
        }
        finally
        {
            isAdjustingTrafficDefinitionNames = false;
        }
    }

    private void RaiseSelectedNodeTrafficEditorPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedNodeRoleOptions));
        OnPropertyChanged(nameof(SelectedNodeTrafficType));
        OnPropertyChanged(nameof(SelectedNodeRoleName));
        OnPropertyChanged(nameof(IsSelectedNodeProducer));
        OnPropertyChanged(nameof(IsSelectedNodeConsumer));
        OnPropertyChanged(nameof(SelectedNodeProduction));
        OnPropertyChanged(nameof(SelectedNodeConsumption));
        OnPropertyChanged(nameof(SelectedNodeConsumerPremiumPerUnit));
        OnPropertyChanged(nameof(SelectedNodeProductionStartPeriod));
        OnPropertyChanged(nameof(SelectedNodeProductionEndPeriod));
        OnPropertyChanged(nameof(SelectedNodeConsumptionStartPeriod));
        OnPropertyChanged(nameof(SelectedNodeConsumptionEndPeriod));
        OnPropertyChanged(nameof(IsSelectedNodeStore));
        OnPropertyChanged(nameof(SelectedNodeStoreCapacity));
        OnPropertyChanged(nameof(SelectedNodeShapeOptions));
        OnPropertyChanged(nameof(SelectedNodeShape));
        OnPropertyChanged(nameof(SelectedNodeTrafficSelectionLabel));
        OnPropertyChanged(nameof(SelectedNodeTrafficRoleSummary));
    }

    private readonly record struct EdgeFlowVisualSummary(double ForwardQuantity, double ReverseQuantity)
    {
        public static EdgeFlowVisualSummary Empty => new(0d, 0d);

        public double TotalQuantity => ForwardQuantity + ReverseQuantity;
    }

    private readonly record struct EdgeTrafficBreakdown(
        Dictionary<string, double> ForwardByTraffic,
        Dictionary<string, double> ReverseByTraffic)
    {
        public static EdgeTrafficBreakdown Empty => new(new Dictionary<string, double>(Comparer), new Dictionary<string, double>(Comparer));
    }

    private readonly record struct NodeFlowVisualSummary(
        double OutboundQuantity,
        double TranshipmentQuantity,
        double InboundQuantity,
        double LocalQuantity)
    {
        public static NodeFlowVisualSummary Empty => new(0d, 0d, 0d, 0d);
    }
}

public sealed record BundledScenarioOption(
    string DisplayName,
    string FileName,
    string ResourceName,
    string Category = "Custom",
    string Description = "")
{
    public string DisplayLabel => $"{Category}: {DisplayName}";
}

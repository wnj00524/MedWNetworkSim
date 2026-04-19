using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.Interaction;
using MedWNetworkSim.Rendering;
using SkiaSharp;

namespace MedWNetworkSim.Presentation;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action execute;
    private readonly Func<bool>? canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class InspectorSection : ObservableObject
{
    private string headline = "Nothing selected";
    private string summary = "Select a node, route, or traffic type to edit it.";
    private IReadOnlyList<string> details = [];

    public string Headline { get => headline; set => SetProperty(ref headline, value); }
    public string Summary { get => summary; set => SetProperty(ref summary, value); }
    public IReadOnlyList<string> Details { get => details; set => SetProperty(ref details, value); }
}

public enum InspectorEditMode
{
    Network,
    Node,
    Edge,
    Selection
}

public enum InspectorTabTarget
{
    Selection,
    TrafficTypes
}

public enum InspectorSectionTarget
{
    None,
    Node,
    Route,
    TrafficRoles
}

public sealed class ReportMetricViewModel
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public required Action Activate { get; init; }
}

public sealed class TrafficReportRowViewModel
{
    public required string TrafficType { get; init; }
    public required string PlannedQuantity { get; init; }
    public required string DeliveredQuantity { get; init; }
    public required string UnmetDemand { get; init; }
    public required string Backlog { get; init; }
}

public sealed class RouteReportRowViewModel
{
    public required string RouteId { get; init; }
    public required string FromTo { get; init; }
    public required string CurrentFlow { get; init; }
    public required string Capacity { get; init; }
    public required string Utilisation { get; init; }
    public required string Pressure { get; init; }
}

public sealed class NodePressureReportRowViewModel
{
    public required string Node { get; init; }
    public required string PressureScore { get; init; }
    public required string TopCause { get; init; }
    public required string UnmetNeed { get; init; }
}

public sealed class TrafficDefinitionListItem(TrafficTypeDefinition model)
{
    public TrafficTypeDefinition Model { get; } = model;
    public string Name => string.IsNullOrWhiteSpace(Model.Name) ? "(unnamed)" : Model.Name;
    public override string ToString() => Name;
}

public sealed class NodeTrafficProfileListItem(int index, NodeTrafficProfile model)
{
    public int Index { get; } = index;
    public NodeTrafficProfile Model { get; } = model;

    public string DisplayLabel
    {
        get
        {
            var role = NodeTrafficRoleCatalog.GetRoleName(Model.Production > 0d, Model.Consumption > 0d, Model.CanTransship);
            return $"{Model.TrafficType} | {role}";
        }
    }

    public override string ToString() => DisplayLabel;
}

public sealed class PermissionRuleEditorRow : ObservableObject
{
    private string trafficType;
    private bool isActive;
    private EdgeTrafficPermissionMode mode;
    private EdgeTrafficLimitKind limitKind;
    private string limitValueText;
    private string effectiveSummary;
    private string validationMessage = string.Empty;

    public PermissionRuleEditorRow(
        string trafficType,
        bool supportsOverrideToggle,
        EdgeTrafficPermissionRule? rule,
        string effectiveSummary)
    {
        this.trafficType = trafficType;
        SupportsOverrideToggle = supportsOverrideToggle;
        isActive = !supportsOverrideToggle || rule?.IsActive != false;
        mode = rule?.Mode ?? EdgeTrafficPermissionMode.Permitted;
        limitKind = rule?.LimitKind ?? EdgeTrafficLimitKind.AbsoluteUnits;
        limitValueText = rule?.LimitValue?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        this.effectiveSummary = effectiveSummary;
        Validate(null);
    }

    public string TrafficType
    {
        get => trafficType;
        set => SetProperty(ref trafficType, value);
    }

    public bool SupportsOverrideToggle { get; }

    public bool IsActive
    {
        get => isActive;
        set
        {
            if (SetProperty(ref isActive, value))
            {
                Validate(null);
            }
        }
    }

    public EdgeTrafficPermissionMode Mode
    {
        get => mode;
        set
        {
            if (SetProperty(ref mode, value))
            {
                Validate(null);
            }
        }
    }

    public EdgeTrafficLimitKind LimitKind
    {
        get => limitKind;
        set
        {
            if (SetProperty(ref limitKind, value))
            {
                Validate(null);
            }
        }
    }

    public string LimitValueText
    {
        get => limitValueText;
        set
        {
            if (SetProperty(ref limitValueText, value))
            {
                Validate(null);
            }
        }
    }

    public string EffectiveSummary
    {
        get => effectiveSummary;
        set => SetProperty(ref effectiveSummary, value);
    }

    public string ValidationMessage
    {
        get => validationMessage;
        private set => SetProperty(ref validationMessage, value);
    }

    public EdgeTrafficPermissionRule ToModel(double? edgeCapacity)
    {
        Validate(edgeCapacity);
        if (!string.IsNullOrWhiteSpace(ValidationMessage))
        {
            throw new InvalidOperationException($"{TrafficType}: {ValidationMessage}");
        }

        return new EdgeTrafficPermissionRule
        {
            TrafficType = TrafficType,
            IsActive = !SupportsOverrideToggle || IsActive,
            Mode = Mode,
            LimitKind = LimitKind,
            LimitValue = Mode == EdgeTrafficPermissionMode.Limited
                ? ParseNullableDouble(LimitValueText)
                : null
        };
    }

    private void Validate(double? edgeCapacity)
    {
        if (SupportsOverrideToggle && !IsActive)
        {
            ValidationMessage = string.Empty;
            return;
        }

        if (Mode != EdgeTrafficPermissionMode.Limited)
        {
            ValidationMessage = string.Empty;
            return;
        }

        var parsed = ParseNullableDouble(LimitValueText);
        if (!parsed.HasValue)
        {
            ValidationMessage = LimitKind == EdgeTrafficLimitKind.PercentOfEdgeCapacity
                ? "Enter a percentage from 0 to 100."
                : "Enter a limit of 0 or more.";
            return;
        }

        if (LimitKind == EdgeTrafficLimitKind.PercentOfEdgeCapacity)
        {
            if (parsed.Value < 0d || parsed.Value > 100d)
            {
                ValidationMessage = "Enter a percentage from 0 to 100.";
                return;
            }

            if (!edgeCapacity.HasValue)
            {
                ValidationMessage = "Set edge capacity before using a percentage limit.";
                return;
            }
        }
        else if (parsed.Value < 0d)
        {
            ValidationMessage = "Enter a limit of 0 or more.";
            return;
        }

        ValidationMessage = string.Empty;
    }

    private static double? ParseNullableDouble(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}

public sealed class WorkspaceViewModel : ObservableObject
{
    internal const double SceneNodeMinWidth = 132d;
    internal const double SceneNodeMaxWidth = 248d;
    internal const double SceneNodeDefaultWidth = 168d;
    internal const double SceneNodeMinHeight = 118d;

    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private readonly NetworkFileService fileService = new();
    private readonly GraphMlFileService graphMlFileService = new();
    private readonly ReportExportService reportExportService = new();
    private readonly NetworkSimulationEngine simulationEngine = new();
    private readonly TemporalNetworkSimulationEngine temporalEngine = new();
    private readonly EdgeTrafficPermissionResolver edgeTrafficPermissionResolver = new();
    private readonly GraphInteractionController interactionController = new();

    private NetworkModel network = new();
    private TemporalNetworkSimulationEngine.TemporalSimulationState? temporalState;
    private TemporalNetworkSimulationEngine.TemporalSimulationStepResult? lastTimelineStepResult;
    private IReadOnlyList<TrafficSimulationOutcome> lastOutcomes = [];
    private IReadOnlyList<ConsumerCostSummary> lastConsumerCosts = [];
    private string statusText = "Select a tool and start editing.";
    private string toolStatusText = "Select mode: select, drag, or marquee.";
    private string toolInstructionText = "Shortcuts: S Select, A Add node, C Connect, Ctrl-drag creates a route.";
    private GraphToolMode activeToolMode = GraphToolMode.Select;
    private bool reducedMotion;
    private int currentPeriod;
    private int timelineMaximum = 12;
    private int timelinePosition;
    private string networkNameText = string.Empty;
    private string networkDescriptionText = string.Empty;
    private string networkTimelineLoopLengthText = string.Empty;
    private string bulkPlaceTypeText = string.Empty;
    private string bulkTranshipmentCapacityText = string.Empty;
    private string nodeNameText = string.Empty;
    private string nodePlaceTypeText = string.Empty;
    private string nodeDescriptionText = string.Empty;
    private string nodeTranshipmentCapacityText = string.Empty;
    private NodeVisualShape nodeShape = NodeVisualShape.Square;
    private NodeKind nodeKind = NodeKind.Ordinary;
    private NodeTrafficProfileListItem? selectedNodeTrafficProfileItem;
    private bool isPopulatingNodeTrafficEditor;
    private string nodeTrafficTypeText = string.Empty;
    private string nodeTrafficRoleText = NodeTrafficRoleCatalog.RoleOptions[0];
    private string nodeProductionText = "0";
    private string nodeConsumptionText = "0";
    private string nodeConsumerPremiumText = "0";
    private string nodeProductionStartText = string.Empty;
    private string nodeProductionEndText = string.Empty;
    private string nodeConsumptionStartText = string.Empty;
    private string nodeConsumptionEndText = string.Empty;
    private bool nodeCanTransship = true;
    private bool nodeStoreEnabled;
    private string nodeStoreCapacityText = string.Empty;
    private string edgeRouteTypeText = string.Empty;
    private string edgeTimeText = "1";
    private string edgeCostText = "1";
    private string edgeCapacityText = string.Empty;
    private bool edgeIsBidirectional = true;
    private string inspectorValidationText = string.Empty;
    private TrafficDefinitionListItem? selectedTrafficDefinitionItem;
    private string trafficNameText = string.Empty;
    private string trafficDescriptionText = string.Empty;
    private RoutingPreference trafficRoutingPreference = RoutingPreference.TotalCost;
    private AllocationMode trafficAllocationMode = AllocationMode.GreedyBestRoute;
    private RouteChoiceModel trafficRouteChoiceModel = RouteChoiceModel.StochasticUserResponsive;
    private FlowSplitPolicy trafficFlowSplitPolicy = FlowSplitPolicy.MultiPath;
    private string trafficCapacityBidText = string.Empty;
    private string trafficPerishabilityText = string.Empty;
    private string trafficValidationText = string.Empty;
    private string pendingTrafficRemovalName = string.Empty;
    private InspectorTabTarget selectedInspectorTab = InspectorTabTarget.Selection;
    private InspectorSectionTarget selectedInspectorSection = InspectorSectionTarget.None;

    public WorkspaceViewModel()
    {
        Scene = new GraphScene();
        Viewport = new GraphViewport();
        Inspector = new InspectorSection();
        ReportMetrics = [];
        TrafficReports = [];
        RouteReports = [];
        NodePressureReports = [];
        SelectedNodeTrafficProfiles = [];
        SelectedEdgePermissionRows = [];
        TrafficDefinitions = [];
        DefaultTrafficPermissionRows = [];

        NewCommand = new RelayCommand(CreateBlankNetwork);
        SimulateCommand = new RelayCommand(RunSimulation);
        StepCommand = new RelayCommand(AdvanceTimeline);
        ResetTimelineCommand = new RelayCommand(ResetTimeline);
        FitCommand = new RelayCommand(() =>
        {
            Viewport.Reset(Scene.GetContentBounds(), LastViewportSize.Width <= 0d ? new GraphSize(1440d, 860d) : LastViewportSize);
            NotifyVisualChanged();
            StatusText = "Fit the graph to the current view.";
        });
        ToggleMotionCommand = new RelayCommand(() =>
        {
            ReducedMotion = !ReducedMotion;
            Scene.Simulation.ReducedMotion = ReducedMotion;
            NotifyVisualChanged();
        });
        SelectToolCommand = new RelayCommand(() => SetActiveTool(GraphToolMode.Select));
        AddNodeToolCommand = new RelayCommand(() => SetActiveTool(GraphToolMode.AddNode));
        ConnectToolCommand = new RelayCommand(() => SetActiveTool(GraphToolMode.Connect));
        DeleteSelectionCommand = new RelayCommand(DeleteSelection, () => CanDeleteSelection);
        ApplyInspectorCommand = new RelayCommand(ApplyInspectorEdits, () => CanApplyInspectorEdits);
        AddNodeTrafficProfileCommand = new RelayCommand(AddNodeTrafficProfile, () => IsEditingNode);
        DuplicateSelectedNodeTrafficProfileCommand = new RelayCommand(DuplicateSelectedNodeTrafficProfile, () => IsEditingNode && SelectedNodeTrafficProfileItem is not null);
        RemoveSelectedNodeTrafficProfileCommand = new RelayCommand(RemoveSelectedNodeTrafficProfile, () => IsEditingNode && SelectedNodeTrafficProfileItem is not null);
        AddTrafficDefinitionCommand = new RelayCommand(AddTrafficDefinition);
        RemoveSelectedTrafficDefinitionCommand = new RelayCommand(RemoveSelectedTrafficDefinition, () => SelectedTrafficDefinitionItem is not null);
        ApplyTrafficDefinitionCommand = new RelayCommand(ApplyTrafficDefinitionEdits, () => SelectedTrafficDefinitionItem is not null);

        CreateBlankNetwork();
        SetActiveTool(GraphToolMode.Select);
    }

    public GraphScene Scene { get; }
    public GraphViewport Viewport { get; }
    public GraphSize LastViewportSize { get; private set; } = new(1440d, 860d);
    public int ViewportVersion { get; private set; }
    public InspectorSection Inspector { get; }
    public ObservableCollection<ReportMetricViewModel> ReportMetrics { get; }
    public ObservableCollection<TrafficReportRowViewModel> TrafficReports { get; }
    public ObservableCollection<RouteReportRowViewModel> RouteReports { get; }
    public ObservableCollection<NodePressureReportRowViewModel> NodePressureReports { get; }
    public ObservableCollection<NodeTrafficProfileListItem> SelectedNodeTrafficProfiles { get; }
    public ObservableCollection<PermissionRuleEditorRow> SelectedEdgePermissionRows { get; }
    public ObservableCollection<TrafficDefinitionListItem> TrafficDefinitions { get; }
    public ObservableCollection<PermissionRuleEditorRow> DefaultTrafficPermissionRows { get; }

    public Array NodeShapeOptions { get; } = Enum.GetValues(typeof(NodeVisualShape));
    public Array NodeKindOptions { get; } = Enum.GetValues(typeof(NodeKind));
    public IReadOnlyList<string> NodeRoleOptions { get; } = NodeTrafficRoleCatalog.RoleOptions;
    public Array RoutingPreferenceOptions { get; } = Enum.GetValues(typeof(RoutingPreference));
    public Array AllocationModeOptions { get; } = Enum.GetValues(typeof(AllocationMode));
    public Array RouteChoiceModelOptions { get; } = Enum.GetValues(typeof(RouteChoiceModel));
    public Array FlowSplitPolicyOptions { get; } = Enum.GetValues(typeof(FlowSplitPolicy));
    public Array PermissionModeOptions { get; } = Enum.GetValues(typeof(EdgeTrafficPermissionMode));
    public Array PermissionLimitKindOptions { get; } = Enum.GetValues(typeof(EdgeTrafficLimitKind));

    public RelayCommand NewCommand { get; }
    public RelayCommand SimulateCommand { get; }
    public RelayCommand StepCommand { get; }
    public RelayCommand ResetTimelineCommand { get; }
    public RelayCommand FitCommand { get; }
    public RelayCommand ToggleMotionCommand { get; }
    public RelayCommand SelectToolCommand { get; }
    public RelayCommand AddNodeToolCommand { get; }
    public RelayCommand ConnectToolCommand { get; }
    public RelayCommand DeleteSelectionCommand { get; }
    public RelayCommand ApplyInspectorCommand { get; }
    public RelayCommand AddNodeTrafficProfileCommand { get; }
    public RelayCommand DuplicateSelectedNodeTrafficProfileCommand { get; }
    public RelayCommand RemoveSelectedNodeTrafficProfileCommand { get; }
    public RelayCommand AddTrafficDefinitionCommand { get; }
    public RelayCommand RemoveSelectedTrafficDefinitionCommand { get; }
    public RelayCommand ApplyTrafficDefinitionCommand { get; }

    public GraphInteractionController InteractionController => interactionController;
    public string WindowTitle => $"MedW Network Sim | Avalonia Workstation | {network.Name}";
    public string SessionSubtitle => $"Active network: {network.Name} · {SimulationSummary}";
    public string StatusText { get => statusText; set => SetProperty(ref statusText, value); }
    public string ToolStatusText { get => toolStatusText; private set => SetProperty(ref toolStatusText, value); }
    public string ToolInstructionText { get => toolInstructionText; private set => SetProperty(ref toolInstructionText, value); }
    public GraphToolMode ActiveToolMode { get => activeToolMode; private set => SetProperty(ref activeToolMode, value); }
    public bool IsSelectToolActive => ActiveToolMode == GraphToolMode.Select;
    public bool IsAddNodeToolActive => ActiveToolMode == GraphToolMode.AddNode;
    public bool IsConnectToolActive => ActiveToolMode == GraphToolMode.Connect;
    public bool ReducedMotion { get => reducedMotion; set => SetProperty(ref reducedMotion, value); }
    public int CurrentPeriod { get => currentPeriod; private set => SetProperty(ref currentPeriod, value); }
    public int TimelineMaximum { get => timelineMaximum; private set => SetProperty(ref timelineMaximum, value); }
    public int TimelinePosition { get => timelinePosition; set => SetProperty(ref timelinePosition, value); }
    public string SimulationSummary => temporalState is null ? "Static mode" : $"Timeline period {CurrentPeriod}";
    public string SelectionSummary => BuildSelectionSummary();
    public bool CanDeleteSelection => Scene.Selection.SelectedNodeIds.Count > 0 || Scene.Selection.SelectedEdgeIds.Count > 0;
    public InspectorEditMode CurrentInspectorEditMode => GetInspectorEditMode();
    public bool IsEditingNetwork => CurrentInspectorEditMode == InspectorEditMode.Network;
    public bool IsEditingNode => CurrentInspectorEditMode == InspectorEditMode.Node;
    public bool IsEditingEdge => CurrentInspectorEditMode == InspectorEditMode.Edge;
    public bool IsEditingSelection => CurrentInspectorEditMode == InspectorEditMode.Selection;
    public InspectorTabTarget SelectedInspectorTab
    {
        get => selectedInspectorTab;
        set
        {
            if (SetProperty(ref selectedInspectorTab, value))
            {
                Raise(nameof(SelectedInspectorTabIndex));
            }
        }
    }
    public int SelectedInspectorTabIndex
    {
        get => (int)SelectedInspectorTab;
        set
        {
            if (Enum.IsDefined(typeof(InspectorTabTarget), value))
            {
                SelectedInspectorTab = (InspectorTabTarget)value;
                Raise(nameof(SelectedInspectorTabIndex));
            }
        }
    }
    public InspectorSectionTarget SelectedInspectorSection { get => selectedInspectorSection; set => SetProperty(ref selectedInspectorSection, value); }
    public string ApplyInspectorLabel => CurrentInspectorEditMode == InspectorEditMode.Node ? "Apply Node Changes" : "Apply Changes";
    public bool IsNodeTrafficRoleSelected => SelectedNodeTrafficProfileItem is not null;
    public bool IsNodeStoreCapacityEnabled => IsNodeTrafficRoleSelected && NodeStoreEnabled;
    public string NodeTrafficRoleValidationText => BuildNodeTrafficRoleValidationText();
    public bool CanApplyInspectorEdits => string.IsNullOrWhiteSpace(NodeTrafficRoleValidationText);
    public string NetworkNameText { get => networkNameText; set => SetProperty(ref networkNameText, value); }
    public string NetworkDescriptionText { get => networkDescriptionText; set => SetProperty(ref networkDescriptionText, value); }
    public string NetworkTimelineLoopLengthText { get => networkTimelineLoopLengthText; set => SetProperty(ref networkTimelineLoopLengthText, value); }
    public string BulkPlaceTypeText { get => bulkPlaceTypeText; set => SetProperty(ref bulkPlaceTypeText, value); }
    public string BulkTranshipmentCapacityText { get => bulkTranshipmentCapacityText; set => SetProperty(ref bulkTranshipmentCapacityText, value); }
    public string NodeNameText { get => nodeNameText; set => SetProperty(ref nodeNameText, value); }
    public string NodePlaceTypeText { get => nodePlaceTypeText; set => SetProperty(ref nodePlaceTypeText, value); }
    public string NodeDescriptionText { get => nodeDescriptionText; set => SetProperty(ref nodeDescriptionText, value); }
    public string NodeTranshipmentCapacityText { get => nodeTranshipmentCapacityText; set => SetProperty(ref nodeTranshipmentCapacityText, value); }
    public NodeVisualShape NodeShape { get => nodeShape; set => SetProperty(ref nodeShape, value); }
    public NodeKind NodeKind { get => nodeKind; set => SetProperty(ref nodeKind, value); }

    public NodeTrafficProfileListItem? SelectedNodeTrafficProfileItem
    {
        get => selectedNodeTrafficProfileItem;
        set
        {
            if (SetProperty(ref selectedNodeTrafficProfileItem, value))
            {
                PopulateSelectedNodeTrafficEditor();
                Raise(nameof(IsNodeTrafficRoleSelected));
                Raise(nameof(IsNodeStoreCapacityEnabled));
                Raise(nameof(NodeTrafficRoleValidationText));
                Raise(nameof(CanApplyInspectorEdits));
                DuplicateSelectedNodeTrafficProfileCommand.NotifyCanExecuteChanged();
                RemoveSelectedNodeTrafficProfileCommand.NotifyCanExecuteChanged();
                ApplyInspectorCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<string> TrafficTypeNameOptions =>
        network.TrafficTypes.Select(definition => definition.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(Comparer).OrderBy(name => name, Comparer).ToList();

    public string NodeTrafficTypeText
    {
        get => nodeTrafficTypeText;
        set
        {
            if (SetProperty(ref nodeTrafficTypeText, value))
            {
                RaiseNodeTrafficRoleValidationStateChanged();
            }
        }
    }
    public string NodeTrafficRoleText
    {
        get => nodeTrafficRoleText;
        set
        {
            if (!SetProperty(ref nodeTrafficRoleText, value))
            {
                return;
            }

            if (!isPopulatingNodeTrafficEditor)
            {
                ApplySelectedNodeTrafficRoleToEditor();
                RaiseNodeTrafficRoleValidationStateChanged();
            }
        }
    }
    public string NodeProductionText { get => nodeProductionText; set => SetProperty(ref nodeProductionText, value); }
    public string NodeConsumptionText { get => nodeConsumptionText; set => SetProperty(ref nodeConsumptionText, value); }
    public string NodeConsumerPremiumText { get => nodeConsumerPremiumText; set => SetProperty(ref nodeConsumerPremiumText, value); }
    public string NodeProductionStartText { get => nodeProductionStartText; set => SetProperty(ref nodeProductionStartText, value); }
    public string NodeProductionEndText { get => nodeProductionEndText; set => SetProperty(ref nodeProductionEndText, value); }
    public string NodeConsumptionStartText { get => nodeConsumptionStartText; set => SetProperty(ref nodeConsumptionStartText, value); }
    public string NodeConsumptionEndText { get => nodeConsumptionEndText; set => SetProperty(ref nodeConsumptionEndText, value); }
    public bool NodeCanTransship { get => nodeCanTransship; set => SetProperty(ref nodeCanTransship, value); }
    public bool NodeStoreEnabled
    {
        get => nodeStoreEnabled;
        set
        {
            if (SetProperty(ref nodeStoreEnabled, value))
            {
                Raise(nameof(IsNodeStoreCapacityEnabled));
            }
        }
    }
    public string NodeStoreCapacityText { get => nodeStoreCapacityText; set => SetProperty(ref nodeStoreCapacityText, value); }
    public string EdgeRouteTypeText { get => edgeRouteTypeText; set => SetProperty(ref edgeRouteTypeText, value); }
    public string EdgeTimeText { get => edgeTimeText; set => SetProperty(ref edgeTimeText, value); }
    public string EdgeCostText { get => edgeCostText; set => SetProperty(ref edgeCostText, value); }
    public string EdgeCapacityText { get => edgeCapacityText; set => SetProperty(ref edgeCapacityText, value); }
    public bool EdgeIsBidirectional { get => edgeIsBidirectional; set => SetProperty(ref edgeIsBidirectional, value); }
    public string InspectorValidationText { get => inspectorValidationText; set => SetProperty(ref inspectorValidationText, value); }

    public TrafficDefinitionListItem? SelectedTrafficDefinitionItem
    {
        get => selectedTrafficDefinitionItem;
        set
        {
            if (SetProperty(ref selectedTrafficDefinitionItem, value))
            {
                PopulateTrafficDefinitionEditor();
                RemoveSelectedTrafficDefinitionCommand.NotifyCanExecuteChanged();
                ApplyTrafficDefinitionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string TrafficNameText { get => trafficNameText; set => SetProperty(ref trafficNameText, value); }
    public string TrafficDescriptionText { get => trafficDescriptionText; set => SetProperty(ref trafficDescriptionText, value); }
    public RoutingPreference TrafficRoutingPreference { get => trafficRoutingPreference; set => SetProperty(ref trafficRoutingPreference, value); }
    public AllocationMode TrafficAllocationMode { get => trafficAllocationMode; set => SetProperty(ref trafficAllocationMode, value); }
    public RouteChoiceModel TrafficRouteChoiceModel { get => trafficRouteChoiceModel; set => SetProperty(ref trafficRouteChoiceModel, value); }
    public FlowSplitPolicy TrafficFlowSplitPolicy { get => trafficFlowSplitPolicy; set => SetProperty(ref trafficFlowSplitPolicy, value); }
    public string TrafficCapacityBidText { get => trafficCapacityBidText; set => SetProperty(ref trafficCapacityBidText, value); }
    public string TrafficPerishabilityText { get => trafficPerishabilityText; set => SetProperty(ref trafficPerishabilityText, value); }
    public string TrafficValidationText { get => trafficValidationText; private set => SetProperty(ref trafficValidationText, value); }

    public GraphInteractionContext CreateInteractionContext(GraphSize viewportSize)
    {
        LastViewportSize = viewportSize;
        return new GraphInteractionContext
        {
            Scene = Scene,
            Viewport = Viewport,
            ViewportSize = viewportSize,
            ToolMode = ActiveToolMode,
            ToolModeChanged = SetActiveTool,
            CreateEdge = CreateEdge,
            AddNodeAt = AddNodeAt,
            DeleteSelection = DeleteSelection,
            FocusNextConnectedEdge = FocusNextConnectedEdge,
            FocusNearbyNode = FocusNearbyNode,
            SelectionChanged = (_, _) => RefreshInspector(),
            StatusChanged = text => StatusText = text
        };
    }

    public void TickAnimation(double elapsedSeconds)
    {
        Scene.Simulation.AnimationTime += ReducedMotion ? elapsedSeconds * 0.2d : elapsedSeconds;
    }

    public void NotifyVisualChanged()
    {
        SyncNetworkNodePositionsFromScene();
        ViewportVersion++;
        Raise(nameof(ViewportVersion));
        Raise(nameof(WindowTitle));
        Raise(nameof(SessionSubtitle));
        Raise(nameof(SelectionSummary));
        Raise(nameof(SimulationSummary));
    }

    public void OpenNetwork(string path)
    {
        LoadNetwork(fileService.Load(path), $"Opened '{Path.GetFileName(path)}'.");
    }

    public void SaveNetwork(string path)
    {
        CommitTransientEditorsToModel();
        fileService.Save(network, path);
        StatusText = $"Saved '{Path.GetFileName(path)}'.";
    }

    public void ImportGraphMl(string path)
    {
        LoadNetwork(graphMlFileService.Load(path, new GraphMlTransferOptions(default, "transship", 25d)), $"Imported '{Path.GetFileName(path)}'.");
    }

    public void ExportGraphMl(string path)
    {
        CommitTransientEditorsToModel();
        graphMlFileService.Save(network, path, new GraphMlTransferOptions(network.TrafficTypes.FirstOrDefault()?.Name, "transship", 25d));
        StatusText = $"Exported GraphML to '{Path.GetFileName(path)}'.";
    }

    public void ExportCurrentReport(string path, ReportExportFormat format)
    {
        CommitTransientEditorsToModel();
        reportExportService.SaveCurrentReport(network, lastOutcomes, lastConsumerCosts, path, format);
        StatusText = $"Exported the current report to '{Path.GetFileName(path)}'.";
    }

    public void ExportTimelineReport(string path, int periods, ReportExportFormat format)
    {
        CommitTransientEditorsToModel();
        var state = temporalEngine.Initialize(network);
        var results = new List<TemporalNetworkSimulationEngine.TemporalSimulationStepResult>();
        for (var index = 0; index < Math.Max(1, periods); index++)
        {
            results.Add(temporalEngine.Advance(network, state));
        }

        reportExportService.SaveTimelineReport(network, results, path, format);
        StatusText = $"Exported {results.Count} timeline periods to '{Path.GetFileName(path)}'.";
    }

    public void SetActiveTool(GraphToolMode toolMode)
    {
        ActiveToolMode = toolMode;
        ToolStatusText = toolMode switch
        {
            GraphToolMode.AddNode => "Add node mode: click the canvas to place a node.",
            GraphToolMode.Connect => "Connect mode: choose a source node, then a target node.",
            _ => "Select mode: select, drag, or marquee."
        };
        ToolInstructionText = toolMode switch
        {
            GraphToolMode.AddNode => "Keyboard: A keeps Add node active. Esc returns to Select.",
            GraphToolMode.Connect => "Keyboard: C keeps Connect active. Esc returns to Select.",
            _ => "Keyboard: S Select, A Add node, C Connect. Ctrl-drag from one node to another to create a route."
        };
        Raise(nameof(IsSelectToolActive));
        Raise(nameof(IsAddNodeToolActive));
        Raise(nameof(IsConnectToolActive));
    }

    private void CreateBlankNetwork()
    {
        LoadNetwork(new NetworkModel
        {
            Name = "Untitled Network",
            Description = string.Empty,
            TimelineLoopLength = 12,
            TrafficTypes =
            [
                new TrafficTypeDefinition
                {
                    Name = "general",
                    RoutingPreference = RoutingPreference.TotalCost,
                    AllocationMode = AllocationMode.GreedyBestRoute
                }
            ],
            EdgeTrafficPermissionDefaults =
            [
                new EdgeTrafficPermissionRule { TrafficType = "general", Mode = EdgeTrafficPermissionMode.Permitted }
            ],
            Nodes = [],
            Edges = []
        }, "Created a blank network.");
    }

    private void LoadNetwork(NetworkModel source, string status)
    {
        network = fileService.NormalizeAndValidate(source);
        temporalState = null;
        lastTimelineStepResult = null;
        lastOutcomes = [];
        lastConsumerCosts = [];
        CurrentPeriod = 0;
        TimelineMaximum = Math.Max(8, network.TimelineLoopLength ?? 12);
        TimelinePosition = 0;
        pendingTrafficRemovalName = string.Empty;
        BuildSceneFromNetwork();
        Viewport.Reset(Scene.GetContentBounds(), LastViewportSize);
        PopulateTrafficDefinitionList();
        PopulateDefaultPermissionRows();
        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        Scene.Selection.KeyboardNodeId = null;
        Scene.Selection.KeyboardEdgeId = null;
        Scene.Transient.ConnectionSourceNodeId = null;
        Scene.Transient.ConnectionWorld = null;
        Scene.Transient.DragCurrentWorld = null;
        Scene.Transient.DragStartWorld = null;
        Scene.Simulation.ShowAnimatedFlows = true;
        Scene.Simulation.ReducedMotion = ReducedMotion;
        ClearDynamicReports();
        RefreshInspector();
        StatusText = status;
        NotifyVisualChanged();
    }

    private void BuildSceneFromNetwork()
    {
        Scene.Nodes.Clear();
        Scene.Edges.Clear();

        foreach (var node in network.Nodes)
        {
            var centerX = node.X ?? 0d;
            var centerY = node.Y ?? 0d;
            var detailLines = BuildNodeDetailLines(node, [], null);
            var typeLabel = string.IsNullOrWhiteSpace(node.PlaceType) ? "Node" : node.PlaceType!;
            var nodeWidth = GraphNodeTextLayout.ComputeNodeWidth(node.Name, typeLabel, detailLines);
            var nodeHeight = GraphNodeTextLayout.BuildLayout(node.Name, typeLabel, detailLines, nodeWidth).Height;
            Scene.Nodes.Add(new GraphNodeSceneItem
            {
                Id = node.Id,
                Name = node.Name,
                TypeLabel = typeLabel,
                MetricsLabel = string.Empty,
                DetailLines = detailLines,
                Bounds = new GraphRect(centerX - (nodeWidth / 2d), centerY - (nodeHeight / 2d), nodeWidth, nodeHeight),
                FillColor = SKColor.Parse("#163149"),
                StrokeColor = SKColor.Parse("#6AAED6"),
                Badges = BuildNodeBadges(node),
                ToolTipText = BuildNodeToolTipText(node, detailLines, null),
                HasWarning = false
            });
        }

        foreach (var edge in network.Edges)
        {
            Scene.Edges.Add(new GraphEdgeSceneItem
            {
                Id = edge.Id,
                FromNodeId = edge.FromNodeId,
                ToNodeId = edge.ToNodeId,
                Label = edge.RouteType ?? edge.Id,
                IsBidirectional = edge.IsBidirectional,
                Capacity = edge.Capacity ?? 0d,
                Cost = edge.Cost,
                Time = edge.Time,
                LoadRatio = 0d,
                FlowRate = 0d,
                ToolTipText = BuildEdgeToolTipText(edge, TemporalNetworkSimulationEngine.EdgeFlowVisualSummary.Empty, 0d, null),
                HasWarning = false
            });
        }
    }

    private bool CreateEdge(string sourceId, string targetId)
    {
        CommitTransientEditorsToModel();
        var edgeId = $"{sourceId}->{targetId}";
        if (network.Edges.Any(edge => Comparer.Equals(edge.Id, edgeId)))
        {
            StatusText = "That route already exists.";
            return false;
        }

        network.Edges.Add(new EdgeModel
        {
            Id = edgeId,
            FromNodeId = sourceId,
            ToNodeId = targetId,
            Time = 1.5d,
            Cost = 1d,
            Capacity = 30d,
            IsBidirectional = false,
            RouteType = "Proposed route",
            TrafficPermissions = []
        });
        BuildSceneFromNetwork();
        RefreshInspector();
        NotifyVisualChanged();
        return true;
    }

    private string AddNodeAt(GraphPoint center)
    {
        CommitTransientEditorsToModel();
        EnsureDefaultTrafficType();
        var index = network.Nodes.Count + 1;
        var id = $"node-{index}";
        var trafficName = network.TrafficTypes.First().Name;
        network.Nodes.Add(new NodeModel
        {
            Id = id,
            Name = $"Node {index}",
            X = center.X,
            Y = center.Y,
            PlaceType = "Draft place",
            LoreDescription = string.Empty,
            Shape = NodeVisualShape.Square,
            NodeKind = NodeKind.Ordinary,
            TranshipmentCapacity = 40d,
            TrafficProfiles =
            [
                new NodeTrafficProfile
                {
                    TrafficType = trafficName,
                    CanTransship = true
                }
            ]
        });
        BuildSceneFromNetwork();
        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedNodeIds.Add(id);
        RefreshInspector();
        NotifyVisualChanged();
        return id;
    }

    private void DeleteSelection()
    {
        CommitTransientEditorsToModel();
        var selectedNodes = Scene.Selection.SelectedNodeIds.ToHashSet(Comparer);
        var selectedEdges = Scene.Selection.SelectedEdgeIds.ToHashSet(Comparer);

        network.Nodes.RemoveAll(node => selectedNodes.Contains(node.Id));
        network.Edges.RemoveAll(edge =>
            selectedEdges.Contains(edge.Id) ||
            selectedNodes.Contains(edge.FromNodeId) ||
            selectedNodes.Contains(edge.ToNodeId));

        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        BuildSceneFromNetwork();
        RefreshInspector();
        NotifyVisualChanged();
        StatusText = "Deleted the current selection.";
    }

    private string? FocusNextConnectedEdge()
    {
        var selectedNodeId = Scene.Selection.SelectedNodeIds.FirstOrDefault();
        var edges = selectedNodeId is null
            ? Scene.Edges.ToList()
            : Scene.Edges.Where(edge => Comparer.Equals(edge.FromNodeId, selectedNodeId) || Comparer.Equals(edge.ToNodeId, selectedNodeId)).ToList();

        if (edges.Count == 0)
        {
            return null;
        }

        var currentIndex = Scene.Selection.KeyboardEdgeId is null ? -1 : edges.FindIndex(edge => Comparer.Equals(edge.Id, Scene.Selection.KeyboardEdgeId));
        return edges[(currentIndex + 1 + edges.Count) % edges.Count].Id;
    }

    private string? FocusNearbyNode(string? currentNodeId, bool reverse, string direction)
    {
        if (Scene.Nodes.Count == 0)
        {
            return null;
        }

        var current = Scene.FindNode(currentNodeId) ?? Scene.Nodes.First();
        var dx = direction switch
        {
            "Left" => -1d,
            "Right" => 1d,
            _ => 0d
        };
        var dy = direction switch
        {
            "Up" => -1d,
            "Down" => 1d,
            _ => 0d
        };

        var best = Scene.Nodes
            .Where(node => !Comparer.Equals(node.Id, current.Id))
            .Select(node => new
            {
                Node = node,
                DeltaX = node.Bounds.CenterX - current.Bounds.CenterX,
                DeltaY = node.Bounds.CenterY - current.Bounds.CenterY
            })
            .Where(item => (dx == 0d || Math.Sign(item.DeltaX) == Math.Sign(dx)) && (dy == 0d || Math.Sign(item.DeltaY) == Math.Sign(dy)))
            .OrderBy(item => Math.Sqrt((item.DeltaX * item.DeltaX) + (item.DeltaY * item.DeltaY)))
            .ToList();

        return reverse ? best.LastOrDefault()?.Node.Id : best.FirstOrDefault()?.Node.Id;
    }

    private void RunSimulation()
    {
        CommitTransientEditorsToModel();
        var outcomes = simulationEngine.Simulate(fileService.NormalizeAndValidate(network));
        lastOutcomes = outcomes;
        lastConsumerCosts = simulationEngine.SummarizeConsumerCosts(outcomes);
        lastTimelineStepResult = null;
        ApplySimulationOutcomes(outcomes.SelectMany(outcome => outcome.Allocations), null);
        StatusText = "Simulation finished.";
    }

    private void AdvanceTimeline()
    {
        CommitTransientEditorsToModel();
        temporalState ??= temporalEngine.Initialize(fileService.NormalizeAndValidate(network));
        var result = temporalEngine.Advance(network, temporalState);
        lastTimelineStepResult = result;
        lastConsumerCosts = simulationEngine.SummarizeConsumerCosts(result.Allocations);
        CurrentPeriod = result.Period;
        TimelinePosition = result.EffectivePeriod;
        ApplySimulationOutcomes(result.Allocations, result);
        StatusText = $"Advanced to period {result.Period}.";
    }

    private void ResetTimeline()
    {
        temporalState = null;
        lastTimelineStepResult = null;
        lastOutcomes = [];
        lastConsumerCosts = [];
        CurrentPeriod = 0;
        TimelinePosition = 0;
        foreach (var edge in Scene.Edges)
        {
            edge.LoadRatio = 0d;
            edge.FlowRate = 0d;
            edge.HasWarning = false;
        }

        foreach (var node in Scene.Nodes)
        {
            var nodeModel = network.Nodes.First(model => Comparer.Equals(model.Id, node.Id));
            node.MetricsLabel = string.Empty;
            node.DetailLines = BuildNodeDetailLines(nodeModel, [], null);
            UpdateSceneNodeLayout(node, nodeModel, null);
            node.HasWarning = false;
        }

        ClearDynamicReports();
        RefreshInspector();
        NotifyVisualChanged();
        StatusText = "Reset the timeline to period 0.";
    }

    private void ApplySimulationOutcomes(IEnumerable<RouteAllocation> allocations, TemporalNetworkSimulationEngine.TemporalSimulationStepResult? timeline)
    {
        var allocationList = allocations.ToList();
        var edgeLoads = Scene.Edges.ToDictionary(edge => edge.Id, _ => 0d, Comparer);
        foreach (var allocation in allocationList)
        {
            foreach (var edgeId in allocation.PathEdgeIds)
            {
                edgeLoads[edgeId] = edgeLoads.GetValueOrDefault(edgeId) + allocation.Quantity;
            }
        }

        var maxLoad = Math.Max(1d, edgeLoads.Values.DefaultIfEmpty(0d).Max());
        foreach (var edge in Scene.Edges)
        {
            var load = edgeLoads.GetValueOrDefault(edge.Id);
            edge.LoadRatio = load / maxLoad;
            edge.FlowRate = load / maxLoad;
            edge.HasWarning = edge.Capacity > 0d && load >= edge.Capacity * 0.8d;
            var edgeModel = network.Edges.First(model => Comparer.Equals(model.Id, edge.Id));
            var edgeFlow = timeline?.EdgeFlows.GetValueOrDefault(edge.Id, TemporalNetworkSimulationEngine.EdgeFlowVisualSummary.Empty)
                ?? new TemporalNetworkSimulationEngine.EdgeFlowVisualSummary(load, 0d);
            var edgeOccupancy = timeline?.EdgeOccupancy.GetValueOrDefault(edge.Id, load) ?? load;
            var edgePressure = timeline?.EdgePressureById.GetValueOrDefault(edge.Id);
            edge.ToolTipText = BuildEdgeToolTipText(edgeModel, edgeFlow, edgeOccupancy, edgePressure);
        }

        if (timeline is not null)
        {
            foreach (var node in Scene.Nodes)
            {
                var nodeModel = network.Nodes.First(model => Comparer.Equals(model.Id, node.Id));
                var state = timeline.NodeStates
                    .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id))
                    .Select(pair => pair.Value)
                    .FirstOrDefault();
                var backlogByTraffic = timeline.NodeStates
                    .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id) && pair.Value.DemandBacklog > 0d)
                    .GroupBy(pair => pair.Key.TrafficType, pair => pair.Value.DemandBacklog, Comparer)
                    .Select(group => new KeyValuePair<string, double>(group.Key, group.Sum()))
                    .ToList();
                var pressure = timeline.NodePressureById.GetValueOrDefault(node.Id);
                node.MetricsLabel = string.Empty;
                node.DetailLines = BuildNodeDetailLines(nodeModel, backlogByTraffic, pressure.Score > 0d ? pressure : null);
                UpdateSceneNodeLayout(node, nodeModel, pressure.Score > 0d ? pressure : null);
                node.HasWarning = pressure.Score > 0d || state.DemandBacklog > 0d;
            }
        }
        else
        {
            foreach (var node in Scene.Nodes)
            {
                var nodeModel = network.Nodes.First(model => Comparer.Equals(model.Id, node.Id));
                node.MetricsLabel = string.Empty;
                node.DetailLines = BuildNodeDetailLines(nodeModel, [], null);
                UpdateSceneNodeLayout(node, nodeModel, null);
                node.HasWarning = false;
            }
        }

        PopulateTrafficReports(timeline, allocationList);
        PopulateRouteReports(timeline, edgeLoads);
        PopulateNodePressureReports(timeline);
        PopulateQuickMetrics(timeline, edgeLoads);
        RefreshInspector();
        NotifyVisualChanged();
    }

    private void PopulateReportMetrics(IEnumerable<ReportMetricViewModel> metrics)
    {
        ReportMetrics.Clear();
        foreach (var metric in metrics)
        {
            ReportMetrics.Add(metric);
        }
    }

    private void PopulateTrafficReports(
        TemporalNetworkSimulationEngine.TemporalSimulationStepResult? timeline,
        IReadOnlyList<RouteAllocation> allocations)
    {
        TrafficReports.Clear();
        var trafficTypes = network.TrafficTypes
            .Select(definition => definition.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(Comparer)
            .OrderBy(name => name, Comparer)
            .ToList();

        foreach (var trafficType in trafficTypes)
        {
            var outcome = lastOutcomes.FirstOrDefault(item => Comparer.Equals(item.TrafficType, trafficType));
            var backlog = timeline is null
                ? outcome?.UnmetDemand ?? 0d
                : timeline.NodeStates
                    .Where(pair => Comparer.Equals(pair.Key.TrafficType, trafficType))
                    .Sum(pair => pair.Value.DemandBacklog);
            var planned = timeline is null
                ? outcome?.TotalProduction ?? allocations.Where(item => Comparer.Equals(item.TrafficType, trafficType)).Sum(item => item.Quantity)
                : allocations.Where(item => Comparer.Equals(item.TrafficType, trafficType)).Sum(item => item.Quantity);
            var delivered = timeline is null
                ? outcome?.TotalDelivered ?? allocations.Where(item => Comparer.Equals(item.TrafficType, trafficType)).Sum(item => item.Quantity)
                : allocations.Where(item => Comparer.Equals(item.TrafficType, trafficType)).Sum(item => item.Quantity);
            var unmetDemand = timeline is null ? outcome?.UnmetDemand ?? 0d : backlog;

            TrafficReports.Add(new TrafficReportRowViewModel
            {
                TrafficType = trafficType,
                PlannedQuantity = ReportExportService.FormatNumber(planned),
                DeliveredQuantity = ReportExportService.FormatNumber(delivered),
                UnmetDemand = ReportExportService.FormatNumber(unmetDemand),
                Backlog = ReportExportService.FormatNumber(backlog)
            });
        }
    }

    private void PopulateRouteReports(
        TemporalNetworkSimulationEngine.TemporalSimulationStepResult? timeline,
        IReadOnlyDictionary<string, double> edgeLoads)
    {
        RouteReports.Clear();
        foreach (var edge in network.Edges.OrderBy(item => item.RouteType ?? item.Id, Comparer))
        {
            var flowSummary = timeline?.EdgeFlows.GetValueOrDefault(edge.Id, TemporalNetworkSimulationEngine.EdgeFlowVisualSummary.Empty)
                ?? TemporalNetworkSimulationEngine.EdgeFlowVisualSummary.Empty;
            var totalFlow = timeline is null
                ? edgeLoads.GetValueOrDefault(edge.Id, 0d)
                : flowSummary.ForwardQuantity + flowSummary.ReverseQuantity;
            var occupancy = timeline?.EdgeOccupancy.GetValueOrDefault(edge.Id, totalFlow) ?? totalFlow;
            var pressure = timeline?.EdgePressureById.GetValueOrDefault(edge.Id);
            var source = ResolveNodeName(edge.FromNodeId);
            var target = ResolveNodeName(edge.ToNodeId);
            var pressureLabel = pressure is { Score: > 0d }
                ? $"{ReportExportService.FormatNumber(pressure.Value.Score)} | {BuildTopCauseText(pressure.Value.TopCause)}"
                : "None";

            RouteReports.Add(new RouteReportRowViewModel
            {
                RouteId = string.IsNullOrWhiteSpace(edge.RouteType) ? edge.Id : edge.RouteType!,
                FromTo = $"{source} -> {target}",
                CurrentFlow = timeline is not null && edge.IsBidirectional
                    ? $"{ReportExportService.FormatNumber(flowSummary.ForwardQuantity)} / {ReportExportService.FormatNumber(flowSummary.ReverseQuantity)}"
                    : ReportExportService.FormatNumber(totalFlow),
                Capacity = ReportExportService.FormatNumber(edge.Capacity),
                Utilisation = ReportExportService.FormatUtilisation(occupancy, edge.Capacity),
                Pressure = pressureLabel
            });
        }
    }

    private void PopulateNodePressureReports(TemporalNetworkSimulationEngine.TemporalSimulationStepResult? timeline)
    {
        NodePressureReports.Clear();
        if (timeline is null)
        {
            return;
        }

        foreach (var node in network.Nodes.OrderBy(item => item.Name, Comparer))
        {
            var pressure = timeline.NodePressureById.GetValueOrDefault(node.Id);
            var backlog = timeline.NodeStates
                .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id) && pair.Value.DemandBacklog > 0d)
                .OrderByDescending(pair => pair.Value.DemandBacklog)
                .ThenBy(pair => pair.Key.TrafficType, Comparer)
                .FirstOrDefault();
            var unmetNeed = backlog.Value.DemandBacklog > 0d
                ? $"{ReportExportService.FormatNumber(backlog.Value.DemandBacklog)} {backlog.Key.TrafficType}"
                : "None";

            NodePressureReports.Add(new NodePressureReportRowViewModel
            {
                Node = ResolveNodeName(node.Id),
                PressureScore = pressure.Score > 0d ? ReportExportService.FormatNumber(pressure.Score) : "None",
                TopCause = pressure.Score > 0d ? BuildTopCauseText(pressure.TopCause) : "None",
                UnmetNeed = unmetNeed
            });
        }
    }

    private void PopulateQuickMetrics(
        TemporalNetworkSimulationEngine.TemporalSimulationStepResult? timeline,
        IReadOnlyDictionary<string, double> edgeLoads)
    {
        var metrics = new List<ReportMetricViewModel>();
        var mostLoadedRoute = network.Edges
            .Select(edge => new
            {
                Edge = edge,
                Occupancy = timeline?.EdgeOccupancy.GetValueOrDefault(edge.Id, edgeLoads.GetValueOrDefault(edge.Id, 0d)) ?? edgeLoads.GetValueOrDefault(edge.Id, 0d)
            })
            .OrderByDescending(item => item.Occupancy)
            .FirstOrDefault(item => item.Occupancy > 0d);
        if (mostLoadedRoute is not null)
        {
            metrics.Add(new ReportMetricViewModel
            {
                Label = "Most loaded route",
                Value = $"{ResolveEdgeLabel(mostLoadedRoute.Edge.Id)} {ReportExportService.FormatUtilisation(mostLoadedRoute.Occupancy, mostLoadedRoute.Edge.Capacity)}",
                Activate = () => SelectRouteForEdit(mostLoadedRoute.Edge.Id)
            });
        }

        if (timeline is not null)
        {
            var mostPressuredNode = network.Nodes
                .Select(node => new { Node = node, Pressure = timeline.NodePressureById.GetValueOrDefault(node.Id) })
                .OrderByDescending(item => item.Pressure.Score)
                .FirstOrDefault(item => item.Pressure.Score > 0d);
            if (mostPressuredNode is not null)
            {
                metrics.Add(new ReportMetricViewModel
                {
                    Label = "Most pressured node",
                    Value = $"{ResolveNodeName(mostPressuredNode.Node.Id)} {ReportExportService.FormatNumber(mostPressuredNode.Pressure.Score)}",
                    Activate = () => SelectNodeForEdit(mostPressuredNode.Node.Id)
                });
            }

            var topUnmetNode = timeline.NodeStates
                .Where(pair => pair.Value.DemandBacklog > 0d)
                .GroupBy(pair => pair.Key.NodeId, pair => pair.Value.DemandBacklog, Comparer)
                .Select(group => new { NodeId = group.Key, Backlog = group.Sum() })
                .OrderByDescending(item => item.Backlog)
                .FirstOrDefault();
            if (topUnmetNode is not null)
            {
                metrics.Add(new ReportMetricViewModel
                {
                    Label = "Top unmet-need node",
                    Value = $"{ResolveNodeName(topUnmetNode.NodeId)} {ReportExportService.FormatNumber(topUnmetNode.Backlog)}",
                    Activate = () => SelectNodeForEdit(topUnmetNode.NodeId)
                });
            }

            var topTrafficBacklog = timeline.NodeStates
                .Where(pair => pair.Value.DemandBacklog > 0d)
                .GroupBy(pair => pair.Key.TrafficType, pair => pair.Value.DemandBacklog, Comparer)
                .Select(group => new { TrafficType = group.Key, Backlog = group.Sum() })
                .OrderByDescending(item => item.Backlog)
                .FirstOrDefault();
            if (topTrafficBacklog is not null)
            {
                metrics.Add(new ReportMetricViewModel
                {
                    Label = "Top traffic backlog",
                    Value = $"{topTrafficBacklog.TrafficType} {ReportExportService.FormatNumber(topTrafficBacklog.Backlog)}",
                    Activate = () =>
                    {
                        SelectedInspectorTab = InspectorTabTarget.TrafficTypes;
                        SelectedTrafficDefinitionItem = TrafficDefinitions.FirstOrDefault(item => Comparer.Equals(item.Name, topTrafficBacklog.TrafficType));
                        NotifyVisualChanged();
                    }
                });
            }
        }

        PopulateReportMetrics(metrics.Take(4));
    }

    private void ClearDynamicReports()
    {
        PopulateReportMetrics([]);
        TrafficReports.Clear();
        RouteReports.Clear();
        NodePressureReports.Clear();
    }

    private void RefreshInspector()
    {
        Raise(nameof(SelectionSummary));
        Raise(nameof(SessionSubtitle));
        Raise(nameof(CurrentInspectorEditMode));
        Raise(nameof(IsEditingNetwork));
        Raise(nameof(IsEditingNode));
        Raise(nameof(IsEditingEdge));
        Raise(nameof(IsEditingSelection));
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        AddNodeTrafficProfileCommand.NotifyCanExecuteChanged();
        DuplicateSelectedNodeTrafficProfileCommand.NotifyCanExecuteChanged();
        RemoveSelectedNodeTrafficProfileCommand.NotifyCanExecuteChanged();
        ApplyInspectorCommand.NotifyCanExecuteChanged();
        Raise(nameof(ApplyInspectorLabel));
        Raise(nameof(CanApplyInspectorEdits));
        Raise(nameof(NodeTrafficRoleValidationText));

        InspectorValidationText = string.Empty;
        var selectedNodeIds = Scene.Selection.SelectedNodeIds.ToList();
        var selectedEdgeIds = Scene.Selection.SelectedEdgeIds.ToList();

        if (selectedNodeIds.Count == 0 && selectedEdgeIds.Count == 0)
        {
            Inspector.Headline = "Network settings";
            Inspector.Summary = "Select a node or route to edit.";
            Inspector.Details =
            [
                $"Traffic types: {network.TrafficTypes.Count}",
                $"Nodes: {network.Nodes.Count}",
                $"Routes: {network.Edges.Count}",
                $"Current tool: {ActiveToolMode}"
            ];
            PopulateNetworkEditor();
            SelectedNodeTrafficProfiles.Clear();
            SelectedNodeTrafficProfileItem = null;
            SelectedEdgePermissionRows.Clear();
            return;
        }

        if (selectedNodeIds.Count + selectedEdgeIds.Count > 1)
        {
            Inspector.Headline = "Bulk edit";
            Inspector.Summary = "Apply shared node values.";
            Inspector.Details =
            [
                $"{selectedNodeIds.Count} nodes selected",
                $"{selectedEdgeIds.Count} routes selected",
                "Only place type and transhipment capacity update in bulk."
            ];
            PopulateBulkEditor(selectedNodeIds);
            SelectedNodeTrafficProfiles.Clear();
            SelectedNodeTrafficProfileItem = null;
            SelectedEdgePermissionRows.Clear();
            return;
        }

        if (selectedNodeIds.Count == 1)
        {
            var node = network.Nodes.First(model => Comparer.Equals(model.Id, selectedNodeIds[0]));
            Inspector.Headline = node.Name;
            Inspector.Summary = "Edit node details and traffic roles.";
            Inspector.Details =
            [
                $"Place type: {(string.IsNullOrWhiteSpace(node.PlaceType) ? "Not set" : node.PlaceType)}",
                $"Description: {(string.IsNullOrWhiteSpace(node.LoreDescription) ? "Not set" : node.LoreDescription)}",
                $"Traffic roles: {node.TrafficProfiles.Count}"
            ];
            PopulateNodeEditor(node);
            SelectedEdgePermissionRows.Clear();
            return;
        }

        var edge = network.Edges.First(model => Comparer.Equals(model.Id, selectedEdgeIds[0]));
        Inspector.Headline = edge.RouteType ?? edge.Id;
        Inspector.Summary = $"{edge.FromNodeId} -> {edge.ToNodeId}";
        Inspector.Details =
        [
            $"Travel time: {edge.Time:0.##}",
            $"Travel cost: {edge.Cost:0.##}",
            $"Capacity: {(edge.Capacity?.ToString("0.##", CultureInfo.InvariantCulture) ?? "Unlimited")}",
            "Edit route access rules to control which traffic types can use this route."
        ];
        PopulateEdgeEditor(edge);
        SelectedNodeTrafficProfiles.Clear();
        SelectedNodeTrafficProfileItem = null;
    }

    private void PopulateNetworkEditor()
    {
        NetworkNameText = network.Name;
        NetworkDescriptionText = network.Description;
        NetworkTimelineLoopLengthText = network.TimelineLoopLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private void PopulateBulkEditor(IEnumerable<string> selectedNodeIds)
    {
        var selectedNodes = network.Nodes.Where(node => selectedNodeIds.Contains(node.Id, Comparer)).ToList();
        BulkPlaceTypeText = selectedNodes.Select(node => node.PlaceType ?? string.Empty).Distinct(Comparer).Count() == 1
            ? selectedNodes.First().PlaceType ?? string.Empty
            : string.Empty;
        BulkTranshipmentCapacityText = selectedNodes.Select(node => node.TranshipmentCapacity).Distinct().Count() == 1
            ? selectedNodes.First().TranshipmentCapacity?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
    }

    private void PopulateNodeEditor(NodeModel node)
    {
        EnsureDefaultTrafficType();
        NodeNameText = node.Name;
        NodePlaceTypeText = node.PlaceType ?? string.Empty;
        NodeDescriptionText = node.LoreDescription ?? string.Empty;
        NodeTranshipmentCapacityText = node.TranshipmentCapacity?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        NodeShape = node.Shape;
        NodeKind = node.NodeKind;
        SelectedNodeTrafficProfiles.Clear();
        for (var index = 0; index < node.TrafficProfiles.Count; index++)
        {
            SelectedNodeTrafficProfiles.Add(new NodeTrafficProfileListItem(index, node.TrafficProfiles[index]));
        }

        SelectedNodeTrafficProfileItem = SelectedNodeTrafficProfiles.FirstOrDefault();
    }

    private void PopulateSelectedNodeTrafficEditor()
    {
        var profile = SelectedNodeTrafficProfileItem?.Model;
        isPopulatingNodeTrafficEditor = true;
        if (profile is null)
        {
            NodeTrafficTypeText = string.Empty;
            NodeTrafficRoleText = NodeTrafficRoleCatalog.RoleOptions[0];
            NodeProductionText = "0";
            NodeConsumptionText = "0";
            NodeConsumerPremiumText = "0";
            NodeProductionStartText = string.Empty;
            NodeProductionEndText = string.Empty;
            NodeConsumptionStartText = string.Empty;
            NodeConsumptionEndText = string.Empty;
            NodeCanTransship = true;
            NodeStoreEnabled = false;
            NodeStoreCapacityText = string.Empty;
            isPopulatingNodeTrafficEditor = false;
            RaiseNodeTrafficRoleValidationStateChanged();
            return;
        }

        NodeTrafficTypeText = profile.TrafficType;
        NodeTrafficRoleText = NodeTrafficRoleCatalog.GetRoleName(profile.Production > 0d, profile.Consumption > 0d, profile.CanTransship);
        NodeProductionText = profile.Production.ToString("0.##", CultureInfo.InvariantCulture);
        NodeConsumptionText = profile.Consumption.ToString("0.##", CultureInfo.InvariantCulture);
        NodeConsumerPremiumText = profile.ConsumerPremiumPerUnit.ToString("0.##", CultureInfo.InvariantCulture);
        NodeProductionStartText = profile.ProductionStartPeriod?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        NodeProductionEndText = profile.ProductionEndPeriod?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        NodeConsumptionStartText = profile.ConsumptionStartPeriod?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        NodeConsumptionEndText = profile.ConsumptionEndPeriod?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        NodeCanTransship = profile.CanTransship;
        NodeStoreEnabled = profile.IsStore;
        NodeStoreCapacityText = profile.StoreCapacity?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        isPopulatingNodeTrafficEditor = false;
        RaiseNodeTrafficRoleValidationStateChanged();
    }

    private void ApplySelectedNodeTrafficRoleToEditor()
    {
        if (!NodeTrafficRoleCatalog.TryParseFlags(NodeTrafficRoleText, out var flags))
        {
            return;
        }

        var production = TryParseRoleQuantity(NodeProductionText);
        var consumption = TryParseRoleQuantity(NodeConsumptionText);
        var fallbackQuantity = Math.Max(Math.Max(production, consumption), 1d);

        NodeProductionText = flags.IsProducer
            ? FormatRoleQuantity(production > 0d ? production : fallbackQuantity)
            : "0";
        NodeConsumptionText = flags.IsConsumer
            ? FormatRoleQuantity(consumption > 0d ? consumption : fallbackQuantity)
            : "0";
        NodeCanTransship = flags.CanTransship;
    }

    private void PopulateEdgeEditor(EdgeModel edge)
    {
        EdgeRouteTypeText = edge.RouteType ?? string.Empty;
        EdgeTimeText = edge.Time.ToString("0.##", CultureInfo.InvariantCulture);
        EdgeCostText = edge.Cost.ToString("0.##", CultureInfo.InvariantCulture);
        EdgeCapacityText = edge.Capacity?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        EdgeIsBidirectional = edge.IsBidirectional;
        PopulateEdgePermissionRows(edge);
    }

    private void PopulateTrafficDefinitionList()
    {
        TrafficDefinitions.Clear();
        foreach (var definition in network.TrafficTypes.OrderBy(definition => definition.Name, Comparer))
        {
            TrafficDefinitions.Add(new TrafficDefinitionListItem(definition));
        }

        SelectedTrafficDefinitionItem = TrafficDefinitions.FirstOrDefault(item =>
            selectedTrafficDefinitionItem is not null && ReferenceEquals(item.Model, selectedTrafficDefinitionItem.Model))
            ?? TrafficDefinitions.FirstOrDefault();
    }

    private void PopulateTrafficDefinitionEditor()
    {
        var definition = SelectedTrafficDefinitionItem?.Model;
        if (definition is null)
        {
            TrafficNameText = string.Empty;
            TrafficDescriptionText = string.Empty;
            TrafficRoutingPreference = RoutingPreference.TotalCost;
            TrafficAllocationMode = AllocationMode.GreedyBestRoute;
            TrafficRouteChoiceModel = RouteChoiceModel.StochasticUserResponsive;
            TrafficFlowSplitPolicy = FlowSplitPolicy.MultiPath;
            TrafficCapacityBidText = string.Empty;
            TrafficPerishabilityText = string.Empty;
            TrafficValidationText = "Select a traffic type to edit its routing behavior.";
            return;
        }

        TrafficNameText = definition.Name;
        TrafficDescriptionText = definition.Description;
        TrafficRoutingPreference = definition.RoutingPreference;
        TrafficAllocationMode = definition.AllocationMode;
        TrafficRouteChoiceModel = definition.RouteChoiceModel;
        TrafficFlowSplitPolicy = definition.FlowSplitPolicy;
        TrafficCapacityBidText = definition.CapacityBidPerUnit?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        TrafficPerishabilityText = definition.PerishabilityPeriods?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        TrafficValidationText = string.Empty;
    }

    private void PopulateDefaultPermissionRows()
    {
        DefaultTrafficPermissionRows.Clear();
        var previewNetwork = BuildPreviewNetwork();
        foreach (var trafficName in GetKnownTrafficTypeNames())
        {
            var rule = network.EdgeTrafficPermissionDefaults.FirstOrDefault(item => Comparer.Equals(item.TrafficType, trafficName));
            var effective = rule is null
                ? "Effective: Permitted"
                : edgeTrafficPermissionResolver.Resolve(previewNetwork, new EdgeModel { Id = "preview", FromNodeId = "a", ToNodeId = "b" }, trafficName).Summary;
            DefaultTrafficPermissionRows.Add(new PermissionRuleEditorRow(trafficName, supportsOverrideToggle: false, rule, effective));
        }
    }

    private void PopulateEdgePermissionRows(EdgeModel edge)
    {
        SelectedEdgePermissionRows.Clear();
        var previewNetwork = BuildPreviewNetwork();
        foreach (var trafficName in GetKnownTrafficTypeNames())
        {
            var rule = edge.TrafficPermissions.FirstOrDefault(item => Comparer.Equals(item.TrafficType, trafficName));
            var effective = edgeTrafficPermissionResolver.Resolve(previewNetwork, edge, trafficName).Summary;
            SelectedEdgePermissionRows.Add(new PermissionRuleEditorRow(trafficName, supportsOverrideToggle: true, rule, effective));
        }
    }

    private void ApplyInspectorEdits()
    {
        InspectorValidationText = string.Empty;

        try
        {
            switch (CurrentInspectorEditMode)
            {
                case InspectorEditMode.Network:
                    ApplyNetworkEdits();
                    break;

                case InspectorEditMode.Node:
                    ApplyNodeEdits();
                    break;

                case InspectorEditMode.Edge:
                    ApplyEdgeEdits();
                    break;

                case InspectorEditMode.Selection:
                    ApplyBulkEdits();
                    break;
            }

            BuildSceneFromNetwork();
            RefreshInspector();
            NotifyVisualChanged();
        }
        catch (Exception ex)
        {
            InspectorValidationText = ex.Message;
            StatusText = ex.Message;
        }
    }

    private void ApplyNetworkEdits()
    {
        network.Name = string.IsNullOrWhiteSpace(NetworkNameText) ? "Untitled Network" : NetworkNameText.Trim();
        network.Description = NetworkDescriptionText?.Trim() ?? string.Empty;
        network.TimelineLoopLength = ParseOptionalPositiveInt(NetworkTimelineLoopLengthText, "Enter a loop length of 1 or more, or leave it blank.");
        CommitDefaultPermissionRows();
        StatusText = "Updated network settings.";
    }

    private void ApplyNodeEdits()
    {
        var nodeId = Scene.Selection.SelectedNodeIds.FirstOrDefault() ?? throw new InvalidOperationException("Select one node to edit.");
        var node = network.Nodes.First(model => Comparer.Equals(model.Id, nodeId));
        node.Name = string.IsNullOrWhiteSpace(NodeNameText) ? node.Id : NodeNameText.Trim();
        node.PlaceType = NormalizeOptionalText(NodePlaceTypeText);
        node.LoreDescription = NormalizeOptionalText(NodeDescriptionText);
        node.TranshipmentCapacity = ParseOptionalNonNegativeDouble(NodeTranshipmentCapacityText, "Enter a transhipment capacity of 0 or more, or leave it blank.");
        node.Shape = NodeShape;
        node.NodeKind = NodeKind;

        var profile = SelectedNodeTrafficProfileItem?.Model;
        if (profile is not null)
        {
            var trafficValidationMessage = BuildNodeTrafficRoleValidationText();
            if (!string.IsNullOrWhiteSpace(trafficValidationMessage))
            {
                throw new InvalidOperationException(trafficValidationMessage);
            }

            var trafficType = string.IsNullOrWhiteSpace(NodeTrafficTypeText) ? network.TrafficTypes.FirstOrDefault()?.Name : NodeTrafficTypeText.Trim();
            if (string.IsNullOrWhiteSpace(trafficType))
            {
                throw new InvalidOperationException("Choose a traffic type before saving the traffic role.");
            }

            if (!network.TrafficTypes.Any(definition => Comparer.Equals(definition.Name, trafficType)))
            {
                throw new InvalidOperationException("Choose a traffic type that exists in the traffic type editor.");
            }

            profile.TrafficType = trafficType;
            profile.CanTransship = NodeCanTransship;
            NodeTrafficRoleCatalog.ApplyRoleSelection(new NodeTrafficRoleAdapter(profile), NodeTrafficRoleText);
            profile.Production = ParseNonNegativeDouble(NodeProductionText, "Enter production as 0 or more.");
            profile.Consumption = ParseNonNegativeDouble(NodeConsumptionText, "Enter consumption as 0 or more.");
            profile.ConsumerPremiumPerUnit = ParseNonNegativeDouble(NodeConsumerPremiumText, "Enter consumer premium as 0 or more.");
            profile.ProductionStartPeriod = ParseOptionalPositiveInt(NodeProductionStartText, "Enter a production start period of 1 or more, or leave it blank.");
            profile.ProductionEndPeriod = ParseOptionalPositiveInt(NodeProductionEndText, "Enter a production end period of 1 or more, or leave it blank.");
            profile.ConsumptionStartPeriod = ParseOptionalPositiveInt(NodeConsumptionStartText, "Enter a consumption start period of 1 or more, or leave it blank.");
            profile.ConsumptionEndPeriod = ParseOptionalPositiveInt(NodeConsumptionEndText, "Enter a consumption end period of 1 or more, or leave it blank.");
            profile.IsStore = NodeStoreEnabled;
            profile.StoreCapacity = NodeStoreEnabled
                ? ParseOptionalNonNegativeDouble(NodeStoreCapacityText, "Enter store capacity as 0 or more, or leave it blank.")
                : null;
        }

        PopulateNodeEditor(node);
        StatusText = $"Updated node '{node.Name}'.";
    }

    private void ApplyEdgeEdits()
    {
        var edgeId = Scene.Selection.SelectedEdgeIds.FirstOrDefault() ?? throw new InvalidOperationException("Select one route to edit.");
        var edge = network.Edges.First(model => Comparer.Equals(model.Id, edgeId));
        edge.RouteType = NormalizeOptionalText(EdgeRouteTypeText);
        edge.Time = ParseNonNegativeDouble(EdgeTimeText, "Enter travel time as 0 or more.");
        edge.Cost = ParseNonNegativeDouble(EdgeCostText, "Enter travel cost as 0 or more.");
        edge.Capacity = ParseOptionalNonNegativeDouble(EdgeCapacityText, "Enter route capacity as 0 or more, or leave it blank.");
        edge.IsBidirectional = EdgeIsBidirectional;
        edge.TrafficPermissions = SelectedEdgePermissionRows.Select(row => row.ToModel(edge.Capacity)).ToList();
        UpdateEffectivePermissionSummaries(edge);
        StatusText = $"Updated route '{edge.Id}'.";
    }

    private void ApplyBulkEdits()
    {
        var selectedNodeIds = Scene.Selection.SelectedNodeIds.ToHashSet(Comparer);
        var updatedPlaceType = NormalizeOptionalText(BulkPlaceTypeText);
        var updatedCapacity = ParseOptionalNonNegativeDouble(BulkTranshipmentCapacityText, "Enter a transhipment capacity of 0 or more, or leave it blank.");
        foreach (var node in network.Nodes.Where(node => selectedNodeIds.Contains(node.Id)))
        {
            node.PlaceType = updatedPlaceType;
            node.TranshipmentCapacity = updatedCapacity;
        }

        StatusText = "Applied bulk node changes.";
    }

    private void AddNodeTrafficProfile()
    {
        EnsureDefaultTrafficType();
        var nodeId = Scene.Selection.SelectedNodeIds.FirstOrDefault();
        if (nodeId is null)
        {
            StatusText = "Select a node to add a traffic role.";
            return;
        }

        var node = network.Nodes.First(model => Comparer.Equals(model.Id, nodeId));
        var trafficType = network.TrafficTypes.First().Name;
        var profile = new NodeTrafficProfile
        {
            TrafficType = trafficType,
            CanTransship = true
        };
        node.TrafficProfiles.Add(profile);
        PopulateNodeEditor(node);
        SelectedNodeTrafficProfileItem = SelectedNodeTrafficProfiles.LastOrDefault();
        FocusInspectorSection(InspectorTabTarget.Selection, InspectorSectionTarget.TrafficRoles);
        StatusText = "Added a new traffic role to the selected node.";
    }

    private void DuplicateSelectedNodeTrafficProfile()
    {
        var nodeId = Scene.Selection.SelectedNodeIds.FirstOrDefault();
        if (nodeId is null || SelectedNodeTrafficProfileItem is null)
        {
            StatusText = "Select a node traffic role to duplicate.";
            return;
        }

        var node = network.Nodes.First(model => Comparer.Equals(model.Id, nodeId));
        var source = SelectedNodeTrafficProfileItem.Model;
        var duplicate = new NodeTrafficProfile
        {
            TrafficType = source.TrafficType,
            Production = source.Production,
            Consumption = source.Consumption,
            ConsumerPremiumPerUnit = source.ConsumerPremiumPerUnit,
            CanTransship = source.CanTransship,
            ProductionStartPeriod = source.ProductionStartPeriod,
            ProductionEndPeriod = source.ProductionEndPeriod,
            ConsumptionStartPeriod = source.ConsumptionStartPeriod,
            ConsumptionEndPeriod = source.ConsumptionEndPeriod,
            IsStore = source.IsStore,
            StoreCapacity = source.StoreCapacity
        };
        node.TrafficProfiles.Add(duplicate);
        PopulateNodeEditor(node);
        SelectedNodeTrafficProfileItem = SelectedNodeTrafficProfiles.FirstOrDefault(item => ReferenceEquals(item.Model, duplicate));
        FocusInspectorSection(InspectorTabTarget.Selection, InspectorSectionTarget.TrafficRoles);
        StatusText = "Duplicated the selected traffic role.";
    }

    private void RemoveSelectedNodeTrafficProfile()
    {
        var nodeId = Scene.Selection.SelectedNodeIds.FirstOrDefault();
        if (nodeId is null || SelectedNodeTrafficProfileItem is null)
        {
            StatusText = "Select a node traffic role to remove.";
            return;
        }

        var node = network.Nodes.First(model => Comparer.Equals(model.Id, nodeId));
        node.TrafficProfiles.Remove(SelectedNodeTrafficProfileItem.Model);
        PopulateNodeEditor(node);
        StatusText = "Removed the selected traffic role.";
    }

    private void AddTrafficDefinition()
    {
        var nextIndex = network.TrafficTypes.Count + 1;
        var nextName = GetNextTrafficName(nextIndex);
        network.TrafficTypes.Add(new TrafficTypeDefinition
        {
            Name = nextName,
            RoutingPreference = RoutingPreference.TotalCost,
            AllocationMode = AllocationMode.GreedyBestRoute,
            RouteChoiceModel = RouteChoiceModel.StochasticUserResponsive,
            FlowSplitPolicy = FlowSplitPolicy.MultiPath
        });
        pendingTrafficRemovalName = string.Empty;
        PopulateTrafficDefinitionList();
        PopulateDefaultPermissionRows();
        RefreshInspector();
        StatusText = $"Added traffic type '{nextName}'.";
    }

    private void RemoveSelectedTrafficDefinition()
    {
        var selected = SelectedTrafficDefinitionItem?.Model;
        if (selected is null)
        {
            return;
        }

        var dependencies = CountTrafficDependencies(selected.Name);
        if (!Comparer.Equals(pendingTrafficRemovalName, selected.Name) && dependencies > 0)
        {
            pendingTrafficRemovalName = selected.Name;
            TrafficValidationText = $"Remove '{selected.Name}' again to also remove {dependencies} dependent node role and route-permission entries.";
            StatusText = TrafficValidationText;
            return;
        }

        pendingTrafficRemovalName = string.Empty;
        network.TrafficTypes.Remove(selected);
        foreach (var node in network.Nodes)
        {
            node.TrafficProfiles.RemoveAll(profile => Comparer.Equals(profile.TrafficType, selected.Name));
        }

        network.EdgeTrafficPermissionDefaults.RemoveAll(rule => Comparer.Equals(rule.TrafficType, selected.Name));
        foreach (var edge in network.Edges)
        {
            edge.TrafficPermissions.RemoveAll(rule => Comparer.Equals(rule.TrafficType, selected.Name));
        }

        EnsureDefaultTrafficType();
        PopulateTrafficDefinitionList();
        PopulateDefaultPermissionRows();
        RefreshInspector();
        StatusText = $"Removed traffic type '{selected.Name}' and cleared matching dependencies.";
        TrafficValidationText = string.Empty;
    }

    private void ApplyTrafficDefinitionEdits()
    {
        var selected = SelectedTrafficDefinitionItem?.Model;
        if (selected is null)
        {
            TrafficValidationText = "Select a traffic type to edit.";
            return;
        }

        try
        {
            var requestedName = TrafficNameText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(requestedName))
            {
                throw new InvalidOperationException("Traffic type name is required.");
            }

            var duplicate = network.TrafficTypes.FirstOrDefault(definition => !ReferenceEquals(definition, selected) && Comparer.Equals(definition.Name, requestedName));
            if (duplicate is not null)
            {
                throw new InvalidOperationException("Traffic type name already exists.");
            }

            var oldName = selected.Name;
            selected.Name = requestedName;
            selected.Description = TrafficDescriptionText?.Trim() ?? string.Empty;
            selected.RoutingPreference = TrafficRoutingPreference;
            selected.AllocationMode = TrafficAllocationMode;
            selected.RouteChoiceModel = TrafficRouteChoiceModel;
            selected.FlowSplitPolicy = TrafficFlowSplitPolicy;
            selected.CapacityBidPerUnit = ParseOptionalNonNegativeDouble(TrafficCapacityBidText, "Enter bid per unit as 0 or more, or leave it blank.");
            selected.PerishabilityPeriods = ParseOptionalPositiveInt(TrafficPerishabilityText, "Enter perishability periods as 1 or more, or leave it blank.");

            if (!Comparer.Equals(oldName, requestedName))
            {
                RenameTrafficReferences(oldName, requestedName);
            }

            PopulateTrafficDefinitionList();
            PopulateDefaultPermissionRows();
            RefreshInspector();
            TrafficValidationText = string.Empty;
            StatusText = $"Updated traffic type '{requestedName}'.";
        }
        catch (Exception ex)
        {
            TrafficValidationText = ex.Message;
            StatusText = ex.Message;
        }
    }

    private void RenameTrafficReferences(string oldName, string newName)
    {
        foreach (var node in network.Nodes)
        {
            foreach (var profile in node.TrafficProfiles.Where(profile => Comparer.Equals(profile.TrafficType, oldName)))
            {
                profile.TrafficType = newName;
            }
        }

        foreach (var rule in network.EdgeTrafficPermissionDefaults.Where(rule => Comparer.Equals(rule.TrafficType, oldName)))
        {
            rule.TrafficType = newName;
        }

        foreach (var edge in network.Edges)
        {
            foreach (var rule in edge.TrafficPermissions.Where(rule => Comparer.Equals(rule.TrafficType, oldName)))
            {
                rule.TrafficType = newName;
            }
        }
    }

    private void CommitDefaultPermissionRows()
    {
        network.EdgeTrafficPermissionDefaults = DefaultTrafficPermissionRows.Select(row => row.ToModel(null)).ToList();
    }

    private void CommitTransientEditorsToModel()
    {
        if (IsEditingNode)
        {
            try
            {
                ApplyNodeEdits();
            }
            catch
            {
                // Keep the last valid model state when the editor currently contains invalid values.
            }
        }
        else if (IsEditingEdge)
        {
            try
            {
                ApplyEdgeEdits();
            }
            catch
            {
            }
        }
        else if (IsEditingNetwork)
        {
            try
            {
                ApplyNetworkEdits();
            }
            catch
            {
            }
        }
    }

    private void UpdateEffectivePermissionSummaries(EdgeModel edge)
    {
        var previewNetwork = BuildPreviewNetwork();
        foreach (var row in DefaultTrafficPermissionRows)
        {
            row.EffectiveSummary = EdgeTrafficPermissionResolver.FormatSummary(row.Mode, row.LimitKind, row.ToModel(null).LimitValue);
        }

        foreach (var row in SelectedEdgePermissionRows)
        {
            var previewEdge = new EdgeModel
            {
                Id = edge.Id,
                FromNodeId = edge.FromNodeId,
                ToNodeId = edge.ToNodeId,
                Time = edge.Time,
                Cost = edge.Cost,
                Capacity = edge.Capacity,
                IsBidirectional = edge.IsBidirectional,
                RouteType = edge.RouteType,
                TrafficPermissions = SelectedEdgePermissionRows.Select(permission => permission.ToModel(edge.Capacity)).ToList()
            };
            row.EffectiveSummary = edgeTrafficPermissionResolver.Resolve(previewNetwork, previewEdge, row.TrafficType).Summary;
        }
    }

    private NetworkModel BuildPreviewNetwork()
    {
        var preview = CloneNetwork(network);
        preview.EdgeTrafficPermissionDefaults = DefaultTrafficPermissionRows.Select(row => row.ToModel(null)).ToList();
        return preview;
    }

    private static NetworkModel CloneNetwork(NetworkModel source)
    {
        return new NetworkModel
        {
            Name = source.Name,
            Description = source.Description,
            TimelineLoopLength = source.TimelineLoopLength,
            DefaultAllocationMode = source.DefaultAllocationMode,
            SimulationSeed = source.SimulationSeed,
            TrafficTypes = source.TrafficTypes.Select(definition => new TrafficTypeDefinition
            {
                Name = definition.Name,
                Description = definition.Description,
                RoutingPreference = definition.RoutingPreference,
                AllocationMode = definition.AllocationMode,
                RouteChoiceModel = definition.RouteChoiceModel,
                FlowSplitPolicy = definition.FlowSplitPolicy,
                RouteChoiceSettings = definition.RouteChoiceSettings,
                CapacityBidPerUnit = definition.CapacityBidPerUnit,
                PerishabilityPeriods = definition.PerishabilityPeriods
            }).ToList(),
            EdgeTrafficPermissionDefaults = source.EdgeTrafficPermissionDefaults.Select(rule => new EdgeTrafficPermissionRule
            {
                TrafficType = rule.TrafficType,
                IsActive = rule.IsActive,
                Mode = rule.Mode,
                LimitKind = rule.LimitKind,
                LimitValue = rule.LimitValue
            }).ToList(),
            Nodes = source.Nodes.Select(node => new NodeModel
            {
                Id = node.Id,
                Name = node.Name,
                Shape = node.Shape,
                NodeKind = node.NodeKind,
                ReferencedSubnetworkId = node.ReferencedSubnetworkId,
                X = node.X,
                Y = node.Y,
                TranshipmentCapacity = node.TranshipmentCapacity,
                PlaceType = node.PlaceType,
                LoreDescription = node.LoreDescription,
                TrafficProfiles = node.TrafficProfiles.Select(profile => new NodeTrafficProfile
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
                    IsStore = profile.IsStore,
                    StoreCapacity = profile.StoreCapacity
                }).ToList()
            }).ToList(),
            Edges = source.Edges.Select(edge => new EdgeModel
            {
                Id = edge.Id,
                FromNodeId = edge.FromNodeId,
                ToNodeId = edge.ToNodeId,
                Time = edge.Time,
                Cost = edge.Cost,
                Capacity = edge.Capacity,
                IsBidirectional = edge.IsBidirectional,
                RouteType = edge.RouteType,
                TrafficPermissions = edge.TrafficPermissions.Select(rule => new EdgeTrafficPermissionRule
                {
                    TrafficType = rule.TrafficType,
                    IsActive = rule.IsActive,
                    Mode = rule.Mode,
                    LimitKind = rule.LimitKind,
                    LimitValue = rule.LimitValue
                }).ToList()
            }).ToList()
        };
    }

    private void SyncNetworkNodePositionsFromScene()
    {
        foreach (var sceneNode in Scene.Nodes)
        {
            var model = network.Nodes.FirstOrDefault(node => Comparer.Equals(node.Id, sceneNode.Id));
            if (model is null)
            {
                continue;
            }

            model.X = sceneNode.Bounds.CenterX;
            model.Y = sceneNode.Bounds.CenterY;
        }
    }

    private string BuildSelectionSummary()
    {
        if (Scene.Selection.SelectedNodeIds.Count == 1)
        {
            var node = network.Nodes.FirstOrDefault(model => Comparer.Equals(model.Id, Scene.Selection.SelectedNodeIds.First()));
            return node is null ? "1 node selected" : $"{node.Name} selected";
        }

        if (Scene.Selection.SelectedEdgeIds.Count == 1)
        {
            var edge = network.Edges.FirstOrDefault(model => Comparer.Equals(model.Id, Scene.Selection.SelectedEdgeIds.First()));
            return edge is null ? "1 route selected" : $"{(edge.RouteType ?? edge.Id)} selected";
        }

        if (Scene.Selection.SelectedNodeIds.Count > 1)
        {
            return $"{Scene.Selection.SelectedNodeIds.Count} nodes selected";
        }

        if (Scene.Selection.SelectedEdgeIds.Count > 1)
        {
            return $"{Scene.Selection.SelectedEdgeIds.Count} routes selected";
        }

        return "No selection";
    }

    private InspectorEditMode GetInspectorEditMode()
    {
        var count = Scene.Selection.SelectedNodeIds.Count + Scene.Selection.SelectedEdgeIds.Count;
        if (count == 0)
        {
            return InspectorEditMode.Network;
        }

        if (Scene.Selection.SelectedNodeIds.Count == 1 && Scene.Selection.SelectedEdgeIds.Count == 0)
        {
            return InspectorEditMode.Node;
        }

        if (Scene.Selection.SelectedEdgeIds.Count == 1 && Scene.Selection.SelectedNodeIds.Count == 0)
        {
            return InspectorEditMode.Edge;
        }

        return InspectorEditMode.Selection;
    }

    private void EnsureDefaultTrafficType()
    {
        if (network.TrafficTypes.Count > 0)
        {
            return;
        }

        network.TrafficTypes.Add(new TrafficTypeDefinition
        {
            Name = "general",
            RoutingPreference = RoutingPreference.TotalCost,
            AllocationMode = AllocationMode.GreedyBestRoute
        });
        StatusText = "Created default traffic type 'general'.";
    }

    public void SelectNodeForEdit(string nodeId, bool focusTrafficRoles = false)
    {
        if (!network.Nodes.Any(node => Comparer.Equals(node.Id, nodeId)))
        {
            return;
        }

        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        Scene.Selection.SelectedNodeIds.Add(nodeId);
        FocusInspectorSection(InspectorTabTarget.Selection, focusTrafficRoles ? InspectorSectionTarget.TrafficRoles : InspectorSectionTarget.Node);
        RefreshInspector();
        NotifyVisualChanged();
    }

    public void SelectRouteForEdit(string edgeId)
    {
        if (!network.Edges.Any(edge => Comparer.Equals(edge.Id, edgeId)))
        {
            return;
        }

        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Add(edgeId);
        FocusInspectorSection(InspectorTabTarget.Selection, InspectorSectionTarget.Route);
        RefreshInspector();
        NotifyVisualChanged();
    }

    public string AddNodeAtPosition(GraphPoint position)
    {
        var nodeId = AddNodeAt(position);
        SelectNodeForEdit(nodeId);
        return nodeId;
    }

    public void ClearSelection()
    {
        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        FocusInspectorSection(InspectorTabTarget.Selection, InspectorSectionTarget.None);
        RefreshInspector();
        NotifyVisualChanged();
        StatusText = "Selection cleared.";
    }

    public void DeleteNodeById(string nodeId)
    {
        if (!network.Nodes.Any(node => Comparer.Equals(node.Id, nodeId)))
        {
            return;
        }

        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        Scene.Selection.SelectedNodeIds.Add(nodeId);
        DeleteSelection();
    }

    public void DeleteRouteById(string edgeId)
    {
        if (!network.Edges.Any(edge => Comparer.Equals(edge.Id, edgeId)))
        {
            return;
        }

        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Add(edgeId);
        DeleteSelection();
    }

    public bool StartEdgeFromNode(string nodeId)
    {
        if (!network.Nodes.Any(node => Comparer.Equals(node.Id, nodeId)))
        {
            return false;
        }

        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        Scene.Selection.SelectedNodeIds.Add(nodeId);
        Scene.Transient.ConnectionSourceNodeId = nodeId;
        Scene.Transient.ConnectionWorld = Scene.FindNode(nodeId) is { } sourceNode
            ? new GraphPoint(sourceNode.Bounds.CenterX, sourceNode.Bounds.CenterY)
            : null;
        FocusInspectorSection(InspectorTabTarget.Selection, InspectorSectionTarget.Node);
        RefreshInspector();
        NotifyVisualChanged();
        StatusText = "Start edge: click another node to connect.";
        return true;
    }

    public void FocusInspectorSection(InspectorTabTarget tab, InspectorSectionTarget section)
    {
        SelectedInspectorTab = tab;
        SelectedInspectorSection = section;
    }

    private string BuildNodeTrafficRoleValidationText()
    {
        if (!IsEditingNode || SelectedNodeTrafficProfileItem is null)
        {
            return string.Empty;
        }

        var normalized = string.IsNullOrWhiteSpace(NodeTrafficTypeText) ? string.Empty : NodeTrafficTypeText.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Traffic type is required.";
        }

        return network.TrafficTypes.Any(definition => Comparer.Equals(definition.Name, normalized))
            ? string.Empty
            : "Traffic type no longer exists. Choose a valid type.";
    }

    private void RaiseNodeTrafficRoleValidationStateChanged()
    {
        Raise(nameof(NodeTrafficRoleValidationText));
        Raise(nameof(CanApplyInspectorEdits));
        ApplyInspectorCommand.NotifyCanExecuteChanged();
    }

    private string GetNextTrafficName(int startIndex)
    {
        var index = startIndex;
        while (true)
        {
            var name = $"traffic-{index}";
            if (!network.TrafficTypes.Any(definition => Comparer.Equals(definition.Name, name)))
            {
                return name;
            }

            index++;
        }
    }

    private int CountTrafficDependencies(string trafficName)
    {
        var nodeProfiles = network.Nodes.Sum(node => node.TrafficProfiles.Count(profile => Comparer.Equals(profile.TrafficType, trafficName)));
        var defaultRules = network.EdgeTrafficPermissionDefaults.Count(rule => Comparer.Equals(rule.TrafficType, trafficName));
        var edgeRules = network.Edges.Sum(edge => edge.TrafficPermissions.Count(rule => Comparer.Equals(rule.TrafficType, trafficName)));
        return nodeProfiles + defaultRules + edgeRules;
    }

    private IReadOnlyList<string> GetKnownTrafficTypeNames()
    {
        return network.TrafficTypes
            .Select(definition => definition.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(Comparer)
            .OrderBy(name => name, Comparer)
            .ToList();
    }

    private IReadOnlyList<GraphNodeTextLine> BuildNodeDetailLines(
        NodeModel node,
        IReadOnlyList<KeyValuePair<string, double>> backlogByTraffic,
        TemporalNetworkSimulationEngine.NodePressureSnapshot? pressure)
    {
        var lines = new List<(int Order, string TrafficType, GraphNodeTextLine Line)>();

        foreach (var profile in node.TrafficProfiles)
        {
            var trafficType = string.IsNullOrWhiteSpace(profile.TrafficType) ? string.Empty : profile.TrafficType.Trim();
            if (string.IsNullOrWhiteSpace(trafficType))
            {
                continue;
            }

            if (profile.Production > 0d)
            {
                lines.Add((0, trafficType, new GraphNodeTextLine($"Produces {profile.Production:0.##} {trafficType}", true, false)));
            }

            if (profile.Consumption > 0d)
            {
                lines.Add((1, trafficType, new GraphNodeTextLine($"Consumes {profile.Consumption:0.##} {trafficType}", true, false)));
            }

            if (profile.CanTransship)
            {
                lines.Add((2, trafficType, new GraphNodeTextLine(
                    node.TranshipmentCapacity.HasValue
                        ? $"Tranships up to {node.TranshipmentCapacity.Value:0.##} {trafficType}"
                        : $"Tranships unlimited {trafficType}",
                    false,
                    false)));
            }

            if (profile.IsStore)
            {
                lines.Add((3, trafficType, new GraphNodeTextLine(
                    profile.StoreCapacity.HasValue
                        ? $"Stores up to {profile.StoreCapacity.Value:0.##} {trafficType}"
                        : $"Stores unlimited {trafficType}",
                    false,
                    false)));
            }
        }

        var ordered = lines
            .OrderBy(item => item.Order)
            .ThenBy(item => item.TrafficType, Comparer)
            .Select(item => item.Line)
            .ToList();

        var visible = ordered.Count <= 4
            ? ordered
            : ordered.Take(3).Append(new GraphNodeTextLine($"+{ordered.Count - 3} more roles", false, false)).ToList();

        var unmetNeedLine = BuildSceneUnmetNeedLine(backlogByTraffic);
        if (!string.IsNullOrWhiteSpace(unmetNeedLine))
        {
            visible.Add(new GraphNodeTextLine(unmetNeedLine, true, true));
        }

        if (pressure is { Score: > 0d })
        {
            visible.Add(new GraphNodeTextLine($"Pressure {pressure.Value.Score:0.##}", true, true));
            var topCause = BuildTopCauseText(pressure.Value.TopCause);
            if (!string.IsNullOrWhiteSpace(topCause))
            {
                visible.Add(new GraphNodeTextLine($"Cause: {topCause}", false, true));
            }
        }

        return visible;
    }

    private static string BuildSceneUnmetNeedLine(IReadOnlyList<KeyValuePair<string, double>> backlogByTraffic)
    {
        var backlog = backlogByTraffic
            .Where(item => item.Value > 0d && !string.IsNullOrWhiteSpace(item.Key))
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (backlog.Count == 0)
        {
            return string.Empty;
        }

        if (backlog.Count == 1 || (backlog.Count > 1 && Math.Abs(backlog[0].Value - backlog[1].Value) > 0.000001d))
        {
            return $"Unmet need {backlog[0].Value:0.##} {backlog[0].Key}";
        }

        return $"Unmet need {backlog.Sum(item => item.Value):0.##}";
    }

    private static IReadOnlyList<string> BuildNodeBadges(NodeModel node)
    {
        var badges = new List<string>();
        var produced = node.TrafficProfiles.Where(profile => profile.Production > 0d).Select(profile => profile.TrafficType).ToList();
        var consumed = node.TrafficProfiles.Where(profile => profile.Consumption > 0d).Select(profile => profile.TrafficType).ToList();
        if (produced.Count > 0)
        {
            badges.Add($"Makes {string.Join("/", produced)}");
        }

        if (consumed.Count > 0)
        {
            badges.Add($"Needs {string.Join("/", consumed)}");
        }

        if (node.TrafficProfiles.Any(profile => profile.CanTransship))
        {
            badges.Add("Can relay");
        }

        if (badges.Count == 0)
        {
            badges.Add("Draft");
        }

        return badges;
    }

    private static void UpdateSceneNodeLayout(
        GraphNodeSceneItem sceneNode,
        NodeModel nodeModel,
        TemporalNetworkSimulationEngine.NodePressureSnapshot? pressure)
    {
        var centerX = sceneNode.Bounds.CenterX;
        var centerY = sceneNode.Bounds.CenterY;
        var width = GraphNodeTextLayout.ComputeNodeWidth(sceneNode.Name, sceneNode.TypeLabel, sceneNode.DetailLines);
        var height = GraphNodeTextLayout.BuildLayout(sceneNode.Name, sceneNode.TypeLabel, sceneNode.DetailLines, width).Height;
        sceneNode.Bounds = new GraphRect(centerX - (width / 2d), centerY - (height / 2d), width, height);
        sceneNode.ToolTipText = BuildNodeToolTipText(nodeModel, sceneNode.DetailLines, pressure);
    }

    private static string BuildNodeToolTipText(
        NodeModel node,
        IReadOnlyList<GraphNodeTextLine> detailLines,
        TemporalNetworkSimulationEngine.NodePressureSnapshot? pressure)
    {
        var lines = new List<string>
        {
            string.IsNullOrWhiteSpace(node.Name) ? node.Id : node.Name,
            string.IsNullOrWhiteSpace(node.PlaceType) ? "Node" : node.PlaceType!
        };
        lines.AddRange(detailLines.Select(line => line.Text));
        if (pressure is { Score: > 0d })
        {
            var breakdown = ReportExportService.FormatCauseBreakdown(pressure.Value.CauseWeights);
            if (!string.IsNullOrWhiteSpace(breakdown) && !string.Equals(breakdown, "None", StringComparison.Ordinal))
            {
                lines.Add($"Cause breakdown: {breakdown}");
            }
        }

        return string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private string BuildEdgeToolTipText(
        EdgeModel edge,
        TemporalNetworkSimulationEngine.EdgeFlowVisualSummary flow,
        double occupancy,
        TemporalNetworkSimulationEngine.EdgePressureSnapshot? pressure)
    {
        var label = ResolveEdgeLabel(edge.Id);
        var source = ResolveNodeName(edge.FromNodeId);
        var target = ResolveNodeName(edge.ToNodeId);
        var flowSummary = edge.IsBidirectional
            ? $"{ReportExportService.FormatNumber(flow.ForwardQuantity)} / {ReportExportService.FormatNumber(flow.ReverseQuantity)}"
            : ReportExportService.FormatNumber(flow.ForwardQuantity + flow.ReverseQuantity);
        var lines = new List<string>
        {
            $"Route {label}",
            $"{source} -> {target}",
            $"Time {ReportExportService.FormatNumber(edge.Time)} | Cost {ReportExportService.FormatNumber(edge.Cost)}",
            $"Capacity {ReportExportService.FormatNumber(edge.Capacity)} | Flow {flowSummary}",
            $"Utilisation {ReportExportService.FormatUtilisation(occupancy, edge.Capacity)}",
            edge.IsBidirectional ? "Bidirectional" : "One-way",
            $"Traffic {ReportExportService.FormatEdgeTrafficPermissions(network, edge)}"
        };

        if (pressure is { Score: > 0d })
        {
            lines.Add($"Pressure {ReportExportService.FormatNumber(pressure.Value.Score)}");
            var topCause = BuildTopCauseText(pressure.Value.TopCause);
            if (!string.IsNullOrWhiteSpace(topCause))
            {
                lines.Add($"Top cause: {topCause}");
            }

            var breakdown = ReportExportService.FormatCauseBreakdown(pressure.Value.CauseWeights);
            if (!string.Equals(breakdown, "None", StringComparison.Ordinal))
            {
                lines.Add($"Cause breakdown: {breakdown}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string ResolveNodeName(string nodeId)
    {
        var node = network.Nodes.FirstOrDefault(model => Comparer.Equals(model.Id, nodeId));
        return node is null || string.IsNullOrWhiteSpace(node.Name) ? nodeId : node.Name;
    }

    private string ResolveEdgeLabel(string edgeId)
    {
        var edge = network.Edges.FirstOrDefault(model => Comparer.Equals(model.Id, edgeId));
        return edge is null || string.IsNullOrWhiteSpace(edge.RouteType) ? edgeId : edge.RouteType!;
    }

    private static string BuildTopCauseText(string? rawCause)
    {
        var formatted = ReportExportService.FormatPressureCause(rawCause);
        return string.IsNullOrWhiteSpace(formatted) ? string.Empty : formatted;
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double ParseNonNegativeDouble(string text, string errorMessage)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0d)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value;
    }

    private static double? ParseOptionalNonNegativeDouble(string text, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return ParseNonNegativeDouble(text, errorMessage);
    }

    private static int? ParseOptionalPositiveInt(string text, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 1)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value;
    }

    private static double TryParseRoleQuantity(string text)
    {
        return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value) && value > 0d
            ? value
            : 0d;
    }

    private static string FormatRoleQuantity(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private sealed class NodeTrafficRoleAdapter(NodeTrafficProfile profile) : NodeTrafficRoleCatalog.NodeTrafficProfileViewModelAdapter
    {
        public double Production { get => profile.Production; set => profile.Production = value; }
        public double Consumption { get => profile.Consumption; set => profile.Consumption = value; }
        public bool CanTransship { get => profile.CanTransship; set => profile.CanTransship = value; }
    }
}

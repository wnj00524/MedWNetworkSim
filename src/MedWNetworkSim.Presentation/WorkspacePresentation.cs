using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using MedWNetworkSim.App.Agents;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Import;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.App.Services.Pathfinding;
using MedWNetworkSim.App.VisualAnalytics;
using MedWNetworkSim.App.Insights;
using MedWNetworkSim.App.VisualAnalytics.Sankey;
using MedWNetworkSim.Interaction;
using MedWNetworkSim.Rendering;
using MedWNetworkSim.Rendering.Geo;
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

public interface IUiExceptionSink
{
    void ReportUiException(string safeMessage, Exception exception);
}

public static class UiExceptionBoundary
{
    private static WeakReference<IUiExceptionSink>? sinkReference;

    public static IUiExceptionSink? Sink
    {
        get
        {
            if (sinkReference is not null && sinkReference.TryGetTarget(out var sink))
            {
                return sink;
            }

            return null;
        }
        set
        {
            sinkReference = value is null ? null : new WeakReference<IUiExceptionSink>(value);
        }
    }

    public static string BuildActionableMessage(string operation, string suggestion) =>
        $"{operation} failed. {suggestion}";

    public static void Report(Exception exception, string safeMessage, string source)
    {
        Trace.WriteLine($"[{source}] {exception}");
        Sink?.ReportUiException(safeMessage, exception);
    }
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
    public void Execute(object? parameter)
    {
        try
        {
            execute();
        }
        catch (Exception ex)
        {
            var safeMessage = UiExceptionBoundary.BuildActionableMessage(
                "The requested action",
                "Please retry. If it keeps failing, save your network and restart the app.");
            UiExceptionBoundary.Report(ex, safeMessage, nameof(RelayCommand));
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T> execute;
    private readonly Predicate<T>? canExecute;

    public RelayCommand(Action<T> execute, Predicate<T>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        if (!TryGetParameter(parameter, out var typedParameter))
        {
            return false;
        }

        return canExecute?.Invoke(typedParameter) ?? true;
    }

    public void Execute(object? parameter)
    {
        if (!TryGetParameter(parameter, out var typedParameter))
        {
            return;
        }

        try
        {
            execute(typedParameter);
        }
        catch (Exception ex)
        {
            var safeMessage = UiExceptionBoundary.BuildActionableMessage(
                "The requested action",
                "Please retry. If it keeps failing, save your network and restart the app.");
            UiExceptionBoundary.Report(ex, safeMessage, nameof(RelayCommand<T>));
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static bool TryGetParameter(object? parameter, out T value)
    {
        if (parameter is T typed)
        {
            value = typed;
            return true;
        }

        if (parameter is null && default(T) is null)
        {
            value = default!;
            return true;
        }

        value = default!;
        return false;
    }
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

public enum WorkspaceMode
{
    Normal,
    EdgeEditor,
    ScenarioEditor,
    OsmImport
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
    public required string PriceSummary { get; init; }
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
    public required string CommodityPrices { get; init; }
    public required string PressureScore { get; init; }
    public required string TopCause { get; init; }
    public required string UnmetNeed { get; init; }
}

public sealed record PieChartSegmentViewModel(string Label, double Value);

public sealed record FlowDataPoint(string Label, double Planned, double Delivered, double UnmetDemand, double Backlog);

public sealed record NodePressurePoint(string Node, double Pressure, string TopCause, string UnmetNeed);

public sealed class AgentViewModel : ObservableObject
{
    private string name = string.Empty;
    private string type = string.Empty;
    private double budget;
    private string allowedTrafficTypes = string.Empty;
    private string allowedActions = string.Empty;

    public string Name { get => name; set => SetProperty(ref name, value); }
    public string Type { get => type; set => SetProperty(ref type, value); }
    public double Budget { get => budget; set => SetProperty(ref budget, value); }
    public string AllowedTrafficTypes { get => allowedTrafficTypes; set => SetProperty(ref allowedTrafficTypes, value); }
    public string AllowedActions { get => allowedActions; set => SetProperty(ref allowedActions, value); }
}

public sealed class UncoveredNodePlanningItem
{
    public required string NodeName { get; init; }
    public required string NearestFacility { get; init; }
    public required string ExtraBudgetNeeded { get; init; }
}

public sealed class FacilityComparisonRowViewModel
{
    public required string Facility { get; init; }
    public required string NodesCovered { get; init; }
    public required string UniqueNodesCovered { get; init; }
    public required string AverageCost { get; init; }
    public required string MaxCost { get; init; }
}

public sealed class ScenarioEventListItem(ScenarioEventModel model)
{
    public ScenarioEventModel Model { get; } = model;
    public string Name => string.IsNullOrWhiteSpace(Model.Name) ? "(unnamed event)" : Model.Name;
    public string KindText => Model.Kind.ToString();
    public string TargetText => string.IsNullOrWhiteSpace(Model.TargetId) ? "No target" : $"{Model.TargetKind}: {Model.TargetId}";
    public string TimingText => Model.EndTime.HasValue ? $"{Model.Time:0.##} to {Model.EndTime.Value:0.##}" : $"{Model.Time:0.##}";
    public string ValueStatusText => $"{Model.Value:0.##} | {(Model.IsEnabled ? "Enabled" : "Disabled")}";
    public string SummaryText => $"{KindText} | {TargetText} | {TimingText} | {ValueStatusText}";
    public override string ToString() => Name;
}

public sealed class ScenarioEditorViewModel : ObservableObject
{
    private readonly Action markDirty;
    private NetworkModel network;
    private List<ScenarioDefinitionModel> snapshot = [];
    private ScenarioDefinitionModel? selectedScenarioDefinition;
    private ScenarioEventListItem? selectedEventItem;
    private bool isDirty;
    private string nameText = string.Empty;
    private string descriptionText = string.Empty;
    private string startTimeText = "0";
    private string endTimeText = "10";
    private string deltaTimeText = "1";
    private bool enableAdaptiveRouting;
    private string eventNameText = string.Empty;
    private ScenarioEventKind eventKind;
    private ScenarioTargetKind eventTargetKind;
    private string eventTargetIdText = string.Empty;
    private string eventTrafficTypeText = string.Empty;
    private string eventStartTimeText = "0";
    private string eventEndTimeText = string.Empty;
    private string eventValueText = "1";
    private string eventNotesText = string.Empty;
    private bool eventIsEnabled = true;
    private string scenarioNameError = string.Empty;
    private string scenarioStartTimeError = string.Empty;
    private string scenarioEndTimeError = string.Empty;
    private string scenarioDeltaTimeError = string.Empty;
    private string eventNameError = string.Empty;
    private string eventTargetError = string.Empty;
    private string eventTrafficTypeError = string.Empty;
    private string eventStartTimeError = string.Empty;
    private string eventEndTimeError = string.Empty;
    private string eventValueError = string.Empty;
    private string validationSummary = "Open or create a scenario to begin.";

    public ScenarioEditorViewModel(NetworkModel network, Action markDirty)
    {
        this.network = network;
        this.markDirty = markDirty;
        EventItems = [];
        CreateScenarioCommand = new RelayCommand(CreateScenario);
        SaveScenarioCommand = new RelayCommand(SaveScenario, () => SelectedScenarioDefinition is not null);
        DeleteScenarioCommand = new RelayCommand(DeleteScenario, () => SelectedScenarioDefinition is not null);
        AddScenarioEventCommand = new RelayCommand(AddEvent, () => SelectedScenarioDefinition is not null);
        EditScenarioEventCommand = new RelayCommand(LoadSelectedEventDraft, () => SelectedEventItem is not null);
        DuplicateScenarioEventCommand = new RelayCommand(DuplicateEvent, () => SelectedScenarioDefinition is not null && SelectedEventItem is not null);
        DeleteScenarioEventCommand = new RelayCommand(DeleteEvent, () => SelectedScenarioDefinition is not null && SelectedEventItem is not null);
        Open();
    }

    public ObservableCollection<ScenarioEventListItem> EventItems { get; }
    public Array ScenarioEventKindOptions { get; } = Enum.GetValues(typeof(ScenarioEventKind));
    public Array ScenarioTargetKindOptions { get; } = Enum.GetValues(typeof(ScenarioTargetKind));
    public IReadOnlyList<ScenarioDefinitionModel> ScenarioDefinitions => network.ScenarioDefinitions;
    public IReadOnlyList<string> NodeIdOptions => network.Nodes.Select(node => node.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
    public IReadOnlyList<string> EdgeIdOptions => network.Edges.Select(edge => edge.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
    public IReadOnlyList<string> TrafficTypeOptions => network.TrafficTypes.Select(type => type.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    public RelayCommand CreateScenarioCommand { get; }
    public RelayCommand SaveScenarioCommand { get; }
    public RelayCommand DeleteScenarioCommand { get; }
    public RelayCommand AddScenarioEventCommand { get; }
    public RelayCommand EditScenarioEventCommand { get; }
    public RelayCommand DuplicateScenarioEventCommand { get; }
    public RelayCommand DeleteScenarioEventCommand { get; }

    public ScenarioDefinitionModel? SelectedScenarioDefinition
    {
        get => selectedScenarioDefinition;
        set
        {
            if (SetProperty(ref selectedScenarioDefinition, value))
            {
                LoadScenarioDraft(value);
                RefreshCommands();
            }
        }
    }

    public ScenarioEventListItem? SelectedEventItem
    {
        get => selectedEventItem;
        set
        {
            if (SetProperty(ref selectedEventItem, value))
            {
                LoadSelectedEventDraft();
                RefreshCommands();
            }
        }
    }

    public bool IsDirty { get => isDirty; private set => SetProperty(ref isDirty, value); }
    public bool HasScenarios => network.ScenarioDefinitions.Count > 0;
    public bool HasSelectedScenario => SelectedScenarioDefinition is not null;
    public bool HasSelectedEvent => SelectedEventItem is not null;
    public bool EventUsesNodeTarget => EventKind is ScenarioEventKind.NodeFailure or ScenarioEventKind.DemandSpike or ScenarioEventKind.ProductionMultiplier or ScenarioEventKind.ConsumptionMultiplier;
    public bool EventUsesEdgeTarget => EventKind is ScenarioEventKind.EdgeClosure or ScenarioEventKind.EdgeCostChange or ScenarioEventKind.RouteCostMultiplier;
    public bool EventUsesTrafficType => EventKind is ScenarioEventKind.DemandSpike or ScenarioEventKind.ProductionMultiplier or ScenarioEventKind.ConsumptionMultiplier or ScenarioEventKind.RouteCostMultiplier;
    public bool EventUsesValue => EventKind is ScenarioEventKind.DemandSpike or ScenarioEventKind.EdgeCostChange or ScenarioEventKind.ProductionMultiplier or ScenarioEventKind.ConsumptionMultiplier or ScenarioEventKind.RouteCostMultiplier;
    public string TargetFieldLabel => EventUsesNodeTarget ? "Target node" : "Target route";
    public string TargetHelperText => EventUsesNodeTarget ? "Choose a node id that exists in this network." : "Choose a route id that exists in this network.";
    public IReadOnlyList<string> TargetIdOptions => EventUsesNodeTarget ? NodeIdOptions : EdgeIdOptions;
    public string EventValueLabel => EventKind switch
    {
        ScenarioEventKind.DemandSpike => "Demand value",
        ScenarioEventKind.EdgeCostChange => "Route cost",
        ScenarioEventKind.ProductionMultiplier => "Production multiplier",
        ScenarioEventKind.ConsumptionMultiplier => "Consumption multiplier",
        ScenarioEventKind.RouteCostMultiplier => "Route cost multiplier",
        _ => "Value"
    };
    public string EventValueHelperText => EventKind == ScenarioEventKind.DemandSpike
        ? "Enter a demand value greater than or equal to 0."
        : "Enter a value greater than 0.";

    public string NameText { get => nameText; set => SetDraftProperty(ref nameText, value, nameof(NameText)); }
    public string DescriptionText { get => descriptionText; set => SetDraftProperty(ref descriptionText, value, nameof(DescriptionText)); }
    public string StartTimeText { get => startTimeText; set => SetDraftProperty(ref startTimeText, value, nameof(StartTimeText)); }
    public string EndTimeText { get => endTimeText; set => SetDraftProperty(ref endTimeText, value, nameof(EndTimeText)); }
    public string DeltaTimeText { get => deltaTimeText; set => SetDraftProperty(ref deltaTimeText, value, nameof(DeltaTimeText)); }
    public bool EnableAdaptiveRouting { get => enableAdaptiveRouting; set => SetDraftProperty(ref enableAdaptiveRouting, value, nameof(EnableAdaptiveRouting)); }
    public string EventNameText { get => eventNameText; set => SetDraftProperty(ref eventNameText, value, nameof(EventNameText)); }
    public ScenarioEventKind EventKind
    {
        get => eventKind;
        set
        {
            if (SetDraftProperty(ref eventKind, value, nameof(EventKind)))
            {
                EventTargetKind = EventUsesNodeTarget ? ScenarioTargetKind.Node : ScenarioTargetKind.Edge;
                RaiseEventVisibilityChanged();
            }
        }
    }
    public ScenarioTargetKind EventTargetKind { get => eventTargetKind; set => SetDraftProperty(ref eventTargetKind, value, nameof(EventTargetKind)); }
    public string EventTargetIdText { get => eventTargetIdText; set => SetDraftProperty(ref eventTargetIdText, value, nameof(EventTargetIdText)); }
    public string EventTrafficTypeText { get => eventTrafficTypeText; set => SetDraftProperty(ref eventTrafficTypeText, value, nameof(EventTrafficTypeText)); }
    public string EventStartTimeText { get => eventStartTimeText; set => SetDraftProperty(ref eventStartTimeText, value, nameof(EventStartTimeText)); }
    public string EventEndTimeText { get => eventEndTimeText; set => SetDraftProperty(ref eventEndTimeText, value, nameof(EventEndTimeText)); }
    public string EventValueText { get => eventValueText; set => SetDraftProperty(ref eventValueText, value, nameof(EventValueText)); }
    public string EventNotesText { get => eventNotesText; set => SetDraftProperty(ref eventNotesText, value, nameof(EventNotesText)); }
    public bool EventIsEnabled { get => eventIsEnabled; set => SetDraftProperty(ref eventIsEnabled, value, nameof(EventIsEnabled)); }
    public string ScenarioNameError { get => scenarioNameError; private set => SetProperty(ref scenarioNameError, value); }
    public string ScenarioStartTimeError { get => scenarioStartTimeError; private set => SetProperty(ref scenarioStartTimeError, value); }
    public string ScenarioEndTimeError { get => scenarioEndTimeError; private set => SetProperty(ref scenarioEndTimeError, value); }
    public string ScenarioDeltaTimeError { get => scenarioDeltaTimeError; private set => SetProperty(ref scenarioDeltaTimeError, value); }
    public string EventNameError { get => eventNameError; private set => SetProperty(ref eventNameError, value); }
    public string EventTargetError { get => eventTargetError; private set => SetProperty(ref eventTargetError, value); }
    public string EventTrafficTypeError { get => eventTrafficTypeError; private set => SetProperty(ref eventTrafficTypeError, value); }
    public string EventStartTimeError { get => eventStartTimeError; private set => SetProperty(ref eventStartTimeError, value); }
    public string EventEndTimeError { get => eventEndTimeError; private set => SetProperty(ref eventEndTimeError, value); }
    public string EventValueError { get => eventValueError; private set => SetProperty(ref eventValueError, value); }
    public string ValidationSummary { get => validationSummary; private set => SetProperty(ref validationSummary, value); }

    public void AttachNetwork(NetworkModel model)
    {
        network = model;
        Open();
        RaiseReferenceDataChanged();
    }

    public void Open()
    {
        snapshot = CloneScenarios(network.ScenarioDefinitions);
        SelectedScenarioDefinition = network.ScenarioDefinitions.FirstOrDefault();
        if (SelectedScenarioDefinition is null)
        {
            LoadScenarioDraft(null);
        }

        IsDirty = false;
        Raise(nameof(HasScenarios));
        Raise(nameof(ScenarioDefinitions));
    }

    public void DiscardChanges()
    {
        if (IsDirty)
        {
            network.ScenarioDefinitions = CloneScenarios(snapshot);
            SelectedScenarioDefinition = network.ScenarioDefinitions.FirstOrDefault();
            RefreshScenarioCollectionState();
        }

        LoadScenarioDraft(SelectedScenarioDefinition);
        IsDirty = false;
        ValidationSummary = "Changes discarded.";
    }

    private bool SetDraftProperty<T>(ref T field, T value, string propertyName)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        IsDirty = true;
        ValidateDraft(updateSummary: false);
        return true;
    }

    private void CreateScenario()
    {
        var scenario = new ScenarioDefinitionModel
        {
            Name = $"Scenario {network.ScenarioDefinitions.Count + 1}",
            Description = string.Empty,
            StartTime = 0d,
            EndTime = 10d,
            DeltaTime = 1d
        };
        network.ScenarioDefinitions.Add(scenario);
        SelectedScenarioDefinition = scenario;
        IsDirty = true;
        markDirty();
        RefreshScenarioCollectionState();
        ValidationSummary = "Scenario created. Complete the details, then save scenario.";
    }

    private void SaveScenario()
    {
        if (SelectedScenarioDefinition is null)
        {
            ValidationSummary = "Create or select a scenario before saving.";
            return;
        }

        if (!ValidateDraft(updateSummary: true))
        {
            return;
        }

        var scenario = SelectedScenarioDefinition;
        scenario.Name = NameText.Trim();
        scenario.Description = DescriptionText.Trim();
        scenario.StartTime = ParseDouble(StartTimeText);
        scenario.EndTime = ParseDouble(EndTimeText);
        scenario.DeltaTime = ParseDouble(DeltaTimeText);
        scenario.EnableAdaptiveRouting = EnableAdaptiveRouting;
        if (SelectedEventItem?.Model is { } evt)
        {
            ApplyEventDraft(evt);
        }

        IsDirty = false;
        snapshot = CloneScenarios(network.ScenarioDefinitions);
        markDirty();
        RefreshScenarioCollectionState();
        RefreshEventItems(SelectedEventItem?.Model);
        ValidationSummary = "Scenario saved.";
    }

    private void DeleteScenario()
    {
        if (SelectedScenarioDefinition is null)
        {
            return;
        }

        network.ScenarioDefinitions.Remove(SelectedScenarioDefinition);
        SelectedScenarioDefinition = network.ScenarioDefinitions.FirstOrDefault();
        IsDirty = false;
        markDirty();
        RefreshScenarioCollectionState();
        ValidationSummary = "Scenario deleted.";
    }

    private static List<ScenarioDefinitionModel> CloneScenarios(IEnumerable<ScenarioDefinitionModel> scenarios) =>
        scenarios.Select(scenario => new ScenarioDefinitionModel
        {
            Id = scenario.Id,
            Name = scenario.Name,
            Description = scenario.Description,
            StartTime = scenario.StartTime,
            EndTime = scenario.EndTime,
            DeltaTime = scenario.DeltaTime,
            EnableAdaptiveRouting = scenario.EnableAdaptiveRouting,
            Events = scenario.Events.Select(evt => new ScenarioEventModel
            {
                Id = evt.Id,
                Name = evt.Name,
                Kind = evt.Kind,
                TargetKind = evt.TargetKind,
                TargetId = evt.TargetId,
                TrafficTypeIdOrName = evt.TrafficTypeIdOrName,
                Time = evt.Time,
                EndTime = evt.EndTime,
                Value = evt.Value,
                Notes = evt.Notes,
                IsEnabled = evt.IsEnabled
            }).ToList()
        }).ToList();

    private void AddEvent()
    {
        if (SelectedScenarioDefinition is null)
        {
            return;
        }

        var evt = new ScenarioEventModel
        {
            Name = "New event",
            Kind = ScenarioEventKind.NodeFailure,
            TargetKind = ScenarioTargetKind.Node,
            Time = ParseDoubleOrDefault(StartTimeText, 0d),
            Value = 1d,
            IsEnabled = true
        };
        SelectedScenarioDefinition.Events.Add(evt);
        RefreshEventItems(evt);
        IsDirty = true;
        markDirty();
        ValidationSummary = "Event added. Fill in the target and save scenario.";
    }

    private void DuplicateEvent()
    {
        if (SelectedScenarioDefinition is null || SelectedEventItem?.Model is not { } source)
        {
            return;
        }

        var copy = new ScenarioEventModel
        {
            Name = $"{source.Name} Copy",
            Kind = source.Kind,
            TargetKind = source.TargetKind,
            TargetId = source.TargetId,
            TrafficTypeIdOrName = source.TrafficTypeIdOrName,
            Time = source.Time,
            EndTime = source.EndTime,
            Value = source.Value,
            Notes = source.Notes,
            IsEnabled = source.IsEnabled
        };
        SelectedScenarioDefinition.Events.Add(copy);
        RefreshEventItems(copy);
        IsDirty = true;
        markDirty();
        ValidationSummary = "Event duplicated.";
    }

    private void DeleteEvent()
    {
        if (SelectedScenarioDefinition is null || SelectedEventItem?.Model is not { } evt)
        {
            return;
        }

        SelectedScenarioDefinition.Events.Remove(evt);
        RefreshEventItems(SelectedScenarioDefinition.Events.FirstOrDefault());
        IsDirty = true;
        markDirty();
        ValidationSummary = "Event deleted.";
    }

    private void LoadScenarioDraft(ScenarioDefinitionModel? scenario)
    {
        var wasDirty = IsDirty;
        if (scenario is null)
        {
            NameText = string.Empty;
            DescriptionText = string.Empty;
            StartTimeText = "0";
            EndTimeText = "10";
            DeltaTimeText = "1";
            EnableAdaptiveRouting = false;
            EventItems.Clear();
            SelectedEventItem = null;
            ClearValidation();
            ValidationSummary = "No scenarios yet. Create a scenario to test closures, demand spikes, or routing changes.";
        }
        else
        {
            nameText = scenario.Name;
            descriptionText = scenario.Description;
            startTimeText = scenario.StartTime.ToString("0.##", CultureInfo.InvariantCulture);
            endTimeText = scenario.EndTime.ToString("0.##", CultureInfo.InvariantCulture);
            deltaTimeText = scenario.DeltaTime.ToString("0.##", CultureInfo.InvariantCulture);
            enableAdaptiveRouting = scenario.EnableAdaptiveRouting;
            RaiseScenarioDraftProperties();
            RefreshEventItems(scenario.Events.FirstOrDefault());
            ValidateDraft(updateSummary: false);
            ValidationSummary = "Review scenario details and events, then save scenario.";
        }

        IsDirty = wasDirty && scenario is not null;
        Raise(nameof(HasSelectedScenario));
    }

    private void LoadSelectedEventDraft()
    {
        var wasDirty = IsDirty;
        var evt = SelectedEventItem?.Model;
        if (evt is null)
        {
            eventNameText = string.Empty;
            eventKind = ScenarioEventKind.NodeFailure;
            eventTargetKind = ScenarioTargetKind.Node;
            eventTargetIdText = string.Empty;
            eventTrafficTypeText = string.Empty;
            eventStartTimeText = "0";
            eventEndTimeText = string.Empty;
            eventValueText = "1";
            eventNotesText = string.Empty;
            eventIsEnabled = true;
        }
        else
        {
            eventNameText = evt.Name;
            eventKind = evt.Kind;
            eventTargetKind = evt.TargetKind;
            eventTargetIdText = evt.TargetId ?? string.Empty;
            eventTrafficTypeText = evt.TrafficTypeIdOrName ?? string.Empty;
            eventStartTimeText = evt.Time.ToString("0.##", CultureInfo.InvariantCulture);
            eventEndTimeText = evt.EndTime?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
            eventValueText = evt.Value.ToString("0.##", CultureInfo.InvariantCulture);
            eventNotesText = evt.Notes;
            eventIsEnabled = evt.IsEnabled;
        }

        RaiseEventDraftProperties();
        ValidateDraft(updateSummary: false);
        IsDirty = wasDirty;
        Raise(nameof(HasSelectedEvent));
    }

    private void ApplyEventDraft(ScenarioEventModel evt)
    {
        evt.Name = EventNameText.Trim();
        evt.Kind = EventKind;
        evt.TargetKind = EventUsesNodeTarget ? ScenarioTargetKind.Node : ScenarioTargetKind.Edge;
        evt.TargetId = string.IsNullOrWhiteSpace(EventTargetIdText) ? null : EventTargetIdText.Trim();
        evt.TrafficTypeIdOrName = EventUsesTrafficType && !string.IsNullOrWhiteSpace(EventTrafficTypeText) ? EventTrafficTypeText.Trim() : null;
        evt.Time = ParseDouble(EventStartTimeText);
        evt.EndTime = string.IsNullOrWhiteSpace(EventEndTimeText) ? null : ParseDouble(EventEndTimeText);
        evt.Value = EventUsesValue ? ParseDouble(EventValueText) : 1d;
        evt.Notes = EventNotesText.Trim();
        evt.IsEnabled = EventIsEnabled;
    }

    private bool ValidateDraft(bool updateSummary)
    {
        ClearValidation();
        var errors = 0;
        if (SelectedScenarioDefinition is not null)
        {
            if (string.IsNullOrWhiteSpace(NameText))
            {
                ScenarioNameError = "Enter a scenario name.";
                errors++;
            }

            var hasStart = TryParseDouble(StartTimeText, out var start);
            var hasEnd = TryParseDouble(EndTimeText, out var end);
            if (!hasStart || start < 0d)
            {
                ScenarioStartTimeError = "Start time must be zero or greater.";
                errors++;
            }

            if (!hasEnd || end <= start)
            {
                ScenarioEndTimeError = "End time must be after start time.";
                errors++;
            }

            if (!TryParseDouble(DeltaTimeText, out var delta) || delta <= 0d)
            {
                ScenarioDeltaTimeError = "Step size must be greater than 0.";
                errors++;
            }
        }

        if (SelectedEventItem is not null)
        {
            if (string.IsNullOrWhiteSpace(EventNameText))
            {
                EventNameError = "Enter an event name.";
                errors++;
            }

            var hasEventStart = TryParseDouble(EventStartTimeText, out var eventStart);
            if (!hasEventStart || eventStart < 0d)
            {
                EventStartTimeError = "Start time must be zero or greater.";
                errors++;
            }

            if (!string.IsNullOrWhiteSpace(EventEndTimeText) &&
                (!TryParseDouble(EventEndTimeText, out var eventEnd) || eventEnd <= eventStart))
            {
                EventEndTimeError = "End time must be after start time.";
                errors++;
            }

            var target = (EventTargetIdText ?? string.Empty).Trim();
            if (EventUsesNodeTarget && (target.Length == 0 || !network.Nodes.Any(node => string.Equals(node.Id, target, StringComparison.OrdinalIgnoreCase))))
            {
                EventTargetError = "Choose a target node.";
                errors++;
            }
            else if (EventUsesEdgeTarget && (target.Length == 0 || !network.Edges.Any(edge => string.Equals(edge.Id, target, StringComparison.OrdinalIgnoreCase))))
            {
                EventTargetError = "Choose a target route.";
                errors++;
            }

            if (EventUsesTrafficType &&
                (string.IsNullOrWhiteSpace(EventTrafficTypeText) || !network.TrafficTypes.Any(type => string.Equals(type.Name, EventTrafficTypeText.Trim(), StringComparison.OrdinalIgnoreCase))))
            {
                EventTrafficTypeError = "Choose a traffic type.";
                errors++;
            }

            if (EventUsesValue)
            {
                if (!TryParseDouble(EventValueText, out var value) || (EventKind == ScenarioEventKind.DemandSpike ? value < 0d : value <= 0d))
                {
                    EventValueError = EventKind == ScenarioEventKind.DemandSpike
                        ? "Enter a demand value greater than or equal to 0."
                        : "Enter a value greater than 0.";
                    errors++;
                }
            }
        }

        if (updateSummary)
        {
            ValidationSummary = errors == 0 ? "Ready to save." : $"Fix {errors} validation issue(s) before saving.";
        }

        return errors == 0;
    }

    private void RefreshEventItems(ScenarioEventModel? selected)
    {
        EventItems.Clear();
        if (SelectedScenarioDefinition is not null)
        {
            foreach (var evt in SelectedScenarioDefinition.Events)
            {
                EventItems.Add(new ScenarioEventListItem(evt));
            }
        }

        SelectedEventItem = selected is null ? EventItems.FirstOrDefault() : EventItems.FirstOrDefault(item => ReferenceEquals(item.Model, selected));
        Raise(nameof(HasSelectedEvent));
    }

    private void RefreshScenarioCollectionState()
    {
        Raise(nameof(ScenarioDefinitions));
        Raise(nameof(HasScenarios));
        Raise(nameof(HasSelectedScenario));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SaveScenarioCommand.NotifyCanExecuteChanged();
        DeleteScenarioCommand.NotifyCanExecuteChanged();
        AddScenarioEventCommand.NotifyCanExecuteChanged();
        EditScenarioEventCommand.NotifyCanExecuteChanged();
        DuplicateScenarioEventCommand.NotifyCanExecuteChanged();
        DeleteScenarioEventCommand.NotifyCanExecuteChanged();
    }

    private void ClearValidation()
    {
        ScenarioNameError = string.Empty;
        ScenarioStartTimeError = string.Empty;
        ScenarioEndTimeError = string.Empty;
        ScenarioDeltaTimeError = string.Empty;
        EventNameError = string.Empty;
        EventTargetError = string.Empty;
        EventTrafficTypeError = string.Empty;
        EventStartTimeError = string.Empty;
        EventEndTimeError = string.Empty;
        EventValueError = string.Empty;
    }

    private void RaiseScenarioDraftProperties()
    {
        Raise(nameof(NameText));
        Raise(nameof(DescriptionText));
        Raise(nameof(StartTimeText));
        Raise(nameof(EndTimeText));
        Raise(nameof(DeltaTimeText));
        Raise(nameof(EnableAdaptiveRouting));
    }

    private void RaiseEventDraftProperties()
    {
        Raise(nameof(EventNameText));
        Raise(nameof(EventKind));
        Raise(nameof(EventTargetKind));
        Raise(nameof(EventTargetIdText));
        Raise(nameof(EventTrafficTypeText));
        Raise(nameof(EventStartTimeText));
        Raise(nameof(EventEndTimeText));
        Raise(nameof(EventValueText));
        Raise(nameof(EventNotesText));
        Raise(nameof(EventIsEnabled));
        RaiseEventVisibilityChanged();
    }

    private void RaiseEventVisibilityChanged()
    {
        Raise(nameof(EventUsesNodeTarget));
        Raise(nameof(EventUsesEdgeTarget));
        Raise(nameof(EventUsesTrafficType));
        Raise(nameof(EventUsesValue));
        Raise(nameof(TargetFieldLabel));
        Raise(nameof(TargetHelperText));
        Raise(nameof(TargetIdOptions));
        Raise(nameof(EventValueLabel));
        Raise(nameof(EventValueHelperText));
    }

    private void RaiseReferenceDataChanged()
    {
        Raise(nameof(NodeIdOptions));
        Raise(nameof(EdgeIdOptions));
        Raise(nameof(TrafficTypeOptions));
        Raise(nameof(TargetIdOptions));
    }

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static double ParseDouble(string text) => TryParseDouble(text, out var value) ? value : 0d;
    private static double ParseDoubleOrDefault(string text, double fallback) => TryParseDouble(text, out var value) ? value : fallback;
}

public sealed class FacilityOriginItem : ObservableObject
{
    private string maxTravelTimeText;

    public FacilityOriginItem(NodeModel node, double maxTravelTime)
    {
        Node = node;
        maxTravelTimeText = Math.Max(0d, maxTravelTime).ToString("0.##", CultureInfo.InvariantCulture);
    }

    public NodeModel Node { get; }
    public string DisplayName => string.IsNullOrWhiteSpace(Node.Name) ? Node.Id : Node.Name;

    public string MaxTravelTimeText
    {
        get => maxTravelTimeText;
        set
        {
            if (SetProperty(ref maxTravelTimeText, value))
            {
                Raise(nameof(DisplaySummary));
            }
        }
    }

    public string DisplaySummary => $"{DisplayName} | Max {MaxTravelTimeText}";

    public bool TryGetMaxTravelTime(out double maxTravelTime)
    {
        if ((double.TryParse(MaxTravelTimeText, NumberStyles.Float, CultureInfo.InvariantCulture, out maxTravelTime) ||
             double.TryParse(MaxTravelTimeText, NumberStyles.Float, CultureInfo.CurrentCulture, out maxTravelTime)) &&
            maxTravelTime >= 0d)
        {
            return true;
        }

        maxTravelTime = 0d;
        return false;
    }
}

public sealed class TrafficDefinitionListItem(TrafficTypeDefinition model)
{
    public TrafficTypeDefinition Model { get; } = model;
    public string Name => string.IsNullOrWhiteSpace(Model.Name) ? "(unnamed)" : Model.Name;
    public string RoutingPreferenceText => Model.RoutingPreference.ToString();
    public string AllocationModeText => Model.AllocationMode.ToString();
    public string SummaryBadgeText
    {
        get
        {
            var badges = new List<string>();
            if (Model.PerishabilityPeriods.HasValue)
            {
                badges.Add($"Perish {Model.PerishabilityPeriods.Value}");
            }

            if (Model.CapacityBidPerUnit.HasValue)
            {
                badges.Add($"Bid {Model.CapacityBidPerUnit.Value:0.##}");
            }

            return badges.Count == 0 ? "Stable defaults" : string.Join(" | ", badges);
        }
    }

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

public sealed class PeriodWindowEditorRow : ObservableObject
{
    private string startText = string.Empty;
    private string endText = string.Empty;

    public PeriodWindowEditorRow()
    {
    }

    public PeriodWindowEditorRow(PeriodWindow model)
    {
        startText = model.StartPeriod?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        endText = model.EndPeriod?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public string StartText
    {
        get => startText;
        set => SetProperty(ref startText, value);
    }

    public string EndText
    {
        get => endText;
        set => SetProperty(ref endText, value);
    }
}

public sealed class InputRequirementEditorRow : ObservableObject
{
    private string trafficType = string.Empty;
    private string inputQuantityText = "1";
    private string outputQuantityText = "1";

    public InputRequirementEditorRow()
    {
    }

    public InputRequirementEditorRow(ProductionInputRequirement model)
    {
        trafficType = model.TrafficType;
        inputQuantityText = (model.InputQuantity > 0d
            ? model.InputQuantity
            : model.QuantityPerOutputUnit.GetValueOrDefault(1d)).ToString("0.##", CultureInfo.InvariantCulture);
        outputQuantityText = (model.OutputQuantity > 0d ? model.OutputQuantity : 1d).ToString("0.##", CultureInfo.InvariantCulture);
    }

    public string TrafficType
    {
        get => trafficType;
        set => SetProperty(ref trafficType, value);
    }

    public string InputQuantityText
    {
        get => inputQuantityText;
        set => SetProperty(ref inputQuantityText, value);
    }

    public string OutputQuantityText
    {
        get => outputQuantityText;
        set => SetProperty(ref outputQuantityText, value);
    }
}

public sealed class PermissionRuleEditorRow : ObservableObject
{
    private string trafficType;
    private bool isActive;
    private EdgeTrafficPermissionMode mode;
    private EdgeTrafficLimitKind limitKind;
    private string limitValueText;
    private string effectiveSummary;
    private string baseValidationMessage = string.Empty;
    private string externalValidationMessage = string.Empty;
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
        set
        {
            if (SetProperty(ref trafficType, value))
            {
                Validate(null);
            }
        }
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

    public void RefreshValidation(double? edgeCapacity, IReadOnlyCollection<string>? knownTrafficTypes = null, bool hasDuplicateTrafficType = false) =>
        Validate(edgeCapacity, knownTrafficTypes, hasDuplicateTrafficType);

    private void Validate(double? edgeCapacity, IReadOnlyCollection<string>? knownTrafficTypes = null, bool hasDuplicateTrafficType = false)
    {
        if (SupportsOverrideToggle && !IsActive)
        {
            baseValidationMessage = string.Empty;
            externalValidationMessage = string.Empty;
            UpdateValidationMessage();
            return;
        }

        baseValidationMessage = string.Empty;
        if (Mode == EdgeTrafficPermissionMode.Limited)
        {
            var parsed = ParseNullableDouble(LimitValueText);
            if (!parsed.HasValue)
            {
                baseValidationMessage = LimitKind == EdgeTrafficLimitKind.PercentOfEdgeCapacity
                    ? "Enter a percentage from 0 to 100."
                    : "Enter a limit of 0 or more.";
            }
            else if (LimitKind == EdgeTrafficLimitKind.PercentOfEdgeCapacity)
            {
                if (parsed.Value < 0d || parsed.Value > 100d)
                {
                    baseValidationMessage = "Enter a percentage from 0 to 100.";
                }
                else if (!edgeCapacity.HasValue)
                {
                    baseValidationMessage = "Set edge capacity before using a percentage limit.";
                }
            }
            else if (parsed.Value < 0d)
            {
                baseValidationMessage = "Enter a limit of 0 or more.";
            }
        }

        externalValidationMessage = string.Empty;
        var normalizedTrafficType = string.IsNullOrWhiteSpace(TrafficType) ? string.Empty : TrafficType.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTrafficType))
        {
            externalValidationMessage = "Choose a traffic type for this route rule.";
        }
        else if (knownTrafficTypes is not null && !knownTrafficTypes.Contains(normalizedTrafficType))
        {
            externalValidationMessage = "Choose a traffic type that exists in the traffic type editor.";
        }
        else if (hasDuplicateTrafficType)
        {
            externalValidationMessage = "Use each traffic type only once per route.";
        }

        UpdateValidationMessage();
    }

    private void UpdateValidationMessage() =>
        ValidationMessage = string.IsNullOrWhiteSpace(baseValidationMessage) ? externalValidationMessage : baseValidationMessage;

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

public sealed class NodeInspectorDraft : ObservableObject
{
    private string? targetNodeId;
    private string nodeIdText = string.Empty;
    private string nodeNameText = string.Empty;
    private string nodeXText = string.Empty;
    private string nodeYText = string.Empty;
    private string placeTypeText = string.Empty;
    private string descriptionText = string.Empty;
    private string transhipmentCapacityText = string.Empty;
    private NodeVisualShape shape = NodeVisualShape.Square;
    private NodeKind nodeKind = NodeKind.Ordinary;
    private string referencedSubnetworkIdText = string.Empty;
    private bool isExternalInterface;
    private string interfaceNameText = string.Empty;
    private string controllingActorText = string.Empty;
    private string tagsText = string.Empty;
    private string templateIdText = string.Empty;

    public NodeInspectorDraft()
    {
        PlaceTypeSuggestions = [];
    }

    public string? TargetNodeId { get => targetNodeId; set => SetProperty(ref targetNodeId, value); }
    public string NodeIdText { get => nodeIdText; set => SetProperty(ref nodeIdText, value); }
    public string NodeNameText { get => nodeNameText; set => SetProperty(ref nodeNameText, value); }
    public string NodeXText { get => nodeXText; set => SetProperty(ref nodeXText, value); }
    public string NodeYText { get => nodeYText; set => SetProperty(ref nodeYText, value); }
    public string PlaceTypeText { get => placeTypeText; set => SetProperty(ref placeTypeText, value); }
    public string DescriptionText { get => descriptionText; set => SetProperty(ref descriptionText, value); }
    public string TranshipmentCapacityText { get => transhipmentCapacityText; set => SetProperty(ref transhipmentCapacityText, value); }
    public NodeVisualShape Shape { get => shape; set => SetProperty(ref shape, value); }
    public NodeKind NodeKind { get => nodeKind; set => SetProperty(ref nodeKind, value); }
    public string ReferencedSubnetworkIdText { get => referencedSubnetworkIdText; set => SetProperty(ref referencedSubnetworkIdText, value); }
    public bool IsExternalInterface { get => isExternalInterface; set => SetProperty(ref isExternalInterface, value); }
    public string InterfaceNameText { get => interfaceNameText; set => SetProperty(ref interfaceNameText, value); }
    public string ControllingActorText { get => controllingActorText; set => SetProperty(ref controllingActorText, value); }
    public string TagsText { get => tagsText; set => SetProperty(ref tagsText, value); }
    public string TemplateIdText { get => templateIdText; set => SetProperty(ref templateIdText, value); }
    public ObservableCollection<string> PlaceTypeSuggestions { get; }

    public void Clear()
    {
        TargetNodeId = null;
        NodeIdText = string.Empty;
        NodeNameText = string.Empty;
        NodeXText = string.Empty;
        NodeYText = string.Empty;
        PlaceTypeText = string.Empty;
        DescriptionText = string.Empty;
        TranshipmentCapacityText = string.Empty;
        Shape = NodeVisualShape.Square;
        NodeKind = NodeKind.Ordinary;
        ReferencedSubnetworkIdText = string.Empty;
        IsExternalInterface = false;
        InterfaceNameText = string.Empty;
        ControllingActorText = string.Empty;
        TagsText = string.Empty;
        TemplateIdText = string.Empty;
    }

    public void LoadFrom(NodeModel node)
    {
        TargetNodeId = node.Id;
        NodeIdText = node.Id;
        NodeNameText = node.Name;
        NodeXText = node.X?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        NodeYText = node.Y?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        PlaceTypeText = node.PlaceType ?? string.Empty;
        DescriptionText = node.LoreDescription ?? string.Empty;
        TranshipmentCapacityText = node.TranshipmentCapacity?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        Shape = node.Shape;
        NodeKind = node.NodeKind;
        ReferencedSubnetworkIdText = node.ReferencedSubnetworkId ?? string.Empty;
        IsExternalInterface = node.IsExternalInterface;
        InterfaceNameText = node.InterfaceName ?? string.Empty;
        ControllingActorText = node.ControllingActor ?? string.Empty;
        TagsText = string.Join(", ", node.Tags);
        TemplateIdText = node.TemplateId ?? string.Empty;
    }

    public void UpdateSuggestions(IEnumerable<string> suggestions)
    {
        PlaceTypeSuggestions.Clear();
        foreach (var suggestion in suggestions)
        {
            PlaceTypeSuggestions.Add(suggestion);
        }
    }
}

public sealed class EdgeInspectorDraft : ObservableObject
{
    private string? targetEdgeId;
    private string routeTypeText = string.Empty;
    private string timeText = "1";
    private string costText = "1";
    private string capacityText = string.Empty;
    private bool isBidirectional = true;

    public EdgeInspectorDraft()
    {
        RouteTypeSuggestions = [];
    }

    public string? TargetEdgeId { get => targetEdgeId; set => SetProperty(ref targetEdgeId, value); }
    public string RouteTypeText { get => routeTypeText; set => SetProperty(ref routeTypeText, value); }
    public string TimeText { get => timeText; set => SetProperty(ref timeText, value); }
    public string CostText { get => costText; set => SetProperty(ref costText, value); }
    public string CapacityText { get => capacityText; set => SetProperty(ref capacityText, value); }
    public bool IsBidirectional { get => isBidirectional; set => SetProperty(ref isBidirectional, value); }
    public ObservableCollection<string> RouteTypeSuggestions { get; }

    public void Clear()
    {
        TargetEdgeId = null;
        RouteTypeText = string.Empty;
        TimeText = "1";
        CostText = "1";
        CapacityText = string.Empty;
        IsBidirectional = true;
    }

    public void LoadFrom(EdgeModel edge)
    {
        TargetEdgeId = edge.Id;
        RouteTypeText = edge.RouteType ?? string.Empty;
        TimeText = edge.Time.ToString("0.##", CultureInfo.InvariantCulture);
        CostText = edge.Cost.ToString("0.##", CultureInfo.InvariantCulture);
        CapacityText = edge.Capacity?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        IsBidirectional = edge.IsBidirectional;
    }

    public void UpdateSuggestions(IEnumerable<string> suggestions)
    {
        RouteTypeSuggestions.Clear();
        foreach (var suggestion in suggestions)
        {
            RouteTypeSuggestions.Add(suggestion);
        }
    }
}

public sealed class BulkSelectionInspectorDraft : ObservableObject
{
    private IReadOnlyList<string> targetNodeIds = [];
    private string placeTypeText = string.Empty;
    private string transhipmentCapacityText = string.Empty;

    public BulkSelectionInspectorDraft()
    {
        PlaceTypeSuggestions = [];
    }

    public IReadOnlyList<string> TargetNodeIds { get => targetNodeIds; set => SetProperty(ref targetNodeIds, value); }
    public string PlaceTypeText { get => placeTypeText; set => SetProperty(ref placeTypeText, value); }
    public string TranshipmentCapacityText { get => transhipmentCapacityText; set => SetProperty(ref transhipmentCapacityText, value); }
    public ObservableCollection<string> PlaceTypeSuggestions { get; }

    public void Clear()
    {
        TargetNodeIds = [];
        PlaceTypeText = string.Empty;
        TranshipmentCapacityText = string.Empty;
    }

    public void LoadFrom(IReadOnlyList<string> nodeIds, IReadOnlyList<NodeModel> nodes)
    {
        TargetNodeIds = nodeIds;
        PlaceTypeText = nodes.Select(node => node.PlaceType ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            ? nodes.FirstOrDefault()?.PlaceType ?? string.Empty
            : string.Empty;
        TranshipmentCapacityText = nodes.Select(node => node.TranshipmentCapacity).Distinct().Count() == 1
            ? nodes.FirstOrDefault()?.TranshipmentCapacity?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
    }

    public void UpdateSuggestions(IEnumerable<string> suggestions)
    {
        PlaceTypeSuggestions.Clear();
        foreach (var suggestion in suggestions)
        {
            PlaceTypeSuggestions.Add(suggestion);
        }
    }
}

public sealed class LayerListItemViewModel : ObservableObject
{
    public required NetworkLayerModel Layer { get; init; }
    public Action? OnStateChanged { get; init; }
    public string Name => Layer.Name;
    public string TypeLabel => Layer.Type.ToString();
    public string VisibilityLabel => Layer.IsVisible ? "Visible" : "Hidden";
    public string LockLabel => Layer.IsLocked ? "Locked" : "Unlocked";
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public bool IsVisible
    {
        get => Layer.IsVisible;
        set
        {
            if (Layer.IsVisible == value)
            {
                return;
            }

            Layer.IsVisible = value;
            Raise(nameof(IsVisible));
            Raise(nameof(VisibilityLabel));
            OnStateChanged?.Invoke();
        }
    }

    public bool IsLocked
    {
        get => Layer.IsLocked;
        set
        {
            if (Layer.IsLocked == value)
            {
                return;
            }

            Layer.IsLocked = value;
            Raise(nameof(IsLocked));
            Raise(nameof(LockLabel));
            OnStateChanged?.Invoke();
        }
    }
}

public enum TopIssueTargetKind
{
    None,
    Node,
    Edge
}

public sealed class TopIssueViewModel
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required TopIssueTargetKind TargetKind { get; init; }
    public string? NodeId { get; init; }
    public string? NodeDisplayName { get; init; }
    public string? EdgeId { get; init; }
    public string? FromNodeName { get; init; }
    public string? ToNodeName { get; init; }
    public required string Breadcrumb { get; init; }
}

public sealed class SimulationActorDecisionViewModel
{
    public int Tick { get; init; }
    public string Actor { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Traffic { get; init; } = string.Empty;
    public string Delta { get; init; } = string.Empty;
    public string Cost { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string ExpectedEffect { get; init; } = string.Empty;
}

public sealed class SimulationActorActionOutcomeViewModel
{
    public string AppliedState { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string ActionKind { get; init; } = string.Empty;
    public string Actor { get; init; } = string.Empty;
}

public sealed class SimulationActorMetricsViewModel
{
    public int Tick { get; init; }
    public string TotalDelivered { get; init; } = string.Empty;
    public string TotalUnmetDemand { get; init; } = string.Empty;
    public string TotalMovementCost { get; init; } = string.Empty;
    public string AverageEdgeUtilisation { get; init; } = string.Empty;
    public string BottleneckEdgeCount { get; init; } = string.Empty;
    public string PolicyRestrictionCount { get; init; } = string.Empty;
    public string CooperationIndex { get; init; } = string.Empty;
}

public sealed class AgentLogViewModel : ObservableObject
{
    private Guid? selectedAgentId;
    private int? minTick;
    private int? maxTick;
    private readonly ObservableCollection<AgentActionLogEntry> allEntries = [];
    private readonly ObservableCollection<Guid> availableAgentIds = [];

    public ObservableCollection<AgentActionLogEntry> Entries { get; } = [];
    public ObservableCollection<Guid> AvailableAgentIds => availableAgentIds;

    public Guid? SelectedAgentId
    {
        get => selectedAgentId;
        set
        {
            if (SetProperty(ref selectedAgentId, value))
            {
                ApplyFilters();
            }
        }
    }

    public int? MinTick
    {
        get => minTick;
        set
        {
            if (SetProperty(ref minTick, value))
            {
                ApplyFilters();
            }
        }
    }

    public int? MaxTick
    {
        get => maxTick;
        set
        {
            if (SetProperty(ref maxTick, value))
            {
                ApplyFilters();
            }
        }
    }

    public void SetEntries(IEnumerable<AgentActionLogEntry> entries)
    {
        allEntries.Clear();
        foreach (var entry in entries.OrderBy(e => e.SimulationTick).ThenBy(e => e.Timestamp))
        {
            allEntries.Add(entry);
        }

        availableAgentIds.Clear();
        foreach (var agentId in allEntries.Select(e => e.AgentId).Distinct().OrderBy(id => id))
        {
            availableAgentIds.Add(agentId);
        }

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = allEntries.Where(entry =>
            (!SelectedAgentId.HasValue || entry.AgentId == SelectedAgentId.Value) &&
            (!MinTick.HasValue || entry.SimulationTick >= MinTick.Value) &&
            (!MaxTick.HasValue || entry.SimulationTick <= MaxTick.Value));

        Entries.Clear();
        foreach (var entry in filtered)
        {
            Entries.Add(entry);
        }
    }
}

public sealed class ActorTrafficTypeSelectionRow : ObservableObject
{
    private readonly Action<ActorTrafficTypeSelectionRow>? onChanged;
    private bool isAllowed;
    private bool isUpdating;

    public ActorTrafficTypeSelectionRow(string trafficType, bool isAllowed, Action<ActorTrafficTypeSelectionRow>? onChanged = null)
    {
        TrafficType = trafficType;
        this.isAllowed = isAllowed;
        this.onChanged = onChanged;
        ToggleCommand = new RelayCommand(() => IsAllowed = !IsAllowed);
    }

    public string TrafficType { get; }

    public bool IsAllowed
    {
        get => isAllowed;
        set
        {
            if (SetProperty(ref isAllowed, value) && !isUpdating)
            {
                onChanged?.Invoke(this);
            }
        }
    }

    public ICommand ToggleCommand { get; }

    public void SetAllowedSilently(bool value)
    {
        isUpdating = true;
        try
        {
            IsAllowed = value;
        }
        finally
        {
            isUpdating = false;
        }
    }
}

public enum SelectionSource
{
    User,
    TopIssue
}

public interface ICanvasSelectionService
{
    void SelectNode(string nodeId, SelectionSource source = SelectionSource.User);
    void SelectEdge(string edgeId, SelectionSource source = SelectionSource.User);
}

public interface IElementEditorCoordinator
{
    void OpenNodeEditor(string nodeId);
    void OpenEdgeEditor(string edgeId);
}

public sealed class WorkspaceViewModel : ObservableObject, IUiExceptionSink, ICanvasSelectionService, IElementEditorCoordinator
{
    private sealed class EdgeEditorSession
    {
        public required string EdgeId { get; init; }
        public required EdgeModel Snapshot { get; init; }
    }

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
    private readonly GraphRenderer graphRenderer = new();
    private readonly IsochroneService isochroneService = new();
    private readonly MultiOriginIsochroneService multiOriginIsochroneService = new();
    private readonly INetworkLayerService networkLayerService = new NetworkLayerResolver();
    private readonly IScenarioRunner scenarioRunner = new ScenarioRunner();
    private readonly IScenarioValidationService scenarioValidationService = new ScenarioValidationService();
    private readonly IExplainabilityService explainabilityService = new ExplainabilityService();
    private readonly ISankeyProjectionService sankeyProjectionService = new SankeyProjectionService();
    private readonly INetworkInsightService networkInsightService = new NetworkInsightService();
    private readonly OsmBoundingBoxImporter osmBoundingBoxImporter = new(new OsmApiClient());
    private readonly IAgentActionLogger agentActionLogger;
    private readonly SimulationActorCoordinator simulationActorCoordinator;

    private NetworkModel network = CreateInitializedNetworkModel();
    private NetworkModel? preAgentMutationNetwork;
    private TemporalNetworkSimulationEngine.TemporalSimulationState? temporalState;
    private TemporalNetworkSimulationEngine.TemporalSimulationStepResult? lastTimelineStepResult;
    private IReadOnlyList<TrafficSimulationOutcome> lastOutcomes = [];
    private IReadOnlyList<ConsumerCostSummary> lastConsumerCosts = [];
    private string statusText = "Select a tool and start editing.";
    private string toolStatusText = "Select mode: select, drag, or marquee.";
    private string toolInstructionText = "Shortcuts: S Select, A Add node, C Connect.";
    private GraphToolMode activeToolMode = GraphToolMode.Select;
    private bool reducedMotion;
    private int currentPeriod;
    private int timelineMaximum = 12;
    private int timelinePosition;
    private string networkNameText = string.Empty;
    private string networkDescriptionText = string.Empty;
    private string networkTimelineLoopLengthText = string.Empty;
    private NodeTrafficProfileListItem? selectedNodeTrafficProfileItem;
    private PeriodWindowEditorRow? selectedNodeProductionWindowItem;
    private PeriodWindowEditorRow? selectedNodeConsumptionWindowItem;
    private InputRequirementEditorRow? selectedNodeInputRequirementItem;
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
    private WorkspaceMode workspaceMode = WorkspaceMode.Normal;
    private AppView activeView = AppView.Network;
    private EdgeEditorSession? edgeEditorSession;
    private string edgeTimeValidationText = string.Empty;
    private string edgeCostValidationText = string.Empty;
    private string edgeCapacityValidationText = string.Empty;
    private string edgeEditorValidationText = string.Empty;
    private bool isRefreshingEdgeEditorState;
    private bool isRefreshingInspectorDrafts;
    private InspectorEditMode currentInspectorEditMode = InspectorEditMode.Network;
    private bool hasUnsavedChanges;
    private string? currentFilePath;
    private bool isIsochroneModeEnabled;
    private double isochroneThresholdMinutes = 15d;
    private HashSet<NodeModel> isochroneNodes = [];
    private Dictionary<string, double> isochroneDistances = new(Comparer);
    private string? cachedIsochroneOriginId;
    private double? cachedIsochroneThreshold;
    private int networkRevision;
    private int cachedIsochroneNetworkRevision = -1;
    private bool isFacilityPlanningMode;
    private double isochroneBudget = 15d;
    private MultiOriginIsochroneResult? currentMultiOriginIsochrone;
    private string facilityPlanningValidationText = string.Empty;
    private FacilityOriginItem? selectedFacilityNodeItem;
    private readonly Dictionary<string, Dictionary<string, double>> cachedFacilityDistances = new(Comparer);
    private Dictionary<string, List<FacilityCoverageInfo>> facilityCoverageByNodeId = new(Comparer);
    private Guid? selectedLayerId;
    private string selectedLayerNameText = string.Empty;
    private LayerListItemViewModel? selectedLayerItem;
    private ScenarioDefinitionModel? selectedScenarioDefinition;
    private ScenarioEventModel? selectedScenarioEvent;
    private string scenarioResultSummary = string.Empty;
    private TopIssueViewModel? selectedTopIssue;
    private string selectedIssueBreadcrumb = "Issue → (none selected)";
    private string topIssueUnmappedSummary = string.Empty;
    private string? pulseNodeId;
    private string? pulseEdgeId;
    private double pulseProgress;
    private string explanationTitle = "Why this item matters";
    private string explanationSummary = "Run a simulation to see constraints, delays, and unmet demand.";
    private IReadOnlyList<string> explanationCauses = [];
    private IReadOnlyList<string> explanationActions = [];
    private IReadOnlyList<string> explanationRelatedIssues = [];
    private VisualAnalyticsSnapshot? visualAnalyticsSnapshot;
    private SankeyDiagramModel? cachedSankeyDiagram;
    private VisualAnalyticsSnapshot? cachedSankeySnapshot;
    private string? cachedSankeyTrafficTypeFilter;
    private bool cachedSankeyShowUnmetDemand;
    private bool cachedSankeyCollapseMinorFlows;
    private int sankeyVersion;
    private string? activeModeLabel;
    private MapCameraState mapCamera = new(0d, 0d, 0.0008d, true);
    private readonly MapWebMercatorProjectionService mapProjectionService = new();
    private bool hasUserMovedMapCamera;
    private bool isOsmAreaSelectionEnabled;
    private OsmBoundingBox? osmSelection;
    private MapGeoCoordinate? osmSelectionStartCoordinate;
    private string osmWestText = string.Empty;
    private string osmSouthText = string.Empty;
    private string osmEastText = string.Empty;
    private string osmNorthText = string.Empty;
    private int osmNodeImportPercentage = 10;
    private string osmValidationMessage = "Drag to select an area.";
    private bool isOsmDownloadInProgress;
    private OsmImportOptions osmImportOptions = new(true, 10, OsmRetentionStrategy.Balanced, true, true);
    private readonly HashSet<string> highlightedNodeIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> highlightedEdgeIds = new(StringComparer.OrdinalIgnoreCase);
    private SimulationActorState? selectedSimulationActor;
    private int actorTick;
    private int actorRunTicks = 1;
    private bool hasActorPreview;
    private string actorStatusMessage = string.Empty;
    private string agentSearchText = string.Empty;
    private string actorNameText = string.Empty;
    private string actorBudgetText = "0";
    private string actorCashText = "0";
    private string actorRiskToleranceText = "0.5";
    private string actorCooperationWeightText = "0.5";
    private string actorNotesText = string.Empty;
    private bool actorIsEnabled = true;
    private bool actorAllowAllTrafficTypes = true;
    private string actorValidationText = string.Empty;
    private bool showAgentTools;
    private IReadOnlyList<PieChartSegmentViewModel> agentStatusDistributionData =
    [
        new("Moving", 0d),
        new("Idle", 1d),
        new("Processing", 0d)
    ];
    private IReadOnlyList<PieChartSegmentViewModel> nodeUtilizationMixData =
    [
        new("Balanced", 0d),
        new("Low", 1d),
        new("High", 0d)
    ];

    public WorkspaceViewModel(IAgentActionLogger? agentActionLogger = null, SimulationActorCoordinator? simulationActorCoordinator = null)
    {
        this.agentActionLogger = agentActionLogger ?? new AgentActionLogger();
        this.simulationActorCoordinator = simulationActorCoordinator ?? new SimulationActorCoordinator(actionLogger: this.agentActionLogger);
        network = CreateInitializedNetworkModel();
        UiExceptionBoundary.Sink = this;
        Scene = new GraphScene();
        Viewport = new GraphViewport();
        Inspector = new InspectorSection();
        ReportMetrics = [];
        TrafficReports = [];
        RouteReports = [];
        NodePressureReports = [];
        UncoveredPlanningItems = [];
        FacilityComparisonRows = [];
        SelectedNodeTrafficProfiles = [];
        SelectedNodeProductionWindows = [];
        SelectedNodeConsumptionWindows = [];
        SelectedNodeInputRequirements = [];
        SelectedEdgePermissionRows = [];
        NodeDraft = new NodeInspectorDraft();
        EdgeDraft = new EdgeInspectorDraft();
        BulkDraft = new BulkSelectionInspectorDraft();
        NodeDraft.PropertyChanged += HandleNodeDraftPropertyChanged;
        EdgeDraft.PropertyChanged += HandleEdgeDraftPropertyChanged;
        SelectedEdgePermissionRows.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems.OfType<PermissionRuleEditorRow>())
                {
                    item.PropertyChanged -= HandleSelectedEdgePermissionRowChanged;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<PermissionRuleEditorRow>())
                {
                    item.PropertyChanged += HandleSelectedEdgePermissionRowChanged;
                }
            }

            RefreshEdgeEditorState();
        };
        TrafficDefinitions = [];
        DefaultTrafficPermissionRows = [];
        SelectedFacilityNodes = [];
        LayerItems = [];
        TopIssues = [];
        TopIssueAdvisories = [];
        ScenarioWarnings = [];
        VisualisationState = new VisualisationState();
        NetworkInsights = [];
        SimulationActors = [];
        Agents = [];
        FilteredSimulationActors = [];
        ActorTrafficTypeRows = [];
        ActorDecisions = [];
        ActorActionOutcomes = [];
        ActorMetrics = [];
        AgentLog = new AgentLogViewModel();
        SimulationActors.CollectionChanged += (_, _) => RefreshAgentViewModels();
        TopIssues.CollectionChanged += (_, _) => Raise(nameof(TopIssueEmptyStateText));
        TopIssueAdvisories.CollectionChanged += (_, _) => Raise(nameof(HasTopIssueAdvisories));
        ScenarioEditor = new ScenarioEditorViewModel(network, MarkDirty);
        DefaultTrafficPermissionRows.CollectionChanged += (_, _) => RaiseTrafficTypeDisplayStateChanged();
        NewCommand = new RelayCommand(CreateBlankNetwork);
        SimulateCommand = new RelayCommand(RunSimulation);
        StepCommand = new RelayCommand(AdvanceTimeline);
        ResetTimelineCommand = new RelayCommand(ResetTimeline);
        FitCommand = new RelayCommand(FitActiveView);
        ToggleMotionCommand = new RelayCommand(() =>
        {
            ReducedMotion = !ReducedMotion;
            Scene.Simulation.ReducedMotion = ReducedMotion;
            NotifyVisualChanged();
        });
        OpenAboutCommand = new RelayCommand(() => AboutRequested?.Invoke(this, EventArgs.Empty));
        SelectToolCommand = new RelayCommand(() => SetActiveTool(GraphToolMode.Select));
        AddNodeToolCommand = new RelayCommand(() => SetActiveTool(GraphToolMode.AddNode));
        ConnectToolCommand = new RelayCommand(() => SetActiveTool(GraphToolMode.Connect));
        AgentToolCommand = new RelayCommand(() => SetActiveTool(GraphToolMode.Agent), () => ShowAgentTools);
        ToggleAgentToolsCommand = new RelayCommand(() => ShowAgentTools = !ShowAgentTools);
        SetNetworkViewCommand = new RelayCommand(() => ActiveView = AppView.Network);
        SetMapViewCommand = new RelayCommand(() => ActiveView = AppView.Map);
        SetSankeyViewCommand = new RelayCommand(() => ActiveView = AppView.Sankey);
        SetOsmImportViewCommand = new RelayCommand(EnterOsmImportView);
        SetAgentsViewCommand = new RelayCommand(() => ActiveView = AppView.Agents);
        SetAnalyticsViewCommand = new RelayCommand(() => ActiveView = AppView.Analytics);
        SetGraphVisualisationCommand = new RelayCommand(() => ActiveView = AppView.Network);
        SetSankeyVisualisationCommand = new RelayCommand(() => ActiveView = AppView.Sankey);
        SetMapVisualisationCommand = new RelayCommand(() => ActiveView = AppView.Map);
        ShowGraphModeCommand = SetGraphVisualisationCommand;
        ShowSankeyModeCommand = SetSankeyVisualisationCommand;
        ShowMapModeCommand = SetMapVisualisationCommand;
        StartOsmAreaSelectionCommand = new RelayCommand(EnterOsmImportWorkspace);
        ToggleOsmAreaSelectionCommand = new RelayCommand(() => IsOsmAreaSelectionEnabled = !IsOsmAreaSelectionEnabled);
        ClearOsmSelectionCommand = new RelayCommand(ClearOsmSelection);
        ImportOsmSelectionCommand = new RelayCommand(() => _ = ImportOsmSelectionAsync(), () => CanImportOsmSelection);
        CancelOsmImportCommand = new RelayCommand(CancelOsmImport, () => IsOsmImportWorkspaceMode);
        FitMapToNetworkCommand = new RelayCommand(FitMapToNetwork);
        ToggleIsochroneModeCommand = new RelayCommand(() => SetIsochroneMode(!IsIsochroneModeEnabled));
        ToggleFacilityPlanningModeCommand = new RelayCommand(() => SetFacilityPlanningMode(!IsFacilityPlanningMode));
        AddFacilityOriginCommand = new RelayCommand(AddSelectedNodeAsFacilityOrigin, () => Scene.Selection.SelectedNodeIds.Count == 1);
        RemoveFacilityOriginCommand = new RelayCommand(RemoveSelectedFacilityOrigin, () => SelectedFacilityNodeItem is not null);
        ClearFacilityOriginsCommand = new RelayCommand(ClearFacilityOrigins, () => SelectedFacilityNodes.Count > 0);
        RunMultiOriginIsochroneCommand = new RelayCommand(RunMultiOriginIsochrone, () => IsFacilityPlanningMode && SelectedFacilityNodes.Count > 0);
        SelectedFacilityNodes.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (var facility in e.OldItems.OfType<FacilityOriginItem>())
                {
                    facility.PropertyChanged -= HandleFacilityOriginChanged;
                    cachedFacilityDistances.Remove(facility.Node.Id);
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var facility in e.NewItems.OfType<FacilityOriginItem>())
                {
                    facility.PropertyChanged += HandleFacilityOriginChanged;
                }
            }

            Raise(nameof(FacilitySelectionSummary));
            Raise(nameof(CoveragePercentageText));
            Raise(nameof(FacilitySelectionCountText));
            Raise(nameof(SelectedFacilityDisplayNames));
            ClearFacilityOriginsCommand.NotifyCanExecuteChanged();
            RunMultiOriginIsochroneCommand.NotifyCanExecuteChanged();
            RefreshFacilityCoverageIfActive(updateStatusText: false);
        };
        DeleteSelectionCommand = new RelayCommand(DeleteSelection, () => CanDeleteSelection);
        ApplyInspectorCommand = new RelayCommand(ApplyInspectorEdits, () => CanApplyInspectorEdits);
        OpenSelectedEdgeEditorCommand = new RelayCommand(EnterEdgeEditor, () => CanOpenSelectedEdgeEditor);
        SaveEdgeEditorCommand = new RelayCommand(SaveEdgeEditor, () => CanSaveEdgeEditor);
        CancelEdgeEditorCommand = new RelayCommand(CancelEdgeEditor, () => IsEdgeEditorWorkspaceMode);
        DeleteSelectedEdgeEditorCommand = new RelayCommand(DeleteSelectedEdgeFromEditor, () => CanDeleteSelectedEdgeEditor);
        AddEdgePermissionRuleCommand = new RelayCommand(AddEdgePermissionRule, () => CanAddEdgePermissionRule);
        AddNodeTrafficProfileCommand = new RelayCommand(AddNodeTrafficProfile, () => IsEditingNode && TrafficTypeNameOptions.Count > 0);
        DuplicateSelectedNodeTrafficProfileCommand = new RelayCommand(DuplicateSelectedNodeTrafficProfile, () => IsEditingNode && SelectedNodeTrafficProfileItem is not null);
        RemoveSelectedNodeTrafficProfileCommand = new RelayCommand(RemoveSelectedNodeTrafficProfile, () => IsEditingNode && SelectedNodeTrafficProfileItem is not null);
        AddNodeProductionWindowCommand = new RelayCommand(AddNodeProductionWindow, () => SelectedNodeTrafficProfileItem is not null);
        RemoveSelectedNodeProductionWindowCommand = new RelayCommand(RemoveSelectedNodeProductionWindow, () => SelectedNodeTrafficProfileItem is not null && SelectedNodeProductionWindowItem is not null);
        AddNodeConsumptionWindowCommand = new RelayCommand(AddNodeConsumptionWindow, () => SelectedNodeTrafficProfileItem is not null);
        RemoveSelectedNodeConsumptionWindowCommand = new RelayCommand(RemoveSelectedNodeConsumptionWindow, () => SelectedNodeTrafficProfileItem is not null && SelectedNodeConsumptionWindowItem is not null);
        AddNodeInputRequirementCommand = new RelayCommand(AddNodeInputRequirement, () => SelectedNodeTrafficProfileItem is not null);
        RemoveSelectedNodeInputRequirementCommand = new RelayCommand(RemoveSelectedNodeInputRequirement, () => SelectedNodeTrafficProfileItem is not null && SelectedNodeInputRequirementItem is not null);
        AddTrafficDefinitionCommand = new RelayCommand(AddTrafficDefinition);
        RemoveSelectedTrafficDefinitionCommand = new RelayCommand(RemoveSelectedTrafficDefinition, () => SelectedTrafficDefinitionItem is not null);
        ApplyTrafficDefinitionCommand = new RelayCommand(ApplyTrafficDefinitionEdits, () => SelectedTrafficDefinitionItem is not null);
        AddLayerCommand = new RelayCommand(() => AddLayerOfType(NetworkLayerType.Physical));
        AddPhysicalLayerCommand = new RelayCommand(() => AddLayerOfType(NetworkLayerType.Physical));
        AddLogicalLayerCommand = new RelayCommand(() => AddLayerOfType(NetworkLayerType.Logical));
        AddPolicyLayerCommand = new RelayCommand(() => AddLayerOfType(NetworkLayerType.Policy));
        RenameLayerCommand = new RelayCommand(RenameSelectedLayer, () => SelectedLayerItem is not null);
        DeleteLayerCommand = new RelayCommand(DeleteSelectedLayer, () => SelectedLayerItem is not null && SelectedLayerItem.NodeCount == 0 && SelectedLayerItem.EdgeCount == 0);
        ToggleLayerVisibilityCommand = new RelayCommand(ToggleSelectedLayerVisibility, () => SelectedLayerItem is not null);
        ToggleLayerLockCommand = new RelayCommand(ToggleSelectedLayerLock, () => SelectedLayerItem is not null);
        ShowAllLayersCommand = new RelayCommand(ShowAllLayers);
        HideNonSelectedLayersCommand = new RelayCommand(HideNonSelectedLayers, () => SelectedLayerItem is not null);
        LockNonSelectedLayersCommand = new RelayCommand(LockNonSelectedLayers, () => SelectedLayerItem is not null);
        UnlockAllLayersCommand = new RelayCommand(UnlockAllLayers);
        AssignSelectedNodesToLayerCommand = new RelayCommand(AssignSelectedNodesToLayer, () => SelectedLayerItem is not null && Scene.Selection.SelectedNodeIds.Count > 0);
        AssignSelectedEdgesToLayerCommand = new RelayCommand(AssignSelectedEdgesToLayer, () => SelectedLayerItem is not null && Scene.Selection.SelectedEdgeIds.Count > 0);
        OpenScenarioEditorCommand = new RelayCommand(OpenScenarioEditor);
        CloseScenarioEditorCommand = new RelayCommand(CloseScenarioEditor, () => IsScenarioEditorWorkspaceMode);
        CreateScenarioCommand = new RelayCommand(CreateScenario);
        RenameScenarioCommand = new RelayCommand(RenameScenario, () => SelectedScenarioDefinition is not null);
        DuplicateScenarioCommand = new RelayCommand(DuplicateScenario, () => SelectedScenarioDefinition is not null);
        DeleteScenarioCommand = new RelayCommand(DeleteScenario, () => SelectedScenarioDefinition is not null);
        AddScenarioEventCommand = new RelayCommand(AddScenarioEvent, () => SelectedScenarioDefinition is not null);
        EditScenarioEventCommand = new RelayCommand(EditScenarioEvent, () => SelectedScenarioEvent is not null && SelectedScenarioDefinition is not null);
        DuplicateScenarioEventCommand = new RelayCommand(DuplicateScenarioEvent, () => SelectedScenarioEvent is not null && SelectedScenarioDefinition is not null);
        DeleteScenarioEventCommand = new RelayCommand(DeleteScenarioEvent, () => SelectedScenarioEvent is not null && SelectedScenarioDefinition is not null);
        RunScenarioCommand = new RelayCommand(RunScenario, () => SelectedScenarioDefinition is not null);
        SelectTopIssueCommand = new RelayCommand<TopIssueViewModel>(SelectTopIssue);
        AddFirmActorCommand = new RelayCommand(AddFirmActor);
        AddGovernmentActorCommand = new RelayCommand(AddGovernmentActor);
        AddLogisticsPlannerActorCommand = new RelayCommand(AddLogisticsPlannerActor);
        RemoveSelectedActorCommand = new RelayCommand(RemoveSelectedActor, () => SelectedSimulationActor is not null);
        DuplicateSelectedActorCommand = new RelayCommand(DuplicateSelectedActor, () => SelectedSimulationActor is not null);
        PreviewActorActionsCommand = new RelayCommand(PreviewActorActions);
        RunActorStepCommand = new RelayCommand(RunActorStep);
        RunActorTicksCommand = new RelayCommand(RunActorTicks);
        ApplyPreviewedActorActionsCommand = new RelayCommand(RunActorStep);
        ResetActorHistoryCommand = new RelayCommand(ResetActorHistory);
        ExportAgentLogsCommand = new RelayCommand(() => ExportAgentLogsRequested?.Invoke(this, EventArgs.Empty));
        AssignSelectedNodeToActorCommand = new RelayCommand(AssignSelectedNodeToActor, () => SelectedSimulationActor is not null);
        AssignSelectedEdgeToActorCommand = new RelayCommand(AssignSelectedEdgeToActor, () => SelectedSimulationActor is not null);
        ClearActorAssignmentsCommand = new RelayCommand(ClearActorAssignments, () => SelectedSimulationActor is not null);
        ApplySelectedActorCommand = new RelayCommand(ApplySelectedActorCommandExecute, () => SelectedSimulationActor is not null);
        VisualisationState.PropertyChanged += HandleVisualisationStatePropertyChanged;
        UpdateActiveModeState();

        CreateBlankNetwork();
        SetActiveTool(GraphToolMode.Select);
    }

    public GraphScene Scene { get; }
    public GraphViewport Viewport { get; }
    public MapCameraState MapCamera { get => mapCamera; private set => SetProperty(ref mapCamera, value); }
    public GraphSize LastViewportSize { get; private set; } = new(1440d, 860d);
    public int ViewportVersion { get; private set; }
    public InspectorSection Inspector { get; }
    public ObservableCollection<ReportMetricViewModel> ReportMetrics { get; }
    public ObservableCollection<TrafficReportRowViewModel> TrafficReports { get; }
    public ObservableCollection<RouteReportRowViewModel> RouteReports { get; }
    public ObservableCollection<NodePressureReportRowViewModel> NodePressureReports { get; }
    public ObservableCollection<UncoveredNodePlanningItem> UncoveredPlanningItems { get; }
    public ObservableCollection<FacilityComparisonRowViewModel> FacilityComparisonRows { get; }
    public ObservableCollection<NodeTrafficProfileListItem> SelectedNodeTrafficProfiles { get; }
    public ObservableCollection<PeriodWindowEditorRow> SelectedNodeProductionWindows { get; }
    public ObservableCollection<PeriodWindowEditorRow> SelectedNodeConsumptionWindows { get; }
    public ObservableCollection<InputRequirementEditorRow> SelectedNodeInputRequirements { get; }
    public ObservableCollection<PermissionRuleEditorRow> SelectedEdgePermissionRows { get; }
    public ObservableCollection<TrafficDefinitionListItem> TrafficDefinitions { get; }
    public ObservableCollection<PermissionRuleEditorRow> DefaultTrafficPermissionRows { get; }
    public ObservableCollection<FacilityOriginItem> SelectedFacilityNodes { get; }
    public ObservableCollection<LayerListItemViewModel> LayerItems { get; }
    public ObservableCollection<TopIssueViewModel> TopIssues { get; }
    public ObservableCollection<string> TopIssueAdvisories { get; }
    public ObservableCollection<string> ScenarioWarnings { get; }
    public NodeInspectorDraft NodeDraft { get; }
    public EdgeInspectorDraft EdgeDraft { get; }
    public BulkSelectionInspectorDraft BulkDraft { get; }
    public ScenarioEditorViewModel ScenarioEditor { get; }
    public VisualisationState VisualisationState { get; }
    public ObservableCollection<NetworkInsight> NetworkInsights { get; }
    public ObservableCollection<SimulationActorState> SimulationActors { get; }
    public ObservableCollection<AgentViewModel> Agents { get; }
    public ObservableCollection<SimulationActorState> FilteredSimulationActors { get; }
    public ObservableCollection<ActorTrafficTypeSelectionRow> ActorTrafficTypeRows { get; }
    public ObservableCollection<SimulationActorDecisionViewModel> ActorDecisions { get; }
    public ObservableCollection<SimulationActorActionOutcomeViewModel> ActorActionOutcomes { get; }
    public ObservableCollection<SimulationActorMetricsViewModel> ActorMetrics { get; }
    public AgentLogViewModel AgentLog { get; }

    public Array NodeShapeOptions { get; } = Enum.GetValues(typeof(NodeVisualShape));
    public Array NodeKindOptions { get; } = Enum.GetValues(typeof(NodeKind));
    public IReadOnlyList<string> NodeRoleOptions { get; } = NodeTrafficRoleCatalog.RoleOptions;
    public Array RoutingPreferenceOptions { get; } = Enum.GetValues(typeof(RoutingPreference));
    public Array AllocationModeOptions { get; } = Enum.GetValues(typeof(AllocationMode));
    public Array RouteChoiceModelOptions { get; } = Enum.GetValues(typeof(RouteChoiceModel));
    public Array FlowSplitPolicyOptions { get; } = Enum.GetValues(typeof(FlowSplitPolicy));
    public Array PermissionModeOptions { get; } = Enum.GetValues(typeof(EdgeTrafficPermissionMode));
    public Array PermissionLimitKindOptions { get; } = Enum.GetValues(typeof(EdgeTrafficLimitKind));
    public Array ScenarioEventKindOptions { get; } = Enum.GetValues(typeof(ScenarioEventKind));
    public Array ScenarioTargetKindOptions { get; } = Enum.GetValues(typeof(ScenarioTargetKind));

    public RelayCommand NewCommand { get; }
    public RelayCommand SimulateCommand { get; }
    public RelayCommand StepCommand { get; }
    public RelayCommand ResetTimelineCommand { get; }
    public RelayCommand FitCommand { get; }
    public RelayCommand ToggleMotionCommand { get; }
    public RelayCommand OpenAboutCommand { get; }
    public RelayCommand SelectToolCommand { get; }
    public RelayCommand AddNodeToolCommand { get; }
    public RelayCommand ConnectToolCommand { get; }
    public RelayCommand AgentToolCommand { get; }
    public RelayCommand ToggleAgentToolsCommand { get; }
    public ICommand SetNetworkViewCommand { get; }
    public ICommand SetMapViewCommand { get; }
    public ICommand SetSankeyViewCommand { get; }
    public ICommand SetOsmImportViewCommand { get; }
    public ICommand SetAgentsViewCommand { get; }
    public ICommand SetAnalyticsViewCommand { get; }
    public RelayCommand SetGraphVisualisationCommand { get; }
    public RelayCommand SetSankeyVisualisationCommand { get; }
    public RelayCommand SetMapVisualisationCommand { get; }
    public RelayCommand ShowGraphModeCommand { get; }
    public RelayCommand ShowSankeyModeCommand { get; }
    public RelayCommand ShowMapModeCommand { get; }
    public RelayCommand StartOsmAreaSelectionCommand { get; }
    public RelayCommand ToggleOsmAreaSelectionCommand { get; }
    public RelayCommand ClearOsmSelectionCommand { get; }
    public RelayCommand ImportOsmSelectionCommand { get; }
    public RelayCommand CancelOsmImportCommand { get; }
    public RelayCommand FitMapToNetworkCommand { get; }
    public bool IsOsmAreaSelectionEnabled
    {
        get => isOsmAreaSelectionEnabled;
        set
        {
            if (SetProperty(ref isOsmAreaSelectionEnabled, value))
            {
                ToolStatusText = value ? "Drag to select an area." : "Map selection disabled.";
                NotifyVisualChanged();
            }
        }
    }

    public bool IsOsmDownloadInProgress { get => isOsmDownloadInProgress; private set => SetProperty(ref isOsmDownloadInProgress, value); }
    public string OsmWestText { get => osmWestText; set => SetOsmCoordinateText(ref osmWestText, value, nameof(OsmWestText)); }
    public string OsmSouthText { get => osmSouthText; set => SetOsmCoordinateText(ref osmSouthText, value, nameof(OsmSouthText)); }
    public string OsmEastText { get => osmEastText; set => SetOsmCoordinateText(ref osmEastText, value, nameof(OsmEastText)); }
    public string OsmNorthText { get => osmNorthText; set => SetOsmCoordinateText(ref osmNorthText, value, nameof(OsmNorthText)); }
    public int OsmNodeImportPercentage
    {
        get => osmNodeImportPercentage;
        set
        {
            if (value is < 1 or > 100)
            {
                OsmValidationMessage = "Choose a value between 1% and 100%.";
                return;
            }

            if (SetProperty(ref osmNodeImportPercentage, value))
            {
                RefreshOsmSelectionMetrics();
            }
        }
    }

    public IReadOnlyList<int> OsmNodeImportPercentagePresets { get; } = [1, 2, 5, 10, 25, 50, 100];
    public string OsmValidationMessage { get => osmValidationMessage; private set => SetProperty(ref osmValidationMessage, value); }
    public string OsmSelectedAreaText => osmSelection is null ? "No area selected" : $"{osmSelection.AreaDegrees:0.####} square degrees";
    public bool CanImportOsmSelection => osmSelection is not null && !IsOsmDownloadInProgress && IsOsmSelectionValid();
    public string OsmTileCountText
    {
        get
        {
            if (osmSelection is null)
            {
                return "0 tiles";
            }

            try
            {
                var count = OsmBoundingBoxTiler.CreateTiles(osmSelection).Count;
                return count == 1 ? "1 tile" : $"{count.ToString(CultureInfo.InvariantCulture)} tiles";
            }
            catch
            {
                return "Too large";
            }
        }
    }
    public OsmBoundingBox? OsmSelection => osmSelection;
    public RelayCommand ToggleIsochroneModeCommand { get; }
    public RelayCommand ToggleFacilityPlanningModeCommand { get; }
    public RelayCommand AddFacilityOriginCommand { get; }
    public RelayCommand RemoveFacilityOriginCommand { get; }
    public RelayCommand ClearFacilityOriginsCommand { get; }
    public RelayCommand RunMultiOriginIsochroneCommand { get; }
    public RelayCommand DeleteSelectionCommand { get; }
    public RelayCommand ApplyInspectorCommand { get; }
    public RelayCommand OpenSelectedEdgeEditorCommand { get; }
    public RelayCommand SaveEdgeEditorCommand { get; }
    public RelayCommand CancelEdgeEditorCommand { get; }
    public RelayCommand DeleteSelectedEdgeEditorCommand { get; }
    public RelayCommand AddEdgePermissionRuleCommand { get; }
    public RelayCommand AddNodeTrafficProfileCommand { get; }
    public RelayCommand DuplicateSelectedNodeTrafficProfileCommand { get; }
    public RelayCommand RemoveSelectedNodeTrafficProfileCommand { get; }
    public RelayCommand AddNodeProductionWindowCommand { get; }
    public RelayCommand RemoveSelectedNodeProductionWindowCommand { get; }
    public RelayCommand AddNodeConsumptionWindowCommand { get; }
    public RelayCommand RemoveSelectedNodeConsumptionWindowCommand { get; }
    public RelayCommand AddNodeInputRequirementCommand { get; }
    public RelayCommand RemoveSelectedNodeInputRequirementCommand { get; }
    public RelayCommand AddTrafficDefinitionCommand { get; }
    public RelayCommand RemoveSelectedTrafficDefinitionCommand { get; }
    public RelayCommand ApplyTrafficDefinitionCommand { get; }
    public RelayCommand AddLayerCommand { get; }
    public RelayCommand AddPhysicalLayerCommand { get; }
    public RelayCommand AddLogicalLayerCommand { get; }
    public RelayCommand AddPolicyLayerCommand { get; }
    public RelayCommand RenameLayerCommand { get; }
    public RelayCommand DeleteLayerCommand { get; }
    public RelayCommand ToggleLayerVisibilityCommand { get; }
    public RelayCommand ToggleLayerLockCommand { get; }
    public RelayCommand ShowAllLayersCommand { get; }
    public RelayCommand HideNonSelectedLayersCommand { get; }
    public RelayCommand LockNonSelectedLayersCommand { get; }
    public RelayCommand UnlockAllLayersCommand { get; }
    public RelayCommand AssignSelectedNodesToLayerCommand { get; }
    public RelayCommand AssignSelectedEdgesToLayerCommand { get; }
    public RelayCommand AddFirmActorCommand { get; }
    public RelayCommand AddGovernmentActorCommand { get; }
    public RelayCommand AddLogisticsPlannerActorCommand { get; }
    public RelayCommand RemoveSelectedActorCommand { get; }
    public RelayCommand DuplicateSelectedActorCommand { get; }
    public RelayCommand PreviewActorActionsCommand { get; }
    public RelayCommand RunActorStepCommand { get; }
    public RelayCommand RunActorTicksCommand { get; }
    public RelayCommand ApplyPreviewedActorActionsCommand { get; }
    public RelayCommand ResetActorHistoryCommand { get; }
    public RelayCommand ExportAgentLogsCommand { get; }
    public RelayCommand AssignSelectedNodeToActorCommand { get; }
    public RelayCommand AssignSelectedEdgeToActorCommand { get; }
    public RelayCommand ClearActorAssignmentsCommand { get; }
    public RelayCommand ApplySelectedActorCommand { get; }
    public RelayCommand OpenScenarioEditorCommand { get; }
    public RelayCommand CloseScenarioEditorCommand { get; }
    public RelayCommand CreateScenarioCommand { get; }
    public RelayCommand RenameScenarioCommand { get; }
    public RelayCommand DuplicateScenarioCommand { get; }
    public RelayCommand DeleteScenarioCommand { get; }
    public RelayCommand AddScenarioEventCommand { get; }
    public RelayCommand EditScenarioEventCommand { get; }
    public RelayCommand DuplicateScenarioEventCommand { get; }
    public RelayCommand DeleteScenarioEventCommand { get; }
    public RelayCommand RunScenarioCommand { get; }
    public ICommand SelectTopIssueCommand { get; }
    public event EventHandler? AboutRequested;
    public event EventHandler? ExportAgentLogsRequested;

    public GraphInteractionController InteractionController => interactionController;
    public bool HasUnsavedChanges
    {
        get => hasUnsavedChanges;
        private set
        {
            if (SetProperty(ref hasUnsavedChanges, value))
            {
                Raise(nameof(WindowTitle));
                Raise(nameof(SessionSubtitle));
            }
        }
    }

    public string? CurrentFilePath
    {
        get => currentFilePath;
        private set
        {
            if (SetProperty(ref currentFilePath, value))
            {
                Raise(nameof(WindowTitle));
                Raise(nameof(SessionSubtitle));
                Raise(nameof(LastSavedDisplayName));
                Raise(nameof(SuggestedFileName));
            }
        }
    }

    public string LastSavedDisplayName => string.IsNullOrWhiteSpace(CurrentFilePath) ? "Untitled Network.json" : Path.GetFileName(CurrentFilePath);
    public string SuggestedFileName => string.IsNullOrWhiteSpace(CurrentFilePath) ? BuildSuggestedFileName() : Path.GetFileName(CurrentFilePath);
    public string WindowTitle => $"{(HasUnsavedChanges ? "*" : string.Empty)}MedW Network Sim | Avalonia Workstation | {network.Name}";
    public string SessionSubtitle => $"Active network: {network.Name} · {SimulationSummary}";
    public string StatusText { get => statusText; set => SetProperty(ref statusText, value); }
    public string ActiveModeLabel
    {
        get => activeModeLabel ?? "Graph view";
        private set => SetProperty(ref activeModeLabel, value);
    }
    public bool IsGraphMode => VisualisationState.ActiveMode == VisualisationMode.Graph;
    public bool IsSankeyMode => VisualisationState.ActiveMode == VisualisationMode.Sankey;
    public bool IsMapMode => VisualisationState.ActiveMode == VisualisationMode.Map;
    public bool HasAnyNodes => network.Nodes.Count > 0;
    public bool HasGeoAnchoredNodes => network.Nodes.Any(node => node.Latitude.HasValue && node.Longitude.HasValue);
    public bool IsLockLayoutToMapEnabled => HasGeoAnchoredNodes;
    public string LockLayoutToMapDisabledReason => HasGeoAnchoredNodes
        ? string.Empty
        : "Import OSM data with coordinates to use map-locked layout.";
    public bool LockLayoutToMap
    {
        get => network.LockLayoutToMap;
        set
        {
            var next = value && HasGeoAnchoredNodes;
            if (network.LockLayoutToMap == next)
            {
                return;
            }

            network.LockLayoutToMap = next;
            BuildSceneFromNetwork();
            if (VisualisationState.ActiveMode == VisualisationMode.Graph)
            {
                FitActiveView();
            }
            else
            {
                NotifyVisualChanged();
            }

            MarkDirty();
            Raise(nameof(LockLayoutToMap));
            Raise(nameof(IsMapLayoutLockedForGraph));
            StatusText = next
                ? "Graph layout locked to map coordinates."
                : "Map layout unlocked. Graph positions can now be edited.";
        }
    }
    public bool IsMapLayoutLockedForGraph => LockLayoutToMap && HasGeoAnchoredNodes;
    public IReadOnlyCollection<string> HighlightedNodeIds => highlightedNodeIds;
    public IReadOnlyCollection<string> HighlightedEdgeIds => highlightedEdgeIds;
    public int SankeyVersion
    {
        get => sankeyVersion;
        private set => SetProperty(ref sankeyVersion, value);
    }
    public SankeyDiagramModel CurrentSankey => BuildSankeyDiagram();
    public IEnumerable<FlowDataPoint> FlowSeries => GetFlowSeries();
    public IEnumerable<NodePressurePoint> NodePressureSeries => GetNodePressure();
    public void ReportUiException(string safeMessage, Exception exception)
    {
        _ = exception;
        StatusText = safeMessage;
    }

    public string ToolStatusText { get => toolStatusText; private set => SetProperty(ref toolStatusText, value); }
    public string ToolInstructionText { get => toolInstructionText; private set => SetProperty(ref toolInstructionText, value); }
    public GraphToolMode ActiveToolMode { get => activeToolMode; private set => SetProperty(ref activeToolMode, value); }
    public bool IsSelectToolActive => ActiveToolMode == GraphToolMode.Select;
    public bool IsAddNodeToolActive => ActiveToolMode == GraphToolMode.AddNode;
    public bool IsConnectToolActive => ActiveToolMode == GraphToolMode.Connect;
    public bool IsAgentToolActive => ActiveToolMode == GraphToolMode.Agent;
    public bool ShowAgentTools
    {
        get => showAgentTools;
        set
        {
            if (SetProperty(ref showAgentTools, value))
            {
                if (!showAgentTools && ActiveToolMode == GraphToolMode.Agent)
                {
                    SetActiveTool(GraphToolMode.Select);
                }

                AgentToolCommand.NotifyCanExecuteChanged();
                BuildSceneFromNetwork();
                NotifyVisualChanged();
            }
        }
    }
    public bool IsIsochroneModeEnabled => isIsochroneModeEnabled;
    public bool IsFacilityPlanningMode => isFacilityPlanningMode;
    public double IsochroneBudget
    {
        get => isochroneBudget;
        set
        {
            var sanitized = Math.Max(0d, value);
            if (SetProperty(ref isochroneBudget, sanitized))
            {
                Raise(nameof(CoveragePercentageText));
            }
        }
    }
    public MultiOriginIsochroneResult? CurrentMultiOriginIsochrone
    {
        get => currentMultiOriginIsochrone;
        private set
        {
            if (SetProperty(ref currentMultiOriginIsochrone, value))
            {
                Raise(nameof(ReachableNodeCountText));
                Raise(nameof(UncoveredNodeCountText));
                Raise(nameof(OverlapNodeCountText));
                Raise(nameof(CoveragePercentageText));
                Raise(nameof(AverageBestCostText));
                Raise(nameof(FacilitySelectionSummary));
                Raise(nameof(FacilityPlanningValidationText));
            }
        }
    }
    public FacilityOriginItem? SelectedFacilityNodeItem
    {
        get => selectedFacilityNodeItem;
        set
        {
            if (SetProperty(ref selectedFacilityNodeItem, value))
            {
                RemoveFacilityOriginCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public string FacilityPlanningValidationText
    {
        get => facilityPlanningValidationText;
        private set => SetProperty(ref facilityPlanningValidationText, value);
    }
    public string FacilitySelectionCountText => $"{SelectedFacilityNodes.Count} selected";
    public string ReachableNodeCountText => (CurrentMultiOriginIsochrone?.ReachableNodes.Count ?? 0).ToString(CultureInfo.InvariantCulture);
    public string UncoveredNodeCountText => (CurrentMultiOriginIsochrone?.UncoveredNodes.Count ?? network.Nodes.Count).ToString(CultureInfo.InvariantCulture);
    public string OverlapNodeCountText => (CurrentMultiOriginIsochrone?.OverlapNodes.Count ?? 0).ToString(CultureInfo.InvariantCulture);
    public string CoveragePercentageText
    {
        get
        {
            if (network.Nodes.Count == 0 || CurrentMultiOriginIsochrone is null)
            {
                return "0%";
            }

            var percentage = (CurrentMultiOriginIsochrone.ReachableNodes.Count / (double)network.Nodes.Count) * 100d;
            return $"{percentage:0.#}%";
        }
    }
    public string AverageBestCostText
    {
        get
        {
            if (CurrentMultiOriginIsochrone is null || CurrentMultiOriginIsochrone.BestCostByNode.Count == 0)
            {
                return "0";
            }

            return CurrentMultiOriginIsochrone.BestCostByNode.Values.Average().ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
    public string SelectedFacilityDisplayNames => SelectedFacilityNodes.Count == 0
        ? "None selected"
        : string.Join(", ", SelectedFacilityNodes.Select(facility => facility.DisplayName));
    public string FacilitySelectionSummary => CurrentMultiOriginIsochrone is null
        ? "Pick facilities, set max times, then run analysis."
        : $"Reachable nodes {ReachableNodeCountText}, uncovered {UncoveredNodeCountText}, overlap {OverlapNodeCountText}.";
    public HashSet<NodeModel> IsochroneNodes
    {
        get => isochroneNodes;
        set => SetProperty(ref isochroneNodes, value);
    }
    public string IsochroneLegendTitle => IsochroneNodes.Count == 0
        ? "Isochrone mode: select an origin node."
        : $"Reachable within {IsochroneThresholdMinutes:0.##} minutes";
    public string IsochroneLegendStrongLabel => "0–25% of threshold (strong highlight)";
    public string IsochroneLegendMediumLabel => "25–50% of threshold (medium highlight)";
    public string IsochroneLegendLightLabel => "50–100% of threshold (light highlight)";
    public double IsochroneThresholdMinutes
    {
        get => isochroneThresholdMinutes;
        set
        {
            var sanitized = Math.Max(0d, value);
            if (SetProperty(ref isochroneThresholdMinutes, sanitized))
            {
                Raise(nameof(IsochroneLegendTitle));
            }
        }
    }
    public bool ReducedMotion { get => reducedMotion; set => SetProperty(ref reducedMotion, value); }
    public int CurrentPeriod { get => currentPeriod; private set => SetProperty(ref currentPeriod, value); }
    public int TimelineMaximum { get => timelineMaximum; private set => SetProperty(ref timelineMaximum, value); }
    public int TimelinePosition { get => timelinePosition; set => SetProperty(ref timelinePosition, value); }
    public string SimulationSummary => temporalState is null ? "Static mode" : $"Timeline period {CurrentPeriod}";
    public string TrafficDeliveredColumnLabel => lastTimelineStepResult is null ? "Delivered" : "Started this period";
    public string SelectionSummary => BuildSelectionSummary();
    public string SelectedNodeIdText => BuildSelectedNodeIdText();
    public string SelectedNodeRoleSummaryText => BuildSelectedNodeRoleSummaryText();
    public bool CanDeleteSelection => Scene.Selection.SelectedNodeIds.Count > 0 || Scene.Selection.SelectedEdgeIds.Count > 0;
    public SimulationActorState? SelectedSimulationActor
    {
        get => selectedSimulationActor;
        set
        {
            if (value is not null)
            {
                EnsureActorReferences(value);
            }

            if (SetProperty(ref selectedSimulationActor, value))
            {
                LoadSelectedActorDraft();
                RefreshSelectedActorDisplayState();
                RemoveSelectedActorCommand.NotifyCanExecuteChanged();
                DuplicateSelectedActorCommand.NotifyCanExecuteChanged();
                AssignSelectedNodeToActorCommand.NotifyCanExecuteChanged();
                AssignSelectedEdgeToActorCommand.NotifyCanExecuteChanged();
                ClearActorAssignmentsCommand.NotifyCanExecuteChanged();
                ApplySelectedActorCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public int ActorTick { get => actorTick; private set => SetProperty(ref actorTick, value); }
    public int ActorRunTicks { get => actorRunTicks; set => SetProperty(ref actorRunTicks, Math.Max(1, value)); }
    public bool HasActorPreview { get => hasActorPreview; private set => SetProperty(ref hasActorPreview, value); }
    public string ActorStatusMessage { get => actorStatusMessage; private set => SetProperty(ref actorStatusMessage, value); }
    public string AgentSearchText
    {
        get => agentSearchText;
        set
        {
            if (SetProperty(ref agentSearchText, value))
            {
                RefreshFilteredSimulationActors();
            }
        }
    }
    public string ActorNameText { get => actorNameText; set => SetProperty(ref actorNameText, value); }
    public string ActorBudgetText { get => actorBudgetText; set => SetProperty(ref actorBudgetText, value); }
    public string ActorCashText { get => actorCashText; set => SetProperty(ref actorCashText, value); }
    public string ActorRiskToleranceText { get => actorRiskToleranceText; set => SetProperty(ref actorRiskToleranceText, value); }
    public string ActorCooperationWeightText { get => actorCooperationWeightText; set => SetProperty(ref actorCooperationWeightText, value); }
    public string ActorNotesText { get => actorNotesText; set => SetProperty(ref actorNotesText, value); }
    public bool ActorIsEnabled { get => actorIsEnabled; set => SetProperty(ref actorIsEnabled, value); }
    public bool ActorAllowAllTrafficTypes
    {
        get => actorAllowAllTrafficTypes;
        set
        {
            if (SetProperty(ref actorAllowAllTrafficTypes, value))
            {
                Raise(nameof(ShowActorTrafficTypeChecklist));
                if (!value)
                {
                    EnsureAllowedTrafficTypesOnlyKnownValues();
                }

                ApplyActorTrafficScopeFromDraft(markDirty: true);
            }
        }
    }
    public string ActorValidationText { get => actorValidationText; private set => SetProperty(ref actorValidationText, value); }
    public IReadOnlyList<PieChartSegmentViewModel> AgentStatusDistributionData
    {
        get => agentStatusDistributionData;
        private set => SetProperty(ref agentStatusDistributionData, value);
    }

    public IReadOnlyList<PieChartSegmentViewModel> NodeUtilizationMixData
    {
        get => nodeUtilizationMixData;
        private set => SetProperty(ref nodeUtilizationMixData, value);
    }

    public string SelectedActorNodeCountText => SelectedSimulationActor is null ? "Nodes: 0" : $"Nodes: {SelectedSimulationActor.ControlledNodeIds?.Count ?? 0}";
    public string SelectedActorEdgeCountText => SelectedSimulationActor is null ? "Routes: 0" : $"Routes: {SelectedSimulationActor.ControlledEdgeIds?.Count ?? 0}";
    public string SelectedActorTrafficScopeText
    {
        get
        {
            if (SelectedSimulationActor?.Capability is null)
            {
                return "Traffic scope: none";
            }

            if (SelectedSimulationActor.Capability.AllowAllTrafficTypes)
            {
                return "Traffic scope: all traffic types";
            }

            var allowed = SelectedSimulationActor.Capability.AllowedTrafficTypes?.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(Comparer).ToList() ?? [];
            return allowed.Count == 0 ? "Traffic scope: no traffic types selected" : $"Traffic scope: {string.Join(", ", allowed)}";
        }
    }
    public string SelectedActorControlledNodesDisplay => SelectedSimulationActor?.ControlledNodeIds is not { Count: > 0 }
        ? "None"
        : string.Join(", ", SelectedSimulationActor.ControlledNodeIds);
    public string SelectedActorControlledEdgesDisplay => SelectedSimulationActor?.ControlledEdgeIds is not { Count: > 0 }
        ? "None"
        : string.Join(", ", SelectedSimulationActor.ControlledEdgeIds);
    public bool ShowActorTrafficTypeChecklist => !ActorAllowAllTrafficTypes;
    public LayerListItemViewModel? SelectedLayerItem
    {
        get => selectedLayerItem;
        set
        {
            if (SetProperty(ref selectedLayerItem, value))
            {
                selectedLayerId = value?.Layer.Id;
                SelectedLayerNameText = value?.Layer.Name ?? string.Empty;
                Raise(nameof(SelectedLayerHelperText));
                RenameLayerCommand.NotifyCanExecuteChanged();
                DeleteLayerCommand.NotifyCanExecuteChanged();
                ToggleLayerVisibilityCommand.NotifyCanExecuteChanged();
                ToggleLayerLockCommand.NotifyCanExecuteChanged();
                HideNonSelectedLayersCommand.NotifyCanExecuteChanged();
                LockNonSelectedLayersCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public string SelectedLayerNameText
    {
        get => selectedLayerNameText;
        set => SetProperty(ref selectedLayerNameText, value);
    }
    public string SelectedLayerHelperText => "Layers separate real routes, supply flows, and policy rules.";
    public IReadOnlyList<ScenarioDefinitionModel> ScenarioDefinitions => network.ScenarioDefinitions;
    public ScenarioDefinitionModel? SelectedScenarioDefinition
    {
        get => selectedScenarioDefinition;
        set
        {
            if (SetProperty(ref selectedScenarioDefinition, value))
            {
                SelectedScenarioEvent = value?.Events.FirstOrDefault();
                AddScenarioEventCommand.NotifyCanExecuteChanged();
                RunScenarioCommand.NotifyCanExecuteChanged();
                RenameScenarioCommand.NotifyCanExecuteChanged();
                DuplicateScenarioCommand.NotifyCanExecuteChanged();
                DeleteScenarioCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public ScenarioEventModel? SelectedScenarioEvent
    {
        get => selectedScenarioEvent;
        set
        {
            if (SetProperty(ref selectedScenarioEvent, value))
            {
                EditScenarioEventCommand.NotifyCanExecuteChanged();
                DuplicateScenarioEventCommand.NotifyCanExecuteChanged();
                DeleteScenarioEventCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public TopIssueViewModel? SelectedTopIssue
    {
        get => selectedTopIssue;
        set
        {
            if (SetProperty(ref selectedTopIssue, value))
            {
                if (SelectTopIssueCommand is RelayCommand<TopIssueViewModel> command)
                {
                    command.NotifyCanExecuteChanged();
                }
            }
        }
    }
    public string SelectedIssueBreadcrumb { get => selectedIssueBreadcrumb; private set => SetProperty(ref selectedIssueBreadcrumb, value); }
    public bool HasTopIssueAdvisories => TopIssueAdvisories.Count > 0;
    public string TopIssueUnmappedSummary { get => topIssueUnmappedSummary; private set => SetProperty(ref topIssueUnmappedSummary, value); }
    public string TopIssueEmptyStateText => TopIssues.Count == 0
        ? "No node or route issues found. Run a simulation or check network-wide advisories."
        : string.Empty;
    public string? PulseNodeId { get => pulseNodeId; private set => SetProperty(ref pulseNodeId, value); }
    public string? PulseEdgeId { get => pulseEdgeId; private set => SetProperty(ref pulseEdgeId, value); }
    public double PulseProgress { get => pulseProgress; private set => SetProperty(ref pulseProgress, value); }
    public string ScenarioResultSummary { get => scenarioResultSummary; private set => SetProperty(ref scenarioResultSummary, value); }
    public string ExplanationTitle { get => explanationTitle; private set => SetProperty(ref explanationTitle, value); }
    public string InsightsEmptyStateText => lastOutcomes.Count == 0 ? "Run a simulation to generate insights." : string.Empty;
    public string ExplanationSummary { get => explanationSummary; private set => SetProperty(ref explanationSummary, value); }
    public IReadOnlyList<string> ExplanationCauses { get => explanationCauses; private set => SetProperty(ref explanationCauses, value); }
    public IReadOnlyList<string> ExplanationActions { get => explanationActions; private set => SetProperty(ref explanationActions, value); }
    public IReadOnlyList<string> ExplanationRelatedIssues { get => explanationRelatedIssues; private set => SetProperty(ref explanationRelatedIssues, value); }
    public InspectorEditMode CurrentInspectorEditMode => currentInspectorEditMode;
    public bool IsEditingNetwork => CurrentInspectorEditMode == InspectorEditMode.Network;
    public bool IsEditingNode => CurrentInspectorEditMode == InspectorEditMode.Node;
    public bool IsEditingEdge => CurrentInspectorEditMode == InspectorEditMode.Edge;
    public bool IsEditingSelection => CurrentInspectorEditMode == InspectorEditMode.Selection;
    public AppView ActiveView
    {
        get => activeView;
        set
        {
            if (!SetProperty(ref activeView, value))
            {
                return;
            }

            VisualisationState.ActiveMode = value switch
            {
                AppView.Map or AppView.OSMImport => VisualisationMode.Map,
                AppView.Sankey or AppView.Analytics => VisualisationMode.Sankey,
                _ => VisualisationMode.Graph
            };
            if (CurrentWorkspaceMode == WorkspaceMode.OsmImport && value != AppView.OSMImport)
            {
                CurrentWorkspaceMode = WorkspaceMode.Normal;
            }

            Raise(nameof(IsNetworkView));
            Raise(nameof(IsMapView));
            Raise(nameof(IsSankeyView));
            Raise(nameof(IsOsmImportView));
            Raise(nameof(IsAgentsView));
            Raise(nameof(IsAnalyticsView));
            NotifyVisualChanged();
        }
    }
    public string SelectedActorAllowedActionsText => SelectedSimulationActor?.Capability?.AllowedActionKinds is { Count: > 0 } actions
        ? string.Join(", ", actions)
        : "Allowed actions: none";
    public bool IsNetworkView => ActiveView == AppView.Network;
    public bool IsMapView => ActiveView == AppView.Map;
    public bool IsSankeyView => ActiveView == AppView.Sankey;
    public bool IsOsmImportView => ActiveView == AppView.OSMImport;
    public bool IsAgentsView => ActiveView == AppView.Agents;
    public bool IsAnalyticsView => ActiveView == AppView.Analytics;
    public WorkspaceMode CurrentWorkspaceMode
    {
        get => workspaceMode;
        private set
        {
            if (SetProperty(ref workspaceMode, value))
            {
                Raise(nameof(IsNormalWorkspaceMode));
                Raise(nameof(IsEdgeEditorWorkspaceMode));
                Raise(nameof(IsScenarioEditorWorkspaceMode));
                Raise(nameof(IsOsmImportWorkspaceMode));
                Raise(nameof(CanOpenSelectedEdgeEditor));
                Raise(nameof(CanSaveEdgeEditor));
                Raise(nameof(CanDeleteSelectedEdgeEditor));
                Raise(nameof(CanAddEdgePermissionRule));
                OpenSelectedEdgeEditorCommand.NotifyCanExecuteChanged();
                SaveEdgeEditorCommand.NotifyCanExecuteChanged();
                CancelEdgeEditorCommand.NotifyCanExecuteChanged();
                CloseScenarioEditorCommand.NotifyCanExecuteChanged();
                CancelOsmImportCommand.NotifyCanExecuteChanged();
                DeleteSelectedEdgeEditorCommand.NotifyCanExecuteChanged();
                AddEdgePermissionRuleCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public bool IsNormalWorkspaceMode => CurrentWorkspaceMode == WorkspaceMode.Normal;
    public bool IsEdgeEditorWorkspaceMode => CurrentWorkspaceMode == WorkspaceMode.EdgeEditor;
    public bool IsScenarioEditorWorkspaceMode => CurrentWorkspaceMode == WorkspaceMode.ScenarioEditor;
    public bool IsOsmImportWorkspaceMode => CurrentWorkspaceMode == WorkspaceMode.OsmImport;
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
    public string SelectedTrafficTypeSummaryText => BuildSelectedTrafficTypeSummaryText();
    public string SelectedTrafficTypeStatusText => SelectedTrafficDefinitionItem is null
        ? "Select a traffic type to edit"
        : string.IsNullOrWhiteSpace(TrafficValidationText)
            ? "Ready to apply"
            : "Fix the issue below before applying";
    public string SelectedTrafficTypeIssueCountText => SelectedTrafficTypeStatusText;
    public string SelectedTrafficTypeDefaultAccessSummaryText => BuildSelectedTrafficTypeDefaultAccessSummaryText();
    public bool CanApplyInspectorEdits => string.IsNullOrWhiteSpace(NodeTrafficRoleValidationText);
    public bool CanOpenSelectedEdgeEditor => GetSelectedEdgeModel() is not null && !IsEdgeEditorWorkspaceMode;
    public bool CanSaveEdgeEditor =>
        IsEdgeEditorWorkspaceMode &&
        GetSelectedEdgeModel() is not null &&
        string.IsNullOrWhiteSpace(EdgeTimeValidationText) &&
        string.IsNullOrWhiteSpace(EdgeCostValidationText) &&
        string.IsNullOrWhiteSpace(EdgeCapacityValidationText) &&
        SelectedEdgePermissionRows.All(row => string.IsNullOrWhiteSpace(row.ValidationMessage));
    public bool CanDeleteSelectedEdgeEditor => IsEdgeEditorWorkspaceMode && GetSelectedEdgeModel() is not null;
    public bool CanAddEdgePermissionRule => IsEdgeEditorWorkspaceMode && AvailableEdgeRuleTrafficTypes.Count > 0;
    public string EdgeTimeValidationText { get => edgeTimeValidationText; private set => SetProperty(ref edgeTimeValidationText, value); }
    public string EdgeCostValidationText { get => edgeCostValidationText; private set => SetProperty(ref edgeCostValidationText, value); }
    public string EdgeCapacityValidationText { get => edgeCapacityValidationText; private set => SetProperty(ref edgeCapacityValidationText, value); }
    public string EdgeEditorValidationText { get => edgeEditorValidationText; private set => SetProperty(ref edgeEditorValidationText, value); }
    public string SelectedEdgeIdText => GetEdgeSummaryContext()?.Id ?? "No route selected";
    public string SelectedNodeLatitudeText => GetNodeSummaryContext()?.Latitude?.ToString("0.######", CultureInfo.InvariantCulture) ?? "Not set";
    public string SelectedNodeLongitudeText => GetNodeSummaryContext()?.Longitude?.ToString("0.######", CultureInfo.InvariantCulture) ?? "Not set";
    public string SelectedEdgeSourceNodeText => GetEdgeSummaryContext()?.FromNodeId ?? "No route selected";
    public string SelectedEdgeTargetNodeText => GetEdgeSummaryContext()?.ToNodeId ?? "No route selected";
    public string SelectedEdgeDirectionSummaryText
    {
        get
        {
            var edge = GetEdgeSummaryContext();
            if (edge is null)
            {
                return "No route selected";
            }

            return EdgeIsBidirectional
                ? $"Bidirectional between {edge.FromNodeId} and {edge.ToNodeId}"
                : $"One-way from {edge.FromNodeId} to {edge.ToNodeId}";
        }
    }

    public string SelectedEdgeRuleCountText
    {
        get
        {
            var activeRules = SelectedEdgePermissionRows.Count(row => !row.SupportsOverrideToggle || row.IsActive);
            var issueCount = SelectedEdgePermissionRows.Count(row => !string.IsNullOrWhiteSpace(row.ValidationMessage));
            return issueCount == 0
                ? $"{activeRules} route rules ready"
                : $"{activeRules} route rules, {issueCount} need attention";
        }
    }

    public string SelectedEdgeValidationStatusText => CanSaveEdgeEditor || !IsEdgeEditorWorkspaceMode
        ? "Ready to save"
        : "Fix the highlighted route details before saving.";
    public string SelectedEdgePreviewTitleText
    {
        get
        {
            var edge = GetEdgeSummaryContext();
            if (edge is null)
            {
                return "No route selected";
            }

            var routeLabel = string.IsNullOrWhiteSpace(EdgeDraft.RouteTypeText) ? edge.RouteType : EdgeDraft.RouteTypeText;
            return string.IsNullOrWhiteSpace(routeLabel) ? edge.Id : routeLabel.Trim();
        }
    }

    public string SelectedEdgePreviewTravelText => $"Time {EdgeDraft.TimeText} | Cost {EdgeDraft.CostText}";
    public string SelectedEdgePreviewCapacityText => string.IsNullOrWhiteSpace(EdgeDraft.CapacityText)
        ? "Capacity unlimited"
        : $"Capacity {EdgeDraft.CapacityText}";
    public IReadOnlyList<string> AvailableEdgeRuleTrafficTypes =>
        SelectedEdgePermissionRows
            .Where(row => row.SupportsOverrideToggle && !row.IsActive)
            .Select(row => row.TrafficType)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, Comparer)
            .ToList();
    public IReadOnlyList<PermissionRuleEditorRow> VisibleEdgePermissionRows =>
        SelectedEdgePermissionRows
            .Where(row => !row.SupportsOverrideToggle || row.IsActive)
            .ToList();
    public string NetworkNameText { get => networkNameText; set => SetProperty(ref networkNameText, value); }
    public string NetworkDescriptionText { get => networkDescriptionText; set => SetProperty(ref networkDescriptionText, value); }
    public string NetworkTimelineLoopLengthText { get => networkTimelineLoopLengthText; set => SetProperty(ref networkTimelineLoopLengthText, value); }
    public string? InspectorNodeTargetId => NodeDraft.TargetNodeId;
    public IReadOnlyList<string> InspectorBulkTargetNodeIds => BulkDraft.TargetNodeIds;

    public NodeTrafficProfileListItem? SelectedNodeTrafficProfileItem
    {
        get => selectedNodeTrafficProfileItem;
        set
        {
            if (SetProperty(ref selectedNodeTrafficProfileItem, value))
            {
                PopulateSelectedNodeTrafficEditor();
                PreviewSelectedNodeSceneLayout();
                Raise(nameof(IsNodeTrafficRoleSelected));
                Raise(nameof(IsNodeStoreCapacityEnabled));
                RaiseNodeTrafficRoleValidationStateChanged();
                DuplicateSelectedNodeTrafficProfileCommand.NotifyCanExecuteChanged();
                RemoveSelectedNodeTrafficProfileCommand.NotifyCanExecuteChanged();
                AddNodeProductionWindowCommand.NotifyCanExecuteChanged();
                RemoveSelectedNodeProductionWindowCommand.NotifyCanExecuteChanged();
                AddNodeConsumptionWindowCommand.NotifyCanExecuteChanged();
                RemoveSelectedNodeConsumptionWindowCommand.NotifyCanExecuteChanged();
                AddNodeInputRequirementCommand.NotifyCanExecuteChanged();
                RemoveSelectedNodeInputRequirementCommand.NotifyCanExecuteChanged();
                ApplyInspectorCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public PeriodWindowEditorRow? SelectedNodeProductionWindowItem
    {
        get => selectedNodeProductionWindowItem;
        set
        {
            if (SetProperty(ref selectedNodeProductionWindowItem, value))
            {
                RemoveSelectedNodeProductionWindowCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public PeriodWindowEditorRow? SelectedNodeConsumptionWindowItem
    {
        get => selectedNodeConsumptionWindowItem;
        set
        {
            if (SetProperty(ref selectedNodeConsumptionWindowItem, value))
            {
                RemoveSelectedNodeConsumptionWindowCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public InputRequirementEditorRow? SelectedNodeInputRequirementItem
    {
        get => selectedNodeInputRequirementItem;
        set
        {
            if (SetProperty(ref selectedNodeInputRequirementItem, value))
            {
                RemoveSelectedNodeInputRequirementCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<string> TrafficTypeNameOptions =>
        network.TrafficTypes.Select(definition => definition.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(Comparer).OrderBy(name => name, Comparer).ToList();
    public IReadOnlyList<string> TrafficTypeOptions => TrafficTypeNameOptions;
    public string SelectedTrafficType
    {
        get => NodeTrafficTypeText;
        set => NodeTrafficTypeText = value;
    }
    public IReadOnlyList<string> SubnetworkIdSuggestions =>
        (network.Subnetworks ?? [])
            .Select(subnetwork => subnetwork.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(Comparer)
            .OrderBy(id => id, Comparer)
            .ToList();
    public IReadOnlyList<string> InterfaceNameSuggestions =>
        network.Nodes
            .Select(node => node.InterfaceName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(Comparer)
            .OrderBy(name => name, Comparer)
            .ToList();

    public string NodeTrafficTypeText
    {
        get => nodeTrafficTypeText;
        set
        {
            if (SetProperty(ref nodeTrafficTypeText, value))
            {
                Raise(nameof(SelectedTrafficType));
                RaiseNodeTrafficRoleValidationStateChanged();
                PreviewSelectedNodeSceneLayout();
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
                PreviewSelectedNodeSceneLayout();
            }
        }
    }
    public string NodeProductionText
    {
        get => nodeProductionText;
        set
        {
            if (SetProperty(ref nodeProductionText, value))
            {
                PreviewSelectedNodeSceneLayout();
            }
        }
    }
    public string NodeConsumptionText
    {
        get => nodeConsumptionText;
        set
        {
            if (SetProperty(ref nodeConsumptionText, value))
            {
                PreviewSelectedNodeSceneLayout();
            }
        }
    }
    public string NodeConsumerPremiumText
    {
        get => nodeConsumerPremiumText;
        set
        {
            if (SetProperty(ref nodeConsumerPremiumText, value))
            {
                PreviewSelectedNodeSceneLayout();
            }
        }
    }
    public string NodeProductionStartText
    {
        get => nodeProductionStartText;
        set
        {
            if (SetProperty(ref nodeProductionStartText, value))
            {
                PreviewSelectedNodeSceneLayout();
            }
        }
    }
    public string NodeProductionEndText
    {
        get => nodeProductionEndText;
        set
        {
            if (SetProperty(ref nodeProductionEndText, value))
            {
                PreviewSelectedNodeSceneLayout();
            }
        }
    }
    public string NodeConsumptionStartText
    {
        get => nodeConsumptionStartText;
        set
        {
            if (SetProperty(ref nodeConsumptionStartText, value))
            {
                PreviewSelectedNodeSceneLayout();
            }
        }
    }
    public string NodeConsumptionEndText
    {
        get => nodeConsumptionEndText;
        set
        {
            if (SetProperty(ref nodeConsumptionEndText, value))
            {
                PreviewSelectedNodeSceneLayout();
            }
        }
    }
    public bool NodeCanTransship
    {
        get => nodeCanTransship;
        set
        {
            if (SetProperty(ref nodeCanTransship, value))
            {
                PreviewSelectedNodeSceneLayout();
            }
        }
    }
    public bool NodeStoreEnabled
    {
        get => nodeStoreEnabled;
        set
        {
            if (SetProperty(ref nodeStoreEnabled, value))
            {
                Raise(nameof(IsNodeStoreCapacityEnabled));
                PreviewSelectedNodeSceneLayout();
            }
        }
    }
    public string NodeStoreCapacityText
    {
        get => nodeStoreCapacityText;
        set
        {
            if (SetProperty(ref nodeStoreCapacityText, value))
            {
                PreviewSelectedNodeSceneLayout();
            }
        }
    }
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
                RaiseTrafficTypeDisplayStateChanged();
            }
        }
    }

    public string TrafficNameText
    {
        get => trafficNameText;
        set
        {
            if (SetProperty(ref trafficNameText, value))
            {
                RaiseTrafficTypeDisplayStateChanged();
            }
        }
    }

    public string TrafficDescriptionText
    {
        get => trafficDescriptionText;
        set
        {
            if (SetProperty(ref trafficDescriptionText, value))
            {
                RaiseTrafficTypeDisplayStateChanged();
            }
        }
    }

    public RoutingPreference TrafficRoutingPreference
    {
        get => trafficRoutingPreference;
        set
        {
            if (SetProperty(ref trafficRoutingPreference, value))
            {
                RaiseTrafficTypeDisplayStateChanged();
            }
        }
    }

    public AllocationMode TrafficAllocationMode
    {
        get => trafficAllocationMode;
        set
        {
            if (SetProperty(ref trafficAllocationMode, value))
            {
                RaiseTrafficTypeDisplayStateChanged();
            }
        }
    }

    public RouteChoiceModel TrafficRouteChoiceModel
    {
        get => trafficRouteChoiceModel;
        set
        {
            if (SetProperty(ref trafficRouteChoiceModel, value))
            {
                RaiseTrafficTypeDisplayStateChanged();
            }
        }
    }

    public FlowSplitPolicy TrafficFlowSplitPolicy
    {
        get => trafficFlowSplitPolicy;
        set
        {
            if (SetProperty(ref trafficFlowSplitPolicy, value))
            {
                RaiseTrafficTypeDisplayStateChanged();
            }
        }
    }

    public string TrafficCapacityBidText
    {
        get => trafficCapacityBidText;
        set
        {
            if (SetProperty(ref trafficCapacityBidText, value))
            {
                RaiseTrafficTypeDisplayStateChanged();
            }
        }
    }

    public string TrafficPerishabilityText
    {
        get => trafficPerishabilityText;
        set
        {
            if (SetProperty(ref trafficPerishabilityText, value))
            {
                RaiseTrafficTypeDisplayStateChanged();
            }
        }
    }

    public string TrafficValidationText
    {
        get => trafficValidationText;
        private set
        {
            if (SetProperty(ref trafficValidationText, value))
            {
                RaiseTrafficTypeDisplayStateChanged();
            }
        }
    }

    private string BulkPlaceTypeText { get => BulkDraft.PlaceTypeText; set => BulkDraft.PlaceTypeText = value; }
    private string BulkTranshipmentCapacityText { get => BulkDraft.TranshipmentCapacityText; set => BulkDraft.TranshipmentCapacityText = value; }
    private string NodeIdText { get => NodeDraft.NodeIdText; set => NodeDraft.NodeIdText = value; }
    private string NodeNameText { get => NodeDraft.NodeNameText; set => NodeDraft.NodeNameText = value; }
    private string NodeXText { get => NodeDraft.NodeXText; set => NodeDraft.NodeXText = value; }
    private string NodeYText { get => NodeDraft.NodeYText; set => NodeDraft.NodeYText = value; }
    private string NodePlaceTypeText { get => NodeDraft.PlaceTypeText; set => NodeDraft.PlaceTypeText = value; }
    private string NodeDescriptionText { get => NodeDraft.DescriptionText; set => NodeDraft.DescriptionText = value; }
    private string NodeTranshipmentCapacityText { get => NodeDraft.TranshipmentCapacityText; set => NodeDraft.TranshipmentCapacityText = value; }
    private NodeVisualShape NodeShape { get => NodeDraft.Shape; set => NodeDraft.Shape = value; }
    private NodeKind NodeKind { get => NodeDraft.NodeKind; set => NodeDraft.NodeKind = value; }
    private string NodeReferencedSubnetworkIdText { get => NodeDraft.ReferencedSubnetworkIdText; set => NodeDraft.ReferencedSubnetworkIdText = value; }
    private bool NodeIsExternalInterface { get => NodeDraft.IsExternalInterface; set => NodeDraft.IsExternalInterface = value; }
    private string NodeInterfaceNameText { get => NodeDraft.InterfaceNameText; set => NodeDraft.InterfaceNameText = value; }
    private string NodeControllingActorText { get => NodeDraft.ControllingActorText; set => NodeDraft.ControllingActorText = value; }
    private string NodeTagsText { get => NodeDraft.TagsText; set => NodeDraft.TagsText = value; }
    private string NodeTemplateIdText { get => NodeDraft.TemplateIdText; set => NodeDraft.TemplateIdText = value; }
    private string EdgeRouteTypeText { get => EdgeDraft.RouteTypeText; set => EdgeDraft.RouteTypeText = value; }
    private string EdgeTimeText { get => EdgeDraft.TimeText; set => EdgeDraft.TimeText = value; }
    private string EdgeCostText { get => EdgeDraft.CostText; set => EdgeDraft.CostText = value; }
    private string EdgeCapacityText { get => EdgeDraft.CapacityText; set => EdgeDraft.CapacityText = value; }
    private bool EdgeIsBidirectional { get => EdgeDraft.IsBidirectional; set => EdgeDraft.IsBidirectional = value; }

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
            SelectionChanged = HandleGraphSelectionChanged,
            StatusChanged = text => StatusText = text,
            CanDragNode = CanDragNodeInGraph,
            GetNodeDragBlockedMessage = _ => "Map-locked nodes cannot be moved. Turn off Lock layout to map to edit positions."
        };
    }

    public VisualAnalyticsSnapshot CreateVisualAnalyticsSnapshot() => visualAnalyticsSnapshot ?? new VisualAnalyticsSnapshot
    {
        Network = network,
        TrafficOutcomes = lastOutcomes,
        ConsumerCosts = lastConsumerCosts,
        Period = CurrentPeriod
    };

    private void HandleGraphSelectionChanged(string? nodeId, string? edgeId)
    {
        if (!string.IsNullOrWhiteSpace(nodeId) &&
            Scene.Selection.SelectedNodeIds.Count == 1 &&
            Scene.Selection.SelectedEdgeIds.Count == 0)
        {
            FocusInspectorSection(InspectorTabTarget.Selection, InspectorSectionTarget.Node);
        }
        else if (!string.IsNullOrWhiteSpace(edgeId) &&
                 Scene.Selection.SelectedNodeIds.Count == 0 &&
                 Scene.Selection.SelectedEdgeIds.Count == 1)
        {
            FocusInspectorSection(InspectorTabTarget.Selection, InspectorSectionTarget.Route);
        }
        else if (Scene.Selection.SelectedNodeIds.Count + Scene.Selection.SelectedEdgeIds.Count > 1)
        {
            FocusInspectorSection(InspectorTabTarget.Selection, InspectorSectionTarget.Node);
        }
        else
        {
            FocusInspectorSection(InspectorTabTarget.Selection, InspectorSectionTarget.None);
        }

        if (ActiveToolMode == GraphToolMode.Agent && SelectedSimulationActor is not null)
        {
            AssignCurrentSelectionToActor();
        }

        RefreshInspector();
    }

    public SankeyDiagramModel BuildSankeyDiagram()
    {
        var snapshot = CreateVisualAnalyticsSnapshot();
        var filter = VisualisationState.ActiveTrafficTypeFilter;
        var showUnmetDemand = VisualisationState.ShowUnmetDemand;
        var collapseMinorFlows = VisualisationState.CollapseMinorFlows;

        if (cachedSankeyDiagram is not null
            && ReferenceEquals(cachedSankeySnapshot, snapshot)
            && string.Equals(cachedSankeyTrafficTypeFilter, filter, StringComparison.OrdinalIgnoreCase)
            && cachedSankeyShowUnmetDemand == showUnmetDemand
            && cachedSankeyCollapseMinorFlows == collapseMinorFlows)
        {
            return cachedSankeyDiagram;
        }

        cachedSankeyDiagram = sankeyProjectionService.Build(snapshot, new SankeyProjectionOptions
        {
            TrafficTypeFilter = filter,
            IncludeUnmetDemandSink = showUnmetDemand,
            CollapseMinorFlows = collapseMinorFlows
        });
        cachedSankeySnapshot = snapshot;
        cachedSankeyTrafficTypeFilter = filter;
        cachedSankeyShowUnmetDemand = showUnmetDemand;
        cachedSankeyCollapseMinorFlows = collapseMinorFlows;
        SankeyVersion++;
        return cachedSankeyDiagram;
    }

    public IEnumerable<FlowDataPoint> GetFlowSeries()
    {
        if (lastOutcomes.Count == 0 && lastTimelineStepResult is null)
        {
            return [];
        }

        if (lastTimelineStepResult is not null)
        {
            return lastTimelineStepResult.NodeStates
                .GroupBy(pair => pair.Key.TrafficType, StringComparer.OrdinalIgnoreCase)
                .Select(group => new FlowDataPoint(
                    group.Key,
                    group.Sum(pair => pair.Value.AvailableSupply + pair.Value.DemandBacklog),
                    lastTimelineStepResult.Allocations
                        .Where(allocation => string.Equals(allocation.TrafficType, group.Key, StringComparison.OrdinalIgnoreCase))
                        .Sum(allocation => allocation.Quantity),
                    group.Sum(pair => pair.Value.DemandBacklog),
                    group.Sum(pair => pair.Value.AvailableSupply)))
                .OrderBy(point => point.Label, Comparer)
                .ToList();
        }

        return lastOutcomes
            .OrderBy(outcome => outcome.TrafficType, Comparer)
            .Select(outcome => new FlowDataPoint(
                outcome.TrafficType,
                outcome.TotalConsumption,
                outcome.TotalDelivered,
                outcome.UnmetDemand,
                0d))
            .ToList();
    }

    public IEnumerable<NodePressurePoint> GetNodePressure()
    {
        if (lastTimelineStepResult is null)
        {
            return [];
        }

        return network.Nodes
            .Select(node =>
            {
                var pressure = lastTimelineStepResult.NodePressureById.GetValueOrDefault(node.Id);
                var backlog = lastTimelineStepResult.NodeStates
                    .Where(pair => Comparer.Equals(pair.Key.NodeId, node.Id) && pair.Value.DemandBacklog > 0d)
                    .OrderByDescending(pair => pair.Value.DemandBacklog)
                    .ThenBy(pair => pair.Key.TrafficType, Comparer)
                    .FirstOrDefault();
                var unmetNeed = backlog.Value.DemandBacklog > 0d
                    ? $"{ReportExportService.FormatNumber(backlog.Value.DemandBacklog)} {backlog.Key.TrafficType}"
                    : "None";
                return new NodePressurePoint(
                    ResolveNodeName(node.Id),
                    pressure.Score,
                    pressure.Score > 0d ? BuildTopCauseText(pressure.TopCause) : "None",
                    unmetNeed);
            })
            .OrderByDescending(point => point.Pressure)
            .ThenBy(point => point.Node, Comparer)
            .ToList();
    }

    public IReadOnlyDictionary<string, (double Latitude, double Longitude)> BuildGeoNodeLookup()
    {
        return network.Nodes
            .Where(node => node.Latitude.HasValue && node.Longitude.HasValue)
            .ToDictionary(node => node.Id, node => (node.Latitude!.Value, node.Longitude!.Value), StringComparer.OrdinalIgnoreCase);
    }

    public MapProjectionViewport BuildMapProjectionViewport(GraphSize viewportSize)
    {
        LastViewportSize = viewportSize;
        EnsureMapCamera(viewportSize);
        return new MapProjectionViewport(viewportSize.Width, viewportSize.Height, MapCamera.CenterLatitude, MapCamera.CenterLongitude, MapCamera.Zoom);
    }

    private bool CanDragNodeInGraph(string nodeId)
    {
        if (!IsMapLayoutLockedForGraph)
        {
            return true;
        }

        var node = network.Nodes.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, nodeId));
        return node is null || !node.Latitude.HasValue || !node.Longitude.HasValue;
    }

    private MapProjectionViewport BuildGraphProjectionViewport(GraphSize viewportSize)
    {
        var effectiveViewport = viewportSize.Width <= 0d || viewportSize.Height <= 0d ? new GraphSize(1440d, 860d) : viewportSize;
        var geo = BuildGeoNodeLookup();
        if (geo.Count == 0)
        {
            return new MapProjectionViewport(effectiveViewport.Width, effectiveViewport.Height, 0d, 0d, 0.0008d);
        }

        var fit = MapGraphRenderer.FitCameraToBoundingBox(
            geo.Values.Min(item => item.Latitude),
            geo.Values.Min(item => item.Longitude),
            geo.Values.Max(item => item.Latitude),
            geo.Values.Max(item => item.Longitude),
            effectiveViewport);
        return new MapProjectionViewport(effectiveViewport.Width, effectiveViewport.Height, fit.CenterLatitude, fit.CenterLongitude, fit.Zoom);
    }

    private bool TryProjectGeoNodeToGraph(NodeModel node, MapProjectionViewport projectionViewport, out GraphPoint point)
    {
        point = default;
        if (!node.Latitude.HasValue || !node.Longitude.HasValue)
        {
            return false;
        }

        var projected = mapProjectionService.Project(new MapGeoCoordinate(node.Latitude.Value, node.Longitude.Value), projectionViewport);
        point = new GraphPoint(projected.X, projected.Y);
        return true;
    }

    private bool TryBuildProjectedGeoBounds(GraphSize viewportSize, out GraphRect bounds)
    {
        bounds = default;
        var projectionViewport = BuildGraphProjectionViewport(viewportSize);
        var points = new List<GraphPoint>();
        foreach (var node in network.Nodes)
        {
            if (TryProjectGeoNodeToGraph(node, projectionViewport, out var projected))
            {
                points.Add(projected);
            }
        }

        if (points.Count == 0)
        {
            return false;
        }

        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        bounds = new GraphRect(minX, minY, Math.Max(1d, maxX - minX), Math.Max(1d, maxY - minY));
        return true;
    }

    public void BeginOsmSelection(MapGeoCoordinate coordinate)
    {
        if (!IsOsmAreaSelectionEnabled)
        {
            return;
        }

        osmSelectionStartCoordinate = coordinate;
        ApplyOsmSelectionFromCoordinates(coordinate, coordinate);
        OsmValidationMessage = "Drag to select an area.";
    }

    public void UpdateOsmSelection(MapGeoCoordinate coordinate)
    {
        if (!IsOsmAreaSelectionEnabled || osmSelectionStartCoordinate is null)
        {
            return;
        }

        ApplyOsmSelectionFromCoordinates(osmSelectionStartCoordinate.Value, coordinate);
    }

    public void EndOsmSelection(MapGeoCoordinate coordinate)
    {
        if (!IsOsmAreaSelectionEnabled || osmSelectionStartCoordinate is null)
        {
            return;
        }

        if (TryCreateBoundingBoxFromCoordinates(osmSelectionStartCoordinate.Value, coordinate, out var bbox, out var error, enforceMinimumSize: true))
        {
            SetOsmSelection(bbox, updateText: true);
        }
        else
        {
            OsmValidationMessage = error ?? "Selected area is invalid.";
        }

        osmSelectionStartCoordinate = null;
        RefreshOsmSelectionMetrics();
    }

    public void PanMap(double screenDeltaX, double screenDeltaY)
    {
        var viewport = BuildMapProjectionViewport(LastViewportSize);
        var projection = new MapWebMercatorProjectionService();
        var centerScreen = new GraphPoint(LastViewportSize.Width / 2d, LastViewportSize.Height / 2d);
        var next = projection.Unproject(centerScreen.X - screenDeltaX, centerScreen.Y - screenDeltaY, viewport);
        MapCamera = MapCamera with { CenterLatitude = next.Latitude, CenterLongitude = next.Longitude, IsLockedToNetworkBounds = false };
        hasUserMovedMapCamera = true;
        NotifyVisualChanged();
    }

    public void ZoomMapAt(GraphPoint anchorScreen, double factor)
    {
        var oldViewport = BuildMapProjectionViewport(LastViewportSize);
        var projection = new MapWebMercatorProjectionService();
        var before = projection.Unproject(anchorScreen.X, anchorScreen.Y, oldViewport);
        var newZoom = Math.Clamp(MapCamera.Zoom * factor, 0.0001d, 128d);
        var temp = new MapProjectionViewport(LastViewportSize.Width, LastViewportSize.Height, MapCamera.CenterLatitude, MapCamera.CenterLongitude, newZoom);
        var afterPoint = projection.Project(before, temp);
        var shifted = projection.Unproject((LastViewportSize.Width / 2d) + (afterPoint.X - anchorScreen.X), (LastViewportSize.Height / 2d) + (afterPoint.Y - anchorScreen.Y), temp);
        MapCamera = new MapCameraState(shifted.Latitude, shifted.Longitude, newZoom, false);
        hasUserMovedMapCamera = true;
        NotifyVisualChanged();
    }

    public MapSelectionOverlay? BuildMapSelectionOverlay()
    {
        if (osmSelection is null)
        {
            return null;
        }

        IReadOnlyList<OsmBoundingBox> tiles;
        try
        {
            tiles = OsmBoundingBoxTiler.CreateTiles(osmSelection);
        }
        catch
        {
            tiles = [];
        }

        return new MapSelectionOverlay(
            new MapGeoCoordinate(osmSelection.MinLat, osmSelection.MinLon),
            new MapGeoCoordinate(osmSelection.MaxLat, osmSelection.MaxLon),
            tiles.Select(tile => (new MapGeoCoordinate(tile.MinLat, tile.MinLon), new MapGeoCoordinate(tile.MaxLat, tile.MaxLon))).ToList(),
            $"{OsmSelectedAreaText}, {OsmTileCountText}");
    }

    public async Task ImportOsmSelectionAsync(CancellationToken ct = default)
    {
        if (!CanImportOsmSelection || osmSelection is null)
        {
            OsmValidationMessage = "Select an OSM area first.";
            StatusText = OsmValidationMessage;
            return;
        }

        if (OsmNodeImportPercentage is < 1 or > 100)
        {
            OsmValidationMessage = "Choose a value between 1% and 100%.";
            StatusText = OsmValidationMessage;
            return;
        }

        try
        {
            IsOsmDownloadInProgress = true;
            ImportOsmSelectionCommand.NotifyCanExecuteChanged();
            var tiles = OsmBoundingBoxTiler.CreateTiles(osmSelection);
            StatusText = tiles.Count > 1 ? $"Downloading OSM data in {tiles.Count} tiles." : "Downloading OSM data.";
            osmImportOptions = osmImportOptions with { NodeRetentionPercentage = OsmNodeImportPercentage };
            var imported = await osmBoundingBoxImporter.ImportAsync(
                osmSelection,
                osmImportOptions,
                ct);
            LoadNetwork(imported, $"Imported {imported.Nodes.Count} nodes and {imported.Edges.Count} edges");
            if (HasGeoAnchoredNodes)
            {
                network.LockLayoutToMap = true;
                Raise(nameof(LockLayoutToMap));
                Raise(nameof(IsMapLayoutLockedForGraph));
            }

            VisualisationState.ActiveMode = VisualisationMode.Map;
            ActiveView = AppView.Map;
            FitMapToNetwork();
            IsOsmAreaSelectionEnabled = false;
            CurrentWorkspaceMode = WorkspaceMode.Normal;
        }
        catch (OsmImportException ex)
        {
            OsmValidationMessage = ex.Message;
            StatusText = ex.Message;
        }
        finally
        {
            IsOsmDownloadInProgress = false;
            ImportOsmSelectionCommand.NotifyCanExecuteChanged();
        }
    }

    public void ImportSelectedOsmArea() => ImportOsmSelectionCommand.Execute(null);

    public void ClearOsmSelection()
    {
        osmSelection = null;
        osmSelectionStartCoordinate = null;
        OsmWestText = string.Empty;
        OsmSouthText = string.Empty;
        OsmEastText = string.Empty;
        OsmNorthText = string.Empty;
        OsmValidationMessage = "Drag to select an area.";
        Raise(nameof(OsmSelection));
        RefreshOsmSelectionMetrics();
        NotifyVisualChanged();
    }

    public void EnterOsmImportWorkspace()
    {
        CurrentWorkspaceMode = WorkspaceMode.OsmImport;
        ActiveView = AppView.OSMImport;
        VisualisationState.ActiveMode = VisualisationMode.Map;
        IsOsmAreaSelectionEnabled = true;
        ToolStatusText = "Pan and zoom the map, then drag to select an area.";
        if (BuildGeoNodeLookup().Count > 0)
        {
            FitMapToNetwork();
            StatusText = "OSM import mode ready. Drag to select an area.";
            return;
        }

        MapCamera = new MapCameraState(51.5074d, -0.1278d, 0.0015d, false);
        hasUserMovedMapCamera = false;
        NotifyVisualChanged();
        StatusText = "OSM import mode ready. Map centered on London by default.";
    }

    private void EnterOsmImportView()
    {
        CurrentWorkspaceMode = WorkspaceMode.Normal;
        ActiveView = AppView.OSMImport;
        IsOsmAreaSelectionEnabled = true;
        ToolStatusText = "Pan and zoom the map, then drag to select an area.";
        if (BuildGeoNodeLookup().Count > 0)
        {
            FitMapToNetwork();
            StatusText = "OSM import view ready. Drag to select an area.";
            return;
        }

        MapCamera = new MapCameraState(51.5074d, -0.1278d, 0.0015d, false);
        hasUserMovedMapCamera = false;
        NotifyVisualChanged();
        StatusText = "OSM import view ready. Map centered on London by default.";
    }

    public void CancelOsmImport()
    {
        IsOsmAreaSelectionEnabled = false;
        CurrentWorkspaceMode = WorkspaceMode.Normal;
        ToolStatusText = "OSM import canceled.";
    }

    public void FitMapToNetwork()
    {
        var geo = BuildGeoNodeLookup();
        if (geo.Count == 0)
        {
            StatusText = "This network has no geographic coordinates yet.";
            return;
        }

        MapCamera = MapGraphRenderer.FitCameraToBoundingBox(
            geo.Values.Min(item => item.Latitude),
            geo.Values.Min(item => item.Longitude),
            geo.Values.Max(item => item.Latitude),
            geo.Values.Max(item => item.Longitude),
            LastViewportSize.Width <= 0d ? new GraphSize(1440d, 860d) : LastViewportSize);
        hasUserMovedMapCamera = false;
        NotifyVisualChanged();
        StatusText = "Fit map to network.";
    }

    private void FitActiveView()
    {
        var viewportSize = LastViewportSize.Width <= 0d ? new GraphSize(1440d, 860d) : LastViewportSize;
        if (VisualisationState.ActiveMode == VisualisationMode.Map)
        {
            FitMapToNetwork();
            return;
        }

        if (VisualisationState.ActiveMode == VisualisationMode.Graph && IsMapLayoutLockedForGraph && TryBuildProjectedGeoBounds(viewportSize, out var projectedBounds))
        {
            Viewport.Reset(projectedBounds, viewportSize);
            NotifyVisualChanged();
            StatusText = "Fit graph to projected map layout.";
            return;
        }

        Viewport.Reset(Scene.GetContentBounds(), viewportSize);
        NotifyVisualChanged();
        StatusText = "Fit the graph to the current view.";
    }

    private void EnsureMapCamera(GraphSize viewportSize)
    {
        if (hasUserMovedMapCamera && !MapCamera.IsLockedToNetworkBounds)
        {
            return;
        }

        var geo = BuildGeoNodeLookup();
        if (geo.Count == 0)
        {
            return;
        }

        MapCamera = MapGraphRenderer.FitCameraToBoundingBox(
            geo.Values.Min(item => item.Latitude),
            geo.Values.Min(item => item.Longitude),
            geo.Values.Max(item => item.Latitude),
            geo.Values.Max(item => item.Longitude),
            viewportSize);
    }

    private void ApplyOsmSelectionFromCoordinates(MapGeoCoordinate a, MapGeoCoordinate b)
    {
        if (TryCreateBoundingBoxFromCoordinates(a, b, out var bbox, out var error))
        {
            SetOsmSelection(bbox, updateText: true);
            return;
        }

        OsmValidationMessage = error ?? "Selected area is invalid.";
    }

    private void SetOsmSelection(OsmBoundingBox bbox, bool updateText)
    {
        osmSelection = bbox.Normalize();
        if (updateText)
        {
            osmWestText = osmSelection.MinLon.ToString("0.######", CultureInfo.InvariantCulture);
            osmSouthText = osmSelection.MinLat.ToString("0.######", CultureInfo.InvariantCulture);
            osmEastText = osmSelection.MaxLon.ToString("0.######", CultureInfo.InvariantCulture);
            osmNorthText = osmSelection.MaxLat.ToString("0.######", CultureInfo.InvariantCulture);
            Raise(nameof(OsmWestText));
            Raise(nameof(OsmSouthText));
            Raise(nameof(OsmEastText));
            Raise(nameof(OsmNorthText));
        }

        Raise(nameof(OsmSelection));
        RefreshOsmSelectionMetrics();
        NotifyVisualChanged();
    }

    private void SetOsmCoordinateText(ref string field, string value, string propertyName)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return;
        }

        TryUpdateOsmSelectionFromText();
    }

    private void TryUpdateOsmSelectionFromText()
    {
        if (!double.TryParse(osmWestText, NumberStyles.Float, CultureInfo.InvariantCulture, out var west) ||
            !double.TryParse(osmSouthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var south) ||
            !double.TryParse(osmEastText, NumberStyles.Float, CultureInfo.InvariantCulture, out var east) ||
            !double.TryParse(osmNorthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var north))
        {
            OsmValidationMessage = "Enter west, south, east, and north coordinates.";
            return;
        }

        if (OsmBoundingBox.TryCreate(west, south, east, north, out var bbox, out var error))
        {
            SetOsmSelection(bbox, updateText: false);
        }
        else
        {
            OsmValidationMessage = error ?? "Selected area is invalid.";
        }
    }

    private void RefreshOsmSelectionMetrics()
    {
        Raise(nameof(OsmSelectedAreaText));
        Raise(nameof(OsmTileCountText));
        Raise(nameof(CanImportOsmSelection));
        ImportOsmSelectionCommand.NotifyCanExecuteChanged();
        if (osmSelection is null)
        {
            OsmValidationMessage = "No selection yet. Pan and zoom the map, then drag to select an area.";
            return;
        }

        OsmValidationMessage = osmSelection.AreaDegrees > OsmBoundingBoxTiler.AutoTileAreaLimitDegrees
            ? "Selected area is too large. Zoom in or select a smaller area."
            : osmSelection.AreaDegrees > OsmBoundingBoxTiler.MaxTileAreaDegrees
                ? "Selection will be downloaded in tiles."
                : osmSelection.AreaDegrees > 1d || OsmNodeImportPercentage > 50
                    ? "This may create a large network. Reduce area or node percentage."
                    : "Selected area ready. Choose Import selected area.";
    }

    public static bool TryCreateBoundingBoxFromCoordinates(MapGeoCoordinate start, MapGeoCoordinate end, out OsmBoundingBox bbox, out string? error, bool enforceMinimumSize = false)
    {
        var west = Math.Clamp(Math.Min(start.Longitude, end.Longitude), -180d, 180d);
        var east = Math.Clamp(Math.Max(start.Longitude, end.Longitude), -180d, 180d);
        var south = Math.Clamp(Math.Min(start.Latitude, end.Latitude), OsmBoundingBox.MinLatitudeLimit, OsmBoundingBox.MaxLatitudeLimit);
        var north = Math.Clamp(Math.Max(start.Latitude, end.Latitude), OsmBoundingBox.MinLatitudeLimit, OsmBoundingBox.MaxLatitudeLimit);

        if (enforceMinimumSize)
        {
            const double minimumSpanDegrees = 0.00001d;
            if ((east - west) < minimumSpanDegrees || (north - south) < minimumSpanDegrees)
            {
                bbox = default!;
                error = "Selected area is too small. Drag a larger box.";
                return false;
            }
        }

        return OsmBoundingBox.TryCreate(west, south, east, north, out bbox, out error);
    }

    private bool IsOsmSelectionValid()
    {
        if (osmSelection is null)
        {
            return false;
        }

        try
        {
            _ = OsmBoundingBoxTiler.CreateTiles(osmSelection);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void SelectInsight(NetworkInsight insight)
    {
        ArgumentNullException.ThrowIfNull(insight);
        highlightedNodeIds.Clear();
        highlightedEdgeIds.Clear();
        if (!string.IsNullOrWhiteSpace(insight.TargetNodeId))
        {
            highlightedNodeIds.Add(insight.TargetNodeId);
            SelectNode(insight.TargetNodeId);
            Raise(nameof(HighlightedNodeIds));
            Raise(nameof(HighlightedEdgeIds));
            return;
        }

        if (!string.IsNullOrWhiteSpace(insight.TargetEdgeId))
        {
            highlightedEdgeIds.Add(insight.TargetEdgeId);
            SelectEdge(insight.TargetEdgeId);
            Raise(nameof(HighlightedNodeIds));
            Raise(nameof(HighlightedEdgeIds));
        }
    }

    public void HighlightRouteEdges(IEnumerable<string> edgeIds)
    {
        highlightedEdgeIds.Clear();
        foreach (var edgeId in edgeIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            highlightedEdgeIds.Add(edgeId);
        }

        Raise(nameof(HighlightedEdgeIds));
    }

    private void HandleVisualisationStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VisualisationState.ActiveMode))
        {
            UpdateActiveModeState();
            return;
        }

        if (e.PropertyName is nameof(VisualisationState.ActiveTrafficTypeFilter)
            or nameof(VisualisationState.ShowUnmetDemand)
            or nameof(VisualisationState.CollapseMinorFlows))
        {
            InvalidateSankeyCache();
        }
    }

    private void UpdateActiveModeState()
    {
        ActiveModeLabel = VisualisationState.ActiveMode switch
        {
            VisualisationMode.Sankey => "Sankey view",
            VisualisationMode.Map => "Map view",
            _ => "Graph view"
        };
        Raise(nameof(IsGraphMode));
        Raise(nameof(IsSankeyMode));
        Raise(nameof(IsMapMode));
        Raise(nameof(ToolStatusText));
        if (VisualisationState.ActiveMode == VisualisationMode.Graph && IsMapLayoutLockedForGraph)
        {
            BuildSceneFromNetwork();
            FitActiveView();
            return;
        }

        NotifyVisualChanged();
    }

    private void InvalidateSankeyCache()
    {
        cachedSankeyDiagram = null;
        cachedSankeySnapshot = null;
        cachedSankeyTrafficTypeFilter = null;
        cachedSankeyShowUnmetDemand = false;
        cachedSankeyCollapseMinorFlows = false;
        SankeyVersion++;
        Raise(nameof(CurrentSankey));
        Raise(nameof(FlowSeries));
        Raise(nameof(NodePressureSeries));
        NotifyVisualChanged();
    }


    private void HandleNodeDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isRefreshingInspectorDrafts)
        {
            return;
        }

        if (e.PropertyName is nameof(NodeInspectorDraft.NodeIdText)
            or nameof(NodeInspectorDraft.NodeNameText)
            or nameof(NodeInspectorDraft.NodeXText)
            or nameof(NodeInspectorDraft.NodeYText)
            or nameof(NodeInspectorDraft.PlaceTypeText)
            or nameof(NodeInspectorDraft.DescriptionText)
            or nameof(NodeInspectorDraft.TranshipmentCapacityText)
            or nameof(NodeInspectorDraft.Shape)
            or nameof(NodeInspectorDraft.NodeKind)
            or nameof(NodeInspectorDraft.ReferencedSubnetworkIdText)
            or nameof(NodeInspectorDraft.IsExternalInterface)
            or nameof(NodeInspectorDraft.InterfaceNameText)
            or nameof(NodeInspectorDraft.ControllingActorText)
            or nameof(NodeInspectorDraft.TagsText)
            or nameof(NodeInspectorDraft.TemplateIdText))
        {
            PreviewSelectedNodeSceneLayout();
        }
    }

    private void HandleEdgeDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isRefreshingInspectorDrafts)
        {
            return;
        }

        if (e.PropertyName is nameof(EdgeInspectorDraft.RouteTypeText)
            or nameof(EdgeInspectorDraft.TimeText)
            or nameof(EdgeInspectorDraft.CostText)
            or nameof(EdgeInspectorDraft.CapacityText)
            or nameof(EdgeInspectorDraft.IsBidirectional))
        {
            RefreshEdgeEditorState();
        }
    }

    public void TickAnimation(double elapsedSeconds)
    {
        Scene.Simulation.AnimationTime += ReducedMotion ? elapsedSeconds * 0.2d : elapsedSeconds;
        if (PulseProgress <= 0d)
        {
            return;
        }

        var decayPerSecond = 1d / 0.7d;
        PulseProgress = Math.Max(0d, PulseProgress - (elapsedSeconds * decayPerSecond));
        Scene.Selection.PulseProgress = PulseProgress;
        if (PulseProgress <= 0d)
        {
            PulseNodeId = null;
            PulseEdgeId = null;
            Scene.Selection.PulseNodeId = null;
            Scene.Selection.PulseEdgeId = null;
            Scene.Selection.PulseProgress = 0d;
        }
    }

    public void NotifyVisualChanged()
    {
        if (SyncNetworkNodePositionsFromScene())
        {
            MarkDirty();
        }
        Scene.Selection.HighlightedNodeIds.Clear();
        foreach (var nodeId in highlightedNodeIds)
        {
            Scene.Selection.HighlightedNodeIds.Add(nodeId);
        }

        Scene.Selection.HighlightedEdgeIds.Clear();
        foreach (var edgeId in highlightedEdgeIds)
        {
            Scene.Selection.HighlightedEdgeIds.Add(edgeId);
        }

        ViewportVersion++;
        Raise(nameof(ViewportVersion));
        Raise(nameof(WindowTitle));
        Raise(nameof(SessionSubtitle));
        Raise(nameof(SelectionSummary));
        Raise(nameof(SelectedNodeIdText));
        Raise(nameof(SelectedNodeRoleSummaryText));
        Raise(nameof(SimulationSummary));
        Raise(nameof(HasAnyNodes));
        Raise(nameof(CurrentSankey));
        Raise(nameof(FlowSeries));
        Raise(nameof(NodePressureSeries));
        RaiseAutoCompleteOptionsChanged();
    }

    private void MarkDirty()
    {
        HasUnsavedChanges = true;
        networkRevision++;
        cachedIsochroneNetworkRevision = -1;
    }

    private string BuildSuggestedFileName()
    {
        var baseName = string.IsNullOrWhiteSpace(network.Name) ? "Untitled Network" : network.Name.Trim();
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(baseName.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Untitled Network";
        }

        return sanitized.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? sanitized : $"{sanitized}.json";
    }

    public void OpenNetwork(string path)
    {
        LoadNetwork(fileService.Load(path), $"Opened '{Path.GetFileName(path)}'.", path);
    }

    public void SaveNetwork(string path)
    {
        CommitTransientEditorsToModel();
        fileService.Save(network, path);
        CurrentFilePath = path;
        HasUnsavedChanges = false;
        StatusText = $"Saved '{Path.GetFileName(path)}'.";
        Raise(nameof(WindowTitle));
        Raise(nameof(SessionSubtitle));
    }

    public void ImportGraphMl(string path)
    {
        LoadNetwork(graphMlFileService.Load(path, new GraphMlTransferOptions(default, "transship", 25d)), $"Imported '{Path.GetFileName(path)}'.", currentFilePath: null);
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

    public void ExportAgentLogsJson(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var options = new JsonSerializerOptions { WriteIndented = true };
        var entries = agentActionLogger.GetAll();
        File.WriteAllText(path, JsonSerializer.Serialize(entries, options));
        StatusText = $"Exported {entries.Count} agent action logs to '{Path.GetFileName(path)}'.";
    }

    public void SetActiveTool(GraphToolMode toolMode)
    {
        ActiveToolMode = toolMode;
        ToolStatusText = toolMode switch
        {
            GraphToolMode.AddNode => "Add node mode: click the canvas to place a node.",
            GraphToolMode.Connect => "Connect mode: choose a source node, then a target node.",
            GraphToolMode.Agent => "Agent mode: select graph elements for assignment and actor inspection.",
            _ => "Select mode: select, drag, or marquee."
        };
        ToolInstructionText = toolMode switch
        {
            GraphToolMode.AddNode => "Keyboard: A keeps Add node active. Esc returns to Select.",
            GraphToolMode.Connect => "Keyboard: C keeps Connect active. Esc returns to Select.",
            GraphToolMode.Agent => "Keyboard: G enters Agent mode. Use Assign commands to map selection to the active actor.",
            _ => "Keyboard: S Select, A Add node, C Connect."
        };
        Raise(nameof(IsSelectToolActive));
        Raise(nameof(IsAddNodeToolActive));
        Raise(nameof(IsConnectToolActive));
        Raise(nameof(IsAgentToolActive));
        Raise(nameof(IsIsochroneModeEnabled));
        Raise(nameof(IsFacilityPlanningMode));
        RunMultiOriginIsochroneCommand.NotifyCanExecuteChanged();
    }

    public void SetIsochroneMode(bool enabled)
    {
        if (isIsochroneModeEnabled == enabled)
        {
            return;
        }

        isIsochroneModeEnabled = enabled;
        if (!enabled)
        {
            ClearIsochroneState();
            BuildSceneFromNetwork();
            NotifyVisualChanged();
            StatusText = "Isochrone mode disabled.";
        }
        else
        {
            if (isFacilityPlanningMode)
            {
                SetFacilityPlanningMode(false);
            }
            StatusText = "Isochrone mode enabled. Click a node and enter a threshold.";
        }

        Raise(nameof(IsIsochroneModeEnabled));
        Raise(nameof(IsochroneLegendTitle));
    }

    public void SetFacilityPlanningMode(bool enabled)
    {
        if (isFacilityPlanningMode == enabled)
        {
            return;
        }

        isFacilityPlanningMode = enabled;
        if (enabled)
        {
            SetIsochroneMode(false);
            StatusText = "Facility planning mode enabled. Click nodes to toggle facilities.";
            RefreshFacilityCoverageIfActive(updateStatusText: false);
        }
        else
        {
            CurrentMultiOriginIsochrone = null;
            FacilityPlanningValidationText = string.Empty;
            facilityCoverageByNodeId = new Dictionary<string, List<FacilityCoverageInfo>>(Comparer);
            BuildSceneFromNetwork();
            NotifyVisualChanged();
            StatusText = "Facility planning mode disabled.";
        }

        Raise(nameof(IsFacilityPlanningMode));
    }

    public bool IsFacilityOriginSelected(string nodeId) =>
        !string.IsNullOrWhiteSpace(nodeId) &&
        SelectedFacilityNodes.Any(candidate => Comparer.Equals(candidate.Node.Id, nodeId));

    public string GetFacilityNodeDisplayName(string nodeId)
    {
        var node = network.Nodes.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, nodeId));
        return node is null || string.IsNullOrWhiteSpace(node.Name) ? nodeId : node.Name;
    }

    public bool ToggleFacilityOriginById(string nodeId, double? maxTravelTime = null)
    {
        if (!IsFacilityPlanningMode || string.IsNullOrWhiteSpace(nodeId))
        {
            return false;
        }

        var node = network.Nodes.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, nodeId));
        if (node is null)
        {
            return false;
        }

        var existing = SelectedFacilityNodes.FirstOrDefault(candidate => Comparer.Equals(candidate.Node.Id, nodeId));
        if (existing is null)
        {
            SelectedFacilityNodes.Add(new FacilityOriginItem(node, maxTravelTime ?? IsochroneBudget));
            StatusText = $"Added facility '{(string.IsNullOrWhiteSpace(node.Name) ? node.Id : node.Name)}'.";
        }
        else
        {
            SelectedFacilityNodes.Remove(existing);
            if (ReferenceEquals(SelectedFacilityNodeItem, existing))
            {
                SelectedFacilityNodeItem = null;
            }

            StatusText = $"Removed facility '{(string.IsNullOrWhiteSpace(node.Name) ? node.Id : node.Name)}'.";
        }

        RefreshFacilityCoverageIfActive(updateStatusText: false);
        return true;
    }

    public void RunMultiOriginIsochrone()
    {
        RefreshFacilityCoverageIfActive(updateStatusText: true);
    }

    public void ClearFacilityOrigins()
    {
        SelectedFacilityNodes.Clear();
        SelectedFacilityNodeItem = null;
        UncoveredPlanningItems.Clear();
        FacilityComparisonRows.Clear();
        CurrentMultiOriginIsochrone = null;
        FacilityPlanningValidationText = string.Empty;
        cachedFacilityDistances.Clear();
        facilityCoverageByNodeId = new Dictionary<string, List<FacilityCoverageInfo>>(Comparer);
        ApplyIsochroneVisuals();
        NotifyVisualChanged();
        StatusText = "Cleared selected facilities.";
    }

    public void RemoveSelectedFacilityOrigin()
    {
        if (SelectedFacilityNodeItem is null)
        {
            return;
        }

        SelectedFacilityNodes.Remove(SelectedFacilityNodeItem);
        SelectedFacilityNodeItem = null;
        if (SelectedFacilityNodes.Count == 0)
        {
            CurrentMultiOriginIsochrone = null;
            UncoveredPlanningItems.Clear();
            FacilityComparisonRows.Clear();
        }

        RefreshFacilityCoverageIfActive(updateStatusText: false);
    }

    private void HandleFacilityOriginChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not FacilityOriginItem facility)
        {
            return;
        }

        if (e.PropertyName is nameof(FacilityOriginItem.MaxTravelTimeText))
        {
            cachedFacilityDistances.Remove(facility.Node.Id);
            RefreshFacilityCoverageIfActive(updateStatusText: false);
        }
    }

    private void AddSelectedNodeAsFacilityOrigin()
    {
        var nodeId = Scene.Selection.SelectedNodeIds.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            ToggleFacilityOriginById(nodeId);
        }
    }

    private void RefreshFacilityCoverageIfActive(bool updateStatusText)
    {
        if (!IsFacilityPlanningMode)
        {
            return;
        }

        if (SelectedFacilityNodes.Count == 0)
        {
            FacilityPlanningValidationText = "Select at least one facility before running analysis.";
            CurrentMultiOriginIsochrone = null;
            facilityCoverageByNodeId = new Dictionary<string, List<FacilityCoverageInfo>>(Comparer);
            UncoveredPlanningItems.Clear();
            FacilityComparisonRows.Clear();
            ApplyIsochroneVisuals();
            NotifyVisualChanged();
            return;
        }

        if (!TryRebuildFacilityCoverageState())
        {
            return;
        }

        ApplyIsochroneVisuals();
        NotifyVisualChanged();
        if (updateStatusText)
        {
            StatusText = $"Facility planning analysis ran for {SelectedFacilityNodes.Count} facilities.";
        }
    }

    private bool TryRebuildFacilityCoverageState()
    {
        var nodesById = network.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, node => node, Comparer);

        var refreshedDistances = new Dictionary<string, Dictionary<string, double>>(Comparer);
        foreach (var facility in SelectedFacilityNodes)
        {
            if (!facility.TryGetMaxTravelTime(out var maxTravelTime))
            {
                FacilityPlanningValidationText = $"Max time for '{facility.DisplayName}' must be 0 or higher.";
                return false;
            }

            refreshedDistances[facility.Node.Id] = ComputeReachableNodeDistances(facility.Node.Id, maxTravelTime);
        }

        cachedFacilityDistances.Clear();
        foreach (var pair in refreshedDistances)
        {
            cachedFacilityDistances[pair.Key] = pair.Value;
        }

        var coverage = new Dictionary<string, List<FacilityCoverageInfo>>(Comparer);
        foreach (var facility in SelectedFacilityNodes)
        {
            if (!cachedFacilityDistances.TryGetValue(facility.Node.Id, out var map))
            {
                continue;
            }

            foreach (var (coveredNodeId, travelTime) in map)
            {
                if (!coverage.TryGetValue(coveredNodeId, out var list))
                {
                    list = [];
                    coverage[coveredNodeId] = list;
                }

                list.Add(new FacilityCoverageInfo
                {
                    FacilityNodeId = facility.Node.Id,
                    FacilityDisplayName = facility.DisplayName,
                    TravelTime = travelTime,
                    IsPrimaryFacility = false
                });
            }
        }

        foreach (var (nodeId, list) in coverage)
        {
            var primaryId = list
                .OrderBy(candidate => candidate.TravelTime)
                .ThenBy(candidate => candidate.FacilityDisplayName, Comparer)
                .Select(candidate => candidate.FacilityNodeId)
                .FirstOrDefault();
            for (var index = 0; index < list.Count; index++)
            {
                var candidate = list[index];
                list[index] = new FacilityCoverageInfo
                {
                    FacilityNodeId = candidate.FacilityNodeId,
                    FacilityDisplayName = candidate.FacilityDisplayName,
                    TravelTime = candidate.TravelTime,
                    IsPrimaryFacility = Comparer.Equals(candidate.FacilityNodeId, primaryId)
                };
            }
        }

        facilityCoverageByNodeId = coverage;
        FacilityPlanningValidationText = string.Empty;
        BuildFacilityPlanningResultSnapshot(nodesById, coverage);
        BuildUncoveredPlanningItems();
        BuildFacilityComparisonRows();
        return true;
    }

    private void BuildFacilityPlanningResultSnapshot(
        IReadOnlyDictionary<string, NodeModel> nodesById,
        IReadOnlyDictionary<string, List<FacilityCoverageInfo>> coverage)
    {
        var bestCostByNode = new Dictionary<NodeModel, double>();
        var bestOriginByNode = new Dictionary<NodeModel, NodeModel>();
        var coveringOriginsByNode = new Dictionary<NodeModel, IReadOnlyList<NodeModel>>();

        foreach (var (nodeId, facilityCoverage) in coverage)
        {
            if (!nodesById.TryGetValue(nodeId, out var nodeModel))
            {
                continue;
            }

            var sorted = facilityCoverage
                .OrderBy(candidate => candidate.TravelTime)
                .ThenBy(candidate => candidate.FacilityDisplayName, Comparer)
                .ToList();
            if (sorted.Count == 0)
            {
                continue;
            }

            bestCostByNode[nodeModel] = sorted[0].TravelTime;
            var coveringNodes = SelectedFacilityNodes
                .Where(candidate => sorted.Any(coverageItem => Comparer.Equals(coverageItem.FacilityNodeId, candidate.Node.Id)))
                .Select(candidate => candidate.Node)
                .ToList();
            if (coveringNodes.Count > 0)
            {
                coveringOriginsByNode[nodeModel] = coveringNodes;
                bestOriginByNode[nodeModel] = coveringNodes.FirstOrDefault(origin => Comparer.Equals(origin.Id, sorted[0].FacilityNodeId)) ?? coveringNodes[0];
            }
        }

        var reachableNodes = bestCostByNode.Keys.ToList();
        var uncoveredNodes = network.Nodes
            .Where(node => string.IsNullOrWhiteSpace(node.Id) || !coverage.ContainsKey(node.Id))
            .ToList();
        var overlapNodes = network.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id) && coverage.TryGetValue(node.Id, out var list) && list.Count > 1)
            .ToList();

        CurrentMultiOriginIsochrone = new MultiOriginIsochroneResult
        {
            BestCostByNode = bestCostByNode,
            BestOriginByNode = bestOriginByNode,
            CoveringOriginsByNode = coveringOriginsByNode,
            ReachableNodes = reachableNodes,
            UncoveredNodes = uncoveredNodes,
            OverlapNodes = overlapNodes
        };
    }

    public bool ComputeIsochrone(string originNodeId, double thresholdMinutes)
    {
        if (!IsIsochroneModeEnabled || string.IsNullOrWhiteSpace(originNodeId))
        {
            return false;
        }

        var origin = network.Nodes.FirstOrDefault(node => Comparer.Equals(node.Id, originNodeId));
        if (origin is null)
        {
            return false;
        }

        var sanitizedThreshold = Math.Max(0d, thresholdMinutes);
        IsochroneThresholdMinutes = sanitizedThreshold;
        if (cachedIsochroneNetworkRevision == networkRevision &&
            Comparer.Equals(cachedIsochroneOriginId, originNodeId) &&
            cachedIsochroneThreshold.HasValue &&
            Math.Abs(cachedIsochroneThreshold.Value - sanitizedThreshold) < 0.0001d)
        {
            ApplyIsochroneVisuals();
            NotifyVisualChanged();
            return true;
        }

        isochroneDistances = ComputeReachableNodeDistances(originNodeId, sanitizedThreshold);
        var reachableIds = isochroneDistances.Keys.ToHashSet(Comparer);
        IsochroneNodes = network.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id) && reachableIds.Contains(node.Id))
            .ToHashSet();
        cachedIsochroneOriginId = originNodeId;
        cachedIsochroneThreshold = sanitizedThreshold;
        cachedIsochroneNetworkRevision = networkRevision;
        Raise(nameof(IsochroneNodes));
        Raise(nameof(IsochroneLegendTitle));

        ApplyIsochroneVisuals();
        NotifyVisualChanged();
        StatusText = $"Isochrone computed from '{origin.Name}' within {sanitizedThreshold:0.##} minutes.";
        return true;
    }

    private Dictionary<string, double> ComputeReachableNodeDistances(string originNodeId, double maxTravelTime)
    {
        if (string.IsNullOrWhiteSpace(originNodeId))
        {
            return new Dictionary<string, double>(Comparer);
        }

        var origin = network.Nodes.FirstOrDefault(node => Comparer.Equals(node.Id, originNodeId));
        if (origin is null)
        {
            return new Dictionary<string, double>(Comparer);
        }

        _ = isochroneService.ComputeIsochrone(
            origin,
            Math.Max(0d, maxTravelTime),
            network.Nodes,
            network.Edges,
            IsochroneService.CostMetric.Time,
            out var distances);
        return distances;
    }

    private void ClearIsochroneState()
    {
        IsochroneNodes = [];
        isochroneDistances = new Dictionary<string, double>(Comparer);
        cachedIsochroneOriginId = null;
        cachedIsochroneThreshold = null;
        cachedIsochroneNetworkRevision = -1;
        Raise(nameof(IsochroneNodes));
        Raise(nameof(IsochroneLegendTitle));
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

    private void LoadNetwork(NetworkModel source, string status, string? currentFilePath = null)
    {
        network = fileService.NormalizeAndValidate(source);
        EnsureNetworkReferences(network);
        preAgentMutationNetwork = null;
        temporalState = null;
        lastTimelineStepResult = null;
        Raise(nameof(TrafficDeliveredColumnLabel));
        lastOutcomes = [];
        lastConsumerCosts = [];
        visualAnalyticsSnapshot = null;
        NetworkInsights.Clear();
        CurrentPeriod = 0;
        TimelineMaximum = Math.Max(8, network.TimelineLoopLength ?? 12);
        TimelinePosition = 0;
        pendingTrafficRemovalName = string.Empty;
        BuildSceneFromNetwork();
        RefreshLayerItems();
        RefreshScenarioItems();
        ScenarioEditor.AttachNetwork(network);
        Viewport.Reset(Scene.GetContentBounds(), LastViewportSize);
        PopulateTrafficDefinitionList();
        PopulateDefaultPermissionRows();
        RaiseTrafficTypeOptionsChanged();
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
        ClearIsochroneState();
        SelectedFacilityNodes.Clear();
        SelectedFacilityNodeItem = null;
        CurrentMultiOriginIsochrone = null;
        FacilityPlanningValidationText = string.Empty;
        UncoveredPlanningItems.Clear();
        FacilityComparisonRows.Clear();
        cachedFacilityDistances.Clear();
        facilityCoverageByNodeId = new Dictionary<string, List<FacilityCoverageInfo>>(Comparer);
        ClearDynamicReports();
        CurrentFilePath = currentFilePath;
        HasUnsavedChanges = false;
        Raise(nameof(HasGeoAnchoredNodes));
        Raise(nameof(IsLockLayoutToMapEnabled));
        Raise(nameof(LockLayoutToMapDisabledReason));
        Raise(nameof(LockLayoutToMap));
        Raise(nameof(IsMapLayoutLockedForGraph));
        Raise(nameof(HasAnyNodes));
        SimulationActors.Clear();
        foreach (var actor in network.Actors)
        {
            EnsureActorCapability(actor);
            SimulationActors.Add(actor);
        }
        ActorDecisions.Clear();
        foreach (var decision in network.ActorDecisions)
        {
            foreach (var action in decision.Actions)
            {
                ActorDecisions.Add(ToDecisionVm(decision, action));
            }
        }
        ActorActionOutcomes.Clear();
        foreach (var outcome in network.ActorActionOutcomes)
        {
            ActorActionOutcomes.Add(new SimulationActorActionOutcomeViewModel
            {
                AppliedState = outcome.Applied ? "Applied" : "Rejected",
                Reason = outcome.Reason,
                Target = outcome.Action.TargetEdgeId ?? outcome.Action.TargetNodeId ?? "(none)",
                ActionKind = outcome.Action.Kind.ToString(),
                Actor = outcome.Action.ActorId
            });
        }
        ActorMetrics.Clear();
        foreach (var metric in network.ActorMetrics)
        {
            ActorMetrics.Add(new SimulationActorMetricsViewModel
            {
                Tick = metric.Tick,
                TotalDelivered = metric.TotalDelivered.ToString("0.##", CultureInfo.InvariantCulture),
                TotalUnmetDemand = metric.TotalUnmetDemand.ToString("0.##", CultureInfo.InvariantCulture),
                TotalMovementCost = metric.TotalMovementCost.ToString("0.##", CultureInfo.InvariantCulture),
                AverageEdgeUtilisation = metric.AverageEdgeUtilisation.ToString("0.##", CultureInfo.InvariantCulture),
                BottleneckEdgeCount = metric.BottleneckEdgeCount.ToString(CultureInfo.InvariantCulture),
                PolicyRestrictionCount = metric.PolicyRestrictionCount.ToString(CultureInfo.InvariantCulture),
                CooperationIndex = metric.CooperationIndex.ToString("0.##", CultureInfo.InvariantCulture)
            });
        }
        agentActionLogger.Clear();
        foreach (var entry in network.AgentActionLogs)
        {
            agentActionLogger.Log(entry);
        }
        AgentLog.SetEntries(agentActionLogger.GetAll());
        ActorTick = network.ActorTick;
        SelectedSimulationActor = SimulationActors.FirstOrDefault();
        RefreshFilteredSimulationActors();
        RefreshNetworkAnalyticsPieChartData();
        RefreshInspector();
        StatusText = status;
        NotifyVisualChanged();
    }

    private void BuildSceneFromNetwork()
    {
        networkLayerService.EnsureLayerIntegrity(network);
        RefreshDraftSuggestions();
        if (IsFacilityPlanningMode && SelectedFacilityNodes.Count > 0)
        {
            TryRebuildFacilityCoverageState();
        }
        Scene.Nodes.Clear();
        Scene.Edges.Clear();
        Scene.Simulation.ShowAgentOverlays = ShowAgentTools;
        var controlledNodes = ShowAgentTools && SelectedSimulationActor is not null
            ? SelectedSimulationActor.ControlledNodeIds.ToHashSet(Comparer)
            : new HashSet<string>(Comparer);
        var controlledEdges = ShowAgentTools && SelectedSimulationActor is not null
            ? SelectedSimulationActor.ControlledEdgeIds.ToHashSet(Comparer)
            : new HashSet<string>(Comparer);
        var zoomTier = graphRenderer.GetZoomTier(Viewport.Zoom);
        var projectionViewport = IsMapLayoutLockedForGraph ? BuildGraphProjectionViewport(LastViewportSize) : (MapProjectionViewport?)null;

        var visibleLayers = network.Layers.Where(layer => layer.IsVisible).Select(layer => layer.Id).ToHashSet();
        foreach (var node in network.Nodes.Where(node => visibleLayers.Contains(node.LayerId)))
        {
            var centerX = node.X ?? 0d;
            var centerY = node.Y ?? 0d;
            if (projectionViewport.HasValue && TryProjectGeoNodeToGraph(node, projectionViewport.Value, out var projected))
            {
                centerX = projected.X;
                centerY = projected.Y;
            }
            var detailLines = BuildNodeDetailLines(node, [], null);
            var typeLabel = string.IsNullOrWhiteSpace(node.PlaceType) ? "Node" : node.PlaceType!;
            var sceneNode = new GraphNodeSceneItem
            {
                Id = node.Id,
                Name = node.Name,
                TypeLabel = typeLabel,
                MetricsLabel = string.Empty,
                DetailLines = detailLines,
                Bounds = new GraphRect(centerX - (GraphNodeTextLayout.DefaultWidth / 2d), centerY - (GraphNodeTextLayout.MinHeight / 2d), GraphNodeTextLayout.DefaultWidth, GraphNodeTextLayout.MinHeight),
                FillColor = SKColor.Parse("#163149"),
                StrokeColor = SKColor.Parse("#6AAED6"),
                Badges = BuildNodeBadges(node),
                ToolTipText = AppendNodeActorControlText(node.Id, BuildNodeToolTipText(node, detailLines, null)),
                HasWarning = false,
                CoveringFacilities = [],
                IsFacilityCovered = false,
                IsMultiFacilityCovered = false,
                PrimaryFacilityId = null,
                PrimaryFacilityTravelTime = null,
                IsActorControlled = controlledNodes.Contains(node.Id)
            };
            if (ShowAgentTools)
            {
                sceneNode.VisualOpacity = SelectedSimulationActor is null || sceneNode.IsActorControlled ? 1d : 0.42d;
            }
            var layout = GraphRenderer.GetOrBuildNodeLayout(sceneNode, zoomTier);
            GraphRenderer.ApplyLayoutBoundsKeepingCenter(sceneNode, layout);
            Scene.Nodes.Add(sceneNode);
        }

        foreach (var edge in network.Edges.Where(edge => visibleLayers.Contains(edge.LayerId)))
        {
            var layer = network.Layers.FirstOrDefault(item => item.Id == edge.LayerId);
            var isLocked = layer?.IsLocked == true;
            Scene.Edges.Add(new GraphEdgeSceneItem
            {
                Id = edge.Id,
                FromNodeId = edge.FromNodeId,
                ToNodeId = edge.ToNodeId,
                Label = isLocked ? $"🔒 {edge.RouteType ?? edge.Id}" : edge.RouteType ?? edge.Id,
                IsBidirectional = edge.IsBidirectional,
                Capacity = edge.Capacity ?? 0d,
                Cost = edge.Cost,
                Time = edge.Time,
                LoadRatio = 0d,
                FlowRate = 0d,
                ToolTipText = AppendEdgeActorControlText(edge.Id, BuildEdgeToolTipText(edge, TemporalNetworkSimulationEngine.EdgeFlowVisualSummary.Empty, 0d, null)),
                HasWarning = isLocked,
                IsActorControlled = controlledEdges.Contains(edge.Id)
            });
            if (ShowAgentTools && SelectedSimulationActor is not null && !controlledEdges.Contains(edge.Id))
            {
                Scene.Edges[^1].VisualOpacity = 0.42d;
            }
        }

        ApplyIsochroneVisuals();
    }

    private void RefreshLayerItems()
    {
        networkLayerService.EnsureLayerIntegrity(network);
        LayerItems.Clear();
        foreach (var layer in network.Layers.OrderBy(item => item.Order))
        {
            LayerItems.Add(new LayerListItemViewModel
            {
                Layer = layer,
                NodeCount = network.Nodes.Count(node => node.LayerId == layer.Id),
                EdgeCount = network.Edges.Count(edge => edge.LayerId == layer.Id),
                OnStateChanged = () =>
                {
                    BuildSceneFromNetwork();
                    MarkDirty();
                }
            });
        }

        SelectedLayerItem = LayerItems.FirstOrDefault(item => item.Layer.Id == selectedLayerId) ?? LayerItems.FirstOrDefault();
        DeleteLayerCommand.NotifyCanExecuteChanged();
    }

    private void AddLayerOfType(NetworkLayerType type)
    {
        var typeCount = network.Layers.Count(existing => existing.Type == type) + 1;
        var layer = new NetworkLayerModel { Name = $"{type} Layer {typeCount}", Type = type, Order = network.Layers.Count, IsVisible = true };
        network.Layers.Add(layer);
        selectedLayerId = layer.Id;
        RefreshLayerItems();
        BuildSceneFromNetwork();
        MarkDirty();
    }

    private void RenameSelectedLayer()
    {
        if (SelectedLayerItem is null)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(SelectedLayerNameText) ? SelectedLayerItem.Layer.Type.ToString() : SelectedLayerNameText.Trim();
        SelectedLayerItem.Layer.Name = name;
        RefreshLayerItems();
        MarkDirty();
    }

    private void DeleteSelectedLayer()
    {
        if (SelectedLayerItem is null)
        {
            return;
        }

        if (SelectedLayerItem.NodeCount > 0 || SelectedLayerItem.EdgeCount > 0)
        {
            StatusText = "Delete blocked: remove or reassign nodes and edges in this layer first.";
            return;
        }

        network.Layers.RemoveAll(layer => layer.Id == SelectedLayerItem.Layer.Id);
        RefreshLayerItems();
        BuildSceneFromNetwork();
        MarkDirty();
    }

    private void ToggleSelectedLayerVisibility()
    {
        if (SelectedLayerItem is null)
        {
            return;
        }

        SelectedLayerItem.Layer.IsVisible = !SelectedLayerItem.Layer.IsVisible;
        RefreshLayerItems();
        BuildSceneFromNetwork();
        MarkDirty();
    }

    private void ToggleSelectedLayerLock()
    {
        if (SelectedLayerItem is null)
        {
            return;
        }

        SelectedLayerItem.Layer.IsLocked = !SelectedLayerItem.Layer.IsLocked;
        RefreshLayerItems();
        BuildSceneFromNetwork();
        MarkDirty();
    }

    private void ShowAllLayers()
    {
        foreach (var layer in network.Layers)
        {
            layer.IsVisible = true;
        }

        RefreshLayerItems();
        BuildSceneFromNetwork();
        MarkDirty();
    }

    private void HideNonSelectedLayers()
    {
        if (SelectedLayerItem is null)
        {
            return;
        }

        foreach (var layer in network.Layers)
        {
            layer.IsVisible = layer.Id == SelectedLayerItem.Layer.Id;
        }

        RefreshLayerItems();
        BuildSceneFromNetwork();
        MarkDirty();
    }

    private void LockNonSelectedLayers()
    {
        if (SelectedLayerItem is null)
        {
            return;
        }

        foreach (var layer in network.Layers)
        {
            layer.IsLocked = layer.Id != SelectedLayerItem.Layer.Id;
        }

        RefreshLayerItems();
        BuildSceneFromNetwork();
        MarkDirty();
    }

    private void UnlockAllLayers()
    {
        foreach (var layer in network.Layers)
        {
            layer.IsLocked = false;
        }

        RefreshLayerItems();
        BuildSceneFromNetwork();
        MarkDirty();
    }

    private void RefreshScenarioItems()
    {
        Raise(nameof(ScenarioDefinitions));
        SelectedScenarioDefinition ??= network.ScenarioDefinitions.FirstOrDefault();
        ScenarioEditor.AttachNetwork(network);
    }

    private void OpenScenarioEditor()
    {
        ScenarioEditor.Open();
        CurrentWorkspaceMode = WorkspaceMode.ScenarioEditor;
        StatusText = "Scenario editor opened.";
    }

    public void CloseScenarioEditor()
    {
        if (!IsScenarioEditorWorkspaceMode)
        {
            return;
        }

        ScenarioEditor.DiscardChanges();
        CurrentWorkspaceMode = WorkspaceMode.Normal;
        RefreshScenarioItems();
        StatusText = "Returned to the network workspace.";
    }

    private void CreateScenario()
    {
        var scenario = new ScenarioDefinitionModel { Name = $"Scenario {network.ScenarioDefinitions.Count + 1}" };
        network.ScenarioDefinitions.Add(scenario);
        SelectedScenarioDefinition = scenario;
        MarkDirty();
    }

    private void RenameScenario()
    {
        if (SelectedScenarioDefinition is null)
        {
            return;
        }

        SelectedScenarioDefinition.Name = string.IsNullOrWhiteSpace(SelectedScenarioDefinition.Name) ? "Scenario" : SelectedScenarioDefinition.Name.Trim();
        Raise(nameof(ScenarioDefinitions));
        MarkDirty();
    }

    private void DuplicateScenario()
    {
        if (SelectedScenarioDefinition is null)
        {
            return;
        }

        var duplicate = new ScenarioDefinitionModel
        {
            Name = $"{SelectedScenarioDefinition.Name} Copy",
            Description = SelectedScenarioDefinition.Description,
            StartTime = SelectedScenarioDefinition.StartTime,
            EndTime = SelectedScenarioDefinition.EndTime,
            DeltaTime = SelectedScenarioDefinition.DeltaTime,
            EnableAdaptiveRouting = SelectedScenarioDefinition.EnableAdaptiveRouting,
            Events = SelectedScenarioDefinition.Events.Select(evt => new ScenarioEventModel
            {
                Name = evt.Name,
                Kind = evt.Kind,
                TargetKind = evt.TargetKind,
                TargetId = evt.TargetId,
                TrafficTypeIdOrName = evt.TrafficTypeIdOrName,
                Time = evt.Time,
                EndTime = evt.EndTime,
                Value = evt.Value,
                Notes = evt.Notes,
                IsEnabled = evt.IsEnabled
            }).ToList()
        };

        network.ScenarioDefinitions.Add(duplicate);
        SelectedScenarioDefinition = duplicate;
        MarkDirty();
    }

    private void DeleteScenario()
    {
        if (SelectedScenarioDefinition is null)
        {
            return;
        }

        network.ScenarioDefinitions.Remove(SelectedScenarioDefinition);
        SelectedScenarioDefinition = network.ScenarioDefinitions.FirstOrDefault();
        Raise(nameof(ScenarioDefinitions));
        MarkDirty();
    }

    private void AddScenarioEvent()
    {
        if (SelectedScenarioDefinition is null)
        {
            return;
        }

        var evt = new ScenarioEventModel { Name = "New Event", Kind = ScenarioEventKind.NodeFailure, TargetKind = ScenarioTargetKind.Node, Time = 0d };
        SelectedScenarioDefinition.Events.Add(evt);
        SelectedScenarioEvent = evt;
        MarkDirty();
    }

    private void EditScenarioEvent()
    {
        if (SelectedScenarioDefinition is null || SelectedScenarioEvent is null)
        {
            return;
        }

        ScenarioEditor.SelectedScenarioDefinition = SelectedScenarioDefinition;
        ScenarioEditor.Open();
        ScenarioEditor.SelectedEventItem = ScenarioEditor.EventItems.FirstOrDefault(item => ReferenceEquals(item.Model, SelectedScenarioEvent));
        CurrentWorkspaceMode = WorkspaceMode.ScenarioEditor;
    }

    private void DuplicateScenarioEvent()
    {
        if (SelectedScenarioDefinition is null || SelectedScenarioEvent is null)
        {
            return;
        }

        var source = SelectedScenarioEvent;
        var copy = new ScenarioEventModel
        {
            Name = $"{source.Name} Copy",
            Kind = source.Kind,
            TargetKind = source.TargetKind,
            TargetId = source.TargetId,
            TrafficTypeIdOrName = source.TrafficTypeIdOrName,
            Time = source.Time,
            EndTime = source.EndTime,
            Value = source.Value,
            Notes = source.Notes,
            IsEnabled = source.IsEnabled
        };
        SelectedScenarioDefinition.Events.Add(copy);
        SelectedScenarioEvent = copy;
        MarkDirty();
    }

    private void DeleteScenarioEvent()
    {
        if (SelectedScenarioDefinition is null || SelectedScenarioEvent is null)
        {
            return;
        }

        SelectedScenarioDefinition.Events.Remove(SelectedScenarioEvent);
        SelectedScenarioEvent = SelectedScenarioDefinition.Events.FirstOrDefault();
        MarkDirty();
    }

    private void RunScenario()
    {
        if (SelectedScenarioDefinition is null)
        {
            return;
        }

        ScenarioWarnings.Clear();
        foreach (var scenarioError in scenarioValidationService.ValidateScenario(SelectedScenarioDefinition))
        {
            ScenarioWarnings.Add(scenarioError);
        }

        foreach (var evt in SelectedScenarioDefinition.Events)
        {
            foreach (var error in scenarioValidationService.ValidateEvent(evt))
            {
                ScenarioWarnings.Add(error);
            }
        }

        if (ScenarioWarnings.Count > 0)
        {
            ScenarioResultSummary = "Scenario has validation warnings. Fix fields and run again.";
            return;
        }

        var result = scenarioRunner.Run(network, SelectedScenarioDefinition, new ScenarioRunOptions
        {
            StartTime = SelectedScenarioDefinition.StartTime,
            EndTime = SelectedScenarioDefinition.EndTime <= SelectedScenarioDefinition.StartTime ? TimelineMaximum : SelectedScenarioDefinition.EndTime,
            DeltaTime = SelectedScenarioDefinition.DeltaTime <= 0d ? 1d : SelectedScenarioDefinition.DeltaTime,
            EnableAdaptiveRouting = SelectedScenarioDefinition.EnableAdaptiveRouting
        });
        PopulateTopIssues(result.Issues);
        foreach (var warning in result.Warnings)
        {
            ScenarioWarnings.Add(warning);
        }

        ScenarioResultSummary = $"Scenario '{result.ScenarioName}' completed · issues {result.Issues.Count}, warnings {result.Warnings.Count}.";
    }

    private bool CreateEdge(string sourceId, string targetId, bool isBidirectional)
    {
        CommitTransientEditorsToModel();
        var layer = SelectedLayerItem?.Layer ?? networkLayerService.GetDefaultLayer(network);
        if (layer.IsLocked)
        {
            StatusText = "Selected layer is locked. Unlock the layer to add routes.";
            return false;
        }
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
            IsBidirectional = isBidirectional,
            RouteType = "Proposed route",
            TrafficPermissions = [],
            LayerId = layer.Id
        });
        BuildSceneFromNetwork();
        RefreshInspector();
        MarkDirty();
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
        var layer = SelectedLayerItem?.Layer ?? networkLayerService.GetDefaultLayer(network);
        if (layer.IsLocked)
        {
            StatusText = "Selected layer is locked. Unlock the layer to add nodes.";
            return string.Empty;
        }
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
            LayerId = layer.Id,
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
        MarkDirty();
        NotifyVisualChanged();
        return id;
    }

    private void DeleteSelection()
    {
        CommitTransientEditorsToModel();
        var selectedNodes = Scene.Selection.SelectedNodeIds.ToHashSet(Comparer);
        var selectedEdges = Scene.Selection.SelectedEdgeIds.ToHashSet(Comparer);

        if (network.Nodes.Any(node => selectedNodes.Contains(node.Id) && IsLockedLayer(node.LayerId)) ||
            network.Edges.Any(edge => selectedEdges.Contains(edge.Id) && IsLockedLayer(edge.LayerId)))
        {
            StatusText = "Selected items include locked-layer content. Unlock the layer before deleting.";
            return;
        }

        network.Nodes.RemoveAll(node => selectedNodes.Contains(node.Id));
        network.Edges.RemoveAll(edge =>
            selectedEdges.Contains(edge.Id) ||
            selectedNodes.Contains(edge.FromNodeId) ||
            selectedNodes.Contains(edge.ToNodeId));
        foreach (var actor in SimulationActors)
        {
            actor.ControlledNodeIds = actor.ControlledNodeIds.Where(id => !selectedNodes.Contains(id)).Distinct(Comparer).ToList();
            actor.ControlledEdgeIds = actor.ControlledEdgeIds.Where(id => !selectedEdges.Contains(id)).Distinct(Comparer).ToList();
            actor.ControlledEdgeIds = actor.ControlledEdgeIds.Where(id => network.Edges.Any(edge => Comparer.Equals(edge.Id, id))).ToList();
        }
        network.Actors = SimulationActors.ToList();

        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        BuildSceneFromNetwork();
        RefreshInspector();
        MarkDirty();
        NotifyVisualChanged();
        StatusText = "Deleted the current selection.";
    }

    private bool IsLockedLayer(Guid layerId) => network.Layers.FirstOrDefault(layer => layer.Id == layerId)?.IsLocked == true;

    private void AssignSelectedNodesToLayer()
    {
        if (SelectedLayerItem is null)
        {
            return;
        }

        foreach (var node in network.Nodes.Where(node => Scene.Selection.SelectedNodeIds.Contains(node.Id)))
        {
            node.LayerId = SelectedLayerItem.Layer.Id;
        }

        RefreshLayerItems();
        BuildSceneFromNetwork();
    }

    private void AssignSelectedEdgesToLayer()
    {
        if (SelectedLayerItem is null)
        {
            return;
        }

        foreach (var edge in network.Edges.Where(edge => Scene.Selection.SelectedEdgeIds.Contains(edge.Id)))
        {
            edge.LayerId = SelectedLayerItem.Layer.Id;
        }

        RefreshLayerItems();
        BuildSceneFromNetwork();
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
        lastConsumerCosts = simulationEngine.SummarizeConsumerCosts(outcomes.SelectMany(outcome => outcome.Allocations));
        visualAnalyticsSnapshot = new VisualAnalyticsSnapshot { Network = network, TrafficOutcomes = lastOutcomes, ConsumerCosts = lastConsumerCosts, Period = CurrentPeriod };
        RefreshInsights();
        lastTimelineStepResult = null;
        Raise(nameof(TrafficDeliveredColumnLabel));
        ApplySimulationOutcomes(outcomes.SelectMany(outcome => outcome.Allocations), null);
        PopulateTopIssues(new BottleneckDetectionService().DetectIssues(network, new SimulationResult { Outcomes = outcomes }));
        StatusText = "Simulation finished.";
    }

    private void LoadSelectedActorDraft()
    {
        var actor = SelectedSimulationActor;
        if (actor is null)
        {
            actorNameText = string.Empty;
            actorBudgetText = "0";
            actorCashText = "0";
            actorRiskToleranceText = "0.5";
            actorCooperationWeightText = "0.5";
            actorNotesText = string.Empty;
            actorIsEnabled = true;
            actorAllowAllTrafficTypes = true;
            ActorValidationText = string.Empty;
            ActorTrafficTypeRows.Clear();
            RaiseActorDraftProperties();
            return;
        }

        EnsureActorCapability(actor);
        EnsureAllowedTrafficTypesOnlyKnownValues(actor);
        actorNameText = actor.Name;
        actorBudgetText = actor.Budget.ToString("0.##", CultureInfo.InvariantCulture);
        actorCashText = actor.Cash.ToString("0.##", CultureInfo.InvariantCulture);
        actorRiskToleranceText = actor.RiskTolerance.ToString("0.##", CultureInfo.InvariantCulture);
        actorCooperationWeightText = actor.CooperationWeight.ToString("0.##", CultureInfo.InvariantCulture);
        actorNotesText = actor.Notes;
        actorIsEnabled = actor.IsEnabled;
        actorAllowAllTrafficTypes = actor.Capability.AllowAllTrafficTypes;
        ActorValidationText = string.Empty;
        RebuildActorTrafficTypeRows();
        RaiseActorDraftProperties();
    }

    private void RaiseActorDraftProperties()
    {
        Raise(nameof(ActorNameText));
        Raise(nameof(ActorBudgetText));
        Raise(nameof(ActorCashText));
        Raise(nameof(ActorRiskToleranceText));
        Raise(nameof(ActorCooperationWeightText));
        Raise(nameof(ActorNotesText));
        Raise(nameof(ActorIsEnabled));
        Raise(nameof(ActorAllowAllTrafficTypes));
        Raise(nameof(ShowActorTrafficTypeChecklist));
    }

    private void RefreshSelectedActorDisplayState()
    {
        Raise(nameof(SelectedActorNodeCountText));
        Raise(nameof(SelectedActorEdgeCountText));
        Raise(nameof(SelectedActorTrafficScopeText));
        Raise(nameof(SelectedActorAllowedActionsText));
        Raise(nameof(SelectedActorControlledNodesDisplay));
        Raise(nameof(SelectedActorControlledEdgesDisplay));
        RefreshAgentViewModels();
    }

    private void RefreshAgentViewModels()
    {
        Agents.Clear();
        foreach (var actor in SimulationActors)
        {
            var capability = actor.Capability;
            Agents.Add(new AgentViewModel
            {
                Name = actor.Name,
                Type = capability.IsCustomActorType && !string.IsNullOrWhiteSpace(capability.CustomActorTypeName)
                    ? capability.CustomActorTypeName!
                    : actor.Kind.ToString(),
                Budget = actor.Budget,
                AllowedTrafficTypes = capability.AllowAllTrafficTypes
                    ? "All"
                    : string.Join(", ", capability.AllowedTrafficTypes),
                AllowedActions = string.Join(", ", capability.AllowedActionKinds)
            });
        }
    }

    private void RebuildActorTrafficTypeRows()
    {
        ActorTrafficTypeRows.Clear();
        if (SelectedSimulationActor?.Capability is null)
        {
            return;
        }

        var allowed = SelectedSimulationActor.Capability.AllowedTrafficTypes.ToHashSet(Comparer);
        foreach (var trafficType in network.TrafficTypes
                     .Select(definition => definition.Name)
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(Comparer)
                     .OrderBy(name => name, Comparer))
        {
            ActorTrafficTypeRows.Add(new ActorTrafficTypeSelectionRow(trafficType, allowed.Contains(trafficType), ToggleActorTrafficType));
        }
    }

    private void ToggleActorTrafficType(ActorTrafficTypeSelectionRow row)
    {
        if (SelectedSimulationActor?.Capability is null || ActorAllowAllTrafficTypes)
        {
            return;
        }

        var allowed = SelectedSimulationActor.Capability.AllowedTrafficTypes.ToHashSet(Comparer);
        if (row.IsAllowed)
        {
            allowed.Add(row.TrafficType);
        }
        else
        {
            allowed.RemoveWhere(name => Comparer.Equals(name, row.TrafficType));
        }

        SelectedSimulationActor.Capability.AllowedTrafficTypes = allowed.OrderBy(name => name, Comparer).ToArray();
        MarkDirty();
        RefreshSelectedActorDisplayState();
    }

    private void ApplySelectedActorCommandExecute()
    {
        if (SelectedSimulationActor is null)
        {
            ActorValidationText = "Select an actor first.";
            return;
        }

        if (!TryParseActorDouble(ActorBudgetText, out var budget) || budget < 0d)
        {
            ActorValidationText = "Budget must be >= 0.";
            return;
        }

        if (!TryParseActorDouble(ActorCashText, out var cash) || cash < 0d)
        {
            ActorValidationText = "Cash must be >= 0.";
            return;
        }

        if (!TryParseActorDouble(ActorRiskToleranceText, out var riskTolerance) || riskTolerance < 0d || riskTolerance > 1d)
        {
            ActorValidationText = "Risk tolerance must be 0..1.";
            return;
        }

        if (!TryParseActorDouble(ActorCooperationWeightText, out var cooperationWeight) || cooperationWeight < 0d || cooperationWeight > 1d)
        {
            ActorValidationText = "Cooperation weight must be 0..1.";
            return;
        }

        var actor = SelectedSimulationActor;
        actor.Name = string.IsNullOrWhiteSpace(ActorNameText) ? actor.Name : ActorNameText.Trim();
        actor.Budget = budget;
        actor.Cash = cash;
        actor.RiskTolerance = riskTolerance;
        actor.CooperationWeight = cooperationWeight;
        actor.Notes = ActorNotesText?.Trim() ?? string.Empty;
        actor.IsEnabled = ActorIsEnabled;
        EnsureActorCapability(actor);
        ApplyActorTrafficScopeFromDraft(markDirty: false);
        network.Actors = SimulationActors.ToList();
        MarkDirty();
        Raise(nameof(SimulationActors));
        RefreshSelectedActorDisplayState();
        ActorValidationText = string.Empty;
        ActorStatusMessage = $"Updated actor '{actor.Name}'.";
    }

    private void ApplyActorTrafficScopeFromDraft(bool markDirty)
    {
        if (SelectedSimulationActor?.Capability is null)
        {
            return;
        }

        SelectedSimulationActor.Capability.AllowAllTrafficTypes = ActorAllowAllTrafficTypes;
        if (ActorAllowAllTrafficTypes)
        {
            SelectedSimulationActor.Capability.AllowedTrafficTypes = [];
        }
        else
        {
            var allowed = ActorTrafficTypeRows.Where(row => row.IsAllowed).Select(row => row.TrafficType).Distinct(Comparer).OrderBy(name => name, Comparer).ToArray();
            SelectedSimulationActor.Capability.AllowedTrafficTypes = allowed;
        }

        if (markDirty)
        {
            MarkDirty();
        }

        RefreshSelectedActorDisplayState();
    }

    private void EnsureAllowedTrafficTypesOnlyKnownValues(SimulationActorState? actor = null)
    {
        actor ??= SelectedSimulationActor;
        if (actor?.Capability is null)
        {
            return;
        }

        var known = network.TrafficTypes.Select(type => type.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToHashSet(Comparer);
        actor.Capability.AllowedTrafficTypes = actor.Capability.AllowedTrafficTypes
            .Where(type => known.Contains(type))
            .Distinct(Comparer)
            .OrderBy(type => type, Comparer)
            .ToArray();
    }

    private static bool TryParseActorDouble(string text, out double value) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static bool IsPolicyOnlyAction(SimulationActorActionKind kind) =>
        kind is SimulationActorActionKind.AdjustRoutePermission
            or SimulationActorActionKind.BanTrafficOnEdge
            or SimulationActorActionKind.TaxRoute
            or SimulationActorActionKind.SubsidiseCapacity
            or SimulationActorActionKind.SetNodePolicy
            or SimulationActorActionKind.SetEdgePolicy;

    private void EnsureActorCapability(SimulationActorState actor, bool preserveTrafficScope = true)
    {
        EnsureActorReferences(actor);
        var previousCapability = actor.Capability;
        var preservedAllowAll = preserveTrafficScope && previousCapability is not null && previousCapability.AllowAllTrafficTypes;
        var preservedAllowedTraffic = preserveTrafficScope && previousCapability is not null
            ? (previousCapability.AllowedTrafficTypes ?? []).ToArray()
            : Array.Empty<string>();

        actor.Capability = SimulationActorCapabilityCatalog.ForKind(actor.Id, actor.Kind);
        actor.Capability.ActorId = actor.Id;
        if (preserveTrafficScope)
        {
            actor.Capability.AllowAllTrafficTypes = preservedAllowAll;
            actor.Capability.AllowedTrafficTypes = preservedAllowAll
                ? []
                : preservedAllowedTraffic.Where(type => !string.IsNullOrWhiteSpace(type)).Distinct(Comparer).ToArray();
            EnsureAllowedTrafficTypesOnlyKnownValues(actor);
        }

        actor.Capability.AllowedActionKinds = actor.Capability.AllowedActionKinds
            .Where(kind => actor.Kind switch
            {
                SimulationActorKind.Firm => !IsPolicyOnlyAction(kind),
                SimulationActorKind.Government => kind is not SimulationActorActionKind.BuyTraffic and not SimulationActorActionKind.SellTraffic,
                SimulationActorKind.LogisticsPlanner => kind is SimulationActorActionKind.PreferRoute or SimulationActorActionKind.AdjustEdgeCapacity or SimulationActorActionKind.AdjustEdgeCost,
                _ => kind == SimulationActorActionKind.NoOp
            })
            .ToArray();
    }

    private void AddFirmActor() => AddActor(CreateDefaultActor(SimulationActorKind.Firm));
    private void AddGovernmentActor() => AddActor(CreateDefaultActor(SimulationActorKind.Government));
    private void AddLogisticsPlannerActor() => AddActor(CreateDefaultActor(SimulationActorKind.LogisticsPlanner));

    private void AddActor(SimulationActorState actor)
    {
        EnsureActorCapability(actor, preserveTrafficScope: true);
        SimulationActors.Add(actor);
        network.Actors = SimulationActors.ToList();
        SelectedSimulationActor = actor;
        RefreshFilteredSimulationActors();
        MarkDirty();
        ActorStatusMessage = $"Added actor '{actor.Name}'.";
    }

    private SimulationActorState CreateDefaultActor(SimulationActorKind kind)
    {
        var prefix = kind switch
        {
            SimulationActorKind.Firm => "actor-firm",
            SimulationActorKind.Government => "actor-government",
            _ => "actor-logistics"
        };
        var next = Enumerable.Range(1, 999)
            .Select(i => $"{prefix}-{i:000}")
            .First(id => SimulationActors.All(actor => !Comparer.Equals(actor.Id, id)));

        return kind switch
        {
            SimulationActorKind.Firm => new SimulationActorState
            {
                Id = next,
                Name = $"Firm {next[^3..]}",
                Kind = kind,
                Objective = SimulationActorObjective.MaximiseProfit,
                Cash = 100,
                Budget = 100,
                RiskTolerance = 0.5d,
                CooperationWeight = 0.3d,
                IsEnabled = true,
                Capability = SimulationActorCapabilityCatalog.ForKind(next, kind)
            },
            SimulationActorKind.Government => new SimulationActorState
            {
                Id = next,
                Name = $"Government {next[^3..]}",
                Kind = kind,
                Objective = SimulationActorObjective.StabiliseNetwork,
                Cash = 1000,
                Budget = 1000,
                RiskTolerance = 0.2d,
                CooperationWeight = 0.8d,
                IsEnabled = true,
                Capability = SimulationActorCapabilityCatalog.ForKind(next, kind)
            },
            _ => new SimulationActorState
            {
                Id = next,
                Name = $"Logistics {next[^3..]}",
                Kind = kind,
                Objective = SimulationActorObjective.MinimiseUnmetDemand,
                Cash = 500,
                Budget = 500,
                RiskTolerance = 0.4d,
                CooperationWeight = 0.7d,
                IsEnabled = true,
                Capability = SimulationActorCapabilityCatalog.ForKind(next, kind)
            }
        };
    }

    private void RemoveSelectedActor()
    {
        if (SelectedSimulationActor is null) return;
        SimulationActors.Remove(SelectedSimulationActor);
        network.Actors = SimulationActors.ToList();
        SelectedSimulationActor = SimulationActors.FirstOrDefault();
        RefreshFilteredSimulationActors();
        MarkDirty();
    }

    private void DuplicateSelectedActor()
    {
        if (SelectedSimulationActor is null) return;
        var copy = CreateDefaultActor(SelectedSimulationActor.Kind);
        copy.Name = $"{SelectedSimulationActor.Name} Copy";
        copy.Objective = SelectedSimulationActor.Objective;
        copy.Budget = SelectedSimulationActor.Budget;
        copy.Cash = SelectedSimulationActor.Cash;
        copy.RiskTolerance = SelectedSimulationActor.RiskTolerance;
        copy.CooperationWeight = SelectedSimulationActor.CooperationWeight;
        copy.ControlledNodeIds = [.. SelectedSimulationActor.ControlledNodeIds];
        copy.ControlledEdgeIds = [.. SelectedSimulationActor.ControlledEdgeIds];
        copy.Notes = SelectedSimulationActor.Notes;
        copy.IsEnabled = SelectedSimulationActor.IsEnabled;
        copy.Capability = new SimulationActorCapability
        {
            ActorId = copy.Id,
            AllowedActionKinds = [.. SelectedSimulationActor.Capability.AllowedActionKinds],
            AllowAllTrafficTypes = SelectedSimulationActor.Capability.AllowAllTrafficTypes,
            AllowedTrafficTypes = [.. SelectedSimulationActor.Capability.AllowedTrafficTypes],
            IsCustomActorType = SelectedSimulationActor.Capability.IsCustomActorType,
            CustomActorTypeName = SelectedSimulationActor.Capability.CustomActorTypeName
        };
        AddActor(copy);
    }

    private void RefreshFilteredSimulationActors()
    {
        var query = AgentSearchText?.Trim() ?? string.Empty;
        var results = string.IsNullOrWhiteSpace(query)
            ? SimulationActors
            : SimulationActors.Where(actor =>
                actor.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                actor.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                actor.Kind.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                actor.Objective.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));
        FilteredSimulationActors.Clear();
        foreach (var actor in results)
        {
            FilteredSimulationActors.Add(actor);
        }
    }

    private bool ValidateActorRun()
    {
        if (SimulationActors.Count == 0) { ActorStatusMessage = "Add an actor first."; return false; }
        if (SimulationActors.All(actor => !actor.IsEnabled)) { ActorStatusMessage = "Enable at least one actor."; return false; }
        if (network.Nodes.Count == 0 || network.Edges.Count == 0) { ActorStatusMessage = "Create or import a network before running actors."; return false; }
        return true;
    }

    private void PreviewActorActions()
    {
        if (!ValidateActorRun()) return;
        ActorDecisions.Clear();
        foreach (var decision in simulationActorCoordinator.PreviewActorActions(network, SimulationActors.ToList(), ActorTick, network.ActorDecisions))
        {
            foreach (var action in decision.Actions)
            {
                ActorDecisions.Add(ToDecisionVm(decision, action));
            }
        }
        HasActorPreview = true;
        ActorStatusMessage = "Previewed actor actions. No network changes applied.";
    }

    private void RunActorStep()
    {
        if (!ValidateActorRun()) return;
        CapturePreAgentMutationNetwork();
        var step = simulationActorCoordinator.StepActorsOnce(network, SimulationActors.ToList(), ActorTick, network.ActorDecisions);
        ApplyActorStep(step, "Actor step applied.");
    }

    private void RunActorTicks()
    {
        if (!ValidateActorRun()) return;
        CapturePreAgentMutationNetwork();
        for (var i = 0; i < ActorRunTicks; i++)
        {
            var step = simulationActorCoordinator.StepActorsOnce(network, SimulationActors.ToList(), ActorTick, network.ActorDecisions);
            ApplyActorStep(step, i == ActorRunTicks - 1 ? "Actor run complete." : string.Empty);
        }
    }

    private void ApplyActorStep(SimulationActorStepResult step, string message, bool refreshSimulation = true)
    {
        network = step.NetworkAfterStep;
        network.Actors = SimulationActors.ToList();
        network.ActorDecisions.AddRange(step.Decisions);
        network.ActorActionOutcomes.AddRange(step.ActionOutcomes);
        network.ActorMetrics.Add(step.Metrics);
        network.AgentActionLogs = [.. agentActionLogger.GetAll()];
        ActorTick += 1;
        network.ActorTick = ActorTick;
        foreach (var decision in step.Decisions.SelectMany(d => d.Actions, (d, a) => (d, a)))
        {
            ActorDecisions.Add(ToDecisionVm(decision.d, decision.a));
        }
        foreach (var outcome in step.ActionOutcomes)
        {
            ActorActionOutcomes.Add(new SimulationActorActionOutcomeViewModel
            {
                AppliedState = outcome.Applied ? "Applied" : "Rejected",
                Reason = outcome.Reason,
                Target = outcome.Action.TargetEdgeId ?? outcome.Action.TargetNodeId ?? "(none)",
                ActionKind = outcome.Action.Kind.ToString(),
                Actor = outcome.Action.ActorId
            });
        }
        AgentLog.SetEntries(agentActionLogger.GetAll());
        ActorMetrics.Add(new SimulationActorMetricsViewModel
        {
            Tick = step.Metrics.Tick,
            TotalDelivered = step.Metrics.TotalDelivered.ToString("0.##", CultureInfo.InvariantCulture),
            TotalUnmetDemand = step.Metrics.TotalUnmetDemand.ToString("0.##", CultureInfo.InvariantCulture),
            TotalMovementCost = step.Metrics.TotalMovementCost.ToString("0.##", CultureInfo.InvariantCulture),
            AverageEdgeUtilisation = step.Metrics.AverageEdgeUtilisation.ToString("0.##", CultureInfo.InvariantCulture),
            BottleneckEdgeCount = step.Metrics.BottleneckEdgeCount.ToString(CultureInfo.InvariantCulture),
            PolicyRestrictionCount = step.Metrics.PolicyRestrictionCount.ToString(CultureInfo.InvariantCulture),
            CooperationIndex = step.Metrics.CooperationIndex.ToString("0.##", CultureInfo.InvariantCulture)
        });
        highlightedNodeIds.Clear();
        highlightedEdgeIds.Clear();
        foreach (var outcome in step.ActionOutcomes)
        {
            if (!string.IsNullOrWhiteSpace(outcome.Action.TargetNodeId)) highlightedNodeIds.Add(outcome.Action.TargetNodeId);
            if (!string.IsNullOrWhiteSpace(outcome.Action.TargetEdgeId)) highlightedEdgeIds.Add(outcome.Action.TargetEdgeId);
        }
        BuildSceneFromNetwork();
        RefreshInspector();
        if (refreshSimulation)
        {
            RunSimulation();
        }

        MarkDirty();
        if (!string.IsNullOrWhiteSpace(message))
        {
            ActorStatusMessage = message;
        }
        HasActorPreview = false;
    }

    private static SimulationActorDecisionViewModel ToDecisionVm(SimulationActorDecision decision, SimulationActorAction action) => new()
    {
        Tick = decision.Tick,
        Actor = decision.ActorId,
        Action = action.Kind.ToString(),
        Target = action.TargetEdgeId ?? action.TargetNodeId ?? "(none)",
        Traffic = action.TrafficType ?? "(all)",
        Delta = action.DeltaValue.ToString("0.##", CultureInfo.InvariantCulture),
        Cost = action.Cost.ToString("0.##", CultureInfo.InvariantCulture),
        Reason = action.Reason,
        ExpectedEffect = action.ExpectedEffect
    };

    private void ResetActorHistory()
    {
        agentActionLogger.Clear();
        ActorDecisions.Clear();
        ActorActionOutcomes.Clear();
        ActorMetrics.Clear();
        AgentLog.SetEntries(agentActionLogger.GetAll());
        network.ActorDecisions.Clear();
        network.ActorActionOutcomes.Clear();
        network.ActorMetrics.Clear();
        network.AgentActionLogs.Clear();
        ActorTick = 0;
        network.ActorTick = 0;
        HasActorPreview = false;
        ActorStatusMessage = "Actor history reset.";
    }

    private void AssignSelectedNodeToActor()
    {
        if (SelectedSimulationActor is null)
        {
            ActorStatusMessage = "Select an actor first.";
            return;
        }

        var (nodeCount, _) = AssignCurrentSelectionToActor(assignNodes: true, assignEdges: false);
        if (nodeCount == 0)
        {
            ActorStatusMessage = "Select one or more nodes first.";
        }
    }

    private void AssignSelectedEdgeToActor()
    {
        if (SelectedSimulationActor is null)
        {
            ActorStatusMessage = "Select an actor first.";
            return;
        }

        var (_, edgeCount) = AssignCurrentSelectionToActor(assignNodes: false, assignEdges: true);
        if (edgeCount == 0)
        {
            ActorStatusMessage = "Select one or more routes first.";
        }
    }

    private (int NodeCount, int EdgeCount) AssignCurrentSelectionToActor(bool assignNodes = true, bool assignEdges = true)
    {
        if (SelectedSimulationActor is null)
        {
            return (0, 0);
        }

        var selectedNodeIds = assignNodes
            ? Scene.Selection.SelectedNodeIds
                .Where(id => network.Nodes.Any(node => Comparer.Equals(node.Id, id)))
                .Distinct(Comparer)
                .ToList()
            : [];
        var selectedEdgeIds = assignEdges
            ? Scene.Selection.SelectedEdgeIds
                .Where(id => network.Edges.Any(edge => Comparer.Equals(edge.Id, id)))
                .Distinct(Comparer)
                .ToList()
            : [];

        if (selectedNodeIds.Count == 0 && selectedEdgeIds.Count == 0)
        {
            return (0, 0);
        }

        var currentNodes = SelectedSimulationActor.ControlledNodeIds.ToHashSet(Comparer);
        foreach (var nodeId in selectedNodeIds)
        {
            currentNodes.Add(nodeId);
        }

        SelectedSimulationActor.ControlledNodeIds = currentNodes.OrderBy(id => id, Comparer).ToList();
        var current = SelectedSimulationActor.ControlledEdgeIds.ToHashSet(Comparer);
        foreach (var edgeId in selectedEdgeIds)
        {
            current.Add(edgeId);
        }

        SelectedSimulationActor.ControlledEdgeIds = current.OrderBy(id => id, Comparer).ToList();
        network.Actors = SimulationActors.ToList();
        MarkDirty();
        RefreshSelectedActorDisplayState();
        BuildSceneFromNetwork();
        Raise(nameof(SimulationActors));
        ActorStatusMessage = selectedNodeIds.Count > 0 && selectedEdgeIds.Count > 0
            ? "Updated actor node and route assignments."
            : selectedNodeIds.Count > 0
                ? "Updated actor node assignments."
                : "Updated actor route assignments.";
        return (selectedNodeIds.Count, selectedEdgeIds.Count);
    }

    private void ClearActorAssignments()
    {
        if (SelectedSimulationActor is null)
        {
            ActorStatusMessage = "Select an actor first.";
            return;
        }

        EnsureActorReferences(SelectedSimulationActor);
        SelectedSimulationActor.ControlledNodeIds.Clear();
        SelectedSimulationActor.ControlledEdgeIds.Clear();
        network.Actors = SimulationActors.ToList();
        MarkDirty();
        RefreshSelectedActorDisplayState();
        BuildSceneFromNetwork();
        Raise(nameof(SimulationActors));
    }

    private void AdvanceTimeline()
    {
        CommitTransientEditorsToModel();
        var agentsRan = RunActorsBeforeTimelineAdvance();
        var validatedNetwork = fileService.NormalizeAndValidate(network);
        temporalState ??= temporalEngine.Initialize(validatedNetwork);
        var result = temporalEngine.Advance(validatedNetwork, temporalState);
        lastTimelineStepResult = result;
        Raise(nameof(TrafficDeliveredColumnLabel));
        lastConsumerCosts = simulationEngine.SummarizeConsumerCosts(result.Allocations);
        visualAnalyticsSnapshot = new VisualAnalyticsSnapshot { Network = network, TrafficOutcomes = lastOutcomes, ConsumerCosts = lastConsumerCosts, Period = CurrentPeriod };
        RefreshInsights();
        CurrentPeriod = result.Period;
        TimelinePosition = result.EffectivePeriod;
        ApplySimulationOutcomes(result.Allocations, result);
        StatusText = agentsRan
            ? $"Advanced to period {result.Period} after applying agent actions."
            : $"Advanced to period {result.Period}.";
    }

    private bool RunActorsBeforeTimelineAdvance()
    {
        if (SimulationActors.Count == 0 || SimulationActors.All(actor => !actor.IsEnabled))
        {
            return false;
        }

        if (network.Nodes.Count == 0 || network.Edges.Count == 0)
        {
            ActorStatusMessage = "Create or import a network before running actors.";
            return false;
        }

        CapturePreAgentMutationNetwork();
        var step = simulationActorCoordinator.StepActorsOnce(network, SimulationActors.ToList(), ActorTick, network.ActorDecisions);
        ApplyActorStep(step, $"Agent actions applied for period {CurrentPeriod + 1}.", refreshSimulation: false);
        return step.Decisions.Count > 0;
    }

    private void ResetTimeline()
    {
        if (preAgentMutationNetwork is not null)
        {
            network = NetworkModelCloneUtility.Clone(preAgentMutationNetwork);
            EnsureNetworkReferences(network);
            preAgentMutationNetwork = null;
            RebuildActorStateFromNetwork();
            BuildSceneFromNetwork();
        }

        temporalState = null;
        lastTimelineStepResult = null;
        Raise(nameof(TrafficDeliveredColumnLabel));
        lastOutcomes = [];
        lastConsumerCosts = [];
        visualAnalyticsSnapshot = null;
        NetworkInsights.Clear();
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
            UpdateSceneNodeLayout(node, nodeModel, null, graphRenderer.GetZoomTier(Viewport.Zoom));
            node.HasWarning = false;
        }

        ApplyIsochroneVisuals();
        ClearDynamicReports();
        RefreshInspector();
        NotifyVisualChanged();
        StatusText = "Reset the timeline to period 0.";
    }

    private void CapturePreAgentMutationNetwork()
    {
        preAgentMutationNetwork ??= NetworkModelCloneUtility.Clone(network);
    }

    private void RebuildActorStateFromNetwork()
    {
        SimulationActors.Clear();
        foreach (var actor in network.Actors)
        {
            EnsureActorCapability(actor);
            SimulationActors.Add(actor);
        }

        ActorDecisions.Clear();
        ActorActionOutcomes.Clear();
        ActorMetrics.Clear();
        agentActionLogger.Clear();
        foreach (var entry in network.AgentActionLogs)
        {
            agentActionLogger.Log(entry);
        }

        AgentLog.SetEntries(agentActionLogger.GetAll());
        ActorTick = network.ActorTick;
        SelectedSimulationActor = SimulationActors.FirstOrDefault();
        RefreshFilteredSimulationActors();
        RefreshSelectedActorDisplayState();
    }

    private void ApplySimulationOutcomes(IEnumerable<RouteAllocation> allocations, TemporalNetworkSimulationEngine.TemporalSimulationStepResult? timeline)
    {
        var allocationList = allocations.ToList();
        var edgeLoads = BuildEdgeLoads(allocationList, timeline);
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
                UpdateSceneNodeLayout(node, nodeModel, pressure.Score > 0d ? pressure : null, graphRenderer.GetZoomTier(Viewport.Zoom));
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
                UpdateSceneNodeLayout(node, nodeModel, null, graphRenderer.GetZoomTier(Viewport.Zoom));
                node.HasWarning = false;
            }
        }

        PopulateTrafficReports(timeline, allocationList);
        PopulateRouteReports(timeline, edgeLoads);
        PopulateNodePressureReports(timeline);
        PopulateQuickMetrics(timeline, edgeLoads);
        RefreshNetworkAnalyticsPieChartData();
        ApplyIsochroneVisuals();
        RefreshInspector();
        UpdateExplanationForSelection();
        NotifyVisualChanged();
    }

    private void PopulateTopIssues(IReadOnlyList<NetworkIssue> issues)
    {
        TopIssues.Clear();
        TopIssueAdvisories.Clear();
        var unmappedIssueCount = 0;
        foreach (var issue in issues)
        {
            var targetId = string.IsNullOrWhiteSpace(issue.TargetId) ? null : issue.TargetId.Trim();
            var node = ResolveNodeIssueTarget(issue, targetId);
            var edge = ResolveEdgeIssueTarget(issue, targetId);
            var viewModel = node is not null
                ? CreateNodeTopIssue(issue.Title, issue.Explanation, node)
                : CreateEdgeTopIssue(issue.Title, issue.Explanation, edge);
            if (viewModel is null)
            {
                unmappedIssueCount++;
                TopIssueAdvisories.Add($"{issue.Title}: {issue.Explanation}");
                continue;
            }

            TopIssues.Add(viewModel);
        }

        TopIssueUnmappedSummary = unmappedIssueCount > 0
            ? "Some network-wide issues are not linked to a node or route."
            : string.Empty;
        SelectedTopIssue = null;
        SelectedIssueBreadcrumb = "Issue → (none selected)";
    }

    private TopIssueViewModel? CreateNodeTopIssue(
        string title,
        string detail,
        NodeModel? node)
    {
        if (node is null)
        {
            return null;
        }

        var nodeLabel = string.IsNullOrWhiteSpace(node.Name) ? node.Id : node.Name;
        return new TopIssueViewModel
        {
            Title = title,
            Detail = detail,
            TargetKind = TopIssueTargetKind.Node,
            NodeId = node.Id,
            NodeDisplayName = nodeLabel,
            Breadcrumb = $"Issue → Node {nodeLabel}"
        };
    }

    private TopIssueViewModel? CreateEdgeTopIssue(
        string title,
        string detail,
        EdgeModel? edge)
    {
        if (edge is null)
        {
            return null;
        }

        var from = network.Nodes.FirstOrDefault(node => Comparer.Equals(node.Id, edge.FromNodeId));
        var to = network.Nodes.FirstOrDefault(node => Comparer.Equals(node.Id, edge.ToNodeId));
        var fromLabel = string.IsNullOrWhiteSpace(from?.Name) ? edge.FromNodeId : from!.Name;
        var toLabel = string.IsNullOrWhiteSpace(to?.Name) ? edge.ToNodeId : to!.Name;
        return new TopIssueViewModel
        {
            Title = title,
            Detail = detail,
            TargetKind = TopIssueTargetKind.Edge,
            EdgeId = edge.Id,
            FromNodeName = fromLabel,
            ToNodeName = toLabel,
            Breadcrumb = $"Issue → Route {fromLabel} → {toLabel}"
        };
    }

    private NodeModel? ResolveNodeIssueTarget(NetworkIssue issue, string? targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return null;
        }

        var node = network.Nodes.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, targetId));
        if (node is not null)
        {
            return node;
        }

        if (issue.Type is NetworkIssueType.StarvedNode or NetworkIssueType.IsolatedNode)
        {
            node = network.Nodes.FirstOrDefault(candidate => Comparer.Equals(candidate.Name, targetId));
        }

        return node;
    }

    private EdgeModel? ResolveEdgeIssueTarget(NetworkIssue issue, string? targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return null;
        }

        var edge = network.Edges.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, targetId));
        if (edge is not null)
        {
            return edge;
        }

        if (issue.Type is NetworkIssueType.CongestedEdge or NetworkIssueType.HighCostRoute)
        {
            edge = network.Edges.FirstOrDefault(candidate => Comparer.Equals(candidate.FromNodeId, targetId) || Comparer.Equals(candidate.ToNodeId, targetId));
        }

        return edge;
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
                PriceSummary = BuildTrafficPriceSummary(trafficType),
                PlannedQuantity = ReportExportService.FormatNumber(planned),
                DeliveredQuantity = ReportExportService.FormatNumber(delivered),
                UnmetDemand = ReportExportService.FormatNumber(unmetDemand),
                Backlog = ReportExportService.FormatNumber(backlog)
            });
        }
    }

    private string BuildTrafficPriceSummary(string trafficType)
    {
        var allocations = GetCurrentAllocations()
            .Where(allocation => Comparer.Equals(allocation.TrafficType, trafficType))
            .ToList();
        var productionPrices = allocations.Count > 0
            ? allocations
                .Where(allocation => allocation.Quantity > 0d)
                .GroupBy(allocation => allocation.ProducerNodeId, Comparer)
                .Select(group => WeightedAverage(group, allocation => allocation.SourceUnitCostPerUnit))
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .Distinct()
                .OrderBy(value => value)
                .Select(value => ReportExportService.FormatNumber(value))
                .ToList()
            : network.Nodes
            .SelectMany(node => node.TrafficProfiles)
            .Where(profile => profile.Production > 0d && Comparer.Equals(profile.TrafficType, trafficType))
            .Select(profile => Math.Max(0d, profile.UnitPrice))
            .Distinct()
            .OrderBy(value => value)
            .Select(value => ReportExportService.FormatNumber(value))
            .ToList();
        var consumptionPrices = allocations.Count > 0
            ? allocations
                .Where(allocation => allocation.Quantity > 0d)
                .GroupBy(allocation => allocation.ConsumerNodeId, Comparer)
                .Select(group => WeightedAverage(group, allocation => allocation.DeliveredCostPerUnit))
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .Distinct()
                .OrderBy(value => value)
                .Select(value => ReportExportService.FormatNumber(value))
                .ToList()
            : network.Nodes
            .SelectMany(node => node.TrafficProfiles)
            .Where(profile => profile.Consumption > 0d && Comparer.Equals(profile.TrafficType, trafficType))
            .Select(profile => Math.Max(0d, profile.UnitPrice))
            .Distinct()
            .OrderBy(value => value)
            .Select(value => ReportExportService.FormatNumber(value))
            .ToList();

        return $"{FormatPriceList(productionPrices)}:{FormatPriceList(consumptionPrices)}";
    }

    private IReadOnlyList<RouteAllocation> GetCurrentAllocations()
    {
        if (lastTimelineStepResult is not null)
        {
            return lastTimelineStepResult.Allocations;
        }

        return lastOutcomes.SelectMany(outcome => outcome.Allocations).ToList();
    }

    private static double? WeightedAverage(IEnumerable<RouteAllocation> allocations, Func<RouteAllocation, double> valueSelector)
    {
        var materialized = allocations.Where(allocation => allocation.Quantity > 0d).ToList();
        var quantity = materialized.Sum(allocation => allocation.Quantity);
        if (quantity <= 0d)
        {
            return null;
        }

        return materialized.Sum(allocation => valueSelector(allocation) * allocation.Quantity) / quantity;
    }

    private static string FormatPriceList(IReadOnlyList<string> prices) => prices.Count switch
    {
        0 => "-",
        1 => prices[0],
        _ => string.Join("/", prices)
    };

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
                CommodityPrices = BuildNodeCommodityPriceSummary(node),
                PressureScore = pressure.Score > 0d ? ReportExportService.FormatNumber(pressure.Score) : "None",
                TopCause = pressure.Score > 0d ? BuildTopCauseText(pressure.TopCause) : "None",
                UnmetNeed = unmetNeed
            });
        }
    }

    private string BuildNodeCommodityPriceSummary(NodeModel node)
    {
        var prices = node.TrafficProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.TrafficType) && (profile.Production > 0d || profile.Consumption > 0d))
            .OrderBy(profile => profile.TrafficType, Comparer)
            .Select(profile =>
            {
                var productionPrice = profile.Production > 0d ? FormatProductionPointPrice(node.Id, profile) : "-";
                var consumptionPrice = profile.Consumption > 0d ? FormatConsumptionPointPrice(node.Id, profile) : "-";
                return $"{profile.TrafficType} {productionPrice}:{consumptionPrice}";
            })
            .ToList();
        return prices.Count == 0 ? "None" : string.Join(", ", prices);
    }

    private string FormatProductionPointPrice(string nodeId, NodeTrafficProfile profile)
    {
        var allocations = GetCurrentAllocations()
            .Where(allocation => Comparer.Equals(allocation.ProducerNodeId, nodeId) && Comparer.Equals(allocation.TrafficType, profile.TrafficType))
            .ToList();
        var computed = WeightedAverage(allocations, allocation => allocation.SourceUnitCostPerUnit);
        return ReportExportService.FormatNumber(computed ?? Math.Max(0d, profile.UnitPrice));
    }

    private string FormatConsumptionPointPrice(string nodeId, NodeTrafficProfile profile)
    {
        var allocations = GetCurrentAllocations()
            .Where(allocation => Comparer.Equals(allocation.ConsumerNodeId, nodeId) && Comparer.Equals(allocation.TrafficType, profile.TrafficType))
            .ToList();
        var computed = WeightedAverage(allocations, allocation => allocation.DeliveredCostPerUnit);
        return ReportExportService.FormatNumber(computed ?? Math.Max(0d, profile.UnitPrice));
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
        TopIssues.Clear();
        TopIssueAdvisories.Clear();
        TopIssueUnmappedSummary = string.Empty;
        SelectedTopIssue = null;
        SelectedIssueBreadcrumb = "Issue → (none selected)";
        RefreshNetworkAnalyticsPieChartData();
    }

    private void RefreshNetworkAnalyticsPieChartData()
    {
        AgentStatusDistributionData = BuildAgentStatusDistributionData();
        NodeUtilizationMixData = BuildNodeUtilizationMixData();
    }

    private IReadOnlyList<PieChartSegmentViewModel> BuildAgentStatusDistributionData()
    {
        var latestActions = agentActionLogger
            .GetAll()
            .GroupBy(entry => entry.AgentId)
            .Select(group => group.OrderByDescending(entry => entry.Timestamp).First())
            .ToList();
        var moving = latestActions.Count(entry => entry.ActionType.Contains("move", StringComparison.OrdinalIgnoreCase));
        var processing = latestActions.Count(entry => entry.ActionType.Contains("process", StringComparison.OrdinalIgnoreCase)
            || entry.ActionType.Contains("load", StringComparison.OrdinalIgnoreCase)
            || entry.ActionType.Contains("unload", StringComparison.OrdinalIgnoreCase));
        var knownAgentCount = Math.Max(SimulationActors.Count, latestActions.Count);
        var idle = Math.Max(0, knownAgentCount - moving - processing);
        if (knownAgentCount == 0)
        {
            idle = 1;
        }

        return
        [
            new PieChartSegmentViewModel("Moving", moving),
            new PieChartSegmentViewModel("Idle", idle),
            new PieChartSegmentViewModel("Processing", processing)
        ];
    }

    private IReadOnlyList<PieChartSegmentViewModel> BuildNodeUtilizationMixData()
    {
        if (network.Nodes.Count == 0)
        {
            return
            [
                new PieChartSegmentViewModel("Balanced", 0d),
                new PieChartSegmentViewModel("Low", 1d),
                new PieChartSegmentViewModel("High", 0d)
            ];
        }

        var low = 0;
        var balanced = 0;
        var high = 0;
        foreach (var node in network.Nodes)
        {
            var score = lastTimelineStepResult?.NodePressureById.GetValueOrDefault(node.Id).Score ?? 0d;
            if (score < 0.25d)
            {
                low++;
            }
            else if (score < 0.7d)
            {
                balanced++;
            }
            else
            {
                high++;
            }
        }

        return
        [
            new PieChartSegmentViewModel("Balanced", balanced),
            new PieChartSegmentViewModel("Low", low),
            new PieChartSegmentViewModel("High", high)
        ];
    }

    private void RefreshInspector()
    {
        EnsureEdgeEditorSelectionState();
        var selectedNodeIds = Scene.Selection.SelectedNodeIds.ToList();
        var selectedEdgeIds = Scene.Selection.SelectedEdgeIds.ToList();
        UpdateExplanationForSelection(selectedNodeIds, selectedEdgeIds);
        currentInspectorEditMode = ResolveInspectorEditMode(selectedNodeIds.Count, selectedEdgeIds.Count);
        Raise(nameof(SelectionSummary));
        Raise(nameof(SelectedNodeIdText));
        Raise(nameof(SelectedNodeRoleSummaryText));
        Raise(nameof(SelectedNodeLatitudeText));
        Raise(nameof(SelectedNodeLongitudeText));
        Raise(nameof(SessionSubtitle));
        Raise(nameof(CurrentInspectorEditMode));
        Raise(nameof(IsEditingNetwork));
        Raise(nameof(IsEditingNode));
        Raise(nameof(IsEditingEdge));
        Raise(nameof(IsEditingSelection));
        Raise(nameof(CurrentWorkspaceMode));
        Raise(nameof(IsNormalWorkspaceMode));
        Raise(nameof(IsEdgeEditorWorkspaceMode));
        Raise(nameof(IsScenarioEditorWorkspaceMode));
        Raise(nameof(IsOsmImportWorkspaceMode));
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        OpenSelectedEdgeEditorCommand.NotifyCanExecuteChanged();
        SaveEdgeEditorCommand.NotifyCanExecuteChanged();
        CancelEdgeEditorCommand.NotifyCanExecuteChanged();
        CloseScenarioEditorCommand.NotifyCanExecuteChanged();
        DeleteSelectedEdgeEditorCommand.NotifyCanExecuteChanged();
        AddNodeTrafficProfileCommand.NotifyCanExecuteChanged();
        DuplicateSelectedNodeTrafficProfileCommand.NotifyCanExecuteChanged();
        RemoveSelectedNodeTrafficProfileCommand.NotifyCanExecuteChanged();
        AddNodeProductionWindowCommand.NotifyCanExecuteChanged();
        RemoveSelectedNodeProductionWindowCommand.NotifyCanExecuteChanged();
        AddNodeConsumptionWindowCommand.NotifyCanExecuteChanged();
        RemoveSelectedNodeConsumptionWindowCommand.NotifyCanExecuteChanged();
        AddNodeInputRequirementCommand.NotifyCanExecuteChanged();
        RemoveSelectedNodeInputRequirementCommand.NotifyCanExecuteChanged();
        AddFacilityOriginCommand.NotifyCanExecuteChanged();
        ApplyInspectorCommand.NotifyCanExecuteChanged();
        AssignSelectedNodesToLayerCommand.NotifyCanExecuteChanged();
        AssignSelectedEdgesToLayerCommand.NotifyCanExecuteChanged();
        Raise(nameof(ApplyInspectorLabel));
        Raise(nameof(CanApplyInspectorEdits));
        Raise(nameof(NodeTrafficRoleValidationText));
        RaiseEdgeDisplayStateChanged();

        InspectorValidationText = string.Empty;
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
            RefreshEdgeEditorState();
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
            RefreshEdgeEditorState();
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
            RefreshEdgeEditorState();
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
        if (!IsEdgeEditorWorkspaceMode || edgeEditorSession is null || !Comparer.Equals(edgeEditorSession.EdgeId, edge.Id))
        {
            PopulateEdgeEditor(edge);
        }
        else
        {
            RefreshEdgeEditorState();
        }
        SelectedNodeTrafficProfiles.Clear();
        SelectedNodeTrafficProfileItem = null;
    }

    private void UpdateExplanationForSelection(IReadOnlyList<string>? selectedNodeIds = null, IReadOnlyList<string>? selectedEdgeIds = null)
    {
        if (lastOutcomes.Count == 0)
        {
            ExplanationTitle = "Why this item matters";
            ExplanationSummary = "Run a simulation to see constraints, delays, and unmet demand.";
            ExplanationCauses = [];
            ExplanationActions = [];
            ExplanationRelatedIssues = [];
            return;
        }

        var result = new SimulationResult { Outcomes = lastOutcomes };
        var nodeIds = selectedNodeIds ?? Scene.Selection.SelectedNodeIds.ToList();
        var edgeIds = selectedEdgeIds ?? Scene.Selection.SelectedEdgeIds.ToList();
        if (nodeIds.Count == 1)
        {
            var explain = explainabilityService.ExplainNode(network, result, nodeIds[0]);
            ExplanationTitle = "Why this node matters";
            ExplanationSummary = explain.Summary;
            ExplanationCauses = explain.Causes.Count == 0 ? ["No major issue detected for this item."] : explain.Causes;
            ExplanationActions = explain.SuggestedActions;
            ExplanationRelatedIssues = TopIssues
                .Where(issue => issue.TargetKind == TopIssueTargetKind.Node && string.Equals(issue.NodeId, nodeIds[0], StringComparison.OrdinalIgnoreCase))
                .Select(issue => issue.Title)
                .ToList();
            return;
        }

        if (edgeIds.Count == 1)
        {
            var explain = explainabilityService.ExplainEdge(network, result, edgeIds[0]);
            ExplanationTitle = "Why this route matters";
            ExplanationSummary = explain.Summary;
            ExplanationCauses = explain.Causes.Count == 0 ? ["No major issue detected for this item."] : explain.Causes;
            ExplanationActions = explain.SuggestedActions;
            ExplanationRelatedIssues = TopIssues
                .Where(issue => issue.TargetKind == TopIssueTargetKind.Edge && string.Equals(issue.EdgeId, edgeIds[0], StringComparison.OrdinalIgnoreCase))
                .Select(issue => issue.Title)
                .ToList();
            return;
        }

        ExplanationTitle = "Why this item matters";
        ExplanationSummary = "No major issue detected for this item.";
        ExplanationCauses = [];
        ExplanationActions = [];
        ExplanationRelatedIssues = [];
    }

    private void PopulateNetworkEditor()
    {
        ClearInspectorTargets();
        NetworkNameText = network.Name;
        NetworkDescriptionText = network.Description;
        NetworkTimelineLoopLengthText = network.TimelineLoopLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private void PopulateBulkEditor(IEnumerable<string> selectedNodeIds)
    {
        var targetNodeIds = selectedNodeIds
            .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
            .Distinct(Comparer)
            .ToList();
        var selectedNodes = network.Nodes.Where(node => targetNodeIds.Contains(node.Id, Comparer)).ToList();

        isRefreshingInspectorDrafts = true;
        try
        {
            BulkDraft.LoadFrom(targetNodeIds, selectedNodes);
        }
        finally
        {
            isRefreshingInspectorDrafts = false;
        }
    }

    private void PopulateNodeEditor(NodeModel node)
    {
        isRefreshingInspectorDrafts = true;
        try
        {
            NodeDraft.LoadFrom(node);
        }
        finally
        {
            isRefreshingInspectorDrafts = false;
        }

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
            SelectedNodeProductionWindows.Clear();
            SelectedNodeConsumptionWindows.Clear();
            SelectedNodeInputRequirements.Clear();
            SelectedNodeProductionWindowItem = null;
            SelectedNodeConsumptionWindowItem = null;
            SelectedNodeInputRequirementItem = null;
            NodeTrafficTypeText = TrafficTypeOptions.FirstOrDefault() ?? string.Empty;
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

        NodeTrafficTypeText = string.IsNullOrWhiteSpace(profile.TrafficType)
            ? (TrafficTypeOptions.FirstOrDefault() ?? string.Empty)
            : profile.TrafficType;
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
        SelectedNodeProductionWindows.Clear();
        foreach (var window in profile.ProductionWindows)
        {
            SelectedNodeProductionWindows.Add(new PeriodWindowEditorRow(window));
        }

        SelectedNodeConsumptionWindows.Clear();
        foreach (var window in profile.ConsumptionWindows)
        {
            SelectedNodeConsumptionWindows.Add(new PeriodWindowEditorRow(window));
        }

        SelectedNodeInputRequirements.Clear();
        foreach (var requirement in profile.InputRequirements)
        {
            SelectedNodeInputRequirements.Add(new InputRequirementEditorRow(requirement));
        }

        SelectedNodeProductionWindowItem = SelectedNodeProductionWindows.FirstOrDefault();
        SelectedNodeConsumptionWindowItem = SelectedNodeConsumptionWindows.FirstOrDefault();
        SelectedNodeInputRequirementItem = SelectedNodeInputRequirements.FirstOrDefault();
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
        isRefreshingInspectorDrafts = true;
        try
        {
            EdgeDraft.LoadFrom(edge);
        }
        finally
        {
            isRefreshingInspectorDrafts = false;
        }

        PopulateEdgePermissionRows(edge);
        RefreshEdgeEditorState();
    }

    private void ClearInspectorTargets()
    {
        isRefreshingInspectorDrafts = true;
        try
        {
            NodeDraft.Clear();
            EdgeDraft.Clear();
            BulkDraft.Clear();
        }
        finally
        {
            isRefreshingInspectorDrafts = false;
        }
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
        RaiseTrafficTypeOptionsChanged();
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
        foreach (var rule in edge.TrafficPermissions.OrderBy(rule => rule.TrafficType, Comparer))
        {
            var effective = BuildEdgePermissionEffectiveSummary(edge, rule.TrafficType);
            SelectedEdgePermissionRows.Add(new PermissionRuleEditorRow(rule.TrafficType, supportsOverrideToggle: true, rule, effective));
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
            MarkDirty();
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
        var nodeId = NodeDraft.TargetNodeId ?? throw new InvalidOperationException("Select one node to edit.");
        var node = network.Nodes.First(model => Comparer.Equals(model.Id, nodeId));
        if (IsLockedLayer(node.LayerId))
        {
            throw new InvalidOperationException("This item is on a locked layer. Unlock the layer to edit it.");
        }
        var requestedNodeId = string.IsNullOrWhiteSpace(NodeIdText) ? node.Id : NodeIdText.Trim();
        if (!Comparer.Equals(node.Id, requestedNodeId) &&
            network.Nodes.Any(model => !ReferenceEquals(model, node) && Comparer.Equals(model.Id, requestedNodeId)))
        {
            throw new InvalidOperationException("Node id must be unique.");
        }

        if (!Comparer.Equals(node.Id, requestedNodeId))
        {
            foreach (var edge in network.Edges)
            {
                if (Comparer.Equals(edge.FromNodeId, node.Id))
                {
                    edge.FromNodeId = requestedNodeId;
                }

                if (Comparer.Equals(edge.ToNodeId, node.Id))
                {
                    edge.ToNodeId = requestedNodeId;
                }
            }
        }

        node.Id = requestedNodeId;
        node.Name = string.IsNullOrWhiteSpace(NodeNameText) ? node.Id : NodeNameText.Trim();
        node.X = ParseOptionalDouble(NodeXText, "Enter an X position, or leave it blank.");
        node.Y = ParseOptionalDouble(NodeYText, "Enter a Y position, or leave it blank.");
        node.PlaceType = NormalizeOptionalText(NodePlaceTypeText);
        node.LoreDescription = NormalizeOptionalText(NodeDescriptionText);
        node.TranshipmentCapacity = ParseOptionalNonNegativeDouble(NodeTranshipmentCapacityText, "Enter a transhipment capacity of 0 or more, or leave it blank.");
        node.Shape = NodeShape;
        node.NodeKind = NodeKind;
        node.ReferencedSubnetworkId = NormalizeOptionalText(NodeReferencedSubnetworkIdText);
        node.IsExternalInterface = NodeIsExternalInterface;
        node.InterfaceName = NormalizeOptionalText(NodeInterfaceNameText);
        node.ControllingActor = NormalizeOptionalText(NodeControllingActorText);
        node.Tags = SplitCommaSeparatedText(NodeTagsText);

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
                throw new InvalidOperationException("Select a traffic type before saving.");
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
            profile.ProductionWindows = BuildPeriodWindows(
                SelectedNodeProductionWindows,
                "production window");
            profile.ConsumptionWindows = BuildPeriodWindows(
                SelectedNodeConsumptionWindows,
                "consumption window");
            profile.InputRequirements = BuildInputRequirements(SelectedNodeInputRequirements);
        }

        PopulateNodeEditor(node);
        StatusText = $"Updated node '{node.Name}'.";
    }

    private void ApplyEdgeEdits()
    {
        var edgeId = EdgeDraft.TargetEdgeId ?? throw new InvalidOperationException("Select one route to edit.");
        var edge = network.Edges.First(model => Comparer.Equals(model.Id, edgeId));
        if (IsLockedLayer(edge.LayerId))
        {
            throw new InvalidOperationException("This item is on a locked layer. Unlock the layer to edit it.");
        }
        edge.RouteType = NormalizeOptionalText(EdgeRouteTypeText);
        edge.Time = ParseNonNegativeDouble(EdgeTimeText, "Enter travel time as 0 or more.");
        edge.Cost = ParseNonNegativeDouble(EdgeCostText, "Enter travel cost as 0 or more.");
        edge.Capacity = ParseOptionalNonNegativeDouble(EdgeCapacityText, "Enter route capacity as 0 or more, or leave it blank.");
        edge.IsBidirectional = EdgeIsBidirectional;
        edge.TrafficPermissions = SelectedEdgePermissionRows.Select(row => row.ToModel(edge.Capacity)).ToList();
        UpdateEffectivePermissionSummaries(edge);
        StatusText = $"Updated route '{edge.Id}'.";
    }

    private void SaveEdgeEditor()
    {
        RefreshEdgeEditorState();
        if (!CanSaveEdgeEditor)
        {
            InspectorValidationText = EdgeEditorValidationText;
            StatusText = EdgeEditorValidationText;
            return;
        }

        ApplyEdgeEdits();
        CurrentWorkspaceMode = WorkspaceMode.Normal;
        edgeEditorSession = null;
        BuildSceneFromNetwork();
        RefreshInspector();
        MarkDirty();
        NotifyVisualChanged();
    }

    private void CancelEdgeEditor()
    {
        var edge = GetSelectedEdgeModel();
        if (edgeEditorSession is not null)
        {
            PopulateEdgeEditor(edge ?? edgeEditorSession.Snapshot);
        }

        CurrentWorkspaceMode = WorkspaceMode.Normal;
        edgeEditorSession = null;
        RefreshInspector();
        NotifyVisualChanged();
        StatusText = edge is null
            ? "Cancelled route editing."
            : $"Cancelled changes for route '{edge.Id}'.";
    }

    private void ApplyBulkEdits()
    {
        var selectedNodeIds = BulkDraft.TargetNodeIds.ToHashSet(Comparer);
        var updatedPlaceType = NormalizeOptionalText(BulkPlaceTypeText);
        var updatedCapacity = ParseOptionalNonNegativeDouble(BulkTranshipmentCapacityText, "Enter a transhipment capacity of 0 or more, or leave it blank.");
        foreach (var node in network.Nodes.Where(node => selectedNodeIds.Contains(node.Id)))
        {
            node.PlaceType = updatedPlaceType;
            node.TranshipmentCapacity = updatedCapacity;
        }

        StatusText = "Applied bulk node changes.";
    }

    public bool WouldBulkApplyTrafficRoleOverwrite(bool applyToAllNodes, string trafficType)
    {
        var normalizedTrafficType = string.IsNullOrWhiteSpace(trafficType) ? string.Empty : trafficType.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTrafficType))
        {
            return false;
        }

        foreach (var node in ResolveTrafficRoleTargets(applyToAllNodes))
        {
            if (node.TrafficProfiles.Any(profile => Comparer.Equals(profile.TrafficType, normalizedTrafficType)))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryBulkApplyTrafficRole(string roleName, string trafficType, bool applyToAllNodes, out string statusMessage)
    {
        statusMessage = string.Empty;
        var normalizedTrafficType = string.IsNullOrWhiteSpace(trafficType) ? string.Empty : trafficType.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTrafficType))
        {
            statusMessage = "Select a traffic type before applying a role.";
            return false;
        }

        if (!network.TrafficTypes.Any(definition => Comparer.Equals(definition.Name, normalizedTrafficType)))
        {
            statusMessage = "Traffic type no longer exists. Choose a valid type.";
            return false;
        }

        if (!NodeTrafficRoleCatalog.TryParseFlags(roleName, out _))
        {
            statusMessage = "Choose a valid traffic role.";
            return false;
        }

        var targets = ResolveTrafficRoleTargets(applyToAllNodes);
        if (targets.Count == 0)
        {
            statusMessage = "No nodes available for bulk traffic role apply.";
            return false;
        }

        foreach (var node in targets)
        {
            var profile = node.TrafficProfiles.FirstOrDefault(item => Comparer.Equals(item.TrafficType, normalizedTrafficType));
            if (profile is null)
            {
                profile = new NodeTrafficProfile { TrafficType = normalizedTrafficType };
                node.TrafficProfiles.Add(profile);
            }

            profile.TrafficType = normalizedTrafficType;
            NodeTrafficRoleCatalog.ApplyRoleSelection(new NodeTrafficRoleAdapter(profile), roleName);
        }

        BuildSceneFromNetwork();
        RefreshInspector();
        MarkDirty();
        NotifyVisualChanged();

        statusMessage = applyToAllNodes
            ? $"Applied traffic role '{roleName}' to all {targets.Count} nodes."
            : $"Applied traffic role '{roleName}' to {targets.Count} selected nodes.";
        StatusText = statusMessage;
        return true;
    }

    private List<NodeModel> ResolveTrafficRoleTargets(bool applyToAllNodes)
    {
        if (applyToAllNodes)
        {
            return network.Nodes.ToList();
        }

        var selectedNodeIds = Scene.Selection.SelectedNodeIds.ToHashSet(Comparer);
        return network.Nodes.Where(node => selectedNodeIds.Contains(node.Id)).ToList();
    }

    private void AddNodeTrafficProfile()
    {
        var nodeId = NodeDraft.TargetNodeId;
        if (nodeId is null)
        {
            StatusText = "Select a node to add a traffic role.";
            return;
        }

        if (network.TrafficTypes.Count == 0)
        {
            StatusText = "Create a traffic type before adding a role.";
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
        MarkDirty();
        StatusText = "Added a new traffic role to the selected node.";
        PreviewSelectedNodeSceneLayout();
    }

    private void AddNodeProductionWindow()
    {
        var row = new PeriodWindowEditorRow();
        SelectedNodeProductionWindows.Add(row);
        SelectedNodeProductionWindowItem = row;
    }

    private void RemoveSelectedNodeProductionWindow()
    {
        if (SelectedNodeProductionWindowItem is null)
        {
            return;
        }

        var index = SelectedNodeProductionWindows.IndexOf(SelectedNodeProductionWindowItem);
        SelectedNodeProductionWindows.Remove(SelectedNodeProductionWindowItem);
        SelectedNodeProductionWindowItem = index >= 0 && index < SelectedNodeProductionWindows.Count
            ? SelectedNodeProductionWindows[index]
            : SelectedNodeProductionWindows.LastOrDefault();
    }

    private void AddNodeConsumptionWindow()
    {
        var row = new PeriodWindowEditorRow();
        SelectedNodeConsumptionWindows.Add(row);
        SelectedNodeConsumptionWindowItem = row;
    }

    private void RemoveSelectedNodeConsumptionWindow()
    {
        if (SelectedNodeConsumptionWindowItem is null)
        {
            return;
        }

        var index = SelectedNodeConsumptionWindows.IndexOf(SelectedNodeConsumptionWindowItem);
        SelectedNodeConsumptionWindows.Remove(SelectedNodeConsumptionWindowItem);
        SelectedNodeConsumptionWindowItem = index >= 0 && index < SelectedNodeConsumptionWindows.Count
            ? SelectedNodeConsumptionWindows[index]
            : SelectedNodeConsumptionWindows.LastOrDefault();
    }

    private void AddNodeInputRequirement()
    {
        var trafficType = network.TrafficTypes.FirstOrDefault()?.Name ?? string.Empty;
        var row = new InputRequirementEditorRow
        {
            TrafficType = trafficType
        };
        SelectedNodeInputRequirements.Add(row);
        SelectedNodeInputRequirementItem = row;
    }

    private void RemoveSelectedNodeInputRequirement()
    {
        if (SelectedNodeInputRequirementItem is null)
        {
            return;
        }

        var index = SelectedNodeInputRequirements.IndexOf(SelectedNodeInputRequirementItem);
        SelectedNodeInputRequirements.Remove(SelectedNodeInputRequirementItem);
        SelectedNodeInputRequirementItem = index >= 0 && index < SelectedNodeInputRequirements.Count
            ? SelectedNodeInputRequirements[index]
            : SelectedNodeInputRequirements.LastOrDefault();
    }

    private void DuplicateSelectedNodeTrafficProfile()
    {
        var nodeId = NodeDraft.TargetNodeId;
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
        MarkDirty();
        StatusText = "Duplicated the selected traffic role.";
        PreviewSelectedNodeSceneLayout();
    }

    private void RemoveSelectedNodeTrafficProfile()
    {
        var nodeId = NodeDraft.TargetNodeId;
        if (nodeId is null || SelectedNodeTrafficProfileItem is null)
        {
            StatusText = "Select a node traffic role to remove.";
            return;
        }

        var node = network.Nodes.First(model => Comparer.Equals(model.Id, nodeId));
        node.TrafficProfiles.Remove(SelectedNodeTrafficProfileItem.Model);
        PopulateNodeEditor(node);
        MarkDirty();
        StatusText = "Removed the selected traffic role.";
        PreviewSelectedNodeSceneLayout();
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
        RaiseTrafficTypeOptionsChanged();
        RefreshInspector();
        MarkDirty();
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
        RaiseTrafficTypeOptionsChanged();
        RefreshInspector();
        MarkDirty();
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
            RaiseTrafficTypeOptionsChanged();
            RefreshInspector();
            TrafficValidationText = string.Empty;
            MarkDirty();
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

    private IReadOnlyList<string> GetPlaceTypeSuggestions()
    {
        return network.Nodes
            .Select(node => node.PlaceType)
            .Append("Draft place")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(Comparer)
            .OrderBy(value => value, Comparer)
            .ToList();
    }

    private IReadOnlyList<string> GetRouteTypeSuggestions()
    {
        return network.Edges
            .Select(edge => edge.RouteType)
            .Append("Proposed route")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(Comparer)
            .OrderBy(value => value, Comparer)
            .ToList();
    }

    private void RefreshDraftSuggestions()
    {
        var placeTypeSuggestions = GetPlaceTypeSuggestions();
        var routeTypeSuggestions = GetRouteTypeSuggestions();
        NodeDraft.UpdateSuggestions(placeTypeSuggestions);
        BulkDraft.UpdateSuggestions(placeTypeSuggestions);
        EdgeDraft.UpdateSuggestions(routeTypeSuggestions);
    }

    private static void ReplaceItems(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
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
            LockLayoutToMap = source.LockLayoutToMap,
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
                Latitude = node.Latitude,
                Longitude = node.Longitude,
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

    private static EdgeModel CloneEdge(EdgeModel source)
    {
        return new EdgeModel
        {
            Id = source.Id,
            FromNodeId = source.FromNodeId,
            ToNodeId = source.ToNodeId,
            Time = source.Time,
            Cost = source.Cost,
            Capacity = source.Capacity,
            IsBidirectional = source.IsBidirectional,
            RouteType = source.RouteType,
            TrafficPermissions = source.TrafficPermissions.Select(rule => new EdgeTrafficPermissionRule
            {
                TrafficType = rule.TrafficType,
                IsActive = rule.IsActive,
                Mode = rule.Mode,
                LimitKind = rule.LimitKind,
                LimitValue = rule.LimitValue
            }).ToList()
        };
    }

    private bool SyncNetworkNodePositionsFromScene()
    {
        var changed = false;
        foreach (var sceneNode in Scene.Nodes)
        {
            var model = network.Nodes.FirstOrDefault(node => Comparer.Equals(node.Id, sceneNode.Id));
            if (model is null)
            {
                continue;
            }

            if (IsMapLayoutLockedForGraph && model.Latitude.HasValue && model.Longitude.HasValue)
            {
                continue;
            }

            if (Math.Abs((model.X ?? 0d) - sceneNode.Bounds.CenterX) > 0.001d)
            {
                model.X = sceneNode.Bounds.CenterX;
                changed = true;
            }

            if (Math.Abs((model.Y ?? 0d) - sceneNode.Bounds.CenterY) > 0.001d)
            {
                model.Y = sceneNode.Bounds.CenterY;
                changed = true;
            }
        }

        return changed;
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

    private string BuildSelectedNodeIdText()
    {
        if (Scene.Selection.SelectedNodeIds.Count != 1)
        {
            return Scene.Selection.SelectedNodeIds.Count == 0 ? "None" : $"{Scene.Selection.SelectedNodeIds.Count} nodes";
        }

        var nodeId = Scene.Selection.SelectedNodeIds.First();
        var node = network.Nodes.FirstOrDefault(model => Comparer.Equals(model.Id, nodeId));
        return node is null || string.IsNullOrWhiteSpace(node.Name) || Comparer.Equals(node.Name, node.Id)
            ? nodeId
            : $"{node.Name} ({node.Id})";
    }

    private string BuildSelectedNodeRoleSummaryText()
    {
        if (Scene.Selection.SelectedNodeIds.Count != 1)
        {
            return Scene.Selection.SelectedNodeIds.Count == 0 ? "No node selected" : "Multiple nodes selected";
        }

        var node = network.Nodes.FirstOrDefault(model => Comparer.Equals(model.Id, Scene.Selection.SelectedNodeIds.First()));
        if (node is null)
        {
            return "Node not found";
        }

        var parts = new List<string>();
        var producers = FormatSelectedNodeTrafficList(node.TrafficProfiles.Where(profile => profile.Production > 0d), profile => profile.Production);
        var consumers = FormatSelectedNodeTrafficList(node.TrafficProfiles.Where(profile => profile.Consumption > 0d), profile => profile.Consumption);
        var stores = FormatSelectedNodeTrafficList(node.TrafficProfiles.Where(profile => profile.IsStore), profile => profile.StoreCapacity.GetValueOrDefault());

        if (node.NodeKind == NodeKind.CompositeSubnetwork)
        {
            parts.Add($"Composite {node.ReferencedSubnetworkId ?? "(unassigned)"}");
        }

        if (!string.IsNullOrWhiteSpace(producers))
        {
            parts.Add($"Produces {producers}");
        }

        if (!string.IsNullOrWhiteSpace(consumers))
        {
            parts.Add($"Needs {consumers}");
        }

        if (!string.IsNullOrWhiteSpace(stores))
        {
            parts.Add($"Stores {stores}");
        }

        if (node.TrafficProfiles.Any(profile => profile.CanTransship))
        {
            parts.Add(node.TranshipmentCapacity.HasValue
                ? $"Transships up to {ReportExportService.FormatNumber(node.TranshipmentCapacity)}"
                : "Can transship");
        }

        return parts.Count == 0 ? "No configured traffic role" : string.Join("; ", parts);
    }

    private static string FormatSelectedNodeTrafficList(IEnumerable<NodeTrafficProfile> profiles, Func<NodeTrafficProfile, double> valueSelector) =>
        string.Join(", ", profiles
            .Select(profile => $"{ReportExportService.FormatNumber(valueSelector(profile))} {profile.TrafficType}")
            .Where(text => !string.IsNullOrWhiteSpace(text)));

    private static InspectorEditMode ResolveInspectorEditMode(int selectedNodeCount, int selectedEdgeCount)
    {
        var count = selectedNodeCount + selectedEdgeCount;
        if (count == 0)
        {
            return InspectorEditMode.Network;
        }

        if (selectedNodeCount == 1 && selectedEdgeCount == 0)
        {
            return InspectorEditMode.Node;
        }

        if (selectedEdgeCount == 1 && selectedNodeCount == 0)
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
        RaiseTrafficTypeOptionsChanged();
        StatusText = "Created default traffic type 'general'.";
    }

    public void SelectNodeForEdit(string nodeId, bool focusTrafficRoles = false)
    {
        SelectNode(nodeId);
        FocusInspectorSection(InspectorTabTarget.Selection, focusTrafficRoles ? InspectorSectionTarget.TrafficRoles : InspectorSectionTarget.Node);
    }

    public void SelectRouteForEdit(string edgeId)
    {
        SelectEdge(edgeId);
        FocusInspectorSection(InspectorTabTarget.Selection, InspectorSectionTarget.Route);
    }

    private Dictionary<string, double> BuildEdgeLoads(
        IReadOnlyList<RouteAllocation> allocations,
        TemporalNetworkSimulationEngine.TemporalSimulationStepResult? timeline)
    {
        if (timeline is not null)
        {
            return Scene.Edges.ToDictionary(
                edge => edge.Id,
                edge => timeline.EdgeOccupancy.GetValueOrDefault(edge.Id, 0d),
                Comparer);
        }

        var edgeLoads = Scene.Edges.ToDictionary(edge => edge.Id, _ => 0d, Comparer);
        foreach (var allocation in allocations)
        {
            foreach (var edgeId in allocation.PathEdgeIds)
            {
                edgeLoads[edgeId] = edgeLoads.GetValueOrDefault(edgeId) + allocation.Quantity;
            }
        }

        return edgeLoads;
    }

    public void OpenRouteEditor(string edgeId)
    {
        SelectRouteForEdit(edgeId);
        EnterEdgeEditor();
    }

    public void EnterEdgeEditor()
    {
        var edge = GetSelectedEdgeModel();
        if (edge is null)
        {
            StatusText = "Select one route before opening the route editor.";
            return;
        }

        edgeEditorSession = new EdgeEditorSession
        {
            EdgeId = edge.Id,
            Snapshot = CloneEdge(edge)
        };
        PopulateEdgeEditor(edge);
        CurrentWorkspaceMode = WorkspaceMode.EdgeEditor;
        RefreshEdgeEditorState();
        StatusText = $"Editing route '{edge.Id}'.";
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

    public void DeleteSelectedEdgeFromEditor()
    {
        var edgeId = Scene.Selection.SelectedEdgeIds.FirstOrDefault();
        if (edgeId is null)
        {
            return;
        }

        CurrentWorkspaceMode = WorkspaceMode.Normal;
        edgeEditorSession = null;
        DeleteRouteById(edgeId);
    }

    public void AddEdgePermissionRule()
    {
        var row = SelectedEdgePermissionRows
            .Where(candidate => candidate.SupportsOverrideToggle && !candidate.IsActive)
            .OrderBy(candidate => candidate.TrafficType, Comparer)
            .FirstOrDefault();
        if (row is null)
        {
            StatusText = "All traffic types already have route rules.";
            return;
        }

        row.IsActive = true;
        RefreshEdgeEditorState();
        StatusText = $"Added route rule for '{row.TrafficType}'.";
    }

    public void RemoveEdgePermissionRule(PermissionRuleEditorRow row)
    {
        if (!SelectedEdgePermissionRows.Contains(row))
        {
            return;
        }

        row.IsActive = false;
        RefreshEdgeEditorState();
        StatusText = $"Removed route rule for '{row.TrafficType}'.";
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

    public void SelectNode(string nodeId, SelectionSource source = SelectionSource.User)
    {
        if (!network.Nodes.Any(node => Comparer.Equals(node.Id, nodeId)))
        {
            return;
        }

        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        Scene.Selection.SelectedNodeIds.Add(nodeId);
        RefreshInspector();
        NotifyVisualChanged();
    }

    public void SelectEdge(string edgeId, SelectionSource source = SelectionSource.User)
    {
        if (!network.Edges.Any(edge => Comparer.Equals(edge.Id, edgeId)))
        {
            return;
        }

        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Add(edgeId);
        RefreshInspector();
        NotifyVisualChanged();
    }

    public void OpenNodeEditor(string nodeId)
    {
        FocusInspectorSection(InspectorTabTarget.Selection, InspectorSectionTarget.Node);
        CurrentWorkspaceMode = WorkspaceMode.Normal;
    }

    public void OpenEdgeEditor(string edgeId)
    {
        FocusInspectorSection(InspectorTabTarget.Selection, InspectorSectionTarget.Route);
        CurrentWorkspaceMode = WorkspaceMode.Normal;
    }

    private void SelectTopIssue(TopIssueViewModel issue)
    {
        SelectedTopIssue = issue;
        SelectedIssueBreadcrumb = issue.Breadcrumb;
        var networkContainsNode = !string.IsNullOrWhiteSpace(issue.NodeId) && network.Nodes.Any(node => Comparer.Equals(node.Id, issue.NodeId));
        var networkContainsEdge = !string.IsNullOrWhiteSpace(issue.EdgeId) && network.Edges.Any(edge => Comparer.Equals(edge.Id, issue.EdgeId));
        var sceneContainsNode = !string.IsNullOrWhiteSpace(issue.NodeId) && Scene.FindNode(issue.NodeId) is not null;
        var sceneContainsEdge = !string.IsNullOrWhiteSpace(issue.EdgeId) && Scene.FindEdge(issue.EdgeId) is not null;
        Trace.WriteLine(
            "Top issue selected: " +
            $"TargetKind={issue.TargetKind}; " +
            $"NodeId={issue.NodeId ?? "(null)"}; " +
            $"EdgeId={issue.EdgeId ?? "(null)"}; " +
            $"Breadcrumb={issue.Breadcrumb}; " +
            $"Network contains node? {networkContainsNode}; " +
            $"Network contains edge? {networkContainsEdge}; " +
            $"Scene contains node? {sceneContainsNode}; " +
            $"Scene contains edge? {sceneContainsEdge}");

        if (issue.TargetKind == TopIssueTargetKind.Node && !string.IsNullOrWhiteSpace(issue.NodeId))
        {
            if (!networkContainsNode)
            {
                StatusText = "Issue target is unavailable in the loaded network.";
                NotifyVisualChanged();
                return;
            }

            SelectNodeForEdit(issue.NodeId);
            FocusElementFromIssue(issue.NodeId, null);
            OpenNodeEditor(issue.NodeId);
            SetPulse(issue.NodeId, null);
            StatusText = $"Focused issue target node '{issue.NodeId}'.";
        }
        else if (issue.TargetKind == TopIssueTargetKind.Edge && !string.IsNullOrWhiteSpace(issue.EdgeId))
        {
            if (!networkContainsEdge)
            {
                StatusText = "Issue target is unavailable in the loaded network.";
                NotifyVisualChanged();
                return;
            }

            SelectRouteForEdit(issue.EdgeId);
            FocusElementFromIssue(null, issue.EdgeId);
            OpenRouteEditor(issue.EdgeId);
            SetPulse(null, issue.EdgeId);
            StatusText = $"Focused issue target route '{issue.EdgeId}'.";
        }
        else
        {
            StatusText = "Issue target is unavailable in the current scene.";
        }

        NotifyVisualChanged();
    }

    public void FocusElementFromIssue(string? nodeId, string? edgeId)
    {
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            var node = EnsureIssueTargetInScene(nodeId, null).Node;
            if (node is not null)
            {
                CenterViewportOnPoint(new GraphPoint(node.Bounds.CenterX, node.Bounds.CenterY));
                if (Viewport.Zoom < 0.7d)
                {
                    Viewport.ZoomAt(new GraphPoint(LastViewportSize.Width / 2d, LastViewportSize.Height / 2d), LastViewportSize, 1.15d);
                }
            }

            NotifyVisualChanged();
            return;
        }

        if (string.IsNullOrWhiteSpace(edgeId))
        {
            return;
        }

        var ensured = EnsureIssueTargetInScene(null, edgeId);
        var edge = ensured.Edge;
        if (edge is not null)
        {
            var from = Scene.FindNode(edge.FromNodeId);
            var to = Scene.FindNode(edge.ToNodeId);
            if (from is not null && to is not null)
            {
                CenterViewportOnPoint(new GraphPoint(
                    (from.Bounds.CenterX + to.Bounds.CenterX) / 2d,
                    (from.Bounds.CenterY + to.Bounds.CenterY) / 2d));
                if (Viewport.Zoom < 0.62d)
                {
                    Viewport.ZoomAt(new GraphPoint(LastViewportSize.Width / 2d, LastViewportSize.Height / 2d), LastViewportSize, 1.12d);
                }
            }
        }

        NotifyVisualChanged();
    }

    private (GraphNodeSceneItem? Node, GraphEdgeSceneItem? Edge) EnsureIssueTargetInScene(string? nodeId, string? edgeId)
    {
        var sceneNode = string.IsNullOrWhiteSpace(nodeId) ? null : Scene.FindNode(nodeId);
        var sceneEdge = string.IsNullOrWhiteSpace(edgeId) ? null : Scene.FindEdge(edgeId);
        var missingNodeInScene = !string.IsNullOrWhiteSpace(nodeId) &&
                                 network.Nodes.Any(node => Comparer.Equals(node.Id, nodeId)) &&
                                 sceneNode is null;
        var missingEdgeInScene = !string.IsNullOrWhiteSpace(edgeId) &&
                                 network.Edges.Any(edge => Comparer.Equals(edge.Id, edgeId)) &&
                                 sceneEdge is null;
        if (!missingNodeInScene && !missingEdgeInScene)
        {
            return (sceneNode, sceneEdge);
        }

        BuildSceneFromNetwork();
        NotifyVisualChanged();
        return (
            string.IsNullOrWhiteSpace(nodeId) ? null : Scene.FindNode(nodeId),
            string.IsNullOrWhiteSpace(edgeId) ? null : Scene.FindEdge(edgeId));
    }

    private void SetPulse(string? nodeId, string? edgeId)
    {
        PulseNodeId = nodeId;
        PulseEdgeId = edgeId;
        PulseProgress = 1d;
        Scene.Selection.PulseNodeId = nodeId;
        Scene.Selection.PulseEdgeId = edgeId;
        Scene.Selection.PulseProgress = PulseProgress;
    }

    private void RefreshInsights()
    {
        NetworkInsights.Clear();
        if (lastOutcomes.Count == 0)
        {
            Raise(nameof(InsightsEmptyStateText));
            return;
        }

        foreach (var insight in networkInsightService.Generate(CreateVisualAnalyticsSnapshot()))
        {
            NetworkInsights.Add(insight);
        }

        Raise(nameof(InsightsEmptyStateText));
    }

    private string BuildIssueEdgeBreadcrumb(string edgeId)
    {
        var edge = network.Edges.FirstOrDefault(candidate => Comparer.Equals(candidate.Id, edgeId));
        if (edge is null)
        {
            return $"Issue → Edge {edgeId}";
        }

        var from = network.Nodes.FirstOrDefault(node => Comparer.Equals(node.Id, edge.FromNodeId));
        var to = network.Nodes.FirstOrDefault(node => Comparer.Equals(node.Id, edge.ToNodeId));
        var fromLabel = string.IsNullOrWhiteSpace(from?.Name) ? edge.FromNodeId : from!.Name;
        var toLabel = string.IsNullOrWhiteSpace(to?.Name) ? edge.ToNodeId : to!.Name;
        return $"Issue → Edge {fromLabel} → {toLabel}";
    }

    private void CenterViewportOnPoint(GraphPoint worldPoint)
    {
        var delta = new GraphVector(Viewport.Center.X - worldPoint.X, Viewport.Center.Y - worldPoint.Y);
        Viewport.Pan(delta);
    }

    private string BuildNodeTrafficRoleValidationText()
    {
        if (!IsEditingNode || SelectedNodeTrafficProfileItem is null)
        {
            return string.Empty;
        }

        if (TrafficTypeOptions.Count == 0)
        {
            return "Create a traffic type before adding a role.";
        }

        var normalized = string.IsNullOrWhiteSpace(NodeTrafficTypeText) ? string.Empty : NodeTrafficTypeText.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Select a traffic type before saving.";
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

    private void HandleSelectedEdgePermissionRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PermissionRuleEditorRow.EffectiveSummary) or nameof(PermissionRuleEditorRow.ValidationMessage))
        {
            return;
        }

        RefreshEdgeEditorState();
    }

    private void EnsureEdgeEditorSelectionState()
    {
        if (!IsEdgeEditorWorkspaceMode)
        {
            return;
        }

        var selectedEdgeId = Scene.Selection.SelectedEdgeIds.FirstOrDefault();
        if (selectedEdgeId is null ||
            Scene.Selection.SelectedNodeIds.Count > 0 ||
            edgeEditorSession is null ||
            !Comparer.Equals(edgeEditorSession.EdgeId, selectedEdgeId))
        {
            CurrentWorkspaceMode = WorkspaceMode.Normal;
            edgeEditorSession = null;
        }
    }

    private void RefreshEdgeEditorState()
    {
        if (isRefreshingEdgeEditorState)
        {
            return;
        }

        isRefreshingEdgeEditorState = true;
        try
        {
        var hasEdgeContext = GetEdgeSummaryContext() is not null || SelectedEdgePermissionRows.Count > 0;
        var capacityParse = TryParseOptionalNonNegativeDouble(
            EdgeCapacityText,
            "Enter route capacity as 0 or more, or leave it blank.");
        var timeParse = TryParseNonNegativeDouble(EdgeTimeText, "Enter travel time as 0 or more.");
        var costParse = TryParseNonNegativeDouble(EdgeCostText, "Enter travel cost as 0 or more.");

        EdgeTimeValidationText = hasEdgeContext ? timeParse.ValidationMessage : string.Empty;
        EdgeCostValidationText = hasEdgeContext ? costParse.ValidationMessage : string.Empty;
        EdgeCapacityValidationText = hasEdgeContext ? capacityParse.ValidationMessage : string.Empty;

        var knownTrafficTypes = GetKnownTrafficTypeNames();
        var duplicateTrafficTypes = SelectedEdgePermissionRows
            .Select(row => string.IsNullOrWhiteSpace(row.TrafficType) ? string.Empty : row.TrafficType.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name, Comparer)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(Comparer);
        var edgeCapacity = string.IsNullOrWhiteSpace(capacityParse.ValidationMessage) ? capacityParse.Value : null;
        foreach (var row in SelectedEdgePermissionRows)
        {
            var normalizedTrafficType = string.IsNullOrWhiteSpace(row.TrafficType) ? string.Empty : row.TrafficType.Trim();
            row.RefreshValidation(
                edgeCapacity,
                knownTrafficTypes,
                hasDuplicateTrafficType: !string.IsNullOrWhiteSpace(normalizedTrafficType) && duplicateTrafficTypes.Contains(normalizedTrafficType));
        }

        UpdateEdgeEditorPermissionSummaries(edgeCapacity);
        EdgeEditorValidationText = BuildEdgeEditorValidationText();
        RaiseEdgeDisplayStateChanged();
        SaveEdgeEditorCommand.NotifyCanExecuteChanged();
        DeleteSelectedEdgeEditorCommand.NotifyCanExecuteChanged();
        AddEdgePermissionRuleCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            isRefreshingEdgeEditorState = false;
        }
    }

    private void UpdateEdgeEditorPermissionSummaries(double? edgeCapacity)
    {
        var edge = GetEdgeSummaryContext();
        if (edge is null)
        {
            return;
        }

        var previewNetwork = BuildPreviewNetwork();
        var previewEdge = new EdgeModel
        {
            Id = edge.Id,
            FromNodeId = edge.FromNodeId,
            ToNodeId = edge.ToNodeId,
            Time = string.IsNullOrWhiteSpace(EdgeTimeValidationText) ? TryParseNonNegativeDouble(EdgeTimeText, string.Empty).Value.GetValueOrDefault(edge.Time) : edge.Time,
            Cost = string.IsNullOrWhiteSpace(EdgeCostValidationText) ? TryParseNonNegativeDouble(EdgeCostText, string.Empty).Value.GetValueOrDefault(edge.Cost) : edge.Cost,
            Capacity = string.IsNullOrWhiteSpace(EdgeCapacityValidationText) ? edgeCapacity : edge.Capacity,
            IsBidirectional = EdgeIsBidirectional,
            RouteType = NormalizeOptionalText(EdgeRouteTypeText) ?? edge.RouteType,
            TrafficPermissions = []
        };

        foreach (var row in SelectedEdgePermissionRows)
        {
            if (!string.IsNullOrWhiteSpace(row.ValidationMessage))
            {
                row.EffectiveSummary = "Fix this rule to preview its effect.";
                continue;
            }

            previewEdge.TrafficPermissions = SelectedEdgePermissionRows
                .Where(permission => string.IsNullOrWhiteSpace(permission.ValidationMessage))
                .Select(permission => permission.ToModel(previewEdge.Capacity))
                .ToList();
            row.EffectiveSummary = edgeTrafficPermissionResolver.Resolve(previewNetwork, previewEdge, row.TrafficType).Summary;
        }
    }

    private string BuildEdgeEditorValidationText()
    {
        if (!string.IsNullOrWhiteSpace(EdgeTimeValidationText))
        {
            return EdgeTimeValidationText;
        }

        if (!string.IsNullOrWhiteSpace(EdgeCostValidationText))
        {
            return EdgeCostValidationText;
        }

        if (!string.IsNullOrWhiteSpace(EdgeCapacityValidationText))
        {
            return EdgeCapacityValidationText;
        }

        var rowIssue = SelectedEdgePermissionRows.FirstOrDefault(row => !string.IsNullOrWhiteSpace(row.ValidationMessage));
        return rowIssue is null ? string.Empty : $"{rowIssue.TrafficType}: {rowIssue.ValidationMessage}";
    }

    private void RaiseEdgeDisplayStateChanged()
    {
        Raise(nameof(CanOpenSelectedEdgeEditor));
        Raise(nameof(CanSaveEdgeEditor));
        Raise(nameof(CanDeleteSelectedEdgeEditor));
        Raise(nameof(CanAddEdgePermissionRule));
        Raise(nameof(AvailableEdgeRuleTrafficTypes));
        Raise(nameof(VisibleEdgePermissionRows));
        Raise(nameof(EdgeTimeValidationText));
        Raise(nameof(EdgeCostValidationText));
        Raise(nameof(EdgeCapacityValidationText));
        Raise(nameof(EdgeEditorValidationText));
        Raise(nameof(SelectedEdgeIdText));
        Raise(nameof(SelectedEdgeSourceNodeText));
        Raise(nameof(SelectedEdgeTargetNodeText));
        Raise(nameof(SelectedEdgeDirectionSummaryText));
        Raise(nameof(SelectedEdgeRuleCountText));
        Raise(nameof(SelectedEdgeValidationStatusText));
        Raise(nameof(SelectedEdgePreviewTitleText));
        Raise(nameof(SelectedEdgePreviewTravelText));
        Raise(nameof(SelectedEdgePreviewCapacityText));
    }

    private EdgeModel? GetSelectedEdgeModel()
    {
        var edgeId = Scene.Selection.SelectedEdgeIds.FirstOrDefault();
        return edgeId is null
            ? null
            : network.Edges.FirstOrDefault(model => Comparer.Equals(model.Id, edgeId));
    }

    private NodeModel? GetNodeSummaryContext()
    {
        if (!string.IsNullOrWhiteSpace(NodeDraft.TargetNodeId))
        {
            var targeted = network.Nodes.FirstOrDefault(model => Comparer.Equals(model.Id, NodeDraft.TargetNodeId));
            if (targeted is not null)
            {
                return targeted;
            }
        }

        var nodeId = Scene.Selection.SelectedNodeIds.FirstOrDefault();
        return nodeId is null
            ? null
            : network.Nodes.FirstOrDefault(model => Comparer.Equals(model.Id, nodeId));
    }

    private EdgeModel? GetEdgeSummaryContext()
    {
        if (!string.IsNullOrWhiteSpace(EdgeDraft.TargetEdgeId))
        {
            var targeted = network.Edges.FirstOrDefault(model => Comparer.Equals(model.Id, EdgeDraft.TargetEdgeId));
            if (targeted is not null)
            {
                return targeted;
            }
        }

        return GetSelectedEdgeModel() ?? edgeEditorSession?.Snapshot;
    }

    private static (double? Value, string ValidationMessage) TryParseNonNegativeDouble(string text, string validationMessage)
    {
        if (double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0d)
        {
            return (parsed, string.Empty);
        }

        return (null, validationMessage);
    }

    private static (double? Value, string ValidationMessage) TryParseOptionalNonNegativeDouble(string text, string validationMessage)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, string.Empty);
        }

        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0d
            ? (parsed, string.Empty)
            : (null, validationMessage);
    }

    private void RaiseTrafficTypeOptionsChanged()
    {
        RaiseAutoCompleteOptionsChanged();
        Raise(nameof(TrafficTypeOptions));
        Raise(nameof(SelectedTrafficType));
        RaiseNodeTrafficRoleValidationStateChanged();
        AddNodeTrafficProfileCommand.NotifyCanExecuteChanged();
        EnsureAllowedTrafficTypesOnlyKnownValues();
        RebuildActorTrafficTypeRows();
        RefreshSelectedActorDisplayState();
    }

    private void RaiseAutoCompleteOptionsChanged()
    {
        Raise(nameof(TrafficTypeNameOptions));
        Raise(nameof(SubnetworkIdSuggestions));
        Raise(nameof(InterfaceNameSuggestions));
    }

    private void RaiseTrafficTypeDisplayStateChanged()
    {
        Raise(nameof(SelectedTrafficTypeSummaryText));
        Raise(nameof(SelectedTrafficTypeStatusText));
        Raise(nameof(SelectedTrafficTypeIssueCountText));
        Raise(nameof(SelectedTrafficTypeDefaultAccessSummaryText));
    }

    private string BuildSelectedTrafficTypeSummaryText()
    {
        if (SelectedTrafficDefinitionItem?.Model is null)
        {
            return "Select a traffic type to review its routing and behaviour.";
        }

        var name = string.IsNullOrWhiteSpace(TrafficNameText) ? "This traffic type" : TrafficNameText.Trim();
        var descriptionClause = string.IsNullOrWhiteSpace(TrafficDescriptionText)
            ? string.Empty
            : $" {TrafficDescriptionText.Trim()}";
        var perishabilityClause = string.IsNullOrWhiteSpace(TrafficPerishabilityText)
            ? "does not set a perishability limit"
            : $"has a perishability limit of {TrafficPerishabilityText.Trim()} periods";
        var bidClause = string.IsNullOrWhiteSpace(TrafficCapacityBidText)
            ? "uses the default bid behaviour"
            : $"bids {TrafficCapacityBidText.Trim()} per unit for constrained capacity";

        return $"{name}{descriptionClause} prefers {TrafficRoutingPreference}, uses {TrafficAllocationMode}, applies {TrafficFlowSplitPolicy}, {bidClause}, and {perishabilityClause}.";
    }

    private string BuildSelectedTrafficTypeDefaultAccessSummaryText()
    {
        var selectedName = SelectedTrafficDefinitionItem?.Model?.Name;
        if (string.IsNullOrWhiteSpace(selectedName))
        {
            return "Select a traffic type to review its default route access.";
        }

        var matchingRows = DefaultTrafficPermissionRows.Count(row => Comparer.Equals(row.TrafficType, selectedName));
        return matchingRows == 1
            ? $"1 default route-access row matches {selectedName}."
            : $"{matchingRows} default route-access rows match {selectedName}.";
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

    private IReadOnlyList<string> GetKnownPlaceTypes()
    {
        return network.Nodes
            .Select(node => node.PlaceType)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(Comparer)
            .OrderBy(value => value, Comparer)
            .ToList();
    }

    private IReadOnlyList<string> GetKnownRouteTypes()
    {
        return network.Edges
            .Select(edge => edge.RouteType)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(Comparer)
            .OrderBy(value => value, Comparer)
            .ToList();
    }

    private IReadOnlyList<string> GetKnownSubnetworkIds()
    {
        return (network.Subnetworks ?? [])
            .Select(subnetwork => subnetwork.Id)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(Comparer)
            .OrderBy(value => value, Comparer)
            .ToList();
    }

    private IReadOnlyList<string> GetKnownInterfaceNames()
    {
        return network.Nodes
            .Select(node => node.InterfaceName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(Comparer)
            .OrderBy(value => value, Comparer)
            .ToList();
    }

    private string BuildEdgePermissionEffectiveSummary(EdgeModel edge, string trafficType)
    {
        var previewNetwork = BuildPreviewNetwork();
        return edgeTrafficPermissionResolver.Resolve(previewNetwork, edge, trafficType).Summary;
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
                lines.Add((0, trafficType, new GraphNodeTextLine($"Produces {profile.Production:0.##} {trafficType} @ {FormatProductionPointPrice(node.Id, profile)}", true, false)));
            }

            if (profile.Consumption > 0d)
            {
                lines.Add((1, trafficType, new GraphNodeTextLine($"Consumes {profile.Consumption:0.##} {trafficType} @ {FormatConsumptionPointPrice(node.Id, profile)}", true, false)));
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

        var specialLines = new List<GraphNodeTextLine>();
        var unmetNeedLine = BuildSceneUnmetNeedLine(backlogByTraffic);
        if (!string.IsNullOrWhiteSpace(unmetNeedLine))
        {
            specialLines.Add(new GraphNodeTextLine(unmetNeedLine, true, true));
        }

        if (pressure is { Score: > 0d })
        {
            specialLines.Add(new GraphNodeTextLine($"Pressure {pressure.Value.Score:0.##}", true, true));
            var topCause = BuildTopCauseText(pressure.Value.TopCause);
            if (!string.IsNullOrWhiteSpace(topCause))
            {
                specialLines.Add(new GraphNodeTextLine($"Cause: {topCause}", false, true));
            }
        }

        var maxRoleLines = specialLines.Count switch
        {
            >= 3 => 1,
            2 => 2,
            1 => 3,
            _ => 4
        };

        var visible = ordered.Count <= maxRoleLines
            ? ordered
            : ordered.Take(maxRoleLines).Append(new GraphNodeTextLine($"+{ordered.Count - maxRoleLines} more roles", false, false)).ToList();

        visible.AddRange(specialLines);
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

    private void ApplyIsochroneVisuals()
    {
        if (IsFacilityPlanningMode)
        {
            ApplyFacilityPlanningVisuals();
            return;
        }

        if (!IsIsochroneModeEnabled || IsochroneNodes.Count == 0 || IsochroneThresholdMinutes <= 0d)
        {
            foreach (var node in Scene.Nodes)
            {
                node.VisualOpacity = 1d;
                node.StrokeColor = SKColor.Parse("#6AAED6");
                node.FillColor = SKColor.Parse("#163149");
            }

            foreach (var edge in Scene.Edges)
            {
                edge.VisualOpacity = 1d;
            }

            return;
        }

        var reachableIds = IsochroneNodes
            .Select(node => node.Id)
            .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
            .ToHashSet(Comparer);

        foreach (var node in Scene.Nodes)
        {
            node.FillColor = SKColor.Parse("#163149");
            if (!reachableIds.Contains(node.Id))
            {
                node.VisualOpacity = 0.25d;
                node.StrokeColor = SKColor.Parse("#46657F");
                continue;
            }

            var distance = isochroneDistances.GetValueOrDefault(node.Id, IsochroneThresholdMinutes);
            var ratio = IsochroneThresholdMinutes <= 0d ? 1d : Math.Clamp(distance / IsochroneThresholdMinutes, 0d, 1d);
            node.VisualOpacity = 1d;
            node.StrokeColor = ratio <= 0.25d
                ? SKColor.Parse("#45E07A")
                : ratio <= 0.5d
                    ? SKColor.Parse("#8EE978")
                    : SKColor.Parse("#B8F0A2");
            node.ToolTipText = AppendIsochroneText(node.ToolTipText, distance, ratio);
        }

        foreach (var edge in Scene.Edges)
        {
            edge.VisualOpacity = reachableIds.Contains(edge.FromNodeId) && reachableIds.Contains(edge.ToNodeId) ? 1d : 0.18d;
        }
    }

    private void ApplyFacilityPlanningVisuals()
    {
        var baseNodesById = network.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, node => node, Comparer);
        var selectedFacilityIds = SelectedFacilityNodes
            .Where(facility => !string.IsNullOrWhiteSpace(facility.Node.Id))
            .Select(facility => facility.Node.Id)
            .ToHashSet(Comparer);
        var reachableIds = facilityCoverageByNodeId.Keys.ToHashSet(Comparer);
        var overlapIds = facilityCoverageByNodeId
            .Where(pair => pair.Value.Count > 1)
            .Select(pair => pair.Key)
            .ToHashSet(Comparer);

        foreach (var sceneNode in Scene.Nodes)
        {
            sceneNode.FillColor = SKColor.Parse("#163149");
            sceneNode.StrokeColor = SKColor.Parse("#6AAED6");
            sceneNode.VisualOpacity = 1d;
            sceneNode.CoveringFacilities = [];
            sceneNode.IsFacilityCovered = false;
            sceneNode.IsMultiFacilityCovered = false;
            sceneNode.PrimaryFacilityId = null;
            sceneNode.PrimaryFacilityTravelTime = null;
            if (!baseNodesById.TryGetValue(sceneNode.Id, out var model))
            {
                continue;
            }

            var badges = BuildNodeBadges(model).ToList();
            var isFacility = selectedFacilityIds.Contains(sceneNode.Id);
            var isReachable = reachableIds.Contains(sceneNode.Id);
            var isOverlap = overlapIds.Contains(sceneNode.Id);
            var coverages = facilityCoverageByNodeId.TryGetValue(sceneNode.Id, out var coveringFacilities)
                ? coveringFacilities.OrderBy(candidate => candidate.TravelTime).ThenBy(candidate => candidate.FacilityDisplayName, Comparer).ToList()
                : [];
            var primary = coverages.FirstOrDefault(candidate => candidate.IsPrimaryFacility) ?? coverages.FirstOrDefault();

            sceneNode.CoveringFacilities = coverages;
            sceneNode.IsFacilityCovered = coverages.Count > 0;
            sceneNode.IsMultiFacilityCovered = coverages.Count > 1;
            sceneNode.PrimaryFacilityId = primary?.FacilityNodeId;
            sceneNode.PrimaryFacilityTravelTime = primary?.TravelTime;

            if (isFacility)
            {
                sceneNode.StrokeColor = SKColor.Parse("#F4A261");
                badges.Add("Facility");
            }

            if (SelectedFacilityNodes.Count > 0)
            {
                if (!isReachable)
                {
                    sceneNode.VisualOpacity = 0.22d;
                    sceneNode.StrokeColor = SKColor.Parse("#46657F");
                    badges.Add("Uncovered");
                }
                else
                {
                    sceneNode.StrokeColor = SKColor.Parse("#45E07A");
                    badges.Add("Reachable");
                }
            }

            if (isOverlap)
            {
                sceneNode.StrokeColor = SKColor.Parse("#EFCB68");
                badges.Add("Overlap");
            }

            sceneNode.Badges = badges.Distinct(Comparer).ToList();
            sceneNode.ToolTipText = BuildFacilityPlanningToolTip(model, sceneNode.DetailLines, sceneNode.CoveringFacilities);
        }

        foreach (var edge in Scene.Edges)
        {
            if (CurrentMultiOriginIsochrone is null)
            {
                edge.VisualOpacity = 1d;
                continue;
            }

            edge.VisualOpacity = reachableIds.Contains(edge.FromNodeId) && reachableIds.Contains(edge.ToNodeId) ? 1d : 0.18d;
        }
    }

    private string BuildFacilityPlanningToolTip(
        NodeModel node,
        IReadOnlyList<GraphNodeTextLine> detailLines,
        IReadOnlyList<FacilityCoverageInfo> coverages)
    {
        var baseText = BuildNodeToolTipText(node, detailLines, null);
        if (coverages.Count == 0)
        {
            return $"{baseText}{Environment.NewLine}{Environment.NewLine}Facility planning: uncovered.";
        }

        var ordered = coverages
            .OrderBy(candidate => candidate.TravelTime)
            .ThenBy(candidate => candidate.FacilityDisplayName, Comparer)
            .ToList();
        var primary = ordered.FirstOrDefault(candidate => candidate.IsPrimaryFacility) ?? ordered[0];
        var lines = new List<string>
        {
            baseText,
            string.Empty,
            "Covered by:"
        };
        lines.AddRange(ordered.Select(candidate => $"- {candidate.FacilityDisplayName}: {candidate.TravelTime:0.##}"));
        lines.Add($"Primary: {primary.FacilityDisplayName}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string AppendIsochroneText(string original, double distance, double ratio)
    {
        var baseText = original.Split($"{Environment.NewLine}{Environment.NewLine}Isochrone:", StringSplitOptions.None)[0];
        var band = ratio <= 0.25d
            ? "Band: 0–25%"
            : ratio <= 0.5d
                ? "Band: 25–50%"
                : "Band: 50–100%";
        return $"{baseText}{Environment.NewLine}{Environment.NewLine}Isochrone: {distance:0.##} minutes from origin. {band}.";
    }

    private double GetFacilityMaxTravelTime(FacilityOriginItem facility) =>
        facility.TryGetMaxTravelTime(out var maxTravelTime) ? maxTravelTime : IsochroneBudget;

    private void BuildUncoveredPlanningItems()
    {
        UncoveredPlanningItems.Clear();
        if (SelectedFacilityNodes.Count == 0)
        {
            return;
        }

        foreach (var uncovered in network.Nodes
                     .Where(node => string.IsNullOrWhiteSpace(node.Id) || !facilityCoverageByNodeId.ContainsKey(node.Id))
                     .OrderBy(node => node.Name, Comparer))
        {
            var nearest = "N/A";
            var extraBudgetNeeded = "N/A";
            var bestDistance = double.PositiveInfinity;
            var nearestLimit = IsochroneBudget;
            foreach (var facility in SelectedFacilityNodes)
            {
                var origin = facility.Node;
                if (string.IsNullOrWhiteSpace(origin.Id))
                {
                    continue;
                }

                if (!cachedFacilityDistances.TryGetValue(origin.Id, out var map))
                {
                    map = ComputeReachableNodeDistances(origin.Id, double.MaxValue);
                    cachedFacilityDistances[origin.Id] = map;
                }

                if (string.IsNullOrWhiteSpace(uncovered.Id) || !map.TryGetValue(uncovered.Id, out var distance))
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearestLimit = GetFacilityMaxTravelTime(facility);
                    nearest = string.IsNullOrWhiteSpace(origin.Name) ? origin.Id : origin.Name;
                }
            }

            if (!double.IsPositiveInfinity(bestDistance))
            {
                var extra = Math.Max(0d, bestDistance - nearestLimit);
                extraBudgetNeeded = extra.ToString("0.##", CultureInfo.InvariantCulture);
            }

            UncoveredPlanningItems.Add(new UncoveredNodePlanningItem
            {
                NodeName = string.IsNullOrWhiteSpace(uncovered.Name) ? uncovered.Id : uncovered.Name,
                NearestFacility = nearest,
                ExtraBudgetNeeded = extraBudgetNeeded
            });
        }
    }

    private void BuildFacilityComparisonRows()
    {
        FacilityComparisonRows.Clear();
        if (SelectedFacilityNodes.Count == 0)
        {
            return;
        }

        foreach (var facility in SelectedFacilityNodes)
        {
            var origin = facility.Node;
            if (!cachedFacilityDistances.TryGetValue(origin.Id, out var costs))
            {
                costs = ComputeReachableNodeDistances(origin.Id, GetFacilityMaxTravelTime(facility));
                cachedFacilityDistances[origin.Id] = costs;
            }

            var coveredNodeIds = costs.Keys.ToList();
            var uniqueCoveredCount = coveredNodeIds.Count(nodeId =>
                facilityCoverageByNodeId.TryGetValue(nodeId, out var coveringOrigins) &&
                coveringOrigins.Count == 1 &&
                coveringOrigins.Any(candidate => Comparer.Equals(candidate.FacilityNodeId, origin.Id)));
            FacilityComparisonRows.Add(new FacilityComparisonRowViewModel
            {
                Facility = facility.DisplayName,
                NodesCovered = coveredNodeIds.Count.ToString(CultureInfo.InvariantCulture),
                UniqueNodesCovered = uniqueCoveredCount.ToString(CultureInfo.InvariantCulture),
                AverageCost = (costs.Count == 0 ? 0d : costs.Values.Average()).ToString("0.##", CultureInfo.InvariantCulture),
                MaxCost = (costs.Count == 0 ? 0d : costs.Values.Max()).ToString("0.##", CultureInfo.InvariantCulture)
            });
        }
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

    private void UpdateSceneNodeLayout(
        GraphNodeSceneItem sceneNode,
        NodeModel nodeModel,
        TemporalNetworkSimulationEngine.NodePressureSnapshot? pressure,
        ZoomTier zoomTier)
    {
        sceneNode.LayoutContentKey = null;
        sceneNode.CachedLayout = null;
        var layout = GraphRenderer.GetOrBuildNodeLayout(sceneNode, zoomTier);
        GraphRenderer.ApplyLayoutBoundsKeepingCenter(sceneNode, layout);
        sceneNode.ToolTipText = BuildNodeToolTipText(nodeModel, sceneNode.DetailLines, pressure);
    }

    private void PreviewSelectedNodeSceneLayout()
    {
        if (!IsEditingNode)
        {
            return;
        }

        var selectedNodeId = NodeDraft.TargetNodeId;
        if (string.IsNullOrWhiteSpace(selectedNodeId))
        {
            return;
        }

        var nodeModel = network.Nodes.FirstOrDefault(node => Comparer.Equals(node.Id, selectedNodeId));
        var sceneNode = Scene.FindNode(selectedNodeId);
        if (nodeModel is null || sceneNode is null)
        {
            return;
        }

        var previewNode = BuildPreviewNodeModel(nodeModel);
        sceneNode.Name = string.IsNullOrWhiteSpace(NodeNameText) ? nodeModel.Id : NodeNameText.Trim();
        sceneNode.TypeLabel = string.IsNullOrWhiteSpace(NodePlaceTypeText) ? "Node" : NodePlaceTypeText.Trim();
        sceneNode.DetailLines = BuildNodeDetailLines(previewNode, [], null);
        sceneNode.Badges = BuildNodeBadges(previewNode);
        sceneNode.ToolTipText = BuildNodeToolTipText(previewNode, sceneNode.DetailLines, null);
        sceneNode.LayoutContentKey = null;
        sceneNode.CachedLayout = null;
        var zoomTier = graphRenderer.GetZoomTier(Viewport.Zoom);
        var layout = GraphRenderer.GetOrBuildNodeLayout(sceneNode, zoomTier);
        GraphRenderer.ApplyLayoutBoundsKeepingCenter(sceneNode, layout);
        NotifyVisualChanged();
    }

    private NodeModel BuildPreviewNodeModel(NodeModel source)
    {
        var preview = new NodeModel
        {
            Id = source.Id,
            Name = string.IsNullOrWhiteSpace(NodeNameText) ? source.Id : NodeNameText.Trim(),
            X = ParseOptionalDouble(NodeXText, "Enter an X position, or leave it blank.") ?? source.X,
            Y = ParseOptionalDouble(NodeYText, "Enter a Y position, or leave it blank.") ?? source.Y,
            PlaceType = NormalizeOptionalText(NodePlaceTypeText),
            LoreDescription = NormalizeOptionalText(NodeDescriptionText),
            TranshipmentCapacity = TryParseOptionalNonNegativeDouble(NodeTranshipmentCapacityText),
            Shape = NodeShape,
            NodeKind = NodeKind,
            ReferencedSubnetworkId = NormalizeOptionalText(NodeReferencedSubnetworkIdText),
            IsExternalInterface = NodeIsExternalInterface,
            InterfaceName = NormalizeOptionalText(NodeInterfaceNameText),
            ControllingActor = NormalizeOptionalText(NodeControllingActorText),
            Tags = SplitCommaSeparatedText(NodeTagsText),
            TemplateId = NormalizeOptionalText(NodeTemplateIdText),
            TrafficProfiles = source.TrafficProfiles.Select(profile => new NodeTrafficProfile
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
                ProductionWindows = profile.ProductionWindows
                    .Select(window => new PeriodWindow { StartPeriod = window.StartPeriod, EndPeriod = window.EndPeriod })
                    .ToList(),
                ConsumptionWindows = profile.ConsumptionWindows
                    .Select(window => new PeriodWindow { StartPeriod = window.StartPeriod, EndPeriod = window.EndPeriod })
                    .ToList(),
                InputRequirements = profile.InputRequirements
                    .Select(requirement => new ProductionInputRequirement
                    {
                        TrafficType = requirement.TrafficType,
                        InputQuantity = requirement.InputQuantity,
                        OutputQuantity = requirement.OutputQuantity,
                        QuantityPerOutputUnit = requirement.QuantityPerOutputUnit
                    })
                    .ToList(),
                IsStore = profile.IsStore,
                StoreCapacity = profile.StoreCapacity
            }).ToList()
        };

        var selectedProfile = SelectedNodeTrafficProfileItem;
        if (selectedProfile is not null && selectedProfile.Index >= 0 && selectedProfile.Index < preview.TrafficProfiles.Count)
        {
            var profile = preview.TrafficProfiles[selectedProfile.Index];
            profile.TrafficType = string.IsNullOrWhiteSpace(NodeTrafficTypeText) ? profile.TrafficType : NodeTrafficTypeText.Trim();
            profile.Production = TryParseNonNegativeDouble(NodeProductionText) ?? profile.Production;
            profile.Consumption = TryParseNonNegativeDouble(NodeConsumptionText) ?? profile.Consumption;
            profile.ConsumerPremiumPerUnit = TryParseNonNegativeDouble(NodeConsumerPremiumText) ?? profile.ConsumerPremiumPerUnit;
            profile.ProductionStartPeriod = TryParseOptionalPositiveInt(NodeProductionStartText);
            profile.ProductionEndPeriod = TryParseOptionalPositiveInt(NodeProductionEndText);
            profile.ConsumptionStartPeriod = TryParseOptionalPositiveInt(NodeConsumptionStartText);
            profile.ConsumptionEndPeriod = TryParseOptionalPositiveInt(NodeConsumptionEndText);
            profile.CanTransship = NodeCanTransship;
            profile.IsStore = NodeStoreEnabled;
            profile.StoreCapacity = NodeStoreEnabled ? TryParseOptionalNonNegativeDouble(NodeStoreCapacityText) : null;
            profile.ProductionWindows = SelectedNodeProductionWindows
                .Select(window => new PeriodWindow
                {
                    StartPeriod = TryParseOptionalPositiveInt(window.StartText),
                    EndPeriod = TryParseOptionalPositiveInt(window.EndText)
                })
                .Where(window => window.StartPeriod.HasValue || window.EndPeriod.HasValue)
                .ToList();
            profile.ConsumptionWindows = SelectedNodeConsumptionWindows
                .Select(window => new PeriodWindow
                {
                    StartPeriod = TryParseOptionalPositiveInt(window.StartText),
                    EndPeriod = TryParseOptionalPositiveInt(window.EndText)
                })
                .Where(window => window.StartPeriod.HasValue || window.EndPeriod.HasValue)
                .ToList();
            profile.InputRequirements = SelectedNodeInputRequirements
                .Where(row => !string.IsNullOrWhiteSpace(row.TrafficType))
                .Select(row => new ProductionInputRequirement
                {
                    TrafficType = row.TrafficType.Trim(),
                    InputQuantity = TryParseNonNegativeDouble(row.InputQuantityText) ?? 0d,
                    OutputQuantity = TryParseNonNegativeDouble(row.OutputQuantityText) ?? 1d
                })
                .ToList();
        }

        return preview;
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

    private string AppendNodeActorControlText(string nodeId, string baseText)
    {
        var controllers = SimulationActors
            .Where(actor => actor.ControlledNodeIds.Contains(nodeId, Comparer))
            .Select(actor => string.IsNullOrWhiteSpace(actor.Name) ? actor.Id : actor.Name)
            .ToList();
        return controllers.Count == 0
            ? $"{baseText}{Environment.NewLine}Controlled by: none"
            : $"{baseText}{Environment.NewLine}Controlled by: {string.Join(", ", controllers)}";
    }

    private string AppendEdgeActorControlText(string edgeId, string baseText)
    {
        var controllers = SimulationActors
            .Where(actor => actor.ControlledEdgeIds.Contains(edgeId, Comparer))
            .Select(actor => string.IsNullOrWhiteSpace(actor.Name) ? actor.Id : actor.Name)
            .ToList();
        return controllers.Count == 0
            ? $"{baseText}{Environment.NewLine}Controlled by: none"
            : $"{baseText}{Environment.NewLine}Controlled by: {string.Join(", ", controllers)}";
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

    private static List<string> SplitCommaSeparatedText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(Comparer)
                .OrderBy(item => item, Comparer)
                .ToList();

    private static NetworkModel CreateInitializedNetworkModel() => new()
    {
        Name = "Untitled Network",
        Description = string.Empty,
        Layers = [],
        ScenarioDefinitions = [],
        PolicyRules = [],
        TrafficTypes = [],
        TimelineEvents = [],
        EdgeTrafficPermissionDefaults = [],
        Subnetworks = [],
        Nodes = [],
        Edges = [],
        Actors = [],
        ActorDecisions = [],
        ActorMetrics = [],
        ActorActionOutcomes = [],
        AgentActionLogs = []
    };

    private static void EnsureNetworkReferences(NetworkModel model)
    {
        model.Name ??= "Untitled Network";
        model.Description ??= string.Empty;
        model.Layers ??= [];
        model.ScenarioDefinitions ??= [];
        model.PolicyRules ??= [];
        model.TrafficTypes ??= [];
        model.TimelineEvents ??= [];
        model.EdgeTrafficPermissionDefaults ??= [];
        model.Subnetworks ??= [];
        model.Nodes ??= [];
        model.Edges ??= [];
        model.Actors ??= [];
        model.ActorDecisions ??= [];
        model.ActorMetrics ??= [];
        model.ActorActionOutcomes ??= [];
        model.AgentActionLogs ??= [];
        foreach (var actor in model.Actors)
        {
            EnsureActorReferences(actor);
        }
    }

    private static void EnsureActorReferences(SimulationActorState actor)
    {
        actor.Id ??= string.Empty;
        actor.Name ??= actor.Id;
        actor.Notes ??= string.Empty;
        actor.ControlledNodeIds ??= [];
        actor.ControlledEdgeIds ??= [];
        actor.Capability ??= SimulationActorCapabilityCatalog.ForKind(actor.Id, actor.Kind);
        actor.Capability.ActorId = string.IsNullOrWhiteSpace(actor.Capability.ActorId) ? actor.Id : actor.Capability.ActorId;
        actor.Capability.AllowedActionKinds ??= [];
        actor.Capability.AllowedTrafficTypes ??= [];
    }

    private static List<PeriodWindow> BuildPeriodWindows(IEnumerable<PeriodWindowEditorRow> rows, string label)
    {
        var result = new List<PeriodWindow>();
        foreach (var row in rows)
        {
            var start = ParseOptionalPositiveInt(row.StartText, $"Enter a {label} start period of 1 or more, or leave it blank.");
            var end = ParseOptionalPositiveInt(row.EndText, $"Enter a {label} end period of 1 or more, or leave it blank.");
            if (start.HasValue && end.HasValue && start.Value > end.Value)
            {
                throw new InvalidOperationException($"A {label} start period cannot be after its end period.");
            }

            if (!start.HasValue && !end.HasValue)
            {
                continue;
            }

            result.Add(new PeriodWindow
            {
                StartPeriod = start,
                EndPeriod = end
            });
        }

        return result;
    }

    private List<ProductionInputRequirement> BuildInputRequirements(IEnumerable<InputRequirementEditorRow> rows)
    {
        var result = new List<ProductionInputRequirement>();
        foreach (var row in rows)
        {
            var trafficType = string.IsNullOrWhiteSpace(row.TrafficType) ? string.Empty : row.TrafficType.Trim();
            if (string.IsNullOrWhiteSpace(trafficType))
            {
                throw new InvalidOperationException("Choose a traffic type for each local input row.");
            }

            if (!network.TrafficTypes.Any(definition => Comparer.Equals(definition.Name, trafficType)))
            {
                throw new InvalidOperationException($"Local input traffic '{trafficType}' must exist in the traffic type editor.");
            }

            var inputQuantity = ParseNonNegativeDouble(row.InputQuantityText, "Enter an input quantity of 0 or more.");
            var outputQuantity = ParseNonNegativeDouble(row.OutputQuantityText, "Enter an output quantity greater than 0.");
            if (outputQuantity <= 0d)
            {
                throw new InvalidOperationException("Enter an output quantity greater than 0.");
            }

            result.Add(new ProductionInputRequirement
            {
                TrafficType = trafficType,
                InputQuantity = inputQuantity,
                OutputQuantity = outputQuantity
            });
        }

        return result;
    }

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

    private static double? ParseOptionalDouble(string text, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value;
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

    private static double? TryParseNonNegativeDouble(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0d
            ? value
            : null;
    }

    private static double? TryParseOptionalNonNegativeDouble(string text) => TryParseNonNegativeDouble(text);

    private static int? TryParseOptionalPositiveInt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 1
            ? value
            : null;
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

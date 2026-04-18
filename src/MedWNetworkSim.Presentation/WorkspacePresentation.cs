using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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

public sealed class SelectionMetric
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required Action Focus { get; init; }
}

public sealed class InspectorSection : ObservableObject
{
    private string headline = "Nothing selected";
    private string summary = "Create or select a graph item to inspect it.";
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

public sealed class ReportMetricViewModel
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public required Action Activate { get; init; }
}

public sealed class WorkspaceViewModel : ObservableObject
{
    private readonly NetworkFileService fileService = new();
    private readonly NetworkSimulationEngine simulationEngine = new();
    private readonly TemporalNetworkSimulationEngine temporalEngine = new();
    private readonly GraphInteractionController interactionController = new();

    private NetworkModel network = new();
    private TemporalNetworkSimulationEngine.TemporalSimulationState? temporalState;
    private string statusText = "Avalonia migration shell ready.";
    private string modeText = "Build mode";
    private double playbackSpeed = 1d;
    private bool reducedMotion;
    private int currentPeriod;
    private int timelineMaximum = 12;
    private int timelinePosition;
    private string inspectorName = string.Empty;
    private string inspectorDescription = string.Empty;
    private string inspectorTimelineLoopLengthText = "12";
    private string inspectorNodePlaceType = string.Empty;
    private string inspectorNodeTranshipmentCapacityText = string.Empty;
    private string inspectorEdgeRouteType = string.Empty;
    private string inspectorEdgeTimeText = "1.5";
    private string inspectorEdgeCostText = "1.0";
    private string inspectorEdgeCapacityText = "30";
    private bool inspectorEdgeIsBidirectional;
    private string inspectorBulkPlaceType = string.Empty;
    private string inspectorBulkTranshipmentCapacityText = string.Empty;
    private string inspectorValidationText = string.Empty;

    public WorkspaceViewModel()
    {
        Scene = new GraphScene();
        Viewport = new GraphViewport();
        Inspector = new InspectorSection();
        ReportMetrics = [];
        TopCommandBar = "New, simulate, step, fit, and inspect from the Avalonia shell.";
        NewCommand = new RelayCommand(CreateBlankNetwork);
        SelectInteractionHelpCommand = new RelayCommand(ShowSelectionHelp);
        AddNodeCommand = new RelayCommand(AddNodeAtViewportCenter);
        ConnectSelectedNodesCommand = new RelayCommand(ConnectSelectedNodes);
        DeleteSelectionCommand = new RelayCommand(DeleteCurrentSelection, () => CanDeleteSelection);
        ApplyInspectorCommand = new RelayCommand(ApplyInspectorEdits);
        SimulateCommand = new RelayCommand(RunSimulation);
        StepCommand = new RelayCommand(AdvanceTimeline);
        ResetTimelineCommand = new RelayCommand(ResetTimeline);
        FitCommand = new RelayCommand(() =>
        {
            Viewport.Reset(Scene.GetContentBounds(), LastViewportSize.Width <= 0d ? new GraphSize(1440d, 860d) : LastViewportSize);
            Raise(nameof(ViewportVersion));
        });
        ToggleMotionCommand = new RelayCommand(() =>
        {
            ReducedMotion = !ReducedMotion;
            Scene.Simulation.ReducedMotion = ReducedMotion;
            Raise(nameof(ViewportVersion));
        });

        CreateBlankNetwork();
    }

    public GraphScene Scene { get; }
    public GraphViewport Viewport { get; }
    public GraphSize LastViewportSize { get; private set; } = new(1440d, 860d);
    public int ViewportVersion { get; private set; }
    public string TopCommandBar { get; }
    public RelayCommand NewCommand { get; }
    public RelayCommand SelectInteractionHelpCommand { get; }
    public RelayCommand AddNodeCommand { get; }
    public RelayCommand ConnectSelectedNodesCommand { get; }
    public RelayCommand DeleteSelectionCommand { get; }
    public RelayCommand ApplyInspectorCommand { get; }
    public RelayCommand SimulateCommand { get; }
    public RelayCommand StepCommand { get; }
    public RelayCommand ResetTimelineCommand { get; }
    public RelayCommand FitCommand { get; }
    public RelayCommand ToggleMotionCommand { get; }
    public InspectorSection Inspector { get; }
    public ObservableCollection<ReportMetricViewModel> ReportMetrics { get; }

    public GraphInteractionContext CreateInteractionContext(GraphSize viewportSize)
    {
        LastViewportSize = viewportSize;
        return new GraphInteractionContext
        {
            Scene = Scene,
            Viewport = Viewport,
            ViewportSize = viewportSize,
            CreateEdge = CreateEdge,
            AddNodeAt = AddNodeAt,
            DeleteSelection = DeleteSelection,
            FocusNextConnectedEdge = FocusNextConnectedEdge,
            FocusNearbyNode = FocusNearbyNode,
            SelectionChanged = (_, _) => RefreshInspector(),
            StatusChanged = text => StatusText = text
        };
    }

    public GraphInteractionController InteractionController => interactionController;

    public string WindowTitle => $"MedW Network Sim | Avalonia Workstation | {network.Name}";
    public string StatusText { get => statusText; set => SetProperty(ref statusText, value); }
    public string ModeText { get => modeText; set => SetProperty(ref modeText, value); }
    public bool ReducedMotion { get => reducedMotion; set => SetProperty(ref reducedMotion, value); }
    public double PlaybackSpeed { get => playbackSpeed; set => SetProperty(ref playbackSpeed, value); }
    public int CurrentPeriod { get => currentPeriod; private set => SetProperty(ref currentPeriod, value); }
    public int TimelineMaximum { get => timelineMaximum; private set => SetProperty(ref timelineMaximum, value); }
    public int TimelinePosition { get => timelinePosition; set => SetProperty(ref timelinePosition, value); }
    public string SimulationSummary => temporalState is null ? "Static mode" : $"Timeline period {CurrentPeriod}";
    public string SelectionSummary => Scene.Selection.SelectedNodeIds.Count switch
    {
        > 1 => $"{Scene.Selection.SelectedNodeIds.Count} nodes selected",
        _ when Scene.Selection.SelectedEdgeIds.Count > 1 => $"{Scene.Selection.SelectedEdgeIds.Count} edges selected",
        _ when Scene.Selection.SelectedNodeIds.Count == 1 => $"Node {Scene.Selection.SelectedNodeIds.First()} selected",
        _ when Scene.Selection.SelectedEdgeIds.Count == 1 => $"Edge {Scene.Selection.SelectedEdgeIds.First()} selected",
        _ => "No selection"
    };
    public bool CanConnectSelectedNodes => Scene.Selection.SelectedNodeIds.Count == 2 && Scene.Selection.SelectedEdgeIds.Count == 0;
    public bool CanDeleteSelection => Scene.Selection.SelectedNodeIds.Count > 0 || Scene.Selection.SelectedEdgeIds.Count > 0;
    public InspectorEditMode CurrentInspectorEditMode => GetInspectorEditMode();
    public bool IsEditingNetwork => CurrentInspectorEditMode == InspectorEditMode.Network;
    public bool IsEditingNode => CurrentInspectorEditMode == InspectorEditMode.Node;
    public bool IsEditingEdge => CurrentInspectorEditMode == InspectorEditMode.Edge;
    public bool IsEditingSelection => CurrentInspectorEditMode == InspectorEditMode.Selection;
    public string InspectorEditModeLabel => CurrentInspectorEditMode switch
    {
        InspectorEditMode.Node => "Editing node",
        InspectorEditMode.Edge => "Editing edge",
        InspectorEditMode.Selection => "Editing selection",
        _ => "Editing network"
    };
    public string InspectorEditModeHelp => CurrentInspectorEditMode switch
    {
        InspectorEditMode.Node => "Select one node to edit node properties.",
        InspectorEditMode.Edge => "Select one edge to edit edge properties.",
        InspectorEditMode.Selection => "Select multiple items for bulk edit.",
        _ => "Select nothing to edit network properties."
    };
    public string ApplyInspectorLabel => CurrentInspectorEditMode switch
    {
        InspectorEditMode.Node => "Apply Node Changes",
        InspectorEditMode.Edge => "Apply Edge Changes",
        InspectorEditMode.Selection => "Apply Bulk Changes",
        _ => "Apply Network Changes"
    };
    public string InspectorName { get => inspectorName; set => SetProperty(ref inspectorName, value); }
    public string InspectorDescription { get => inspectorDescription; set => SetProperty(ref inspectorDescription, value); }
    public string InspectorTimelineLoopLengthText { get => inspectorTimelineLoopLengthText; set => SetProperty(ref inspectorTimelineLoopLengthText, value); }
    public string InspectorNodePlaceType { get => inspectorNodePlaceType; set => SetProperty(ref inspectorNodePlaceType, value); }
    public string InspectorNodeTranshipmentCapacityText { get => inspectorNodeTranshipmentCapacityText; set => SetProperty(ref inspectorNodeTranshipmentCapacityText, value); }
    public string InspectorEdgeRouteType { get => inspectorEdgeRouteType; set => SetProperty(ref inspectorEdgeRouteType, value); }
    public string InspectorEdgeTimeText { get => inspectorEdgeTimeText; set => SetProperty(ref inspectorEdgeTimeText, value); }
    public string InspectorEdgeCostText { get => inspectorEdgeCostText; set => SetProperty(ref inspectorEdgeCostText, value); }
    public string InspectorEdgeCapacityText { get => inspectorEdgeCapacityText; set => SetProperty(ref inspectorEdgeCapacityText, value); }
    public bool InspectorEdgeIsBidirectional { get => inspectorEdgeIsBidirectional; set => SetProperty(ref inspectorEdgeIsBidirectional, value); }
    public string InspectorBulkPlaceType { get => inspectorBulkPlaceType; set => SetProperty(ref inspectorBulkPlaceType, value); }
    public string InspectorBulkTranshipmentCapacityText { get => inspectorBulkTranshipmentCapacityText; set => SetProperty(ref inspectorBulkTranshipmentCapacityText, value); }
    public string InspectorValidationText { get => inspectorValidationText; set => SetProperty(ref inspectorValidationText, value); }
    public bool HasInspectorValidationText => !string.IsNullOrWhiteSpace(InspectorValidationText);

    public void TickAnimation(double elapsedSeconds)
    {
        Scene.Simulation.AnimationTime += ReducedMotion ? elapsedSeconds * 0.2d : elapsedSeconds;
    }

    public void NotifyVisualChanged()
    {
        ViewportVersion++;
        Raise(nameof(ViewportVersion));
        Raise(nameof(WindowTitle));
        Raise(nameof(SelectionSummary));
        Raise(nameof(SimulationSummary));
    }

    private void CreateBlankNetwork()
    {
        var blankNetwork = new NetworkModel
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
            Nodes = [],
            Edges = []
        };

        LoadNetwork(blankNetwork, "Created blank network.");
    }

    private void LoadNetwork(NetworkModel source, string statusMessage)
    {
        network = fileService.NormalizeAndValidate(source);
        temporalState = null;
        CurrentPeriod = 0;
        TimelineMaximum = Math.Max(8, network.TimelineLoopLength ?? 12);
        TimelinePosition = 0;
        ModeText = "Build mode";
        BuildSceneFromNetwork();
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
        Viewport.Reset(Scene.GetContentBounds(), LastViewportSize);
        RefreshInspector();
        PopulateReportMetrics([]);
        StatusText = statusMessage;
        NotifyVisualChanged();
    }

    private void BuildSceneFromNetwork()
    {
        Scene.Nodes.Clear();
        Scene.Edges.Clear();

        foreach (var node in network.Nodes)
        {
            Scene.Nodes.Add(new GraphNodeSceneItem
            {
                Id = node.Id,
                Name = node.Name,
                TypeLabel = string.IsNullOrWhiteSpace(node.PlaceType) ? "Node" : node.PlaceType,
                MetricsLabel = BuildNodeMetricsLabel(node),
                Bounds = new GraphRect(node.X ?? 0d, node.Y ?? 0d, 190d, 116d),
                FillColor = SKColor.Parse("#163149"),
                StrokeColor = SKColor.Parse("#6AAED6"),
                Badges = BuildNodeBadges(node),
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
                HasWarning = false
            });
        }
    }

    private bool CreateEdge(string sourceId, string targetId)
    {
        var edgeId = $"{sourceId}->{targetId}";
        if (network.Edges.Any(edge => string.Equals(edge.Id, edgeId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        network.Edges.Add(new EdgeModel
        {
            Id = edgeId,
            FromNodeId = sourceId,
            ToNodeId = targetId,
            Time = 1.5,
            Cost = 1.0,
            Capacity = 30,
            IsBidirectional = false,
            RouteType = "Proposed route"
        });

        Scene.Edges.Add(new GraphEdgeSceneItem
        {
            Id = edgeId,
            FromNodeId = sourceId,
            ToNodeId = targetId,
            Label = "Proposed route",
            IsBidirectional = false,
            Capacity = 30,
            Cost = 1.0,
            Time = 1.5,
            LoadRatio = 0d,
            FlowRate = 0d,
            HasWarning = false
        });

        NotifyVisualChanged();
        return true;
    }

    private string AddNodeAt(GraphPoint center)
    {
        EnsureDefaultTrafficType();
        var id = $"node-{network.Nodes.Count + 1}";
        var model = new NodeModel
        {
            Id = id,
            Name = $"Node {network.Nodes.Count + 1}",
            X = center.X,
            Y = center.Y,
            PlaceType = "Draft",
            TranshipmentCapacity = 40,
            TrafficProfiles = [new NodeTrafficProfile { TrafficType = network.TrafficTypes.First().Name, CanTransship = true }]
        };
        network.Nodes.Add(model);
        Scene.Nodes.Add(new GraphNodeSceneItem
        {
            Id = id,
            Name = model.Name,
            TypeLabel = model.PlaceType ?? "Node",
            MetricsLabel = BuildNodeMetricsLabel(model),
            Bounds = new GraphRect(center.X, center.Y, 190d, 116d),
            FillColor = SKColor.Parse("#163149"),
            StrokeColor = SKColor.Parse("#6AAED6"),
            Badges = BuildNodeBadges(model),
            HasWarning = false
        });
        NotifyVisualChanged();
        return id;
    }

    private void DeleteSelection()
    {
        var selectedNodes = Scene.Selection.SelectedNodeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedEdges = Scene.Selection.SelectedEdgeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selectedNodes.Count > 0)
        {
            network.Nodes.RemoveAll(node => selectedNodes.Contains(node.Id));
            network.Edges.RemoveAll(edge => selectedNodes.Contains(edge.FromNodeId) || selectedNodes.Contains(edge.ToNodeId));
            foreach (var node in Scene.Nodes.Where(node => selectedNodes.Contains(node.Id)).ToList())
            {
                Scene.Nodes.Remove(node);
            }

            foreach (var edge in Scene.Edges.Where(edge => selectedNodes.Contains(edge.FromNodeId) || selectedNodes.Contains(edge.ToNodeId)).ToList())
            {
                Scene.Edges.Remove(edge);
            }
        }

        if (selectedEdges.Count > 0)
        {
            network.Edges.RemoveAll(edge => selectedEdges.Contains(edge.Id));
            foreach (var edge in Scene.Edges.Where(edge => selectedEdges.Contains(edge.Id)).ToList())
            {
                Scene.Edges.Remove(edge);
            }
        }

        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        RefreshInspector();
        NotifyVisualChanged();
    }

    private string? FocusNextConnectedEdge()
    {
        var selectedNode = Scene.Selection.SelectedNodeIds.FirstOrDefault();
        if (selectedNode is null)
        {
            return Scene.Edges.FirstOrDefault()?.Id;
        }

        var edges = Scene.Edges.Where(edge => edge.FromNodeId == selectedNode || edge.ToNodeId == selectedNode).ToList();
        if (edges.Count == 0)
        {
            return null;
        }

        var currentIndex = Scene.Selection.KeyboardEdgeId is null ? -1 : edges.FindIndex(edge => edge.Id == Scene.Selection.KeyboardEdgeId);
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
            .Where(node => !string.Equals(node.Id, current.Id, StringComparison.OrdinalIgnoreCase))
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
        var outcomes = simulationEngine.Simulate(network);
        ApplySimulationOutcomes(outcomes.SelectMany(outcome => outcome.Allocations), null);
        StatusText = "Static simulation run complete.";
        ModeText = "Simulation snapshot";
        NotifyVisualChanged();
    }

    private void AdvanceTimeline()
    {
        temporalState ??= temporalEngine.Initialize(network);
        var result = temporalEngine.Advance(network, temporalState);
        CurrentPeriod = result.Period;
        TimelinePosition = result.EffectivePeriod;
        ApplySimulationOutcomes(result.Allocations, result);
        StatusText = $"Advanced to period {result.Period}.";
        ModeText = "Timeline playback";
        NotifyVisualChanged();
    }

    private void ResetTimeline()
    {
        temporalState = null;
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
            node.MetricsLabel = "Tranship ready";
            node.HasWarning = false;
        }

        PopulateReportMetrics([]);
        StatusText = "Timeline reset.";
        ModeText = "Build mode";
        NotifyVisualChanged();
    }

    private void ApplySimulationOutcomes(IEnumerable<RouteAllocation> allocations, TemporalNetworkSimulationEngine.TemporalSimulationStepResult? timeline)
    {
        var edgeLoads = Scene.Edges.ToDictionary(edge => edge.Id, _ => 0d, StringComparer.OrdinalIgnoreCase);
        foreach (var allocation in allocations)
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
        }

        if (timeline is not null)
        {
            foreach (var node in Scene.Nodes)
            {
                var state = timeline.NodeStates
                    .Where(pair => string.Equals(pair.Key.NodeId, node.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Value)
                    .FirstOrDefault();
                node.MetricsLabel = $"Supply {state.AvailableSupply:0.#} | Backlog {state.DemandBacklog:0.#}";
                node.HasWarning = timeline.NodePressureById.TryGetValue(node.Id, out var pressure) && pressure.Score > 0d;
            }
        }
        else
        {
            foreach (var node in Scene.Nodes)
            {
                var inbound = allocations.Where(allocation => allocation.ConsumerNodeId == node.Id).Sum(allocation => allocation.Quantity);
                var outbound = allocations.Where(allocation => allocation.ProducerNodeId == node.Id).Sum(allocation => allocation.Quantity);
                node.MetricsLabel = $"Out {outbound:0.#} | In {inbound:0.#}";
                node.HasWarning = false;
            }
        }

        PopulateReportMetrics(edgeLoads
            .OrderByDescending(pair => pair.Value)
            .Take(4)
            .Select(pair => new ReportMetricViewModel
            {
                Label = pair.Key,
                Value = $"{pair.Value:0.#} load",
                Activate = () =>
                {
                    Scene.Selection.SelectedEdgeIds.Clear();
                    Scene.Selection.SelectedEdgeIds.Add(pair.Key);
                    RefreshInspector();
                    NotifyVisualChanged();
                }
            }));
        RefreshInspector();
    }

    private void PopulateReportMetrics(IEnumerable<ReportMetricViewModel> metrics)
    {
        ReportMetrics.Clear();
        foreach (var metric in metrics)
        {
            ReportMetrics.Add(metric);
        }
    }

    private void RefreshInspector()
    {
        var selectedNodes = Scene.Selection.SelectedNodeIds.ToList();
        var selectedEdges = Scene.Selection.SelectedEdgeIds.ToList();
        if (selectedNodes.Count == 0 && selectedEdges.Count == 0)
        {
            Inspector.Headline = network.Name;
            Inspector.Summary = "Select one node to edit node properties. Select one edge to edit edge properties. Select nothing to edit network properties. Select multiple items for bulk edit.";
            Inspector.Details =
            [
                $"Traffic types: {network.TrafficTypes.Count}",
                $"Nodes: {network.Nodes.Count}",
                $"Edges: {network.Edges.Count}",
                $"Reduced motion: {(ReducedMotion ? "On" : "Off")}"
            ];
            PopulateInspectorEditor();
            return;
        }

        if (selectedNodes.Count + selectedEdges.Count > 1)
        {
            Inspector.Headline = "Selection";
            Inspector.Summary = "Select multiple items for bulk edit.";
            Inspector.Details =
            [
                $"{selectedNodes.Count} nodes selected",
                $"{selectedEdges.Count} edges selected",
                "Delete removes the current selection.",
                "Right-drag between nodes creates a route."
            ];
            PopulateInspectorEditor();
            return;
        }

        if (selectedNodes.Count == 1)
        {
            var node = network.Nodes.First(model => model.Id == selectedNodes[0]);
            Inspector.Headline = node.Name;
            Inspector.Summary = string.IsNullOrWhiteSpace(node.PlaceType) ? "Node" : node.PlaceType!;
            Inspector.Details = node.TrafficProfiles.Select(profile =>
                $"{profile.TrafficType}: prod {profile.Production:0.#}, cons {profile.Consumption:0.#}, tranship {(profile.CanTransship ? "yes" : "no")}").ToList();
            PopulateInspectorEditor();
            return;
        }

        var edgeModel = network.Edges.First(model => model.Id == selectedEdges[0]);
        Inspector.Headline = edgeModel.RouteType ?? edgeModel.Id;
        Inspector.Summary = $"{edgeModel.FromNodeId} -> {edgeModel.ToNodeId}";
        Inspector.Details =
        [
            $"Capacity: {(edgeModel.Capacity?.ToString("0.#") ?? "Unlimited")}",
            $"Time: {edgeModel.Time:0.#}",
            $"Cost: {edgeModel.Cost:0.#}",
            $"Direction: {(edgeModel.IsBidirectional ? "Bidirectional" : "Forward only")}"
        ];
        PopulateInspectorEditor();
    }

    private void ShowSelectionHelp()
    {
        StatusText = "Selection mode active. Left click to select, drag nodes to move, right-drag between nodes to connect, press N to add a node, and press Delete to remove the selection.";
    }

    private void AddNodeAtViewportCenter()
    {
        var nodeId = AddNodeAt(Viewport.Center);
        SelectSingleNode(nodeId);
        var node = network.Nodes.First(model => model.Id == nodeId);
        StatusText = $"Added node '{node.Name}'.";
    }

    private void ConnectSelectedNodes()
    {
        var selectedNodes = Scene.Nodes
            .Where(node => Scene.Selection.SelectedNodeIds.Contains(node.Id))
            .Take(2)
            .Select(node => node.Id)
            .ToList();

        if (selectedNodes.Count != 2)
        {
            StatusText = "Select two nodes to create a connection.";
            return;
        }

        if (!CreateEdge(selectedNodes[0], selectedNodes[1]))
        {
            StatusText = "A connection between the selected nodes already exists.";
            return;
        }

        SelectSingleEdge($"{selectedNodes[0]}->{selectedNodes[1]}");
        StatusText = $"Connected '{selectedNodes[0]}' to '{selectedNodes[1]}'.";
    }

    private void DeleteCurrentSelection()
    {
        if (!CanDeleteSelection)
        {
            StatusText = "Select an item to delete.";
            return;
        }

        DeleteSelection();
        StatusText = "Deleted current selection.";
    }

    private void ApplyInspectorEdits()
    {
        InspectorValidationText = string.Empty;

        try
        {
            switch (CurrentInspectorEditMode)
            {
                case InspectorEditMode.Node:
                    ApplyNodeEdits();
                    break;

                case InspectorEditMode.Edge:
                    ApplyEdgeEdits();
                    break;

                case InspectorEditMode.Selection:
                    ApplyBulkEdits();
                    break;

                default:
                    ApplyNetworkEdits();
                    break;
            }

            RefreshInspector();
            NotifyVisualChanged();
        }
        catch (InvalidOperationException ex)
        {
            InspectorValidationText = ex.Message;
            Raise(nameof(HasInspectorValidationText));
        }
    }

    private void ApplyNetworkEdits()
    {
        if (!TryParsePositiveInt(InspectorTimelineLoopLengthText, out var loopLength))
        {
            throw new InvalidOperationException("Enter a timeline length of 1 or more.");
        }

        network.Name = string.IsNullOrWhiteSpace(InspectorName) ? "Untitled Network" : InspectorName.Trim();
        network.Description = InspectorDescription?.Trim() ?? string.Empty;
        network.TimelineLoopLength = loopLength;
        TimelineMaximum = Math.Max(8, loopLength);
        StatusText = $"Updated network '{network.Name}'.";
    }

    private void ApplyNodeEdits()
    {
        var nodeId = Scene.Selection.SelectedNodeIds.FirstOrDefault()
            ?? throw new InvalidOperationException("Select one node to edit node properties.");
        var node = network.Nodes.First(model => model.Id == nodeId);
        if (!TryParseOptionalDouble(InspectorNodeTranshipmentCapacityText, out var transhipmentCapacity))
        {
            throw new InvalidOperationException("Enter a transhipment capacity of 0 or more, or leave it blank.");
        }

        node.Name = string.IsNullOrWhiteSpace(InspectorName) ? node.Id : InspectorName.Trim();
        node.PlaceType = string.IsNullOrWhiteSpace(InspectorNodePlaceType) ? null : InspectorNodePlaceType.Trim();
        node.TranshipmentCapacity = transhipmentCapacity;
        UpdateSceneNode(node);
        StatusText = $"Updated node '{node.Name}'.";
    }

    private void ApplyEdgeEdits()
    {
        var edgeId = Scene.Selection.SelectedEdgeIds.FirstOrDefault()
            ?? throw new InvalidOperationException("Select one edge to edit edge properties.");
        var edge = network.Edges.First(model => model.Id == edgeId);
        if (!TryParseNonNegativeDouble(InspectorEdgeTimeText, out var time))
        {
            throw new InvalidOperationException("Enter a time of 0 or more.");
        }

        if (!TryParseNonNegativeDouble(InspectorEdgeCostText, out var cost))
        {
            throw new InvalidOperationException("Enter a cost of 0 or more.");
        }

        if (!TryParseOptionalDouble(InspectorEdgeCapacityText, out var capacity))
        {
            throw new InvalidOperationException("Enter a capacity of 0 or more, or leave it blank.");
        }

        edge.RouteType = string.IsNullOrWhiteSpace(InspectorEdgeRouteType) ? null : InspectorEdgeRouteType.Trim();
        edge.Time = time;
        edge.Cost = cost;
        edge.Capacity = capacity;
        edge.IsBidirectional = InspectorEdgeIsBidirectional;
        UpdateSceneEdge(edge);
        StatusText = $"Updated edge '{edge.Id}'.";
    }

    private void ApplyBulkEdits()
    {
        var selectedNodeIds = Scene.Selection.SelectedNodeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedNodeIds.Count == 0)
        {
            throw new InvalidOperationException("Select multiple items for bulk edit.");
        }

        if (!TryParseOptionalDouble(InspectorBulkTranshipmentCapacityText, out var transhipmentCapacity))
        {
            throw new InvalidOperationException("Enter a transhipment capacity of 0 or more, or leave it blank.");
        }

        foreach (var node in network.Nodes.Where(model => selectedNodeIds.Contains(model.Id)))
        {
            node.PlaceType = string.IsNullOrWhiteSpace(InspectorBulkPlaceType) ? null : InspectorBulkPlaceType.Trim();
            node.TranshipmentCapacity = transhipmentCapacity;
            UpdateSceneNode(node);
        }

        StatusText = "Updated selected nodes.";
    }

    private void PopulateInspectorEditor()
    {
        InspectorValidationText = string.Empty;
        switch (CurrentInspectorEditMode)
        {
            case InspectorEditMode.Node:
                var selectedNode = network.Nodes.First(model => model.Id == Scene.Selection.SelectedNodeIds.First());
                InspectorName = selectedNode.Name;
                InspectorNodePlaceType = selectedNode.PlaceType ?? string.Empty;
                InspectorNodeTranshipmentCapacityText = selectedNode.TranshipmentCapacity?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
                break;

            case InspectorEditMode.Edge:
                var selectedEdge = network.Edges.First(model => model.Id == Scene.Selection.SelectedEdgeIds.First());
                InspectorEdgeRouteType = selectedEdge.RouteType ?? string.Empty;
                InspectorEdgeTimeText = selectedEdge.Time.ToString("0.###", CultureInfo.InvariantCulture);
                InspectorEdgeCostText = selectedEdge.Cost.ToString("0.###", CultureInfo.InvariantCulture);
                InspectorEdgeCapacityText = selectedEdge.Capacity?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
                InspectorEdgeIsBidirectional = selectedEdge.IsBidirectional;
                break;

            case InspectorEditMode.Selection:
                var selectedModels = network.Nodes
                    .Where(model => Scene.Selection.SelectedNodeIds.Contains(model.Id))
                    .ToList();
                InspectorBulkPlaceType = selectedModels.Select(model => model.PlaceType).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
                    ? selectedModels.First().PlaceType ?? string.Empty
                    : string.Empty;
                InspectorBulkTranshipmentCapacityText = selectedModels
                    .Select(model => model.TranshipmentCapacity)
                    .Distinct()
                    .Count() == 1
                    ? selectedModels.First().TranshipmentCapacity?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty
                    : string.Empty;
                break;

            default:
                InspectorName = network.Name;
                InspectorDescription = network.Description;
                InspectorTimelineLoopLengthText = (network.TimelineLoopLength ?? 12).ToString(CultureInfo.InvariantCulture);
                break;
        }

        RaiseInspectorEditorPropertiesChanged();
    }

    private void RaiseInspectorEditorPropertiesChanged()
    {
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        Raise(nameof(CanConnectSelectedNodes));
        Raise(nameof(CanDeleteSelection));
        Raise(nameof(CurrentInspectorEditMode));
        Raise(nameof(IsEditingNetwork));
        Raise(nameof(IsEditingNode));
        Raise(nameof(IsEditingEdge));
        Raise(nameof(IsEditingSelection));
        Raise(nameof(InspectorEditModeLabel));
        Raise(nameof(InspectorEditModeHelp));
        Raise(nameof(ApplyInspectorLabel));
        Raise(nameof(HasInspectorValidationText));
    }

    private InspectorEditMode GetInspectorEditMode()
    {
        var nodeCount = Scene.Selection.SelectedNodeIds.Count;
        var edgeCount = Scene.Selection.SelectedEdgeIds.Count;
        var selectionCount = nodeCount + edgeCount;

        if (selectionCount == 0)
        {
            return InspectorEditMode.Network;
        }

        if (nodeCount == 1 && edgeCount == 0)
        {
            return InspectorEditMode.Node;
        }

        if (edgeCount == 1 && nodeCount == 0)
        {
            return InspectorEditMode.Edge;
        }

        return InspectorEditMode.Selection;
    }

    private void SelectSingleNode(string nodeId)
    {
        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        Scene.Selection.SelectedNodeIds.Add(nodeId);
        Scene.Selection.KeyboardNodeId = nodeId;
        Scene.Selection.KeyboardEdgeId = null;
        RefreshInspector();
        NotifyVisualChanged();
    }

    private void SelectSingleEdge(string edgeId)
    {
        Scene.Selection.SelectedNodeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Clear();
        Scene.Selection.SelectedEdgeIds.Add(edgeId);
        Scene.Selection.KeyboardNodeId = null;
        Scene.Selection.KeyboardEdgeId = edgeId;
        RefreshInspector();
        NotifyVisualChanged();
    }

    private void UpdateSceneNode(NodeModel node)
    {
        var sceneNode = Scene.Nodes.First(item => string.Equals(item.Id, node.Id, StringComparison.OrdinalIgnoreCase));
        sceneNode.Name = node.Name;
        sceneNode.TypeLabel = string.IsNullOrWhiteSpace(node.PlaceType) ? "Node" : node.PlaceType;
        sceneNode.MetricsLabel = BuildNodeMetricsLabel(node);
        sceneNode.Badges = BuildNodeBadges(node);
    }

    private void UpdateSceneEdge(EdgeModel edge)
    {
        var sceneEdge = Scene.Edges.First(item => string.Equals(item.Id, edge.Id, StringComparison.OrdinalIgnoreCase));
        sceneEdge.Label = edge.RouteType ?? edge.Id;
        sceneEdge.IsBidirectional = edge.IsBidirectional;
        sceneEdge.Capacity = edge.Capacity ?? 0d;
        sceneEdge.Cost = edge.Cost;
        sceneEdge.Time = edge.Time;
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
    }

    private static string BuildNodeMetricsLabel(NodeModel node) =>
        $"Tranship {node.TranshipmentCapacity?.ToString("0.#", CultureInfo.InvariantCulture) ?? "inf"}";

    private static IReadOnlyList<string> BuildNodeBadges(NodeModel node)
    {
        var produced = node.TrafficProfiles.Where(profile => profile.Production > 0d).Select(profile => profile.TrafficType).ToList();
        var consumed = node.TrafficProfiles.Where(profile => profile.Consumption > 0d).Select(profile => profile.TrafficType).ToList();
        var badges = new List<string>();
        if (produced.Count > 0)
        {
            badges.Add($"Out {string.Join("/", produced)}");
        }

        if (consumed.Count > 0)
        {
            badges.Add($"In {string.Join("/", consumed)}");
        }

        if (node.TrafficProfiles.Any(profile => profile.CanTransship))
        {
            badges.Add("Relay");
        }

        if (badges.Count == 0)
        {
            badges.Add("Draft");
        }

        return badges;
    }

    private static bool TryParseOptionalDouble(string text, out double? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0d)
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryParseNonNegativeDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0d;

    private static bool TryParsePositiveInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 1;
}

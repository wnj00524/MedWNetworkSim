using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly GraphMlFileService graphMlFileService = new();
    private readonly ReportExportService reportExportService = new();
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
    private string selectedTrafficType = string.Empty;
    private string inspectorName = string.Empty;
    private string inspectorType = string.Empty;
    private string inspectorProduction = "0";
    private string inspectorConsumption = "0";
    private string inspectorCapacity = string.Empty;
    private string inspectorTime = "1";
    private string inspectorCost = "1";
    private string inspectorBulkType = string.Empty;
    private string inspectorValidationMessage = string.Empty;
    private IReadOnlyList<TrafficSimulationOutcome> lastOutcomes = [];

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
        ApplyInspectorCommand = new RelayCommand(ApplyInspectorEdits);
        AddTrafficTypeCommand = new RelayCommand(AddTrafficType);
        RemoveTrafficTypeCommand = new RelayCommand(RemoveSelectedTrafficType, () => !string.IsNullOrWhiteSpace(SelectedTrafficType));

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
    public RelayCommand ApplyInspectorCommand { get; }
    public RelayCommand AddTrafficTypeCommand { get; }
    public RelayCommand RemoveTrafficTypeCommand { get; }
    public InspectorSection Inspector { get; }
    public ObservableCollection<ReportMetricViewModel> ReportMetrics { get; }
    public IReadOnlyList<TrafficTypeDefinition> TrafficTypes => network.TrafficTypes;
    public bool IsNodeInspectorEditable => Scene.Selection.SelectedNodeIds.Count == 1 && Scene.Selection.SelectedEdgeIds.Count == 0;
    public bool IsEdgeInspectorEditable => Scene.Selection.SelectedEdgeIds.Count == 1 && Scene.Selection.SelectedNodeIds.Count == 0;
    public bool IsBulkInspectorEditable => Scene.Selection.SelectedNodeIds.Count + Scene.Selection.SelectedEdgeIds.Count > 1;
    public bool IsNetworkInspectorEditable => Scene.Selection.SelectedNodeIds.Count == 0 && Scene.Selection.SelectedEdgeIds.Count == 0;
    public string InspectorName { get => inspectorName; set => SetProperty(ref inspectorName, value); }
    public string InspectorType { get => inspectorType; set => SetProperty(ref inspectorType, value); }
    public string InspectorProduction { get => inspectorProduction; set => SetProperty(ref inspectorProduction, value); }
    public string InspectorConsumption { get => inspectorConsumption; set => SetProperty(ref inspectorConsumption, value); }
    public string InspectorCapacity { get => inspectorCapacity; set => SetProperty(ref inspectorCapacity, value); }
    public string InspectorTime { get => inspectorTime; set => SetProperty(ref inspectorTime, value); }
    public string InspectorCost { get => inspectorCost; set => SetProperty(ref inspectorCost, value); }
    public string InspectorBulkType { get => inspectorBulkType; set => SetProperty(ref inspectorBulkType, value); }
    public string InspectorValidationMessage { get => inspectorValidationMessage; set => SetProperty(ref inspectorValidationMessage, value); }
    public string SelectedTrafficType
    {
        get => selectedTrafficType;
        set
        {
            if (SetProperty(ref selectedTrafficType, value))
            {
                RemoveTrafficTypeCommand.NotifyCanExecuteChanged();
            }
        }
    }

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
        lastOutcomes = [];
        StatusText = "Ready to edit your network.";
        NotifyVisualChanged();
    }

    public void OpenNetwork(string path)
    {
        network = fileService.Load(path);
        ResetAfterNetworkLoad($"Opened '{Path.GetFileName(path)}'.");
    }

    public void SaveNetwork(string path)
    {
        fileService.Save(network, path);
        StatusText = $"Saved '{Path.GetFileName(path)}'.";
    }

    public void ImportGraphMl(string path)
    {
        network = graphMlFileService.Load(path, new GraphMlTransferOptions(default, "transship", 25d));
        ResetAfterNetworkLoad($"Imported GraphML file '{Path.GetFileName(path)}'.");
    }

    public void ExportGraphMl(string path)
    {
        graphMlFileService.Save(network, path, new GraphMlTransferOptions(network.TrafficTypes.FirstOrDefault()?.Name, "transship", 25d));
        StatusText = $"Exported GraphML file '{Path.GetFileName(path)}'.";
    }

    public void ExportCurrentReport(string path, ReportExportFormat format)
    {
        reportExportService.SaveCurrentReport(network, lastOutcomes, [], path, format);
        StatusText = $"Exported current report to '{Path.GetFileName(path)}'.";
    }

    public void ExportTimelineReport(string path, int periods, ReportExportFormat format)
    {
        var state = temporalEngine.Initialize(network);
        var results = new List<TemporalNetworkSimulationEngine.TemporalSimulationStepResult>();
        for (var index = 0; index < Math.Max(1, periods); index++)
        {
            results.Add(temporalEngine.Advance(network, state));
        }

        reportExportService.SaveTimelineReport(network, results, path, format);
        StatusText = $"Exported timeline report ({results.Count} periods) to '{Path.GetFileName(path)}'.";
    }

    private static NetworkModel BuildDefaultNetwork()
    {
        return new NetworkModel
        {
            Name = "Avalonia Migration Pilot",
            Description = "Cross-platform shell with render-driven graph canvas.",
            TimelineLoopLength = 12,
            TrafficTypes =
            [
                new TrafficTypeDefinition { Name = "grain", RoutingPreference = RoutingPreference.TotalCost, AllocationMode = AllocationMode.GreedyBestRoute },
                new TrafficTypeDefinition { Name = "tools", RoutingPreference = RoutingPreference.Speed, AllocationMode = AllocationMode.GreedyBestRoute }
            ],
            Nodes =
            [
                new NodeModel
                {
                    Id = "granary",
                    Name = "Granary",
                    X = -420,
                    Y = -120,
                    PlaceType = "Storehouse",
                    TranshipmentCapacity = 100,
                    TrafficProfiles =
                    [
                        new NodeTrafficProfile { TrafficType = "grain", Production = 42, CanTransship = true, IsStore = true, StoreCapacity = 60 },
                        new NodeTrafficProfile { TrafficType = "tools", Consumption = 6, CanTransship = true }
                    ]
                },
                new NodeModel
                {
                    Id = "market",
                    Name = "Market Hall",
                    X = -60,
                    Y = 40,
                    PlaceType = "Exchange",
                    TranshipmentCapacity = 80,
                    TrafficProfiles =
                    [
                        new NodeTrafficProfile { TrafficType = "grain", Consumption = 24, CanTransship = true },
                        new NodeTrafficProfile { TrafficType = "tools", Consumption = 10, CanTransship = true }
                    ]
                },
                new NodeModel
                {
                    Id = "forge",
                    Name = "Forge Quarter",
                    X = 310,
                    Y = -80,
                    PlaceType = "Workshop",
                    TranshipmentCapacity = 60,
                    TrafficProfiles =
                    [
                        new NodeTrafficProfile { TrafficType = "grain", Consumption = 8, CanTransship = true },
                        new NodeTrafficProfile { TrafficType = "tools", Production = 18, CanTransship = true }
                    ]
                },
                new NodeModel
                {
                    Id = "harbor",
                    Name = "Harbor Gate",
                    X = 520,
                    Y = 220,
                    PlaceType = "Transit",
                    TranshipmentCapacity = 120,
                    TrafficProfiles =
                    [
                        new NodeTrafficProfile { TrafficType = "grain", Consumption = 16, CanTransship = true },
                        new NodeTrafficProfile { TrafficType = "tools", Consumption = 4, CanTransship = true }
                    ]
                }
            ],
            Edges =
            [
                new EdgeModel { Id = "granary->market", FromNodeId = "granary", ToNodeId = "market", Time = 1.2, Cost = 1.1, Capacity = 40, IsBidirectional = true, RouteType = "Cart path" },
                new EdgeModel { Id = "market->forge", FromNodeId = "market", ToNodeId = "forge", Time = 1.7, Cost = 1.4, Capacity = 26, IsBidirectional = true, RouteType = "Stone road" },
                new EdgeModel { Id = "forge->harbor", FromNodeId = "forge", ToNodeId = "harbor", Time = 2.1, Cost = 2.4, Capacity = 28, IsBidirectional = false, RouteType = "River lane" },
                new EdgeModel { Id = "market->harbor", FromNodeId = "market", ToNodeId = "harbor", Time = 2.6, Cost = 2.0, Capacity = 34, IsBidirectional = true, RouteType = "Merchant road" }
            ]
        };
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
        lastOutcomes = outcomes;
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
        Raise(nameof(IsNodeInspectorEditable));
        Raise(nameof(IsEdgeInspectorEditable));
        Raise(nameof(IsBulkInspectorEditable));
        Raise(nameof(IsNetworkInspectorEditable));
        Raise(nameof(TrafficTypes));
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
            InspectorName = network.Name;
            InspectorType = network.Description;
            InspectorCapacity = network.TimelineLoopLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
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
                "Set a place type to update all selected nodes.",
                "Delete removes the current selection."
            ];
            InspectorBulkType = "Updated place type";
            return;
        }

        if (selectedNodes.Count == 1)
        {
            var node = network.Nodes.First(model => model.Id == selectedNodes[0]);
            Inspector.Headline = node.Name;
            Inspector.Summary = string.IsNullOrWhiteSpace(node.PlaceType) ? "Node" : node.PlaceType!;
            Inspector.Details = node.TrafficProfiles.Select(profile =>
                $"{profile.TrafficType}: prod {profile.Production:0.#}, cons {profile.Consumption:0.#}, tranship {(profile.CanTransship ? "yes" : "no")}").ToList();
            InspectorName = node.Name;
            InspectorType = node.PlaceType ?? string.Empty;
            var profile = node.TrafficProfiles.FirstOrDefault();
            SelectedTrafficType = profile?.TrafficType ?? network.TrafficTypes.FirstOrDefault()?.Name ?? string.Empty;
            InspectorProduction = profile?.Production.ToString(CultureInfo.InvariantCulture) ?? "0";
            InspectorConsumption = profile?.Consumption.ToString(CultureInfo.InvariantCulture) ?? "0";
            InspectorCapacity = node.TranshipmentCapacity?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
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
        InspectorType = edgeModel.RouteType ?? string.Empty;
        InspectorCapacity = edgeModel.Capacity?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        InspectorTime = edgeModel.Time.ToString(CultureInfo.InvariantCulture);
        InspectorCost = edgeModel.Cost.ToString(CultureInfo.InvariantCulture);
        InspectorName = edgeModel.Id;
    }

    private void ResetAfterNetworkLoad(string status)
    {
        temporalState = null;
        CurrentPeriod = 0;
        TimelinePosition = 0;
        TimelineMaximum = Math.Max(8, network.TimelineLoopLength ?? 12);
        BuildSceneFromNetwork();
        Viewport.Reset(Scene.GetContentBounds(), LastViewportSize);
        lastOutcomes = [];
        PopulateReportMetrics([]);
        RefreshInspector();
        StatusText = status;
        NotifyVisualChanged();
    }

    private void AddTrafficType()
    {
        var index = network.TrafficTypes.Count + 1;
        var name = $"traffic-{index}";
        network.TrafficTypes.Add(new TrafficTypeDefinition { Name = name, RoutingPreference = RoutingPreference.TotalCost });
        SelectedTrafficType = name;
        StatusText = $"Added traffic type '{name}'.";
        RefreshInspector();
    }

    private void RemoveSelectedTrafficType()
    {
        if (string.IsNullOrWhiteSpace(SelectedTrafficType))
        {
            return;
        }

        network.TrafficTypes.RemoveAll(type => string.Equals(type.Name, SelectedTrafficType, StringComparison.OrdinalIgnoreCase));
        foreach (var node in network.Nodes)
        {
            node.TrafficProfiles.RemoveAll(profile => string.Equals(profile.TrafficType, SelectedTrafficType, StringComparison.OrdinalIgnoreCase));
        }

        SelectedTrafficType = network.TrafficTypes.FirstOrDefault()?.Name ?? string.Empty;
        BuildSceneFromNetwork();
        RefreshInspector();
        NotifyVisualChanged();
    }

    private void ApplyInspectorEdits()
    {
        InspectorValidationMessage = string.Empty;
        try
        {
            if (IsNetworkInspectorEditable)
            {
                network.Name = string.IsNullOrWhiteSpace(InspectorName) ? "Untitled Network" : InspectorName.Trim();
                network.Description = InspectorType?.Trim() ?? string.Empty;
                network.TimelineLoopLength = int.TryParse(InspectorCapacity, out var timeline) && timeline > 0 ? timeline : null;
                StatusText = "Updated network settings.";
                NotifyVisualChanged();
                return;
            }

            if (IsNodeInspectorEditable)
            {
                var nodeId = Scene.Selection.SelectedNodeIds.First();
                var node = network.Nodes.First(model => model.Id == nodeId);
                node.Name = string.IsNullOrWhiteSpace(InspectorName) ? node.Id : InspectorName.Trim();
                node.PlaceType = string.IsNullOrWhiteSpace(InspectorType) ? null : InspectorType.Trim();
                node.TranshipmentCapacity = double.TryParse(InspectorCapacity, out var capacity) ? Math.Max(0d, capacity) : null;
                var trafficType = string.IsNullOrWhiteSpace(SelectedTrafficType) ? network.TrafficTypes.FirstOrDefault()?.Name ?? "goods" : SelectedTrafficType.Trim();
                var profile = node.TrafficProfiles.FirstOrDefault(item => string.Equals(item.TrafficType, trafficType, StringComparison.OrdinalIgnoreCase));
                if (profile is null)
                {
                    profile = new NodeTrafficProfile { TrafficType = trafficType, CanTransship = true };
                    node.TrafficProfiles.Add(profile);
                }

                profile.Production = double.TryParse(InspectorProduction, out var production) ? Math.Max(0d, production) : 0d;
                profile.Consumption = double.TryParse(InspectorConsumption, out var consumption) ? Math.Max(0d, consumption) : 0d;
                BuildSceneFromNetwork();
                RefreshInspector();
                NotifyVisualChanged();
                StatusText = $"Updated node '{node.Name}'.";
                return;
            }

            if (IsEdgeInspectorEditable)
            {
                var edgeId = Scene.Selection.SelectedEdgeIds.First();
                var edge = network.Edges.First(model => model.Id == edgeId);
                edge.RouteType = string.IsNullOrWhiteSpace(InspectorType) ? null : InspectorType.Trim();
                edge.Time = double.TryParse(InspectorTime, out var time) ? Math.Max(0d, time) : edge.Time;
                edge.Cost = double.TryParse(InspectorCost, out var cost) ? Math.Max(0d, cost) : edge.Cost;
                edge.Capacity = double.TryParse(InspectorCapacity, out var capacity) ? Math.Max(0d, capacity) : null;
                BuildSceneFromNetwork();
                RefreshInspector();
                NotifyVisualChanged();
                StatusText = $"Updated edge '{edge.Id}'.";
                return;
            }

            if (IsBulkInspectorEditable)
            {
                var updatedType = string.IsNullOrWhiteSpace(InspectorBulkType) ? null : InspectorBulkType.Trim();
                if (!string.IsNullOrWhiteSpace(updatedType))
                {
                    var selectedNodes = Scene.Selection.SelectedNodeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var node in network.Nodes.Where(node => selectedNodes.Contains(node.Id)))
                    {
                        node.PlaceType = updatedType;
                    }
                }

                BuildSceneFromNetwork();
                RefreshInspector();
                NotifyVisualChanged();
                StatusText = "Applied bulk edits to the current selection.";
            }
        }
        catch (Exception ex)
        {
            InspectorValidationMessage = ex.Message;
        }
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

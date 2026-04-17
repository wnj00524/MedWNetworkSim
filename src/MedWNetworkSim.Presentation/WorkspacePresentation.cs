using System.Collections.ObjectModel;
using System.ComponentModel;
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

    public WorkspaceViewModel()
    {
        Scene = new GraphScene();
        Viewport = new GraphViewport();
        Inspector = new InspectorSection();
        ReportMetrics = [];
        TopCommandBar = "New, simulate, step, fit, and inspect from the Avalonia shell.";
        NewCommand = new RelayCommand(CreateDefaultNetwork);
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

        CreateDefaultNetwork();
    }

    public GraphScene Scene { get; }
    public GraphViewport Viewport { get; }
    public GraphSize LastViewportSize { get; private set; } = new(1440d, 860d);
    public int ViewportVersion { get; private set; }
    public string TopCommandBar { get; }
    public RelayCommand NewCommand { get; }
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

    private void CreateDefaultNetwork()
    {
        network = fileService.NormalizeAndValidate(BuildDefaultNetwork());
        temporalState = null;
        CurrentPeriod = 0;
        TimelineMaximum = Math.Max(8, network.TimelineLoopLength ?? 12);
        TimelinePosition = 0;
        BuildSceneFromNetwork();
        Scene.Simulation.ShowAnimatedFlows = true;
        Scene.Simulation.ReducedMotion = ReducedMotion;
        Viewport.Reset(Scene.GetContentBounds(), LastViewportSize);
        RefreshInspector();
        PopulateReportMetrics([]);
        StatusText = "Created a sample migration workspace in the Avalonia shell.";
        NotifyVisualChanged();
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

            Scene.Nodes.Add(new GraphNodeSceneItem
            {
                Id = node.Id,
                Name = node.Name,
                TypeLabel = string.IsNullOrWhiteSpace(node.PlaceType) ? "Node" : node.PlaceType,
                MetricsLabel = $"Tranship {node.TranshipmentCapacity?.ToString("0.#") ?? "inf"}",
                Bounds = new GraphRect(node.X ?? 0d, node.Y ?? 0d, 190d, 116d),
                FillColor = SKColor.Parse("#163149"),
                StrokeColor = SKColor.Parse("#6AAED6"),
                Badges = badges,
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
            TypeLabel = "Draft",
            MetricsLabel = "Tranship 40",
            Bounds = new GraphRect(center.X, center.Y, 190d, 116d),
            FillColor = SKColor.Parse("#163149"),
            StrokeColor = SKColor.Parse("#6AAED6"),
            Badges = ["Draft"],
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
            Inspector.Summary = "Select a node or edge, or press N to add a node at the viewport center.";
            Inspector.Details =
            [
                $"Traffic types: {network.TrafficTypes.Count}",
                $"Nodes: {network.Nodes.Count}",
                $"Edges: {network.Edges.Count}",
                $"Reduced motion: {(ReducedMotion ? "On" : "Off")}"
            ];
            return;
        }

        if (selectedNodes.Count > 1 || selectedEdges.Count > 1)
        {
            Inspector.Headline = "Multi-selection";
            Inspector.Summary = "Bulk editing is staged in the inspector for the Avalonia path.";
            Inspector.Details =
            [
                $"{selectedNodes.Count} nodes selected",
                $"{selectedEdges.Count} edges selected",
                "Delete removes the current selection.",
                "Right-drag between nodes creates a route."
            ];
            return;
        }

        if (selectedNodes.Count == 1)
        {
            var node = network.Nodes.First(model => model.Id == selectedNodes[0]);
            Inspector.Headline = node.Name;
            Inspector.Summary = string.IsNullOrWhiteSpace(node.PlaceType) ? "Node" : node.PlaceType!;
            Inspector.Details = node.TrafficProfiles.Select(profile =>
                $"{profile.TrafficType}: prod {profile.Production:0.#}, cons {profile.Consumption:0.#}, tranship {(profile.CanTransship ? "yes" : "no")}").ToList();
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
    }
}

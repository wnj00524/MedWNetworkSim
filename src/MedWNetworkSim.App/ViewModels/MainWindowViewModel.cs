using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private readonly NetworkFileService fileService = new();
    private readonly NetworkSimulationEngine simulationEngine = new();
    private readonly List<RouteAllocationRowViewModel> allAllocations = [];
    private readonly List<ConsumerCostSummaryRowViewModel> allConsumerCostSummaries = [];

    private NetworkModel currentNetwork = new();
    private string activeFileLabel = "Bundled sample";
    private string networkName = "MedW Network Simulator";
    private string networkDescription = "Load a JSON network file or use the bundled sample to start modelling routes.";
    private string statusMessage = "Load a network file, move nodes on the canvas, then run the simulation.";
    private TrafficSummaryViewModel? selectedTraffic;
    private double workspaceWidth = 1600d;
    private double workspaceHeight = 1000d;
    private bool hasNetwork;

    public MainWindowViewModel()
    {
        LoadBundledSampleIfAvailable();
    }

    public ObservableCollection<NodeViewModel> Nodes { get; } = [];

    public ObservableCollection<EdgeViewModel> Edges { get; } = [];

    public ObservableCollection<TrafficSummaryViewModel> TrafficTypes { get; } = [];

    public ObservableCollection<RouteAllocationRowViewModel> VisibleAllocations { get; } = [];

    public ObservableCollection<ConsumerCostSummaryRowViewModel> VisibleConsumerCostSummaries { get; } = [];

    public string WindowTitle => HasNetwork ? $"MedW Network Simulator - {NetworkName}" : "MedW Network Simulator";

    public string ActiveFileLabel
    {
        get => activeFileLabel;
        private set => SetProperty(ref activeFileLabel, value);
    }

    public string NetworkName
    {
        get => networkName;
        private set
        {
            if (SetProperty(ref networkName, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    public string NetworkDescription
    {
        get => networkDescription;
        private set => SetProperty(ref networkDescription, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public bool HasNetwork
    {
        get => hasNetwork;
        private set
        {
            if (SetProperty(ref hasNetwork, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    public int NodeCount => Nodes.Count;

    public int EdgeCount => Edges.Count;

    public int TrafficTypeCount => TrafficTypes.Count;

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
            RefreshVisibleAllocations();
            RefreshVisibleConsumerCostSummaries();
        }
    }

    public string VisibleAllocationHeadline => SelectedTraffic is null
        ? $"{VisibleAllocations.Count} routed movement(s) across all traffic"
        : $"{VisibleAllocations.Count} routed movement(s) for {SelectedTraffic.Name}";

    public string VisibleConsumerCostHeadline => SelectedTraffic is null
        ? $"{VisibleConsumerCostSummaries.Count} consumer cost row(s) across all traffic"
        : $"{VisibleConsumerCostSummaries.Count} consumer cost row(s) for {SelectedTraffic.Name}";

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

    public void LoadFromFile(string path)
    {
        var network = fileService.Load(path);
        LoadNetwork(network, path, $"Loaded network file '{Path.GetFileName(path)}'.");
    }

    public void SaveToFile(string path)
    {
        var network = BuildCurrentNetwork();
        fileService.Save(network, path);
        ActiveFileLabel = path;
        StatusMessage = $"Saved the current network to '{Path.GetFileName(path)}'.";
    }

    public void LoadBundledSample()
    {
        var samplePath = Path.Combine(AppContext.BaseDirectory, "Samples", "sample-network.json");
        if (!File.Exists(samplePath))
        {
            throw new FileNotFoundException("The bundled sample network was not found.", samplePath);
        }

        var network = fileService.Load(samplePath);
        LoadNetwork(network, samplePath, "Loaded the bundled sample network.");
    }

    public void RunSimulation()
    {
        if (!HasNetwork)
        {
            return;
        }

        var current = BuildCurrentNetwork();
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

        allAllocations.Clear();
        allAllocations.AddRange(outcomes.SelectMany(outcome => outcome.Allocations).Select(allocation => new RouteAllocationRowViewModel(allocation)));
        allConsumerCostSummaries.Clear();
        allConsumerCostSummaries.AddRange(
            simulationEngine.SummarizeConsumerCosts(outcomes)
                .Select(summary => new ConsumerCostSummaryRowViewModel(summary)));
        RefreshVisibleAllocations();
        RefreshVisibleConsumerCostSummaries();

        var totalDelivered = outcomes.Sum(outcome => outcome.TotalDelivered);
        StatusMessage = $"Simulation complete. Routed {allAllocations.Count} movement(s) delivering {totalDelivered:0.##} unit(s).";
    }

    public void AutoArrangeNodes()
    {
        if (!HasNetwork)
        {
            return;
        }

        var current = BuildCurrentNetwork();
        var arranged = fileService.AutoArrange(current);
        LoadNetwork(arranged, ActiveFileLabel, "Auto-arranged all node positions.");
    }

    public void MoveNode(NodeViewModel node, double deltaX, double deltaY)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.MoveBy(deltaX, deltaY);
        RecalculateWorkspace();
    }

    private void LoadBundledSampleIfAvailable()
    {
        try
        {
            LoadBundledSample();
        }
        catch
        {
            HasNetwork = false;
        }
    }

    private void LoadNetwork(NetworkModel network, string? activeFilePath, string successMessage)
    {
        currentNetwork = network;

        foreach (var node in Nodes)
        {
            node.PositionChanged -= HandleNodePositionChanged;
        }

        Nodes.Clear();
        Edges.Clear();
        TrafficTypes.Clear();
        VisibleAllocations.Clear();
        VisibleConsumerCostSummaries.Clear();
        allAllocations.Clear();
        allConsumerCostSummaries.Clear();
        SelectedTraffic = null;

        var nodeMap = new Dictionary<string, NodeViewModel>(Comparer);

        foreach (var nodeModel in network.Nodes)
        {
            var nodeViewModel = new NodeViewModel(nodeModel);
            nodeViewModel.PositionChanged += HandleNodePositionChanged;
            Nodes.Add(nodeViewModel);
            nodeMap[nodeModel.Id] = nodeViewModel;
        }

        foreach (var edgeModel in network.Edges)
        {
            if (!nodeMap.TryGetValue(edgeModel.FromNodeId, out var sourceNode) ||
                !nodeMap.TryGetValue(edgeModel.ToNodeId, out var targetNode))
            {
                continue;
            }

            Edges.Add(new EdgeViewModel(edgeModel, sourceNode, targetNode));
        }

        foreach (var summary in BuildTrafficSummaries(network))
        {
            TrafficTypes.Add(summary);
        }

        NetworkName = network.Name;
        NetworkDescription = string.IsNullOrWhiteSpace(network.Description)
            ? "No description was provided in the source file."
            : network.Description;
        ActiveFileLabel = activeFilePath ?? "Unsaved network";
        HasNetwork = true;
        RecalculateWorkspace();
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(EdgeCount));
        OnPropertyChanged(nameof(TrafficTypeCount));
        StatusMessage = successMessage;
    }

    private List<TrafficSummaryViewModel> BuildTrafficSummaries(NetworkModel network)
    {
        var definitionsByTraffic = network.TrafficTypes
            .ToDictionary(definition => definition.Name, definition => definition, Comparer);

        var summaries = new List<TrafficSummaryViewModel>();

        foreach (var trafficName in GetOrderedTrafficNames(network))
        {
            var profiles = network.Nodes
                .Select(node => node.TrafficProfiles.FirstOrDefault(profile => Comparer.Equals(profile.TrafficType, trafficName)))
                .Where(profile => profile is not null)
                .Cast<NodeTrafficProfile>()
                .ToList();

            var definition = definitionsByTraffic.GetValueOrDefault(trafficName)
                ?? new TrafficTypeDefinition
                {
                    Name = trafficName,
                    RoutingPreference = RoutingPreference.TotalCost
                };

            summaries.Add(new TrafficSummaryViewModel(
                trafficName,
                definition.RoutingPreference,
                profiles.Sum(profile => profile.Production),
                profiles.Sum(profile => profile.Consumption),
                profiles.Count(profile => profile.Production > 0),
                profiles.Count(profile => profile.Consumption > 0),
                profiles.Count(profile => profile.CanTransship)));
        }

        return summaries;
    }

    private IEnumerable<string> GetOrderedTrafficNames(NetworkModel network)
    {
        var orderedNames = new List<string>();
        var seen = new HashSet<string>(Comparer);

        foreach (var definition in network.TrafficTypes)
        {
            if (!string.IsNullOrWhiteSpace(definition.Name) && seen.Add(definition.Name))
            {
                orderedNames.Add(definition.Name);
            }
        }

        var undeclaredNames = network.Nodes
            .SelectMany(node => node.TrafficProfiles)
            .Select(profile => profile.TrafficType)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !seen.Contains(name))
            .Distinct(Comparer)
            .OrderBy(name => name, Comparer);

        orderedNames.AddRange(undeclaredNames);
        return orderedNames;
    }

    private NetworkModel BuildCurrentNetwork()
    {
        currentNetwork = fileService.NormalizeAndValidate(new NetworkModel
        {
            Name = NetworkName,
            Description = currentNetwork.Description,
            TrafficTypes = currentNetwork.TrafficTypes
                .Select(definition => new TrafficTypeDefinition
                {
                    Name = definition.Name,
                    Description = definition.Description,
                    RoutingPreference = definition.RoutingPreference,
                    CapacityBidPerUnit = definition.CapacityBidPerUnit
                })
                .ToList(),
            Nodes = Nodes.Select(node => node.ToModel()).ToList(),
            Edges = Edges.Select(edge => edge.ToModel()).ToList()
        });

        return currentNetwork;
    }

    private void HandleNodePositionChanged(object? sender, EventArgs e)
    {
        RecalculateWorkspace();
    }

    private void RecalculateWorkspace()
    {
        if (Nodes.Count == 0)
        {
            WorkspaceWidth = 1600d;
            WorkspaceHeight = 1000d;
            return;
        }

        var maxX = Nodes.Max(node => node.CenterX + (node.Width / 2d));
        var maxY = Nodes.Max(node => node.CenterY + (node.Height / 2d));

        WorkspaceWidth = Math.Max(1400d, maxX + 180d);
        WorkspaceHeight = Math.Max(900d, maxY + 180d);
    }

    private void RefreshVisibleAllocations()
    {
        VisibleAllocations.Clear();

        var source = SelectedTraffic is null
            ? allAllocations
            : allAllocations.Where(allocation => Comparer.Equals(allocation.TrafficType, SelectedTraffic.Name));

        foreach (var allocation in source)
        {
            VisibleAllocations.Add(allocation);
        }

        OnPropertyChanged(nameof(VisibleAllocationHeadline));
    }

    private void RefreshVisibleConsumerCostSummaries()
    {
        VisibleConsumerCostSummaries.Clear();

        var source = SelectedTraffic is null
            ? allConsumerCostSummaries
            : allConsumerCostSummaries.Where(summary => Comparer.Equals(summary.TrafficType, SelectedTraffic.Name));

        foreach (var summary in source)
        {
            VisibleConsumerCostSummaries.Add(summary);
        }

        OnPropertyChanged(nameof(VisibleConsumerCostHeadline));
    }
}

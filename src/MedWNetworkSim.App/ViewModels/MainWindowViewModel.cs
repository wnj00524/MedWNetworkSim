using System.Collections.ObjectModel;
using System.ComponentModel;
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

    private string activeFileLabel = "Bundled sample";
    private string networkName = "MedW Network Simulator";
    private string networkDescription = "Load a JSON network file, or create a new one and edit it directly in the app.";
    private string statusMessage = "Load a network file or create a new one, then edit nodes and edges directly in the application.";
    private TrafficSummaryViewModel? selectedTraffic;
    private NodeViewModel? selectedNode;
    private NodeTrafficProfileViewModel? selectedNodeTrafficProfile;
    private EdgeViewModel? selectedEdge;
    private TrafficTypeDefinitionEditorViewModel? selectedTrafficDefinition;
    private bool isNormalizingNodeTrafficProfiles;
    private double workspaceWidth = 1600d;
    private double workspaceHeight = 1000d;
    private bool hasNetwork;

    public MainWindowViewModel()
    {
        LoadBundledSampleIfAvailable();
    }

    public ObservableCollection<NodeViewModel> Nodes { get; } = [];

    public ObservableCollection<EdgeViewModel> Edges { get; } = [];

    public ObservableCollection<TrafficTypeDefinitionEditorViewModel> TrafficDefinitions { get; } = [];

    public ObservableCollection<TrafficSummaryViewModel> TrafficTypes { get; } = [];

    public ObservableCollection<string> NodeIdOptions { get; } = [];

    public ObservableCollection<string> TrafficTypeNameOptions { get; } = [];

    public ObservableCollection<RouteAllocationRowViewModel> VisibleAllocations { get; } = [];

    public ObservableCollection<ConsumerCostSummaryRowViewModel> VisibleConsumerCostSummaries { get; } = [];

    public string WindowTitle => HasNetwork ? $"MedW Network Simulator - {NetworkName}" : "MedW Network Simulator";

    public Array RoutingPreferences { get; } = Enum.GetValues(typeof(RoutingPreference));

    public string ActiveFileLabel
    {
        get => activeFileLabel;
        private set => SetProperty(ref activeFileLabel, value);
    }

    public string NetworkName
    {
        get => networkName;
        set
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
        set => SetProperty(ref networkDescription, value);
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

    public int TrafficTypeCount => TrafficDefinitions.Count;

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

    public NodeViewModel? SelectedNode
    {
        get => selectedNode;
        set
        {
            if (!SetProperty(ref selectedNode, value))
            {
                return;
            }

            SelectedNodeTrafficProfile = value?.TrafficProfiles.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedNodeTrafficRoleHeadline));
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
        ? "Traffic Roles"
        : $"Traffic Roles For {SelectedNode.Name}";

    public IReadOnlyList<string> SelectedNodeRoleOptions => SelectedNodeTrafficProfile?.RoleOptions ?? [];

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
        get => SelectedNodeTrafficProfile?.SelectedRoleName;
        set
        {
            if (SelectedNodeTrafficProfile is null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (string.Equals(SelectedNodeTrafficProfile.SelectedRoleName, value, StringComparison.Ordinal))
            {
                return;
            }

            SelectedNodeTrafficProfile.SelectedRoleName = value;
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

    public string SelectedNodeTrafficSelectionLabel => SelectedNodeTrafficProfile?.SelectionLabel ?? "No traffic role selected";

    public string SelectedNodeTrafficRoleSummary => SelectedNodeTrafficProfile?.RoleSummary ?? "Choose or add a traffic role entry.";

    public EdgeViewModel? SelectedEdge
    {
        get => selectedEdge;
        set => SetProperty(ref selectedEdge, value);
    }

    public TrafficTypeDefinitionEditorViewModel? SelectedTrafficDefinition
    {
        get => selectedTrafficDefinition;
        set => SetProperty(ref selectedTrafficDefinition, value);
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

    public void CreateNewNetwork()
    {
        LoadNetwork(
            new NetworkModel
            {
                Name = "New Network",
                Description = "Describe the network here."
            },
            null,
            "Created a new network. Add traffic types, nodes, and edges in the editor.");
    }

    public void LoadFromFile(string path)
    {
        var network = fileService.Load(path);
        LoadNetwork(network, path, $"Loaded network file '{Path.GetFileName(path)}'.");
    }

    public void SaveToFile(string path)
    {
        var network = BuildValidatedNetwork();
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
        StatusMessage = "Auto-arranged all node positions.";
    }

    public void AddTrafficDefinition()
    {
        EnsureNetworkExists();

        var definition = new TrafficTypeDefinitionEditorViewModel(new TrafficTypeDefinition
        {
            Name = GetNextUniqueName("Traffic", TrafficDefinitions.Select(item => item.Name)),
            RoutingPreference = RoutingPreference.TotalCost
        });

        RegisterTrafficDefinition(definition);
        SelectedTrafficDefinition = definition;
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

    public void AddNode()
    {
        EnsureNetworkExists();

        var primaryTrafficDefinition = EnsurePrimaryTrafficDefinition();

        var nodeIndex = Nodes.Count + 1;
        var node = new NodeViewModel(new NodeModel
        {
            Id = GetNextUniqueName("N", Nodes.Select(item => item.Id)),
            Name = $"Node {nodeIndex}",
            X = 220d + ((nodeIndex - 1) % 4 * 220d),
            Y = 180d + ((nodeIndex - 1) / 4 * 170d)
        });
        var initialProfile = new NodeTrafficProfileViewModel(new NodeTrafficProfile
        {
            TrafficType = primaryTrafficDefinition.Name
        });

        node.AddTrafficProfile(initialProfile);

        RegisterNode(node);
        SelectedNode = node;
        SelectedNodeTrafficProfile = initialProfile;
        RefreshDerivedStateAfterStructureChange("Added a new node.");
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
        var profile = new NodeTrafficProfileViewModel(new NodeTrafficProfile
        {
            TrafficType = trafficName
        });

        SelectedNode.AddTrafficProfile(profile);
        SelectedNodeTrafficProfile = profile;
        RefreshDerivedStateAfterStructureChange("Added a traffic profile to the selected node.");
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

        RegisterEdge(edge);
        SelectedEdge = edge;
        RefreshDerivedStateAfterStructureChange("Added a new edge.");
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

    public void MoveNode(NodeViewModel node, double deltaX, double deltaY)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.MoveBy(deltaX, deltaY);
        RecalculateWorkspace();
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

        var definition = new TrafficTypeDefinitionEditorViewModel(new TrafficTypeDefinition
        {
            Name = GetNextUniqueName("Traffic", TrafficDefinitions.Select(item => item.Name)),
            RoutingPreference = RoutingPreference.TotalCost
        });

        RegisterTrafficDefinition(definition);
        SelectedTrafficDefinition = definition;
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
        NodeIdOptions.Clear();
        TrafficTypeNameOptions.Clear();
        VisibleAllocations.Clear();
        VisibleConsumerCostSummaries.Clear();
        allAllocations.Clear();
        allConsumerCostSummaries.Clear();
        SelectedTraffic = null;
        SelectedNode = null;
        SelectedNodeTrafficProfile = null;
        SelectedEdge = null;
        SelectedTrafficDefinition = null;

        foreach (var definition in BuildDefinitionEditors(network))
        {
            RegisterTrafficDefinition(definition);
        }

        foreach (var nodeModel in network.Nodes)
        {
            RegisterNode(new NodeViewModel(nodeModel));
        }

        RefreshNodeIdOptions();

        var nodeMap = CreateNodeMap();
        foreach (var edgeModel in network.Edges)
        {
            nodeMap.TryGetValue(edgeModel.FromNodeId, out var sourceNode);
            nodeMap.TryGetValue(edgeModel.ToNodeId, out var targetNode);
            RegisterEdge(new EdgeViewModel(edgeModel, sourceNode, targetNode));
        }

        NetworkName = network.Name;
        NetworkDescription = string.IsNullOrWhiteSpace(network.Description)
            ? string.Empty
            : network.Description;
        ActiveFileLabel = activeFilePath ?? "Unsaved network";
        HasNetwork = true;
        RefreshNodeIdOptions();
        RefreshTrafficTypeNameOptions();
        RefreshTrafficSummariesFromCurrentState();
        RecalculateWorkspace();
        RefreshCounts();
        StatusMessage = successMessage;
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
                    RoutingPreference = RoutingPreference.TotalCost
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
            TrafficTypes = TrafficDefinitions.Select(definition => definition.ToModel()).ToList(),
            Nodes = Nodes.Select(node => node.ToModel()).ToList(),
            Edges = Edges.Select(edge => edge.ToModel()).ToList()
        };
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
        if (isNormalizingNodeTrafficProfiles)
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
        RefreshDerivedStateAfterStructureChange("Updated edge data.");
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

        RefreshDerivedStateAfterStructureChange("Updated traffic type settings.");
    }

    private void HandleTrafficDefinitionNameChanged(object? sender, ValueChangedEventArgs<string> e)
    {
        foreach (var node in Nodes)
        {
            foreach (var profile in node.TrafficProfiles.Where(profile => Comparer.Equals(profile.TrafficType, e.OldValue)))
            {
                profile.TrafficType = e.NewValue;
            }
        }

        RefreshDerivedStateAfterStructureChange("Renamed a traffic type and updated matching node profiles.");
    }

    private void RefreshDerivedStateAfterStructureChange(string message)
    {
        // Centralize all the "network shape changed" refresh work so the UI stays consistent after edits.
        RefreshNodeIdOptions();
        RefreshTrafficTypeNameOptions();
        RefreshEdgeBindings();
        RefreshTrafficSummariesFromCurrentState();
        RecalculateWorkspace();
        RefreshCounts();
        InvalidateSimulationResults(message);
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

    private void RefreshTrafficTypeNameOptions()
    {
        var trafficTypeNames = TrafficDefinitions
            .Select(definition => definition.Name)
            .Concat(Nodes.SelectMany(node => node.TrafficProfiles).Select(profile => profile.TrafficType))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(Comparer)
            .OrderBy(name => name, Comparer);

        SynchronizeCollection(TrafficTypeNameOptions, trafficTypeNames);
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
                profiles.Sum(profile => profile.Production),
                profiles.Sum(profile => profile.Consumption),
                profiles.Count(profile => profile.Production > 0),
                profiles.Count(profile => profile.Consumption > 0),
                profiles.Count(profile => profile.CanTransship)));
        }
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

    private void InvalidateSimulationResults(string message)
    {
        allAllocations.Clear();
        allConsumerCostSummaries.Clear();
        VisibleAllocations.Clear();
        VisibleConsumerCostSummaries.Clear();

        foreach (var traffic in TrafficTypes)
        {
            traffic.ClearOutcome();
        }

        OnPropertyChanged(nameof(VisibleAllocationHeadline));
        OnPropertyChanged(nameof(VisibleConsumerCostHeadline));
        StatusMessage = message;
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

    private static void SynchronizeCollection(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
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

    private void RaiseSelectedNodeTrafficEditorPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedNodeRoleOptions));
        OnPropertyChanged(nameof(SelectedNodeTrafficType));
        OnPropertyChanged(nameof(SelectedNodeRoleName));
        OnPropertyChanged(nameof(IsSelectedNodeProducer));
        OnPropertyChanged(nameof(IsSelectedNodeConsumer));
        OnPropertyChanged(nameof(SelectedNodeProduction));
        OnPropertyChanged(nameof(SelectedNodeConsumption));
        OnPropertyChanged(nameof(SelectedNodeTrafficSelectionLabel));
        OnPropertyChanged(nameof(SelectedNodeTrafficRoleSummary));
    }
}

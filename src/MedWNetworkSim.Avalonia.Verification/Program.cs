using Avalonia;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.Interaction;
using MedWNetworkSim.Presentation;
using MedWNetworkSim.Rendering;
using MedWNetworkSim.UI;

ScenarioCoordinateTransformPreservesLogicalInput();
ScenarioCoordinateTransformClampsPointerOutsideCanvas();
ScenarioAddNodePlacementMatchesClickedWorldPosition();
ScenarioDragAndConnectUseRenderedCoordinates();
ScenarioMultipleTrafficProfilesCanBeSwitched();
ScenarioNodeTrafficRoleCanBeEditedInInspector();
ScenarioTrafficDefinitionRenameAndRemovalPropagate();
ScenarioNodeEditsPersistThroughSaveLoad();
ScenarioToolCommandsReflectRealModes();
ScenarioEscapeReturnsSelectTool();
ScenarioNodeBoundsGrowWhenTextWraps();
ScenarioEdgeTooltipIncludesRouteDetails();
ScenarioPressureExplanationAppearsInNodeDetails();
ScenarioReportsPopulateAndResetAroundTimeline();

Console.WriteLine("Avalonia verification passed.");

static void ScenarioCoordinateTransformPreservesLogicalInput()
{
    var transform = GraphCanvasCoordinateTransform.Create(new Size(800d, 600d), 1.25d, 1.5d);
    var pointer = transform.PointerToGraph(new Point(400d, 300d));

    AssertNumberEqual(1000d, transform.PixelViewport.Width, "coordinate transform width");
    AssertNumberEqual(900d, transform.PixelViewport.Height, "coordinate transform height");
    AssertNumberEqual(400d, pointer.X, "coordinate transform pointer x");
    AssertNumberEqual(300d, pointer.Y, "coordinate transform pointer y");
}

static void ScenarioCoordinateTransformClampsPointerOutsideCanvas()
{
    var transform = GraphCanvasCoordinateTransform.Create(new Size(900d, 500d), 1.5d);
    var pointer = transform.PointerToGraph(new Point(1250d, -20d));

    AssertNumberEqual(900d, pointer.X, "coordinate clamp pointer x");
    AssertNumberEqual(0d, pointer.Y, "coordinate clamp pointer y");
}

static void ScenarioAddNodePlacementMatchesClickedWorldPosition()
{
    var workspace = new WorkspaceViewModel();
    workspace.AddNodeToolCommand.Execute(null);
    var context = workspace.CreateInteractionContext(new GraphSize(900d, 600d));
    var screenPoint = new GraphPoint(240d, 180d);
    var expectedWorld = context.Viewport.ScreenToWorld(screenPoint, context.ViewportSize);

    workspace.InteractionController.OnPointerPressed(context, GraphPointerButton.Left, screenPoint, false, false, false);

    var saved = SaveAndReload(workspace);
    var created = saved.Nodes.Single();
    AssertNumberNear(expectedWorld.X, created.X!.Value, 0.001d, "add node saved x");
    AssertNumberNear(expectedWorld.Y, created.Y!.Value, 0.001d, "add node saved y");
}

static void ScenarioDragAndConnectUseRenderedCoordinates()
{
    var workspace = new WorkspaceViewModel();
    workspace.AddNodeToolCommand.Execute(null);
    var addContext = workspace.CreateInteractionContext(new GraphSize(1000d, 700d));
    workspace.InteractionController.OnPointerPressed(addContext, GraphPointerButton.Left, new GraphPoint(300d, 260d), false, false, false);
    workspace.InteractionController.OnPointerPressed(addContext, GraphPointerButton.Left, new GraphPoint(700d, 260d), false, false, false);

    workspace.SelectToolCommand.Execute(null);
    var selectContext = workspace.CreateInteractionContext(new GraphSize(1000d, 700d));
    var firstNode = workspace.Scene.Nodes.First();
    var draggedNodeId = firstNode.Id;
    var firstNodeCenter = workspace.Viewport.WorldToScreen(new GraphPoint(firstNode.Bounds.CenterX, firstNode.Bounds.CenterY), selectContext.ViewportSize);
    workspace.InteractionController.OnPointerPressed(selectContext, GraphPointerButton.Left, firstNodeCenter, false, false, false);
    var draggedTo = new GraphPoint(firstNodeCenter.X + 120d, firstNodeCenter.Y + 45d);
    var expectedDraggedWorld = selectContext.Viewport.ScreenToWorld(draggedTo, selectContext.ViewportSize);
    workspace.InteractionController.OnPointerMoved(selectContext, draggedTo);
    workspace.InteractionController.OnPointerReleased(selectContext, GraphPointerButton.Left, draggedTo, false);
    workspace.NotifyVisualChanged();

    workspace.ConnectToolCommand.Execute(null);
    var connectContext = workspace.CreateInteractionContext(new GraphSize(1000d, 700d));
    var sourceScreen = workspace.Viewport.WorldToScreen(new GraphPoint(workspace.Scene.Nodes[0].Bounds.CenterX, workspace.Scene.Nodes[0].Bounds.CenterY), connectContext.ViewportSize);
    var targetScreen = workspace.Viewport.WorldToScreen(new GraphPoint(workspace.Scene.Nodes[1].Bounds.CenterX, workspace.Scene.Nodes[1].Bounds.CenterY), connectContext.ViewportSize);
    workspace.InteractionController.OnPointerPressed(connectContext, GraphPointerButton.Left, sourceScreen, false, false, false);
    workspace.InteractionController.OnPointerMoved(connectContext, targetScreen);
    workspace.InteractionController.OnPointerReleased(connectContext, GraphPointerButton.Left, targetScreen, false);
    workspace.NotifyVisualChanged();

    var saved = SaveAndReload(workspace);
    var movedNode = saved.Nodes.Single(node => node.Id == draggedNodeId);
    AssertNumberNear(expectedDraggedWorld.X, movedNode.X!.Value, 0.001d, "dragged node x");
    AssertNumberNear(expectedDraggedWorld.Y, movedNode.Y!.Value, 0.001d, "dragged node y");
    AssertAtLeast(1, saved.Edges.Count, "created edge count");
}

static void ScenarioMultipleTrafficProfilesCanBeSwitched()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Profiles",
        TrafficTypes =
        [
            new TrafficTypeDefinition { Name = "grain" },
            new TrafficTypeDefinition { Name = "tools" }
        ],
        Nodes =
        [
            new NodeModel
            {
                Id = "market",
                Name = "Market",
                X = 0d,
                Y = 0d,
                TrafficProfiles =
                [
                    new NodeTrafficProfile { TrafficType = "grain", Production = 5d },
                    new NodeTrafficProfile { TrafficType = "tools", Consumption = 7d }
                ]
            }
        ],
        Edges = []
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        SelectFirstNode(workspace, new GraphSize(1000d, 700d));
        AssertNumberEqual(2d, workspace.SelectedNodeTrafficProfiles.Count, "profile count");
        workspace.SelectedNodeTrafficProfileItem = workspace.SelectedNodeTrafficProfiles[1];
        AssertTextEqual("tools", workspace.NodeTrafficTypeText, "second profile traffic");
        AssertTextEqual("7", workspace.NodeConsumptionText, "second profile consumption");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioNodeTrafficRoleCanBeEditedInInspector()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Role Edit",
        TrafficTypes = [new TrafficTypeDefinition { Name = "grain" }],
        Nodes =
        [
            new NodeModel
            {
                Id = "granary",
                Name = "Granary",
                X = 0d,
                Y = 0d,
                TrafficProfiles =
                [
                    new NodeTrafficProfile { TrafficType = "grain", Production = 4d, CanTransship = true }
                ]
            }
        ],
        Edges = []
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        SelectFirstNode(workspace, new GraphSize(1000d, 700d));

        workspace.NodeTrafficRoleText = NodeTrafficRoleCatalog.ConsumerRole;

        AssertTextEqual("0", workspace.NodeProductionText, "role edit clears production");
        AssertTextEqual("4", workspace.NodeConsumptionText, "role edit preserves quantity");
        AssertTrue(!workspace.NodeCanTransship, "role edit updates transshipment flag");

        workspace.ApplyInspectorCommand.Execute(null);

        var saved = SaveAndReload(workspace);
        var profile = saved.Nodes.Single().TrafficProfiles.Single();
        AssertNumberEqual(0d, profile.Production, "saved edited role production");
        AssertNumberEqual(4d, profile.Consumption, "saved edited role consumption");
        AssertTrue(!profile.CanTransship, "saved edited role transshipment");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioTrafficDefinitionRenameAndRemovalPropagate()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Traffic",
        TrafficTypes =
        [
            new TrafficTypeDefinition { Name = "grain" },
            new TrafficTypeDefinition { Name = "tools" }
        ],
        EdgeTrafficPermissionDefaults =
        [
            new EdgeTrafficPermissionRule { TrafficType = "grain", Mode = EdgeTrafficPermissionMode.Limited, LimitKind = EdgeTrafficLimitKind.AbsoluteUnits, LimitValue = 5d },
            new EdgeTrafficPermissionRule { TrafficType = "tools", Mode = EdgeTrafficPermissionMode.Permitted }
        ],
        Nodes =
        [
            new NodeModel
            {
                Id = "a",
                Name = "A",
                X = 0d,
                Y = 0d,
                TrafficProfiles =
                [
                    new NodeTrafficProfile { TrafficType = "grain", Production = 5d }
                ]
            },
            new NodeModel
            {
                Id = "b",
                Name = "B",
                X = 200d,
                Y = 0d,
                TrafficProfiles =
                [
                    new NodeTrafficProfile { TrafficType = "grain", Consumption = 5d }
                ]
            }
        ],
        Edges =
        [
            new EdgeModel
            {
                Id = "a->b",
                FromNodeId = "a",
                ToNodeId = "b",
                Time = 1d,
                Cost = 1d,
                TrafficPermissions =
                [
                    new EdgeTrafficPermissionRule { TrafficType = "grain", Mode = EdgeTrafficPermissionMode.Blocked }
                ]
            }
        ]
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        workspace.SelectedTrafficDefinitionItem = workspace.TrafficDefinitions.First(item => item.Name == "grain");
        workspace.TrafficNameText = "cereal";
        workspace.ApplyTrafficDefinitionCommand.Execute(null);

        var renamed = SaveAndReload(workspace);
        AssertTrue(renamed.TrafficTypes.Any(definition => definition.Name == "cereal"), "renamed traffic definition");
        AssertTrue(renamed.Nodes.SelectMany(node => node.TrafficProfiles).All(profile => profile.TrafficType != "grain"), "renamed node profiles");
        AssertTrue(renamed.EdgeTrafficPermissionDefaults.All(rule => rule.TrafficType != "grain"), "renamed default permissions");
        AssertTrue(renamed.Edges.SelectMany(edge => edge.TrafficPermissions).All(rule => rule.TrafficType != "grain"), "renamed edge permissions");

        workspace.SelectedTrafficDefinitionItem = workspace.TrafficDefinitions.First(item => item.Name == "cereal");
        workspace.RemoveSelectedTrafficDefinitionCommand.Execute(null);
        workspace.RemoveSelectedTrafficDefinitionCommand.Execute(null);

        var removed = SaveAndReload(workspace);
        AssertTrue(removed.TrafficTypes.All(definition => definition.Name != "cereal"), "removed traffic definition");
        AssertTrue(removed.Nodes.SelectMany(node => node.TrafficProfiles).All(profile => profile.TrafficType != "cereal"), "removed dependent profiles");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioNodeEditsPersistThroughSaveLoad()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Persist",
        TrafficTypes = [new TrafficTypeDefinition { Name = "grain" }],
        Nodes =
        [
            new NodeModel
            {
                Id = "granary",
                Name = "Granary",
                X = 0d,
                Y = 0d,
                TrafficProfiles =
                [
                    new NodeTrafficProfile { TrafficType = "grain", Production = 3d, CanTransship = true }
                ]
            }
        ],
        Edges = []
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        SelectFirstNode(workspace, new GraphSize(1000d, 700d));
        workspace.NodeNameText = "Central Granary";
        workspace.NodePlaceTypeText = "Storehouse";
        workspace.NodeDescriptionText = "Feeds the market";
        workspace.NodeTranshipmentCapacityText = "80";
        workspace.NodeTrafficRoleText = "Producer";
        workspace.NodeProductionText = "12";
        workspace.NodeConsumptionText = "0";
        workspace.NodeConsumerPremiumText = "2";
        workspace.NodeProductionStartText = "2";
        workspace.NodeProductionEndText = "6";
        workspace.NodeStoreEnabled = true;
        workspace.NodeStoreCapacityText = "30";
        workspace.ApplyInspectorCommand.Execute(null);

        var saved = SaveAndReload(workspace);
        var node = saved.Nodes.Single();
        var profile = node.TrafficProfiles.Single();
        AssertTextEqual("Central Granary", node.Name, "saved node name");
        AssertTextEqual("Storehouse", node.PlaceType!, "saved node place type");
        AssertTextEqual("Feeds the market", node.LoreDescription!, "saved node description");
        AssertNumberEqual(80d, node.TranshipmentCapacity!.Value, "saved node capacity");
        AssertNumberEqual(12d, profile.Production, "saved node production");
        AssertNumberEqual(2d, profile.ConsumerPremiumPerUnit, "saved node premium");
        AssertNumberEqual(2d, profile.ProductionStartPeriod ?? 0, "saved node production start");
        AssertNumberEqual(6d, profile.ProductionEndPeriod ?? 0, "saved node production end");
        AssertTrue(profile.IsStore, "saved node store enabled");
        AssertNumberEqual(30d, profile.StoreCapacity!.Value, "saved node store capacity");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioToolCommandsReflectRealModes()
{
    var workspace = new WorkspaceViewModel();
    workspace.AddNodeToolCommand.Execute(null);
    AssertTrue(workspace.IsAddNodeToolActive, "add node tool active");
    workspace.ConnectToolCommand.Execute(null);
    AssertTrue(workspace.IsConnectToolActive, "connect tool active");
    workspace.SelectToolCommand.Execute(null);
    AssertTrue(workspace.IsSelectToolActive, "select tool active");
}

static void ScenarioEscapeReturnsSelectTool()
{
    var workspace = new WorkspaceViewModel();
    workspace.ConnectToolCommand.Execute(null);
    AssertTrue(workspace.IsConnectToolActive, "escape precondition connect mode");
    var handled = workspace.InteractionController.OnKeyDown(
        workspace.CreateInteractionContext(new GraphSize(1000d, 700d)),
        "Escape",
        false);

    AssertTrue(handled, "escape key handled");
    AssertTrue(workspace.IsSelectToolActive, "escape returns select mode");
}

static void ScenarioNodeBoundsGrowWhenTextWraps()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Wrap",
        TrafficTypes = [new TrafficTypeDefinition { Name = "medical-supplies" }],
        Nodes =
        [
            new NodeModel
            {
                Id = "long-node",
                Name = "Very Long Humanitarian Distribution Hub With Overflowing Descriptors",
                PlaceType = "Regional logistics coordination centre with extensive storage duties",
                X = 0d,
                Y = 0d,
                TrafficProfiles =
                [
                    new NodeTrafficProfile { TrafficType = "medical-supplies", Production = 15d, CanTransship = true, IsStore = true, StoreCapacity = 45d }
                ]
            }
        ],
        Edges = []
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        var node = workspace.Scene.Nodes.Single();
        AssertTrue(node.Bounds.Height > GraphNodeTextLayout.MinHeight, "wrapped node grows beyond minimum height");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioEdgeTooltipIncludesRouteDetails()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Edges",
        TrafficTypes = [new TrafficTypeDefinition { Name = "aid" }],
        Nodes =
        [
            new NodeModel
            {
                Id = "a",
                Name = "Depot Alpha",
                X = 0d,
                Y = 0d,
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "aid", Production = 5d }]
            },
            new NodeModel
            {
                Id = "b",
                Name = "Clinic Bravo",
                X = 200d,
                Y = 0d,
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "aid", Consumption = 5d }]
            }
        ],
        Edges =
        [
            new EdgeModel
            {
                Id = "a->b",
                RouteType = "Northern Corridor",
                FromNodeId = "a",
                ToNodeId = "b",
                Time = 2d,
                Cost = 3d,
                Capacity = 8d,
                IsBidirectional = true
            }
        ]
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        var tooltip = workspace.Scene.Edges.Single().ToolTipText;
        AssertTrue(tooltip.Contains("Route Northern Corridor", StringComparison.Ordinal), "edge tooltip route label");
        AssertTrue(tooltip.Contains("Depot Alpha -> Clinic Bravo", StringComparison.Ordinal), "edge tooltip endpoints");
        AssertTrue(tooltip.Contains("Traffic aid", StringComparison.Ordinal), "edge tooltip traffic permissions");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioPressureExplanationAppearsInNodeDetails()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Pressure",
        TrafficTypes = [new TrafficTypeDefinition { Name = "water" }],
        Nodes =
        [
            new NodeModel
            {
                Id = "consumer",
                Name = "Remote Clinic",
                X = 0d,
                Y = 0d,
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "water", Consumption = 12d }]
            }
        ],
        Edges = []
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        workspace.StepCommand.Execute(null);
        var node = workspace.Scene.Nodes.Single();
        AssertTrue(node.DetailLines.Any(line => line.Text.StartsWith("Pressure ", StringComparison.Ordinal)), "node detail pressure line");
        AssertTrue(node.DetailLines.Any(line => line.Text.StartsWith("Cause: ", StringComparison.Ordinal)), "node detail cause line");
        AssertTrue(node.ToolTipText.Contains("Cause breakdown:", StringComparison.Ordinal), "node tooltip cause breakdown");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioReportsPopulateAndResetAroundTimeline()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Reports",
        TrafficTypes = [new TrafficTypeDefinition { Name = "fuel" }],
        Nodes =
        [
            new NodeModel
            {
                Id = "source",
                Name = "Fuel Depot",
                X = 0d,
                Y = 0d,
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "fuel", Production = 10d }]
            },
            new NodeModel
            {
                Id = "sink",
                Name = "Field Hospital",
                X = 220d,
                Y = 0d,
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "fuel", Consumption = 10d }]
            }
        ],
        Edges =
        [
            new EdgeModel
            {
                Id = "source->sink",
                RouteType = "Fuel Line",
                FromNodeId = "source",
                ToNodeId = "sink",
                Time = 1d,
                Cost = 1d,
                Capacity = 10d
            }
        ]
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        workspace.StepCommand.Execute(null);

        AssertAtLeast(1, workspace.TrafficReports.Count, "traffic reports after step");
        AssertAtLeast(1, workspace.RouteReports.Count, "route reports after step");
        AssertAtLeast(1, workspace.NodePressureReports.Count, "node pressure reports after step");

        workspace.ResetTimelineCommand.Execute(null);

        AssertNumberEqual(0d, workspace.TrafficReports.Count, "traffic reports after reset");
        AssertNumberEqual(0d, workspace.RouteReports.Count, "route reports after reset");
        AssertNumberEqual(0d, workspace.NodePressureReports.Count, "node pressure reports after reset");
    }
    finally
    {
        TryDelete(path);
    }
}

static string WriteTempNetwork(NetworkModel network)
{
    var path = Path.Combine(Path.GetTempPath(), $"medw-avalonia-{Guid.NewGuid():N}.json");
    new NetworkFileService().Save(network, path);
    return path;
}

static NetworkModel SaveAndReload(WorkspaceViewModel workspace)
{
    var path = Path.Combine(Path.GetTempPath(), $"medw-avalonia-save-{Guid.NewGuid():N}.json");
    try
    {
        workspace.SaveNetwork(path);
        return new NetworkFileService().Load(path);
    }
    finally
    {
        TryDelete(path);
    }
}

static void SelectFirstNode(WorkspaceViewModel workspace, GraphSize viewportSize)
{
    workspace.SelectToolCommand.Execute(null);
    var context = workspace.CreateInteractionContext(viewportSize);
    var node = workspace.Scene.Nodes.First();
    var nodeCenter = workspace.Viewport.WorldToScreen(new GraphPoint(node.Bounds.CenterX, node.Bounds.CenterY), viewportSize);
    workspace.InteractionController.OnPointerPressed(context, GraphPointerButton.Left, nodeCenter, false, false, false);
}

static void TryDelete(string path)
{
    if (File.Exists(path))
    {
        File.Delete(path);
    }
}

static void AssertTrue(bool condition, string scenario)
{
    if (!condition)
    {
        throw new InvalidOperationException($"{scenario} expected condition to be true.");
    }
}

static void AssertAtLeast(int expectedMinimum, int actual, string scenario)
{
    if (actual < expectedMinimum)
    {
        throw new InvalidOperationException($"{scenario} expected at least {expectedMinimum} but got {actual}.");
    }
}

static void AssertTextEqual(string expected, string actual, string scenario)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{scenario} expected '{expected}' but got '{actual}'.");
    }
}

static void AssertNumberEqual(double expected, double actual, string scenario)
{
    AssertNumberNear(expected, actual, 0.000001d, scenario);
}

static void AssertNumberNear(double expected, double actual, double tolerance, string scenario)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"{scenario} expected {expected:0.###} but got {actual:0.###}.");
    }
}

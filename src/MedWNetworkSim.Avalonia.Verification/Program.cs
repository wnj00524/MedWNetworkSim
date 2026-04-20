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
ScenarioRouteEditorWorkspaceModeTransitions();
ScenarioRouteEditorCanAddTrafficRule();
ScenarioRouteEditorValidationBlocksSave();
ScenarioRouteEditorDeleteReturnsToNormalWorkspace();
ScenarioTrafficDefinitionRenameAndRemovalPropagate();
ScenarioNodeEditsPersistThroughSaveLoad();
ScenarioToolCommandsReflectRealModes();
ScenarioEscapeReturnsSelectTool();
ScenarioNodeBoundsGrowWhenTextWraps();
ScenarioSelectedNodePreviewResizesAndKeepsCenter();
ScenarioEdgeAnchorsAndHitTestingFollowResizedBounds();
ScenarioNodeLayoutCacheReusesAndInvalidatesByContentAndTier();
ScenarioEdgeTooltipIncludesRouteDetails();
ScenarioPressureExplanationAppearsInNodeDetails();
ScenarioTimelineStepUsesEdgeOccupancyForVisualState();
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

static void ScenarioRouteEditorWorkspaceModeTransitions()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Route Editor",
        TrafficTypes = [new TrafficTypeDefinition { Name = "aid" }],
        Nodes =
        [
            new NodeModel { Id = "source", Name = "Source", X = 0d, Y = 0d },
            new NodeModel { Id = "target", Name = "Target", X = 220d, Y = 0d }
        ],
        Edges =
        [
            new EdgeModel
            {
                Id = "source->target",
                RouteType = "Relief Corridor",
                FromNodeId = "source",
                ToNodeId = "target",
                Time = 2d,
                Cost = 3d,
                Capacity = 9d,
                IsBidirectional = true,
                TrafficPermissions = [new EdgeTrafficPermissionRule { TrafficType = "aid", Mode = EdgeTrafficPermissionMode.Permitted }]
            }
        ]
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        workspace.SelectRouteForEdit("source->target");
        workspace.EnterEdgeEditor();

        AssertTrue(workspace.IsEdgeEditorWorkspaceMode, "route editor enters dedicated workspace mode");
        workspace.EdgeRouteTypeText = "Updated Corridor";
        workspace.EdgeTimeText = "5";
        workspace.CancelEdgeEditorCommand.Execute(null);

        AssertTrue(workspace.IsNormalWorkspaceMode, "route editor cancel returns to normal workspace");
        AssertTextEqual("Relief Corridor", workspace.EdgeRouteTypeText, "route editor cancel restores route label");
        AssertTextEqual("2", workspace.EdgeTimeText, "route editor cancel restores travel time");

        workspace.EnterEdgeEditor();
        workspace.EdgeRouteTypeText = "Updated Corridor";
        workspace.EdgeTimeText = "5";
        workspace.EdgeCostText = "7";
        workspace.EdgeCapacityText = "12";
        workspace.SaveEdgeEditorCommand.Execute(null);

        AssertTrue(workspace.IsNormalWorkspaceMode, "route editor save returns to normal workspace");
        var saved = SaveAndReload(workspace);
        var edge = saved.Edges.Single();
        AssertTextEqual("Updated Corridor", edge.RouteType!, "route editor save updates route label");
        AssertNumberEqual(5d, edge.Time, "route editor save updates time");
        AssertNumberEqual(7d, edge.Cost, "route editor save updates cost");
        AssertNumberEqual(12d, edge.Capacity!.Value, "route editor save updates capacity");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioRouteEditorValidationBlocksSave()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Route Validation",
        TrafficTypes = [new TrafficTypeDefinition { Name = "aid" }],
        Nodes =
        [
            new NodeModel { Id = "a", Name = "A", X = 0d, Y = 0d },
            new NodeModel { Id = "b", Name = "B", X = 200d, Y = 0d }
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
                IsBidirectional = false
            }
        ]
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        workspace.OpenRouteEditor("a->b");
        workspace.AddEdgePermissionRuleCommand.Execute(null);

        var row = workspace.SelectedEdgePermissionRows.Single(permission => permission.TrafficType == "aid");
        row.IsActive = true;
        row.Mode = EdgeTrafficPermissionMode.Limited;
        row.LimitKind = EdgeTrafficLimitKind.PercentOfEdgeCapacity;
        row.LimitValueText = "35";

        AssertTextEqual("Set edge capacity before using a percentage limit.", row.ValidationMessage, "route editor percent limit requires capacity");
        AssertTrue(!workspace.CanSaveEdgeEditor, "route editor blocks save while permission rule is invalid");

        workspace.EdgeCapacityText = "20";
        AssertTextEqual(string.Empty, row.ValidationMessage, "route editor clears permission validation after capacity is set");
        AssertTrue(workspace.CanSaveEdgeEditor, "route editor enables save once validation passes");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioRouteEditorCanAddTrafficRule()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Route Add Rule",
        TrafficTypes =
        [
            new TrafficTypeDefinition { Name = "aid" },
            new TrafficTypeDefinition { Name = "water" }
        ],
        Nodes =
        [
            new NodeModel { Id = "a", Name = "A", X = 0d, Y = 0d },
            new NodeModel { Id = "b", Name = "B", X = 200d, Y = 0d }
        ],
        Edges =
        [
            new EdgeModel
            {
                Id = "a->b",
                FromNodeId = "a",
                ToNodeId = "b",
                Time = 1d,
                Cost = 1d
            }
        ]
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        workspace.OpenRouteEditor("a->b");

        AssertNumberEqual(0d, workspace.VisibleEdgePermissionRows.Count, "route editor starts with no active route rules");
        workspace.AddEdgePermissionRuleCommand.Execute(null);

        AssertNumberEqual(1d, workspace.VisibleEdgePermissionRows.Count, "route editor activates one route rule");
        AssertTextEqual("aid", workspace.VisibleEdgePermissionRows.Single().TrafficType, "route editor adds first available traffic type");

        workspace.VisibleEdgePermissionRows.Single().Mode = EdgeTrafficPermissionMode.Blocked;
        workspace.SaveEdgeEditorCommand.Execute(null);

        var saved = SaveAndReload(workspace);
        var edge = saved.Edges.Single();
        var addedRule = edge.TrafficPermissions.Single(rule => rule.TrafficType == "aid");
        AssertTrue(addedRule.IsActive, "route editor saves activated route rule");
        AssertTrue(addedRule.Mode == EdgeTrafficPermissionMode.Blocked, "route editor saves activated route rule mode");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioRouteEditorDeleteReturnsToNormalWorkspace()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Route Delete",
        TrafficTypes = [new TrafficTypeDefinition { Name = "aid" }],
        Nodes =
        [
            new NodeModel { Id = "a", Name = "A", X = 0d, Y = 0d },
            new NodeModel { Id = "b", Name = "B", X = 200d, Y = 0d }
        ],
        Edges =
        [
            new EdgeModel
            {
                Id = "a->b",
                FromNodeId = "a",
                ToNodeId = "b",
                Time = 1d,
                Cost = 1d
            }
        ]
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        workspace.OpenRouteEditor("a->b");
        workspace.DeleteSelectedEdgeFromEditor();

        AssertTrue(workspace.IsNormalWorkspaceMode, "route delete returns to normal workspace");
        AssertNumberEqual(0d, workspace.Scene.Selection.SelectedEdgeIds.Count, "route delete clears deleted edge selection");

        var saved = SaveAndReload(workspace);
        AssertNumberEqual(0d, saved.Edges.Count, "route delete removes edge from saved network");
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

static void ScenarioSelectedNodePreviewResizesAndKeepsCenter()
{
    var workspace = new WorkspaceViewModel();
    workspace.AddNodeToolCommand.Execute(null);
    var context = workspace.CreateInteractionContext(new GraphSize(1100d, 700d));
    workspace.InteractionController.OnPointerPressed(context, GraphPointerButton.Left, new GraphPoint(420d, 280d), false, false, false);
    SelectFirstNode(workspace, context.ViewportSize);
    var node = workspace.Scene.Nodes.Single();
    var centerBefore = new GraphPoint(node.Bounds.CenterX, node.Bounds.CenterY);
    var widthBefore = node.Bounds.Width;
    var heightBefore = node.Bounds.Height;

    workspace.NodeNameText = "Emergency Logistics Node With A Much Longer Live Preview Name";

    var updated = workspace.Scene.Nodes.Single();
    AssertTrue(updated.Bounds.Height >= heightBefore, "live preview height does not shrink with longer name");
    AssertTrue(updated.Bounds.Width >= widthBefore, "live preview width does not shrink with longer name");
    AssertNumberNear(centerBefore.X, updated.Bounds.CenterX, 0.001d, "live preview center x preserved");
    AssertNumberNear(centerBefore.Y, updated.Bounds.CenterY, 0.001d, "live preview center y preserved");
}

static void ScenarioEdgeAnchorsAndHitTestingFollowResizedBounds()
{
    var workspace = new WorkspaceViewModel();
    workspace.AddNodeToolCommand.Execute(null);
    var viewportSize = new GraphSize(1200d, 760d);
    var addContext = workspace.CreateInteractionContext(viewportSize);
    workspace.InteractionController.OnPointerPressed(addContext, GraphPointerButton.Left, new GraphPoint(340d, 320d), false, false, false);
    workspace.InteractionController.OnPointerPressed(addContext, GraphPointerButton.Left, new GraphPoint(860d, 320d), false, false, false);

    workspace.ConnectToolCommand.Execute(null);
    var connectContext = workspace.CreateInteractionContext(viewportSize);
    var sourceNode = workspace.Scene.Nodes[0];
    var targetNode = workspace.Scene.Nodes[1];
    var sourceCenter = workspace.Viewport.WorldToScreen(new GraphPoint(sourceNode.Bounds.CenterX, sourceNode.Bounds.CenterY), viewportSize);
    var targetCenter = workspace.Viewport.WorldToScreen(new GraphPoint(targetNode.Bounds.CenterX, targetNode.Bounds.CenterY), viewportSize);
    workspace.InteractionController.OnPointerPressed(connectContext, GraphPointerButton.Left, sourceCenter, false, false, false);
    workspace.InteractionController.OnPointerReleased(connectContext, GraphPointerButton.Left, targetCenter, false);

    SelectFirstNode(workspace, viewportSize);
    workspace.NodeNameText = "Source Hub With Long Preview Name That Forces A Wider Box";

    var resizedSource = workspace.Scene.Nodes.First(node => node.Id == sourceNode.Id);
    var anchor = GraphHitTester.GetEdgeAnchor(workspace.Scene, resizedSource.Id, targetNode.Id);
    AssertNumberNear(resizedSource.Bounds.Right, anchor.X, 0.001d, "edge anchor tracks resized source width");
    AssertNumberNear(resizedSource.Bounds.CenterY, anchor.Y, 0.001d, "edge anchor tracks resized source center");

    var hit = new GraphHitTester().HitTest(workspace.Scene, new GraphPoint(resizedSource.Bounds.CenterX, resizedSource.Bounds.CenterY));
    AssertTextEqual(resizedSource.Id, hit.NodeId ?? string.Empty, "hit testing at resized center finds node");
}

static void ScenarioNodeLayoutCacheReusesAndInvalidatesByContentAndTier()
{
    var renderer = new GraphRenderer();
    var node = new GraphNodeSceneItem
    {
        Id = "cache-node",
        Name = "Cache Node",
        TypeLabel = "Depot",
        MetricsLabel = string.Empty,
        DetailLines = [new GraphNodeTextLine("Produces 4 aid", true, false)],
        Bounds = new GraphRect(-84d, -59d, 168d, 118d),
        FillColor = default,
        StrokeColor = default,
        Badges = ["Makes aid"],
        HasWarning = false
    };

    var medium1 = GraphRenderer.GetOrBuildNodeLayout(node, renderer.GetZoomTier(0.8d));
    var medium2 = GraphRenderer.GetOrBuildNodeLayout(node, renderer.GetZoomTier(0.85d));
    AssertTrue(ReferenceEquals(medium1, medium2), "layout cache reused within same zoom tier");

    node.Name = "Cache Node Updated";
    var mediumAfterContentChange = GraphRenderer.GetOrBuildNodeLayout(node, renderer.GetZoomTier(0.8d));
    AssertTrue(!ReferenceEquals(medium2, mediumAfterContentChange), "layout cache rebuilt after content change");

    var near = GraphRenderer.GetOrBuildNodeLayout(node, renderer.GetZoomTier(1.5d));
    AssertTrue(!ReferenceEquals(mediumAfterContentChange, near), "layout cache rebuilt after zoom tier change");

    var otherNode = new GraphNodeSceneItem
    {
        Id = "other-cache-node",
        Name = "Other Node",
        TypeLabel = "Clinic",
        MetricsLabel = string.Empty,
        DetailLines = [new GraphNodeTextLine("Consumes 3 aid", true, false)],
        Bounds = new GraphRect(160d, -59d, 168d, 118d),
        FillColor = default,
        StrokeColor = default,
        Badges = ["Needs aid"],
        HasWarning = false
    };

    var otherLayout1 = GraphRenderer.GetOrBuildNodeLayout(otherNode, renderer.GetZoomTier(0.8d));
    _ = GraphRenderer.GetOrBuildNodeLayout(node, renderer.GetZoomTier(0.8d));
    var otherLayout2 = GraphRenderer.GetOrBuildNodeLayout(otherNode, renderer.GetZoomTier(0.8d));
    AssertTrue(ReferenceEquals(otherLayout1, otherLayout2), "other nodes keep cached layout when unchanged");
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

static void ScenarioTimelineStepUsesEdgeOccupancyForVisualState()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Timeline Occupancy",
        TrafficTypes = [new TrafficTypeDefinition { Name = "aid" }],
        Nodes =
        [
            new NodeModel
            {
                Id = "source",
                Name = "Source Depot",
                X = 0d,
                Y = 0d,
                TrafficProfiles =
                [
                    new NodeTrafficProfile
                    {
                        TrafficType = "aid",
                        Production = 5d,
                        ProductionStartPeriod = 1,
                        ProductionEndPeriod = 1
                    }
                ]
            },
            new NodeModel
            {
                Id = "sink",
                Name = "Remote Clinic",
                X = 260d,
                Y = 0d,
                TrafficProfiles =
                [
                    new NodeTrafficProfile
                    {
                        TrafficType = "aid",
                        Consumption = 5d,
                        ConsumptionStartPeriod = 1,
                        ConsumptionEndPeriod = 1
                    }
                ]
            }
        ],
        Edges =
        [
            new EdgeModel
            {
                Id = "source->sink",
                RouteType = "Mountain Pass",
                FromNodeId = "source",
                ToNodeId = "sink",
                Time = 3d,
                Cost = 1d,
                Capacity = 5d
            }
        ]
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);

        workspace.StepCommand.Execute(null);
        var period1Edge = workspace.Scene.Edges.Single();
        AssertTextEqual("Started this period", workspace.TrafficDeliveredColumnLabel, "timeline traffic column label");
        AssertTrue(period1Edge.LoadRatio > 0d, "timeline period 1 edge load active");
        AssertTrue(period1Edge.FlowRate > 0d, "timeline period 1 edge flow active");
        AssertTextEqual("5", workspace.TrafficReports.Single().DeliveredQuantity, "timeline period 1 starts are reported");

        workspace.StepCommand.Execute(null);
        var period2Edge = workspace.Scene.Edges.Single();
        AssertTrue(period2Edge.LoadRatio > 0d, "timeline period 2 edge stays occupied after allocations stop");
        AssertTrue(period2Edge.FlowRate > 0d, "timeline period 2 edge visuals use occupancy");
        AssertTextEqual("0", workspace.TrafficReports.Single().DeliveredQuantity, "timeline period 2 reports no new starts");

        workspace.StepCommand.Execute(null);
        var period3Edge = workspace.Scene.Edges.Single();
        AssertTrue(period3Edge.LoadRatio > 0d, "timeline period 3 edge remains occupied before arrival");
        AssertTrue(period3Edge.FlowRate > 0d, "timeline period 3 edge visuals still reflect in-flight movement");
        AssertTextEqual("0", workspace.TrafficReports.Single().DeliveredQuantity, "timeline period 3 reports no new starts");

        workspace.StepCommand.Execute(null);
        var period4Edge = workspace.Scene.Edges.Single();
        AssertNumberEqual(0d, period4Edge.LoadRatio, "timeline period 4 edge clears after arrival");
        AssertNumberEqual(0d, period4Edge.FlowRate, "timeline period 4 edge flow clears after arrival");
        AssertTextEqual("0", workspace.TrafficReports.Single().DeliveredQuantity, "timeline period 4 reports no new starts");

        workspace.ResetTimelineCommand.Execute(null);
        AssertTextEqual("Delivered", workspace.TrafficDeliveredColumnLabel, "static traffic column label after reset");
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.App.Services.Pathfinding;
using MedWNetworkSim.Interaction;
using MedWNetworkSim.Presentation;
using MedWNetworkSim.Rendering;
using MedWNetworkSim.UI;
using System.Globalization;
using System.Reflection;

ScenarioCoordinateTransformPreservesLogicalInput();
ScenarioCoordinateTransformClampsPointerOutsideCanvas();
ScenarioAddNodePlacementMatchesClickedWorldPosition();
ScenarioWorkspaceDoubleClickCreatesNodeAndRequestsFullEditor();
ScenarioDragAndConnectUseRenderedCoordinates();
ScenarioModifierDragCreatesExpectedRouteDirection();
ScenarioMultipleTrafficProfilesCanBeSwitched();
ScenarioNodeTrafficRoleCanBeEditedInInspector();
ScenarioSingleNodeInspectorApplyUsesLoadedNodeTarget();
ScenarioSingleRouteInspectorApplyUsesLoadedEdgeTarget();
ScenarioBulkSelectionApplyUsesLoadedSelectionTarget();
ScenarioSwitchingFromSelectionModeToNodeModeDoesNotRetainBulkDraftBehavior();
ScenarioNodeAndBulkDraftsStayIndependent();
ScenarioEdgeDraftDoesNotMirrorNodeDraft();
ScenarioAutoCompleteTextBoxRespectsParentDataContextBindings();
ScenarioPlaceTypeSuggestionsPopulateFromCurrentNetwork();
ScenarioRouteTypeSuggestionsPopulateFromCurrentNetwork();
ScenarioAutocompleteSuggestionsRefreshAfterInspectorEdits();
ScenarioAutocompleteSuggestionsImportGraphMlRefreshesCollections();
ScenarioRouteEditorWorkspaceModeTransitions();
ScenarioEditorWorkspaceCreatesAndValidatesEvents();
ScenarioRouteEditorCanAddTrafficRule();
ScenarioRouteEditorValidationBlocksSave();
ScenarioRouteEditorDeleteReturnsToNormalWorkspace();
ScenarioTrafficTypesRailButtonOpensAndClosesTrafficWorkspace();
ScenarioTrafficDefinitionRenameAndRemovalPropagate();
ScenarioNodeEditsPersistThroughSaveLoad();
ScenarioToolCommandsReflectRealModes();
ScenarioEscapeReturnsSelectTool();
ScenarioNodeBoundsGrowWhenTextWraps();
ScenarioSelectedNodePreviewResizesAndKeepsCenter();
ScenarioEdgeAnchorsAndHitTestingFollowResizedBounds();
ScenarioOneWayEdgeArrowTracksEdgeGeometry();
ScenarioNodeLayoutCacheReusesAndInvalidatesByContentAndTier();
ScenarioEdgeTooltipIncludesRouteDetails();
ScenarioPressureExplanationAppearsInNodeDetails();
ScenarioTimelineStepUsesEdgeOccupancyForVisualState();
ScenarioReportsPopulateAndResetAroundTimeline();
ScenarioFacilityIso_EmptyOriginsReturnsNoReachableNodes();
ScenarioFacilityIso_SingleOriginMatchesLegacyIsochrone();
ScenarioFacilityIso_MultipleOriginsCombineCoverage();
ScenarioFacilityIso_BestOriginChoosesLowestCost();
ScenarioFacilityIso_OverlapIncludesMultiCoveredNodes();
ScenarioFacilityIso_UncoveredExcludesReachable();
ScenarioFacilityIso_BudgetLimitExcludesOverBudgetNodes();
ScenarioFacilityIso_DirectedEdgesRespected();
ScenarioFacilityPlanning_SingleFacilityCoversReachableNodes();
ScenarioFacilityPlanning_MultipleFacilitiesShareCoverage();
ScenarioFacilityPlanning_UniqueCoverageExcludesSharedNodes();
ScenarioFacilityPlanning_RemovingFacilityPreservesOtherCoverage();
ScenarioFacilityPlanning_ChangingMaxTravelTimeRecomputesCoverage();
ScenarioFacilityPlanning_ComputeIsochroneStillWorks();

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

static void ScenarioWorkspaceDoubleClickCreatesNodeAndRequestsFullEditor()
{
    var workspace = new WorkspaceViewModel();
    var canvas = new GraphCanvasControl
    {
        ViewModel = workspace
    };
    var viewportSize = new GraphSize(960d, 640d);
    var context = workspace.CreateInteractionContext(viewportSize);
    var screenPoint = new GraphPoint(360d, 240d);
    var expectedWorld = context.Viewport.ScreenToWorld(screenPoint, viewportSize);
    var requestedNodeId = string.Empty;
    canvas.FullNodeEditorRequested += (_, args) => requestedNodeId = args.NodeId;

    var handled = canvas.TryHandleWorkspaceDoubleClick(context, screenPoint);

    AssertTrue(handled, "workspace double click handled");
    AssertTrue(workspace.IsEditingNode, "workspace double click selects created node");
    AssertTrue(!string.IsNullOrWhiteSpace(requestedNodeId), "workspace double click requests full node editor");

    var saved = SaveAndReload(workspace);
    var created = saved.Nodes.Single(node => node.Id == requestedNodeId);
    AssertNumberNear(expectedWorld.X, created.X!.Value, 0.001d, "workspace double click saved x");
    AssertNumberNear(expectedWorld.Y, created.Y!.Value, 0.001d, "workspace double click saved y");
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

static void ScenarioModifierDragCreatesExpectedRouteDirection()
{
    var workspace = new WorkspaceViewModel();
    workspace.AddNodeToolCommand.Execute(null);
    var viewportSize = new GraphSize(1000d, 700d);
    var addContext = workspace.CreateInteractionContext(viewportSize);
    workspace.InteractionController.OnPointerPressed(addContext, GraphPointerButton.Left, new GraphPoint(300d, 260d), false, false, false);
    workspace.InteractionController.OnPointerPressed(addContext, GraphPointerButton.Left, new GraphPoint(700d, 260d), false, false, false);

    workspace.SelectToolCommand.Execute(null);
    var selectContext = workspace.CreateInteractionContext(viewportSize);
    var sourceCenter = workspace.Viewport.WorldToScreen(new GraphPoint(workspace.Scene.Nodes[0].Bounds.CenterX, workspace.Scene.Nodes[0].Bounds.CenterY), selectContext.ViewportSize);
    var targetCenter = workspace.Viewport.WorldToScreen(new GraphPoint(workspace.Scene.Nodes[1].Bounds.CenterX, workspace.Scene.Nodes[1].Bounds.CenterY), selectContext.ViewportSize);

    workspace.InteractionController.OnPointerPressed(selectContext, GraphPointerButton.Left, sourceCenter, false, false, true);
    workspace.InteractionController.OnPointerMoved(selectContext, targetCenter);
    workspace.InteractionController.OnPointerReleased(selectContext, GraphPointerButton.Left, targetCenter, false);

    var afterBidirectional = SaveAndReload(workspace);
    var createdBidirectional = afterBidirectional.Edges.Single();
    AssertTrue(createdBidirectional.IsBidirectional, "ctrl drag creates a bidirectional route");

    workspace.DeleteRouteById(createdBidirectional.Id);
    selectContext = workspace.CreateInteractionContext(viewportSize);
    sourceCenter = workspace.Viewport.WorldToScreen(new GraphPoint(workspace.Scene.Nodes[0].Bounds.CenterX, workspace.Scene.Nodes[0].Bounds.CenterY), selectContext.ViewportSize);
    targetCenter = workspace.Viewport.WorldToScreen(new GraphPoint(workspace.Scene.Nodes[1].Bounds.CenterX, workspace.Scene.Nodes[1].Bounds.CenterY), selectContext.ViewportSize);

    workspace.InteractionController.OnPointerPressed(selectContext, GraphPointerButton.Left, sourceCenter, true, false, false);
    workspace.InteractionController.OnPointerMoved(selectContext, targetCenter);
    workspace.InteractionController.OnPointerReleased(selectContext, GraphPointerButton.Left, targetCenter, true);

    var afterOneWay = SaveAndReload(workspace);
    var createdOneWay = afterOneWay.Edges.Single();
    AssertTrue(!createdOneWay.IsBidirectional, "shift drag creates a one-way route");
    AssertTextEqual(afterOneWay.Nodes[0].Id, createdOneWay.FromNodeId, "shift drag preserves source direction");
    AssertTextEqual(afterOneWay.Nodes[1].Id, createdOneWay.ToNodeId, "shift drag preserves target direction");
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

static void ScenarioSingleNodeInspectorApplyUsesLoadedNodeTarget()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Single Node Apply",
        TrafficTypes = [new TrafficTypeDefinition { Name = "grain" }],
        Nodes =
        [
            new NodeModel { Id = "alpha", Name = "Alpha", PlaceType = "Village", X = 0d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
            new NodeModel { Id = "beta", Name = "Beta", PlaceType = "Town", X = 10d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] }
        ],
        Edges = []
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        workspace.SelectNodeForEdit("alpha");
        workspace.NodeDraft.PlaceTypeText = "Harbor";

        workspace.Scene.Selection.SelectedNodeIds.Clear();
        workspace.Scene.Selection.SelectedNodeIds.Add("beta");

        workspace.ApplyInspectorCommand.Execute(null);

        var saved = SaveAndReload(workspace);
        AssertTextEqual("Harbor", saved.Nodes.Single(node => node.Id == "alpha").PlaceType!, "single-node apply updates loaded target");
        AssertTextEqual("Town", saved.Nodes.Single(node => node.Id == "beta").PlaceType!, "single-node apply leaves other node unchanged");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioBulkSelectionApplyUsesLoadedSelectionTarget()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Bulk Apply",
        TrafficTypes = [new TrafficTypeDefinition { Name = "grain" }],
        Nodes =
        [
            new NodeModel { Id = "alpha", Name = "Alpha", PlaceType = "Village", X = 0d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
            new NodeModel { Id = "beta", Name = "Beta", PlaceType = "Village", X = 10d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
            new NodeModel { Id = "gamma", Name = "Gamma", PlaceType = "Fort", X = 20d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] }
        ],
        Edges = []
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        SetSelectedNodes(workspace, "alpha", "beta");
        workspace.BulkDraft.PlaceTypeText = "Market";

        workspace.Scene.Selection.SelectedNodeIds.Clear();
        workspace.Scene.Selection.SelectedNodeIds.Add("beta");
        workspace.Scene.Selection.SelectedNodeIds.Add("gamma");

        workspace.ApplyInspectorCommand.Execute(null);

        var saved = SaveAndReload(workspace);
        AssertTextEqual("Market", saved.Nodes.Single(node => node.Id == "alpha").PlaceType!, "bulk apply updates first loaded selection node");
        AssertTextEqual("Market", saved.Nodes.Single(node => node.Id == "beta").PlaceType!, "bulk apply updates second loaded selection node");
        AssertTextEqual("Fort", saved.Nodes.Single(node => node.Id == "gamma").PlaceType!, "bulk apply leaves nodes outside loaded selection unchanged");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioSingleRouteInspectorApplyUsesLoadedEdgeTarget()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Single Route Apply",
        TrafficTypes = [new TrafficTypeDefinition { Name = "grain" }],
        Nodes =
        [
            new NodeModel { Id = "alpha", Name = "Alpha", PlaceType = "Village", X = 0d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
            new NodeModel { Id = "beta", Name = "Beta", PlaceType = "Town", X = 10d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] }
        ],
        Edges =
        [
            new EdgeModel { Id = "alpha->beta", FromNodeId = "alpha", ToNodeId = "beta", Time = 1d, Cost = 1d, IsBidirectional = true, RouteType = "Road" }
        ]
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        workspace.SelectRouteForEdit("alpha->beta");
        workspace.EdgeDraft.RouteTypeText = "Canal";

        workspace.Scene.Selection.SelectedEdgeIds.Clear();
        workspace.Scene.Selection.SelectedNodeIds.Clear();
        workspace.Scene.Selection.SelectedNodeIds.Add("alpha");
        workspace.Scene.Selection.SelectedNodeIds.Add("beta");

        workspace.ApplyInspectorCommand.Execute(null);

        var saved = SaveAndReload(workspace);
        AssertTextEqual("Canal", saved.Edges.Single().RouteType!, "single-route apply updates loaded route target");
        AssertTextEqual("Village", saved.Nodes.Single(node => node.Id == "alpha").PlaceType!, "single-route apply leaves first node unchanged");
        AssertTextEqual("Town", saved.Nodes.Single(node => node.Id == "beta").PlaceType!, "single-route apply leaves second node unchanged");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioSwitchingFromSelectionModeToNodeModeDoesNotRetainBulkDraftBehavior()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Switch Modes",
        TrafficTypes = [new TrafficTypeDefinition { Name = "grain" }],
        Nodes =
        [
            new NodeModel { Id = "alpha", Name = "Alpha", PlaceType = "Village", X = 0d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
            new NodeModel { Id = "beta", Name = "Beta", PlaceType = "Village", X = 10d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
            new NodeModel { Id = "gamma", Name = "Gamma", PlaceType = "Fort", X = 20d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] }
        ],
        Edges = []
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        SetSelectedNodes(workspace, "alpha", "beta");
        workspace.BulkDraft.PlaceTypeText = "Market";

        workspace.SelectNodeForEdit("gamma");
        workspace.NodeDraft.PlaceTypeText = "Port";
        workspace.ApplyInspectorCommand.Execute(null);

        var saved = SaveAndReload(workspace);
        AssertTextEqual("Village", saved.Nodes.Single(node => node.Id == "alpha").PlaceType!, "switching to node mode does not reuse bulk draft for alpha");
        AssertTextEqual("Village", saved.Nodes.Single(node => node.Id == "beta").PlaceType!, "switching to node mode does not reuse bulk draft for beta");
        AssertTextEqual("Port", saved.Nodes.Single(node => node.Id == "gamma").PlaceType!, "switching to node mode applies node draft only to selected node");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioNodeAndBulkDraftsStayIndependent()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Draft Independence",
        TrafficTypes = [new TrafficTypeDefinition { Name = "grain" }],
        Nodes =
        [
            new NodeModel { Id = "alpha", Name = "Alpha", PlaceType = "Village", X = 0d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
            new NodeModel { Id = "beta", Name = "Beta", PlaceType = "Town", X = 10d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] }
        ],
        Edges = []
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);

        workspace.SelectNodeForEdit("alpha");
        workspace.NodeDraft.PlaceTypeText = "Harbor";

        SetSelectedNodes(workspace, "alpha", "beta");
        workspace.BulkDraft.PlaceTypeText = "Market";

        AssertTextEqual("Harbor", workspace.NodeDraft.PlaceTypeText, "node draft keeps its own place type text");
        AssertTextEqual("Market", workspace.BulkDraft.PlaceTypeText, "bulk draft keeps its own place type text");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioEdgeDraftDoesNotMirrorNodeDraft()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Cross Draft Isolation",
        TrafficTypes = [new TrafficTypeDefinition { Name = "grain" }],
        Nodes =
        [
            new NodeModel { Id = "alpha", Name = "Alpha", PlaceType = "Village", X = 0d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
            new NodeModel { Id = "beta", Name = "Beta", PlaceType = "Town", X = 10d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] }
        ],
        Edges =
        [
            new EdgeModel { Id = "alpha->beta", FromNodeId = "alpha", ToNodeId = "beta", Time = 1d, Cost = 1d, IsBidirectional = true, RouteType = "Road" }
        ]
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);

        workspace.SelectNodeForEdit("alpha");
        workspace.NodeDraft.PlaceTypeText = "Harbor";
        workspace.SelectRouteForEdit("alpha->beta");
        workspace.EdgeDraft.RouteTypeText = "Canal";

        AssertTextEqual("Harbor", workspace.NodeDraft.PlaceTypeText, "edge draft updates do not overwrite node draft text");
        AssertTextEqual("Canal", workspace.EdgeDraft.RouteTypeText, "edge draft keeps its own route type text");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioAutoCompleteTextBoxRespectsParentDataContextBindings()
{
    var host = new AutoCompleteBindingHost
    {
        PlaceType = "Village",
        PlaceTypeSuggestions = ["Village", "Harbor"]
    };

    var control = new MedWNetworkSim.UI.Controls.AutoCompleteTextBox
    {
        DataContext = host
    };

    control.Bind(MedWNetworkSim.UI.Controls.AutoCompleteTextBox.TextProperty, new Avalonia.Data.Binding(nameof(AutoCompleteBindingHost.PlaceType), Avalonia.Data.BindingMode.TwoWay));
    control.Bind(MedWNetworkSim.UI.Controls.AutoCompleteTextBox.SuggestionsProperty, new Avalonia.Data.Binding(nameof(AutoCompleteBindingHost.PlaceTypeSuggestions)));

    AssertTextEqual("Village", control.Text!, "autocomplete text binding reads from parent data context");
    AssertNumberEqual(2d, control.Suggestions!.Count(), "autocomplete suggestions binding reads from parent data context");

    control.Text = "Harbor";

    AssertTextEqual("Harbor", host.PlaceType, "autocomplete text binding writes back to parent data context");
}

static void ScenarioPlaceTypeSuggestionsPopulateFromCurrentNetwork()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Place Suggestions",
        TrafficTypes = [new TrafficTypeDefinition { Name = "grain" }],
        Nodes =
        [
            new NodeModel { Id = "alpha", Name = "Alpha", PlaceType = "Village", X = 0d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
            new NodeModel { Id = "beta", Name = "Beta", PlaceType = "Harbor", X = 10d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
            new NodeModel { Id = "gamma", Name = "Gamma", PlaceType = "village", X = 20d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
            new NodeModel { Id = "delta", Name = "Delta", PlaceType = "", X = 30d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] }
        ],
        Edges = []
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);

        AssertTrue(workspace.NodeDraft.PlaceTypeSuggestions.Contains("Village"), "place type suggestions include existing value");
        AssertTrue(workspace.NodeDraft.PlaceTypeSuggestions.Contains("Harbor"), "place type suggestions include second existing value");
        AssertTrue(workspace.NodeDraft.PlaceTypeSuggestions.Contains("Draft place"), "place type suggestions include default place value");
        AssertNumberEqual(3d, workspace.NodeDraft.PlaceTypeSuggestions.Count, "place type suggestions remove duplicate and blank values");
        AssertTrue(workspace.BulkDraft.PlaceTypeSuggestions.Contains("Harbor"), "bulk draft place suggestions refresh with network values");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioRouteTypeSuggestionsPopulateFromCurrentNetwork()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Route Suggestions",
        TrafficTypes = [new TrafficTypeDefinition { Name = "grain" }],
        Nodes =
        [
            new NodeModel { Id = "alpha", Name = "Alpha", X = 0d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
            new NodeModel { Id = "beta", Name = "Beta", X = 10d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] }
        ],
        Edges =
        [
            new EdgeModel { Id = "alpha->beta", FromNodeId = "alpha", ToNodeId = "beta", Time = 1d, Cost = 1d, IsBidirectional = true, RouteType = "Canal" },
            new EdgeModel { Id = "beta->alpha", FromNodeId = "beta", ToNodeId = "alpha", Time = 1d, Cost = 1d, IsBidirectional = true, RouteType = "canal" },
            new EdgeModel { Id = "alpha->alpha", FromNodeId = "alpha", ToNodeId = "alpha", Time = 1d, Cost = 1d, IsBidirectional = true, RouteType = "" }
        ]
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);

        AssertTrue(workspace.EdgeDraft.RouteTypeSuggestions.Contains("Canal"), "route type suggestions include existing value");
        AssertTrue(workspace.EdgeDraft.RouteTypeSuggestions.Contains("Proposed route"), "route type suggestions include default route value");
        AssertNumberEqual(2d, workspace.EdgeDraft.RouteTypeSuggestions.Count, "route type suggestions remove duplicate and blank values");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioAutocompleteSuggestionsRefreshAfterInspectorEdits()
{
    var path = WriteTempNetwork(new NetworkModel
    {
        Name = "Suggestion Refresh",
        TrafficTypes = [new TrafficTypeDefinition { Name = "grain" }],
        Nodes =
        [
            new NodeModel { Id = "alpha", Name = "Alpha", PlaceType = "Village", X = 0d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
            new NodeModel { Id = "beta", Name = "Beta", PlaceType = "Town", X = 10d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] }
        ],
        Edges =
        [
            new EdgeModel { Id = "alpha->beta", FromNodeId = "alpha", ToNodeId = "beta", Time = 1d, Cost = 1d, IsBidirectional = true, RouteType = "Road" }
        ]
    });

    try
    {
        var workspace = new WorkspaceViewModel();
        workspace.OpenNetwork(path);
        workspace.SelectNodeForEdit("alpha");
        workspace.NodeDraft.PlaceTypeText = "Harbor";
        workspace.ApplyInspectorCommand.Execute(null);

        workspace.SelectRouteForEdit("alpha->beta");
        workspace.EdgeDraft.RouteTypeText = "Canal";
        workspace.ApplyInspectorCommand.Execute(null);

        AssertTrue(workspace.NodeDraft.PlaceTypeSuggestions.Contains("Harbor"), "place type suggestions refresh after inspector edit");
        AssertTrue(workspace.BulkDraft.PlaceTypeSuggestions.Contains("Harbor"), "bulk place type suggestions refresh after inspector edit");
        AssertTrue(workspace.EdgeDraft.RouteTypeSuggestions.Contains("Canal"), "route type suggestions refresh after inspector edit");
    }
    finally
    {
        TryDelete(path);
    }
}

static void ScenarioAutocompleteSuggestionsImportGraphMlRefreshesCollections()
{
    var graphMlPath = Path.Combine(Path.GetTempPath(), $"medw-avalonia-import-{Guid.NewGuid():N}.graphml");

    try
    {
        var source = new WorkspaceViewModel();
        var networkPath = WriteTempNetwork(new NetworkModel
        {
            Name = "Import Suggestions",
            TrafficTypes = [new TrafficTypeDefinition { Name = "grain" }],
            Nodes =
            [
                new NodeModel { Id = "alpha", Name = "Alpha", PlaceType = "Village", X = 0d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
                new NodeModel { Id = "beta", Name = "Beta", PlaceType = "village", X = 10d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] },
                new NodeModel { Id = "gamma", Name = "Gamma", PlaceType = "", X = 20d, Y = 0d, TrafficProfiles = [new NodeTrafficProfile { TrafficType = "grain" }] }
            ],
            Edges =
            [
                new EdgeModel { Id = "alpha->beta", FromNodeId = "alpha", ToNodeId = "beta", Time = 1d, Cost = 1d, IsBidirectional = true, RouteType = "Road" },
                new EdgeModel { Id = "beta->gamma", FromNodeId = "beta", ToNodeId = "gamma", Time = 1d, Cost = 1d, IsBidirectional = true, RouteType = "road" },
                new EdgeModel { Id = "gamma->alpha", FromNodeId = "gamma", ToNodeId = "alpha", Time = 1d, Cost = 1d, IsBidirectional = true, RouteType = "" }
            ]
        });

        try
        {
            source.OpenNetwork(networkPath);
            source.ExportGraphMl(graphMlPath);
        }
        finally
        {
            TryDelete(networkPath);
        }

        var workspace = new WorkspaceViewModel();
        workspace.NodeDraft.PlaceTypeText = "Stale place";
        workspace.EdgeDraft.RouteTypeText = "Stale route";
        workspace.ImportGraphMl(graphMlPath);

        AssertTrue(!workspace.NodeDraft.PlaceTypeSuggestions.Contains("Stale place"), "place type suggestions refresh after import");
        AssertTrue(!workspace.BulkDraft.PlaceTypeSuggestions.Contains("Stale place"), "bulk place suggestions refresh after import");
        AssertTrue(!workspace.EdgeDraft.RouteTypeSuggestions.Contains("Stale route"), "route type suggestions refresh after import");
        AssertTrue(workspace.NodeDraft.PlaceTypeSuggestions.Count >= 1, "place type suggestions are repopulated after import");
        AssertTrue(workspace.EdgeDraft.RouteTypeSuggestions.Count >= 1, "route type suggestions are repopulated after import");
    }
    finally
    {
        TryDelete(graphMlPath);
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
        workspace.EdgeDraft.RouteTypeText = "Updated Corridor";
        workspace.EdgeDraft.TimeText = "5";
        workspace.CancelEdgeEditorCommand.Execute(null);

        AssertTrue(workspace.IsNormalWorkspaceMode, "route editor cancel returns to normal workspace");
        AssertTextEqual("Relief Corridor", workspace.EdgeDraft.RouteTypeText, "route editor cancel restores route label");
        AssertTextEqual("2", workspace.EdgeDraft.TimeText, "route editor cancel restores travel time");

        workspace.EnterEdgeEditor();
        workspace.EdgeDraft.RouteTypeText = "Updated Corridor";
        workspace.EdgeDraft.TimeText = "5";
        workspace.EdgeDraft.CostText = "7";
        workspace.EdgeDraft.CapacityText = "12";
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

static void ScenarioEditorWorkspaceCreatesAndValidatesEvents()
{
    var workspace = new WorkspaceViewModel();
    workspace.AddNodeAtPosition(new GraphPoint(0d, 0d));
    workspace.OpenScenarioEditorCommand.Execute(null);

    AssertTrue(workspace.IsScenarioEditorWorkspaceMode, "scenario editor enters dedicated workspace mode");
    workspace.ScenarioEditor.CreateScenarioCommand.Execute(null);
    workspace.ScenarioEditor.NameText = "Storm closure";
    workspace.ScenarioEditor.StartTimeText = "1";
    workspace.ScenarioEditor.EndTimeText = "4";
    workspace.ScenarioEditor.DeltaTimeText = "1";
    workspace.ScenarioEditor.AddScenarioEventCommand.Execute(null);
    workspace.ScenarioEditor.EventKind = ScenarioEventKind.DemandSpike;
    workspace.ScenarioEditor.EventTargetIdText = "missing";
    workspace.ScenarioEditor.EventTrafficTypeText = "general";
    workspace.ScenarioEditor.EventValueText = "-1";
    workspace.ScenarioEditor.SaveScenarioCommand.Execute(null);

    AssertTextEqual("Choose a target node.", workspace.ScenarioEditor.EventTargetError, "scenario editor validates target node");
    AssertTextEqual("Enter a demand value greater than or equal to 0.", workspace.ScenarioEditor.EventValueError, "scenario editor validates demand value");

    workspace.ScenarioEditor.EventTargetIdText = workspace.ScenarioEditor.NodeIdOptions.FirstOrDefault() ?? string.Empty;
    workspace.ScenarioEditor.EventValueText = "2";
    workspace.ScenarioEditor.SaveScenarioCommand.Execute(null);

    AssertTextEqual(string.Empty, workspace.ScenarioEditor.EventTargetError, "scenario editor clears target validation");
    AssertTrue(!workspace.ScenarioEditor.IsDirty, "scenario editor save clears dirty state");

    workspace.CloseScenarioEditorCommand.Execute(null);

    AssertTrue(workspace.IsNormalWorkspaceMode, "scenario editor returns to normal workspace");
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

        workspace.EdgeDraft.CapacityText = "20";
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

static void ScenarioTrafficTypesRailButtonOpensAndClosesTrafficWorkspace()
{
    var shell = new ShellWindow();
    var trafficButton = shell
        .GetVisualDescendants()
        .OfType<Button>()
        .FirstOrDefault(button => NormalizeButtonLabel(button.Content) == "Traffic Types");

    AssertTrue(trafficButton is not null, "traffic types rail button exists");
    AssertTrue(trafficButton!.Focusable, "traffic types rail button is keyboard focusable");
    AssertTextEqual(
        "Edit traffic types used by nodes and routes",
        ToolTip.GetTip(trafficButton)?.ToString() ?? string.Empty,
        "traffic types rail button tooltip");

    trafficButton.Command?.Execute(null);

    var trafficHost = GetPrivateField<Border>(shell, "trafficTypeWorkspaceHost");
    var standardHost = GetPrivateField<Grid>(shell, "standardWorkspaceHost");
    var shellWorkspaceMode = GetPrivateField<object>(shell, "shellWorkspaceMode");
    AssertTrue(trafficHost?.IsVisible == true, "traffic types rail button opens traffic workspace");
    AssertTrue(standardHost?.IsVisible == false, "traffic types workspace hides standard workspace");
    AssertTextEqual("TrafficTypes", shellWorkspaceMode?.ToString() ?? string.Empty, "traffic types workspace mode");

    var backButton = trafficHost!
        .GetVisualDescendants()
        .OfType<Button>()
        .FirstOrDefault(button => NormalizeButtonLabel(button.Content) == "Back to Network");
    AssertTrue(backButton is not null, "traffic types workspace back button exists");
    backButton!.Command?.Execute(null);

    shellWorkspaceMode = GetPrivateField<object>(shell, "shellWorkspaceMode");
    AssertTrue(trafficHost.IsVisible == false, "traffic workspace back button closes traffic workspace");
    AssertTrue(standardHost?.IsVisible == true, "traffic workspace back button restores standard workspace");
    AssertTextEqual("Standard", shellWorkspaceMode?.ToString() ?? string.Empty, "traffic workspace returns to standard mode");
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
        workspace.NodeDraft.NodeNameText = "Central Granary";
        workspace.NodeDraft.PlaceTypeText = "Storehouse";
        workspace.NodeDraft.DescriptionText = "Feeds the market";
        workspace.NodeDraft.TranshipmentCapacityText = "80";
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

    workspace.NodeDraft.NodeNameText = "Emergency Logistics Node With A Much Longer Live Preview Name";

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
    workspace.NodeDraft.NodeNameText = "Source Hub With Long Preview Name That Forces A Wider Box";

    var resizedSource = workspace.Scene.Nodes.First(node => node.Id == sourceNode.Id);
    var anchor = GraphHitTester.GetEdgeAnchor(workspace.Scene, resizedSource.Id, targetNode.Id);
    AssertNumberNear(resizedSource.Bounds.Right, anchor.X, 0.001d, "edge anchor tracks resized source width");
    AssertNumberNear(resizedSource.Bounds.CenterY, anchor.Y, 0.001d, "edge anchor tracks resized source center");

    var hit = new GraphHitTester().HitTest(workspace.Scene, new GraphPoint(resizedSource.Bounds.CenterX, resizedSource.Bounds.CenterY));
    AssertTextEqual(resizedSource.Id, hit.NodeId ?? string.Empty, "hit testing at resized center finds node");
}

static void ScenarioOneWayEdgeArrowTracksEdgeGeometry()
{
    var workspace = new WorkspaceViewModel();
    workspace.AddNodeToolCommand.Execute(null);
    var viewportSize = new GraphSize(1200d, 760d);
    var addContext = workspace.CreateInteractionContext(viewportSize);
    workspace.InteractionController.OnPointerPressed(addContext, GraphPointerButton.Left, new GraphPoint(340d, 320d), false, false, false);
    workspace.InteractionController.OnPointerPressed(addContext, GraphPointerButton.Left, new GraphPoint(860d, 320d), false, false, false);

    workspace.SelectToolCommand.Execute(null);
    var connectContext = workspace.CreateInteractionContext(viewportSize);
    var sourceNode = workspace.Scene.Nodes[0];
    var targetNode = workspace.Scene.Nodes[1];
    var sourceCenter = workspace.Viewport.WorldToScreen(new GraphPoint(sourceNode.Bounds.CenterX, sourceNode.Bounds.CenterY), connectContext.ViewportSize);
    var targetCenter = workspace.Viewport.WorldToScreen(new GraphPoint(targetNode.Bounds.CenterX, targetNode.Bounds.CenterY), connectContext.ViewportSize);
    workspace.InteractionController.OnPointerPressed(connectContext, GraphPointerButton.Left, sourceCenter, true, false, false);
    workspace.InteractionController.OnPointerMoved(connectContext, targetCenter);
    workspace.InteractionController.OnPointerReleased(connectContext, GraphPointerButton.Left, targetCenter, true);

    var edge = workspace.Scene.Edges.Single();
    AssertTrue(!edge.IsBidirectional, "one-way arrow scenario starts with one-way edge");
    var start = workspace.Viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(workspace.Scene, edge.FromNodeId, edge.ToNodeId), viewportSize);
    var end = workspace.Viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(workspace.Scene, edge.ToNodeId, edge.FromNodeId), viewportSize);
    var arrow = GraphRenderer.GetDirectionalArrowHead(start, end, 2.4d + (edge.LoadRatio * 1.6d));
    AssertTrue(arrow.HasValue, "one-way edge exposes arrow geometry");
    var arrowHead = arrow.GetValueOrDefault();
    AssertTrue(arrowHead.Tip.X > arrowHead.Left.X, "one-way arrow points toward the target");
    AssertTrue(arrowHead.Tip.X > arrowHead.Right.X, "one-way arrow tip stays ahead of the arrow base");

    var moveContext = workspace.CreateInteractionContext(viewportSize);
    var movedTargetStart = workspace.Viewport.WorldToScreen(new GraphPoint(workspace.Scene.Nodes[1].Bounds.CenterX, workspace.Scene.Nodes[1].Bounds.CenterY), viewportSize);
    var movedTargetEnd = new GraphPoint(movedTargetStart.X + 120d, movedTargetStart.Y + 40d);
    workspace.InteractionController.OnPointerPressed(moveContext, GraphPointerButton.Left, movedTargetStart, false, false, false);
    workspace.InteractionController.OnPointerMoved(moveContext, movedTargetEnd);
    workspace.InteractionController.OnPointerReleased(moveContext, GraphPointerButton.Left, movedTargetEnd, false);

    var updatedEdge = workspace.Scene.Edges.Single();
    var updatedStart = workspace.Viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(workspace.Scene, updatedEdge.FromNodeId, updatedEdge.ToNodeId), viewportSize);
    var updatedEnd = workspace.Viewport.WorldToScreen(GraphHitTester.GetEdgeAnchor(workspace.Scene, updatedEdge.ToNodeId, updatedEdge.FromNodeId), viewportSize);
    var updatedArrow = GraphRenderer.GetDirectionalArrowHead(updatedStart, updatedEnd, 2.4d + (updatedEdge.LoadRatio * 1.6d));
    AssertTrue(updatedArrow.HasValue, "one-way arrow geometry remains available after node resize");
    var updatedArrowHead = updatedArrow.GetValueOrDefault();
    AssertTrue(updatedArrowHead.Tip.X > arrowHead.Tip.X, "one-way arrow moves with the edge when anchors move");
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

static void SetSelectedNodes(WorkspaceViewModel workspace, params string[] nodeIds)
{
    workspace.Scene.Selection.SelectedEdgeIds.Clear();
    workspace.Scene.Selection.SelectedNodeIds.Clear();
    foreach (var nodeId in nodeIds)
    {
        workspace.Scene.Selection.SelectedNodeIds.Add(nodeId);
    }

    InvokePrivate(workspace, "RefreshInspector");
}

static void InvokePrivate(object instance, string methodName)
{
    var method = instance.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Unable to find private method '{methodName}'.");
    method.Invoke(instance, null);
}

static void InvokePrivateWithArgs(object instance, string methodName, params object?[] args)
{
    var methods = instance.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
        .ToList();
    var method = methods.FirstOrDefault(candidate => candidate.GetParameters().Length == args.Length)
        ?? throw new InvalidOperationException($"Unable to find private method '{methodName}' with {args.Length} args.");
    method.Invoke(instance, args);
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

static string NormalizeButtonLabel(object? content)
{
    var text = content?.ToString() ?? string.Empty;
    return text.StartsWith("● ", StringComparison.Ordinal) ? text[2..].TrimStart() : text;
}

static T? GetPrivateField<T>(object instance, string fieldName) where T : class
{
    var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
    return field?.GetValue(instance) as T;
}

static void ScenarioFacilityIso_EmptyOriginsReturnsNoReachableNodes()
{
    var network = CreateFacilityIsoNetwork();
    var service = new MultiOriginIsochroneService();

    var result = service.Compute(network.Nodes, network.Edges, [], 4d);

    AssertTrue(result.ReachableNodes.Count == 0, "facility iso empty origins reachable");
    AssertTrue(result.UncoveredNodes.Count == network.Nodes.Count, "facility iso empty origins uncovered");
}

static void ScenarioFacilityIso_SingleOriginMatchesLegacyIsochrone()
{
    var network = CreateFacilityIsoNetwork();
    var origin = network.Nodes.Single(node => node.Id == "A");
    var legacy = new IsochroneService();
    var multi = new MultiOriginIsochroneService();

    var legacyNodes = legacy.ComputeIsochrone(origin, 4d, network.Nodes, network.Edges, IsochroneService.CostMetric.Time, out _);
    var multiResult = multi.Compute(network.Nodes, network.Edges, [origin], 4d);

    AssertTrue(legacyNodes.Count == multiResult.ReachableNodes.Count, "facility iso single origin count");
}

static void ScenarioFacilityIso_MultipleOriginsCombineCoverage()
{
    var network = CreateFacilityIsoNetwork();
    var service = new MultiOriginIsochroneService();
    var origins = network.Nodes.Where(node => node.Id is "A" or "E").ToList();

    var result = service.Compute(network.Nodes, network.Edges, origins, 2d);

    AssertTrue(result.ReachableNodes.Count >= 4, "facility iso multiple origins combine coverage");
}

static void ScenarioFacilityIso_BestOriginChoosesLowestCost()
{
    var network = CreateFacilityIsoNetwork();
    var service = new MultiOriginIsochroneService();
    var originA = network.Nodes.Single(node => node.Id == "A");
    var originE = network.Nodes.Single(node => node.Id == "E");
    var nodeD = network.Nodes.Single(node => node.Id == "D");

    var result = service.Compute(network.Nodes, network.Edges, [originA, originE], 6d);

    AssertTextEqual("E", result.BestOriginByNode[nodeD].Id, "facility iso best origin by cost");
}

static void ScenarioFacilityIso_OverlapIncludesMultiCoveredNodes()
{
    var network = CreateFacilityIsoNetwork();
    var service = new MultiOriginIsochroneService();
    var origins = network.Nodes.Where(node => node.Id is "A" or "E").ToList();

    var result = service.Compute(network.Nodes, network.Edges, origins, 6d);

    AssertTrue(result.OverlapNodes.Any(node => node.Id == "C"), "facility iso overlap includes shared node");
}

static void ScenarioFacilityIso_UncoveredExcludesReachable()
{
    var network = CreateFacilityIsoNetwork();
    var service = new MultiOriginIsochroneService();
    var origins = new[] { network.Nodes.Single(node => node.Id == "A") };

    var result = service.Compute(network.Nodes, network.Edges, origins, 1d);

    AssertTrue(result.UncoveredNodes.All(node => !result.ReachableNodes.Contains(node)), "facility iso uncovered excludes reachable");
}

static void ScenarioFacilityIso_BudgetLimitExcludesOverBudgetNodes()
{
    var network = CreateFacilityIsoNetwork();
    var service = new MultiOriginIsochroneService();
    var origins = new[] { network.Nodes.Single(node => node.Id == "A") };

    var result = service.Compute(network.Nodes, network.Edges, origins, 1.5d);

    AssertTrue(result.ReachableNodes.All(node => node.Id is not "D" and not "E"), "facility iso excludes over budget nodes");
}

static void ScenarioFacilityIso_DirectedEdgesRespected()
{
    var network = new NetworkModel
    {
        Nodes =
        [
            new NodeModel { Id = "X", Name = "X", TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }] },
            new NodeModel { Id = "Y", Name = "Y", TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }] }
        ],
        Edges =
        [
            new EdgeModel { Id = "X->Y", FromNodeId = "X", ToNodeId = "Y", Time = 1d, Cost = 1d, IsBidirectional = false }
        ]
    };

    var service = new MultiOriginIsochroneService();
    var fromY = service.Compute(network.Nodes, network.Edges, [network.Nodes[1]], 2d);

    AssertTrue(fromY.ReachableNodes.All(node => node.Id != "X"), "facility iso directed edges respected");
}

static void ScenarioFacilityPlanning_SingleFacilityCoversReachableNodes()
{
    var workspace = BuildFacilityPlanningWorkspace();
    workspace.SetFacilityPlanningMode(true);
    workspace.ToggleFacilityOriginById("A");

    AssertTrue(workspace.CurrentMultiOriginIsochrone is not null, "facility planning single coverage result exists");
    AssertTrue(workspace.CurrentMultiOriginIsochrone!.ReachableNodes.Any(node => node.Id == "C"), "facility planning single coverage reaches C");
}

static void ScenarioFacilityPlanning_MultipleFacilitiesShareCoverage()
{
    var workspace = BuildFacilityPlanningWorkspace();
    workspace.SetFacilityPlanningMode(true);
    workspace.ToggleFacilityOriginById("A");
    workspace.ToggleFacilityOriginById("E");

    var nodeC = workspace.Scene.Nodes.Single(node => node.Id == "C");
    AssertTrue(nodeC.CoveringFacilities.Count >= 2, "facility planning shared node has multiple coverings");
    AssertTrue(nodeC.IsMultiFacilityCovered, "facility planning shared node flag");
}

static void ScenarioFacilityPlanning_UniqueCoverageExcludesSharedNodes()
{
    var workspace = BuildFacilityPlanningWorkspace();
    workspace.SetFacilityPlanningMode(true);
    workspace.ToggleFacilityOriginById("A");
    workspace.ToggleFacilityOriginById("E");
    workspace.RunMultiOriginIsochrone();

    var rowForA = workspace.FacilityComparisonRows.Single(row => row.Facility == "A");
    var rowForE = workspace.FacilityComparisonRows.Single(row => row.Facility == "E");
    AssertTrue(double.Parse(rowForA.UniqueNodesCovered, CultureInfo.InvariantCulture) < double.Parse(rowForA.NodesCovered, CultureInfo.InvariantCulture), "facility planning unique excludes overlap A");
    AssertTrue(double.Parse(rowForE.UniqueNodesCovered, CultureInfo.InvariantCulture) < double.Parse(rowForE.NodesCovered, CultureInfo.InvariantCulture), "facility planning unique excludes overlap E");
}

static void ScenarioFacilityPlanning_RemovingFacilityPreservesOtherCoverage()
{
    var workspace = BuildFacilityPlanningWorkspace();
    workspace.SetFacilityPlanningMode(true);
    workspace.ToggleFacilityOriginById("A");
    workspace.ToggleFacilityOriginById("E");
    workspace.ToggleFacilityOriginById("E");

    var nodeC = workspace.Scene.Nodes.Single(node => node.Id == "C");
    AssertTextEqual("A", nodeC.PrimaryFacilityId ?? string.Empty, "facility planning remove keeps remaining primary");
    AssertTrue(nodeC.CoveringFacilities.Count == 1, "facility planning remove keeps one covering");
}

static void ScenarioFacilityPlanning_ChangingMaxTravelTimeRecomputesCoverage()
{
    var workspace = BuildFacilityPlanningWorkspace();
    workspace.SetFacilityPlanningMode(true);
    workspace.ToggleFacilityOriginById("A");

    var facility = workspace.SelectedFacilityNodes.Single();
    facility.MaxTravelTimeText = "1";
    AssertTrue(workspace.Scene.Nodes.Single(node => node.Id == "D").IsFacilityCovered is false, "facility planning low budget excludes D");

    facility.MaxTravelTimeText = "5";
    AssertTrue(workspace.Scene.Nodes.Single(node => node.Id == "D").IsFacilityCovered, "facility planning increased budget includes D");
}

static void ScenarioFacilityPlanning_ComputeIsochroneStillWorks()
{
    var workspace = BuildFacilityPlanningWorkspace();
    workspace.SetIsochroneMode(true);

    var success = workspace.ComputeIsochrone("A", 2d);
    AssertTrue(success, "facility planning compute isochrone still succeeds");
    AssertTrue(workspace.IsochroneNodes.Any(node => node.Id == "C"), "facility planning compute isochrone reaches C");
}

static WorkspaceViewModel BuildFacilityPlanningWorkspace()
{
    var workspace = new WorkspaceViewModel();
    var network = CreateFacilityIsoNetwork();
    InvokePrivateWithArgs(workspace, "LoadNetwork", network, "Loaded for facility planning verification.", null);
    return workspace;
}

static NetworkModel CreateFacilityIsoNetwork()
{
    return new NetworkModel
    {
        Nodes =
        [
            new NodeModel { Id = "A", Name = "A", TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }] },
            new NodeModel { Id = "B", Name = "B", TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }] },
            new NodeModel { Id = "C", Name = "C", TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }] },
            new NodeModel { Id = "D", Name = "D", TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }] },
            new NodeModel { Id = "E", Name = "E", TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }] }
        ],
        Edges =
        [
            new EdgeModel { Id = "A-B", FromNodeId = "A", ToNodeId = "B", Time = 1d, Cost = 1d, IsBidirectional = true },
            new EdgeModel { Id = "B-C", FromNodeId = "B", ToNodeId = "C", Time = 1d, Cost = 1d, IsBidirectional = true },
            new EdgeModel { Id = "C-D", FromNodeId = "C", ToNodeId = "D", Time = 2d, Cost = 1d, IsBidirectional = true },
            new EdgeModel { Id = "D-E", FromNodeId = "D", ToNodeId = "E", Time = 1d, Cost = 1d, IsBidirectional = true },
            new EdgeModel { Id = "E-C", FromNodeId = "E", ToNodeId = "C", Time = 1d, Cost = 1d, IsBidirectional = true }
        ]
    };
}

sealed class AutoCompleteBindingHost : ObservableObject
{
    private string placeType = string.Empty;
    private IReadOnlyList<string> placeTypeSuggestions = [];

    public string PlaceType
    {
        get => placeType;
        set => SetProperty(ref placeType, value);
    }

    public IReadOnlyList<string> PlaceTypeSuggestions
    {
        get => placeTypeSuggestions;
        set => SetProperty(ref placeTypeSuggestions, value);
    }
}

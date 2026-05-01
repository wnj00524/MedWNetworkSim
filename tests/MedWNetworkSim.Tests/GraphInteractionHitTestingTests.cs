using MedWNetworkSim.Interaction;
using MedWNetworkSim.Rendering;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class GraphInteractionHitTestingTests
{
    [Fact]
    public void NodeHitPadding_SelectsNearNodeBoundary()
    {
        var scene = BuildScene();
        var hit = new GraphHitTester().HitTest(scene, new GraphPoint(106, 50), zoom: 1d);
        Assert.Equal("a", hit.NodeId);
    }

    [Fact]
    public void EdgeHitTesting_UsesTolerance()
    {
        var scene = BuildScene();
        var hit = new GraphHitTester().HitTest(scene, new GraphPoint(140, 54), zoom: 1d);
        Assert.Equal("a->b", hit.EdgeId);
    }

    [Fact]
    public void EdgeMidpointHandle_IsSelectable()
    {
        var scene = BuildScene();
        var edge = scene.Edges[0];
        var midpoint = GraphHitTester.GetEdgeMidpoint(scene, edge);
        var hit = new GraphHitTester().HitTest(scene, midpoint, zoom: 1d);
        Assert.Equal(edge.Id, hit.EdgeId);
    }

    [Fact]
    public void MarqueeSelection_SelectsEdgesAndNodes()
    {
        var scene = BuildScene();
        var controller = new GraphInteractionController();
        var viewport = new GraphViewport();
        viewport.Reset(scene.GetContentBounds(), new GraphSize(1000, 700));
        var context = BuildContext(scene, viewport);

        var start = viewport.WorldToScreen(new GraphPoint(10, 10), context.ViewportSize);
        var end = viewport.WorldToScreen(new GraphPoint(200, 100), context.ViewportSize);
        controller.OnPointerPressed(context, GraphPointerButton.Left, start, shiftPressed: false, altPressed: false, controlPressed: false);
        controller.OnPointerMoved(context, end);
        controller.OnPointerReleased(context, GraphPointerButton.Left, end, shiftPressed: false);

        Assert.Contains("a", scene.Selection.SelectedNodeIds);
        Assert.Contains("b", scene.Selection.SelectedNodeIds);
        Assert.Contains("a->b", scene.Selection.SelectedEdgeIds);
    }

    private static GraphScene BuildScene()
    {
        var scene = new GraphScene();
        scene.Nodes.Add(new GraphNodeSceneItem
        {
            Id = "a",
            Name = "A",
            TypeLabel = "Node",
            MetricsLabel = string.Empty,
            DetailLines = [],
            Bounds = new GraphRect(20, 20, 80, 60),
            FillColor = default,
            StrokeColor = default,
            Badges = [],
            HasWarning = false
        });
        scene.Nodes.Add(new GraphNodeSceneItem
        {
            Id = "b",
            Name = "B",
            TypeLabel = "Node",
            MetricsLabel = string.Empty,
            DetailLines = [],
            Bounds = new GraphRect(220, 20, 80, 60),
            FillColor = default,
            StrokeColor = default,
            Badges = [],
            HasWarning = false
        });
        scene.Edges.Add(new GraphEdgeSceneItem
        {
            Id = "a->b",
            FromNodeId = "a",
            ToNodeId = "b",
            Label = "a->b",
            IsBidirectional = false,
            Capacity = 1,
            Cost = 1,
            Time = 1,
            LoadRatio = 0,
            FlowRate = 0,
            HasWarning = false
        });
        return scene;
    }

    private static GraphInteractionContext BuildContext(GraphScene scene, GraphViewport viewport)
    {
        return new GraphInteractionContext
        {
            Scene = scene,
            Viewport = viewport,
            ViewportSize = new GraphSize(1000, 700),
            ToolMode = GraphToolMode.Select,
            CreateEdge = (_, _, _) => false,
            AddNodeAt = _ => string.Empty,
            DeleteSelection = () => { },
            FocusNextConnectedEdge = () => null,
            FocusNearbyNode = (_, _, _) => null,
            SelectionChanged = (_, _) => { },
            StatusChanged = _ => { },
            ToolModeChanged = _ => { },
            CanDragNode = _ => true,
            GetNodeDragBlockedMessage = _ => string.Empty
        };
    }
}

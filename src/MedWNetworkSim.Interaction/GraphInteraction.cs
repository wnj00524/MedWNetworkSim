using MedWNetworkSim.Rendering;

namespace MedWNetworkSim.Interaction;

public enum GraphPointerButton
{
    Left,
    Middle,
    Right
}

public sealed class GraphInteractionContext
{
    public required GraphScene Scene { get; init; }
    public required GraphViewport Viewport { get; init; }
    public required GraphSize ViewportSize { get; init; }
    public required Func<string, string, bool> CreateEdge { get; init; }
    public required Func<GraphPoint, string> AddNodeAt { get; init; }
    public required Action DeleteSelection { get; init; }
    public required Func<string?> FocusNextConnectedEdge { get; init; }
    public required Func<string?, bool, string, string?> FocusNearbyNode { get; init; }
    public required Action<string?, string?> SelectionChanged { get; init; }
    public required Action<string> StatusChanged { get; init; }
}

public sealed class GraphInteractionController
{
    private readonly GraphHitTester hitTester = new();
    private string? dragNodeId;
    private GraphPoint dragNodeOrigin;
    private GraphPoint dragPointerOrigin;
    private GraphPoint panPointerOrigin;
    private bool isPanning;
    private bool isMarqueeSelecting;
    private bool isConnectionGesture;
    private bool keyboardConnectMode;
    private string? keyboardConnectSource;

    public void OnPointerPressed(GraphInteractionContext context, GraphPointerButton button, GraphPoint screenPoint, bool shiftPressed, bool altPressed, bool spacePressed)
    {
        var worldPoint = context.Viewport.ScreenToWorld(screenPoint, context.ViewportSize);
        var hit = hitTester.HitTest(context.Scene, worldPoint);

        if (button == GraphPointerButton.Middle || (button == GraphPointerButton.Left && spacePressed))
        {
            isPanning = true;
            panPointerOrigin = screenPoint;
            context.StatusChanged("Panning canvas.");
            return;
        }

        if (button == GraphPointerButton.Right && hit.NodeId is not null)
        {
            isConnectionGesture = true;
            context.Scene.Transient.ConnectionSourceNodeId = hit.NodeId;
            context.Scene.Transient.ConnectionWorld = worldPoint;
            context.StatusChanged("Connect gesture active. Release over another node to create an edge.");
            return;
        }

        if (button != GraphPointerButton.Left)
        {
            return;
        }

        if (hit.NodeId is not null)
        {
            SelectNode(context, hit.NodeId, additive: shiftPressed);
            dragNodeId = hit.NodeId;
            dragNodeOrigin = context.Scene.FindNode(hit.NodeId)?.Bounds is { } bounds
                ? new GraphPoint(bounds.X, bounds.Y)
                : worldPoint;
            dragPointerOrigin = worldPoint;
            context.StatusChanged("Dragging node.");
            return;
        }

        if (hit.EdgeId is not null)
        {
            SelectEdge(context, hit.EdgeId, additive: shiftPressed);
            context.StatusChanged("Edge selected.");
            return;
        }

        if (!shiftPressed)
        {
            context.Scene.Selection.SelectedNodeIds.Clear();
            context.Scene.Selection.SelectedEdgeIds.Clear();
        }

        isMarqueeSelecting = true;
        context.Scene.Transient.DragStartWorld = worldPoint;
        context.Scene.Transient.DragCurrentWorld = worldPoint;
        context.SelectionChanged(GetPrimaryNode(context.Scene), GetPrimaryEdge(context.Scene));
        context.StatusChanged("Marquee selection active.");
    }

    public void OnPointerMoved(GraphInteractionContext context, GraphPoint screenPoint)
    {
        var worldPoint = context.Viewport.ScreenToWorld(screenPoint, context.ViewportSize);
        if (isPanning)
        {
            var current = screenPoint;
            var deltaScreen = new GraphVector(current.X - panPointerOrigin.X, current.Y - panPointerOrigin.Y);
            context.Viewport.Pan(new GraphVector(deltaScreen.X / context.Viewport.Zoom, deltaScreen.Y / context.Viewport.Zoom));
            panPointerOrigin = current;
            return;
        }

        if (dragNodeId is not null)
        {
            var node = context.Scene.FindNode(dragNodeId);
            if (node is null)
            {
                return;
            }

            var worldDelta = worldPoint - dragPointerOrigin;
            node.Bounds = node.Bounds with
            {
                X = dragNodeOrigin.X + worldDelta.X,
                Y = dragNodeOrigin.Y + worldDelta.Y
            };
            return;
        }

        if (isMarqueeSelecting)
        {
            context.Scene.Transient.DragCurrentWorld = worldPoint;
            return;
        }

        if (isConnectionGesture)
        {
            context.Scene.Transient.ConnectionWorld = worldPoint;
            return;
        }

        var hit = hitTester.HitTest(context.Scene, worldPoint);
        context.Scene.Selection.HoverNodeId = hit.NodeId;
        context.Scene.Selection.HoverEdgeId = hit.EdgeId;
    }

    public void OnPointerReleased(GraphInteractionContext context, GraphPointerButton button, GraphPoint screenPoint, bool shiftPressed)
    {
        var worldPoint = context.Viewport.ScreenToWorld(screenPoint, context.ViewportSize);
        if (button == GraphPointerButton.Middle && isPanning)
        {
            isPanning = false;
            context.StatusChanged("Pan complete.");
            return;
        }

        if (dragNodeId is not null && button == GraphPointerButton.Left)
        {
            dragNodeId = null;
            context.StatusChanged("Node position updated.");
            return;
        }

        if (isMarqueeSelecting && button == GraphPointerButton.Left)
        {
            isMarqueeSelecting = false;
            var selectionRect = GraphRect.FromPoints(context.Scene.Transient.DragStartWorld ?? worldPoint, context.Scene.Transient.DragCurrentWorld ?? worldPoint);
            foreach (var node in context.Scene.Nodes.Where(node => selectionRect.Contains(new GraphPoint(node.Bounds.CenterX, node.Bounds.CenterY))))
            {
                context.Scene.Selection.SelectedNodeIds.Add(node.Id);
            }

            context.Scene.Transient.DragStartWorld = null;
            context.Scene.Transient.DragCurrentWorld = null;
            context.SelectionChanged(GetPrimaryNode(context.Scene), GetPrimaryEdge(context.Scene));
            context.StatusChanged($"{context.Scene.Selection.SelectedNodeIds.Count} nodes selected.");
            return;
        }

        if (isConnectionGesture && button == GraphPointerButton.Right)
        {
            var sourceId = context.Scene.Transient.ConnectionSourceNodeId;
            var target = hitTester.HitTest(context.Scene, worldPoint);
            context.Scene.Transient.ConnectionSourceNodeId = null;
            context.Scene.Transient.ConnectionWorld = null;
            isConnectionGesture = false;

            if (sourceId is not null && target.NodeId is not null && !string.Equals(sourceId, target.NodeId, StringComparison.OrdinalIgnoreCase))
            {
                if (context.CreateEdge(sourceId, target.NodeId))
                {
                    SelectEdge(context, $"{sourceId}->{target.NodeId}", additive: shiftPressed);
                    context.StatusChanged("Edge created.");
                }

                return;
            }

            context.StatusChanged("Connect gesture cancelled.");
        }
    }

    public void OnPointerWheel(GraphInteractionContext context, GraphPoint screenPoint, double delta)
    {
        var factor = delta > 0d ? 1.12d : 1d / 1.12d;
        context.Viewport.ZoomAt(screenPoint, context.ViewportSize, factor);
        context.StatusChanged($"Zoom {context.Viewport.Zoom:0.00}x.");
    }

    public bool OnKeyDown(GraphInteractionContext context, string key, bool shiftPressed)
    {
        switch (key)
        {
            case "Delete":
                context.DeleteSelection();
                context.StatusChanged("Selection deleted.");
                return true;

            case "Add":
            case "OemPlus":
                context.Viewport.ZoomAt(new GraphPoint(context.ViewportSize.Width / 2d, context.ViewportSize.Height / 2d), context.ViewportSize, 1.12d);
                return true;

            case "Subtract":
            case "OemMinus":
                context.Viewport.ZoomAt(new GraphPoint(context.ViewportSize.Width / 2d, context.ViewportSize.Height / 2d), context.ViewportSize, 1d / 1.12d);
                return true;

            case "F":
                context.Viewport.Reset(context.Scene.GetContentBounds(), context.ViewportSize);
                context.StatusChanged("Fit to content.");
                return true;

            case "N":
                var addedNodeId = context.AddNodeAt(context.Viewport.Center);
                SelectNode(context, addedNodeId, additive: false);
                context.StatusChanged("Node added.");
                return true;

            case "Tab":
                var edgeId = context.FocusNextConnectedEdge();
                if (edgeId is not null)
                {
                    SelectEdge(context, edgeId, additive: false);
                    context.StatusChanged("Focused connected edge.");
                }

                return true;

            case "E":
                keyboardConnectSource = GetPrimaryNode(context.Scene);
                keyboardConnectMode = keyboardConnectSource is not null;
                context.StatusChanged(keyboardConnectMode ? "Keyboard connect mode active. Focus another node and press Enter." : "Select a node before starting a connection.");
                return true;

            case "Enter":
                if (keyboardConnectMode && keyboardConnectSource is not null && GetPrimaryNode(context.Scene) is { } targetNode && !string.Equals(keyboardConnectSource, targetNode, StringComparison.OrdinalIgnoreCase))
                {
                    if (context.CreateEdge(keyboardConnectSource, targetNode))
                    {
                        keyboardConnectMode = false;
                        keyboardConnectSource = null;
                        context.StatusChanged("Edge created from keyboard.");
                    }
                }

                return true;

            case "Escape":
                keyboardConnectMode = false;
                keyboardConnectSource = null;
                context.Scene.Transient.ConnectionSourceNodeId = null;
                context.Scene.Transient.ConnectionWorld = null;
                context.StatusChanged("Transient interaction cleared.");
                return true;

            case "Left":
            case "Right":
            case "Up":
            case "Down":
                var next = context.FocusNearbyNode(GetPrimaryNode(context.Scene), shiftPressed, key);
                if (next is not null)
                {
                    SelectNode(context, next, additive: false);
                    context.StatusChanged($"Focused {next}.");
                }

                return true;
        }

        return false;
    }

    private static void SelectNode(GraphInteractionContext context, string nodeId, bool additive)
    {
        if (!additive)
        {
            context.Scene.Selection.SelectedNodeIds.Clear();
            context.Scene.Selection.SelectedEdgeIds.Clear();
        }

        context.Scene.Selection.SelectedNodeIds.Add(nodeId);
        context.Scene.Selection.KeyboardNodeId = nodeId;
        context.Scene.Selection.KeyboardEdgeId = null;
        context.SelectionChanged(nodeId, null);
    }

    private static void SelectEdge(GraphInteractionContext context, string edgeId, bool additive)
    {
        if (!additive)
        {
            context.Scene.Selection.SelectedNodeIds.Clear();
            context.Scene.Selection.SelectedEdgeIds.Clear();
        }

        context.Scene.Selection.SelectedEdgeIds.Add(edgeId);
        context.Scene.Selection.KeyboardNodeId = null;
        context.Scene.Selection.KeyboardEdgeId = edgeId;
        context.SelectionChanged(null, edgeId);
    }

    private static string? GetPrimaryNode(GraphScene scene) => scene.Selection.SelectedNodeIds.FirstOrDefault();
    private static string? GetPrimaryEdge(GraphScene scene) => scene.Selection.SelectedEdgeIds.FirstOrDefault();
}

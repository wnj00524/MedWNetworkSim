using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.Interaction;
using MedWNetworkSim.Presentation;
using MedWNetworkSim.Rendering;
using MedWNetworkSim.Rendering.Geo;
using MedWNetworkSim.Rendering.VisualAnalytics.Sankey;
using MedWNetworkSim.App.VisualAnalytics;
using MedWNetworkSim.App.Insights;
using MedWNetworkSim.App.VisualAnalytics.Sankey;
using MedWNetworkSim.UI.Controls;
using SkiaSharp;

namespace MedWNetworkSim.UI;

public sealed class GraphCanvasStatusChangedEventArgs : EventArgs
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required bool IsError { get; init; }
    public required bool HasVisibleFrame { get; init; }
}

public sealed class GraphCanvasFullNodeEditorRequestedEventArgs : EventArgs
{
    public required string NodeId { get; init; }
}

public readonly record struct GraphCanvasCoordinateTransform(GraphSize LogicalViewport, PixelSize PixelViewport)
{
    public double ScaleX => LogicalViewport.Width <= 0d ? 1d : PixelViewport.Width / LogicalViewport.Width;
    public double ScaleY => LogicalViewport.Height <= 0d ? 1d : PixelViewport.Height / LogicalViewport.Height;

    public GraphPoint PointerToGraph(Point localPoint)
    {
        var x = Math.Clamp(localPoint.X, 0d, LogicalViewport.Width);
        var y = Math.Clamp(localPoint.Y, 0d, LogicalViewport.Height);
        return new GraphPoint(x, y);
    }

    public static GraphCanvasCoordinateTransform Create(Size logicalBounds, double renderScalingX, double? renderScalingY = null)
    {
        var safeWidth = Math.Max(1d, logicalBounds.Width);
        var safeHeight = Math.Max(1d, logicalBounds.Height);
        var safeScaleX = Math.Max(1d, renderScalingX);
        var safeScaleY = Math.Max(1d, renderScalingY ?? renderScalingX);

        // Keep input in logical units and render in device pixels.
        // This alignment stays stable at 100%, 125%, and 150% display scaling because both paths share the same logical viewport.
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(safeWidth * safeScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(safeHeight * safeScaleY));
        return new GraphCanvasCoordinateTransform(new GraphSize(safeWidth, safeHeight), new PixelSize(pixelWidth, pixelHeight));
    }
}

internal static class FacilityPlanningDialogs
{
    public static async Task<double?> PromptMaxTravelTimeAsync(Control ownerControl, string facilityName, double currentValue)
    {
        var owner = ownerControl.GetVisualRoot() as Window;
        if (owner is null)
        {
            return null;
        }

        var result = (double?)null;
        var dialog = new Window
        {
            Width = 430,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.None,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome,
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            Title = "Facility max time"
        };

        var input = new TextBox
        {
            Watermark = "Max time",
            Text = Math.Max(0d, currentValue).ToString("0.##", CultureInfo.InvariantCulture),
            Background = new SolidColorBrush(AvaloniaDashboardTheme.InputBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.InputBorder),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            Padding = new Thickness(10, 6)
        };
        var helper = new TextBlock
        {
            Text = $"Enter the maximum travel time for {facilityName}.",
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
            TextWrapping = TextWrapping.Wrap
        };
        var validation = new TextBlock
        {
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.Danger),
            TextWrapping = TextWrapping.Wrap
        };

        void Apply()
        {
            if ((double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                 double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)) &&
                parsed >= 0d)
            {
                result = parsed;
                dialog.Close();
                return;
            }

            validation.Text = "Enter a number of 0 or greater.";
        }

        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Apply();
                e.Handled = true;
            }
        };

        var applyButton = new Button
        {
            Content = "Add facility",
            Background = new SolidColorBrush(AvaloniaDashboardTheme.Accent),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            Padding = new Thickness(14, 8)
        };
        applyButton.Click += (_, _) => Apply();
        var cancelButton = new Button
        {
            Content = "Cancel",
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ToolbarButtonBackground),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            Padding = new Thickness(14, 8)
        };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorderStrong),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Facility Max Time",
                        FontSize = 20,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                    },
                    helper,
                    input,
                    validation,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { applyButton, cancelButton }
                    }
                }
            }
        };

        dialog.Opened += (_, _) => input.Focus();
        await dialog.ShowDialog(owner);
        return result;
    }
}

public sealed class GraphCanvasControl : Control, IDisposable
{
    private const double ClickDragThresholdPixels = 4d;

    public static readonly StyledProperty<WorkspaceViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<GraphCanvasControl, WorkspaceViewModel?>(nameof(ViewModel));

    private readonly GraphRenderer renderer = new();
    private readonly SankeyRenderer sankeyRenderer = new();
    private readonly OsmRasterTileProvider osmTileProvider = new();
    private readonly MapGraphRenderer mapRenderer;
    private readonly IMapProjectionService mapProjectionService = new MapWebMercatorProjectionService();
    private SankeyDiagramModel? lastSankeyModel;
    private SankeyRenderDiagram? lastSankeyDiagram;
    private readonly Dictionary<string, SankeyNode> sankeyNodeById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SankeyLink> sankeyLinkById = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer animationTimer;
    private WriteableBitmap? bitmap;
    private DateTimeOffset lastFrame = DateTimeOffset.UtcNow;
    private string statusTitle = "Canvas placeholder";
    private string statusDetail = "Waiting for the graph scene.";
    private bool hasVisibleFrame;
    private bool hasError;
    private int statusNotificationVersion;
    private string? hoveredNodeId;
    private string? hoveredEdgeId;
    private Point? mapPointerDownPoint;
    private Point? lastMapPointerPosition;
    private bool isMapPanning;
    private bool isMiddleMousePanning;
    private bool isDraggingOsmSelection;
    private bool didMapDrag;
    private bool isDisposed;

    public GraphCanvasControl()
    {
        mapRenderer = new MapGraphRenderer(new MapWebMercatorProjectionService(), osmTileProvider);
        Focusable = true;
        ClipToBounds = true;
        MinHeight = 420;
        MinWidth = 720;

        animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, HandleAnimationTick);
        AttachedToVisualTree += (_, _) =>
        {
            if (!animationTimer.IsEnabled)
            {
                animationTimer.Start();
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            animationTimer.Stop();
            Dispose();
        };
        osmTileProvider.TilesChanged += HandleTilesChanged;
    }

    private void HandleTilesChanged(object? sender, EventArgs args)
    {
        if (isDisposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (ViewModel?.VisualisationState.ActiveMode == VisualisationMode.Map)
            {
                InvalidateVisual();
            }
        }, DispatcherPriority.Background);
    }

    public event EventHandler<GraphCanvasStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<GraphCanvasFullNodeEditorRequestedEventArgs>? FullNodeEditorRequested;

    public WorkspaceViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        animationTimer.Stop();
        bitmap?.Dispose();
        bitmap = null;
        osmTileProvider.TilesChanged -= HandleTilesChanged;
        osmTileProvider.Dispose();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        DrawBaseBackground(context);
        if (isDisposed)
        {
            UpdateStatus("Canvas detached", "This canvas has been detached from the visual tree.", isError: false, visibleFrame: false);
            return;
        }

        if (ViewModel is null)
        {
            UpdateStatus("Waiting for scene", "The graph workspace is still loading.", isError: false, visibleFrame: false);
            DrawStatusPanel(context, statusTitle, statusDetail, isError: false);
            return;
        }

        if (Bounds.Width <= 0d || Bounds.Height <= 0d)
        {
            UpdateStatus("Waiting for layout", "Canvas size is not ready yet.", isError: false, visibleFrame: false);
            DrawStatusPanel(context, statusTitle, statusDetail, isError: false);
            return;
        }

        try
        {
            var transform = GetCoordinateTransform();
            EnsureBitmap(transform);
            if (bitmap is null)
            {
                throw new InvalidOperationException("Unable to allocate the canvas bitmap.");
            }

            var interactionContext = ViewModel.CreateInteractionContext(transform.LogicalViewport);
            using var locked = bitmap.Lock();
            var imageInfo = new SKImageInfo(locked.Size.Width, locked.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(imageInfo, locked.Address, locked.RowBytes)
                ?? throw new InvalidOperationException("Unable to create the Skia surface.");

            surface.Canvas.Clear(SKColor.Empty);
            surface.Canvas.Scale((float)transform.ScaleX, (float)transform.ScaleY);
            var mode = ViewModel.VisualisationState.ActiveMode;
            if (mode == VisualisationMode.Sankey)
            {
                var model = ViewModel.BuildSankeyDiagram();
                if (!ReferenceEquals(lastSankeyModel, model))
                {
                    lastSankeyModel = model;
                    lastSankeyDiagram = new SankeyRenderDiagram
                    {
                        EmptyStateMessage = model.EmptyStateMessage,
                        Nodes = model.Nodes.Select(n => new SankeyRenderNode { Id = n.Id, Label = n.Label, Kind = n.Kind.ToString() }).ToArray(),
                        Links = model.Links.Select(l => new SankeyRenderLink { Id = l.Id, SourceNodeId = l.SourceNodeId, TargetNodeId = l.TargetNodeId, TrafficType = l.TrafficType, Value = l.Value, RouteSignature = l.RouteSignature, RouteEdgeIds = l.RouteEdgeIds, IsUnmetDemand = l.IsUnmetDemand }).ToArray()
                    };
                    sankeyNodeById.Clear();
                    foreach (var node in model.Nodes)
                    {
                        sankeyNodeById[node.Id] = node;
                    }

                    sankeyLinkById.Clear();
                    foreach (var link in model.Links)
                    {
                        sankeyLinkById[link.Id] = link;
                    }
                }

                if (lastSankeyDiagram is null)
                {
                    return;
                }

                sankeyRenderer.Render(surface.Canvas, lastSankeyDiagram, transform.LogicalViewport, interactionContext.Scene.Selection.KeyboardNodeId, interactionContext.Scene.Selection.KeyboardEdgeId);
            }
            else if (mode == VisualisationMode.Map)
            {
                var geoLookup = ViewModel.BuildGeoNodeLookup().ToDictionary(item => item.Key, item => new MapGeoCoordinate(item.Value.Latitude, item.Value.Longitude), StringComparer.OrdinalIgnoreCase);
                mapRenderer.Render(surface.Canvas, interactionContext.Scene, interactionContext.Viewport, transform.LogicalViewport, geoLookup, ViewModel.VisualisationState.ShowMapBackground, ViewModel.MapCamera, ViewModel.BuildMapSelectionOverlay(), out _);
            }
            else
            {
                renderer.Render(surface.Canvas, interactionContext.Scene, interactionContext.Viewport, transform.LogicalViewport);
            }
            surface.Canvas.Flush();

            context.DrawImage(
                bitmap,
                new Rect(0d, 0d, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                new Rect(0d, 0d, transform.LogicalViewport.Width, transform.LogicalViewport.Height));

            Debug.Assert(Math.Abs((bitmap.PixelSize.Width / transform.LogicalViewport.Width) - transform.ScaleX) < 0.01d);
            Debug.Assert(Math.Abs((bitmap.PixelSize.Height / transform.LogicalViewport.Height) - transform.ScaleY) < 0.01d);

            LogDebug($"Render viewport {transform.LogicalViewport.Width:0.##}x{transform.LogicalViewport.Height:0.##}, pixels {bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}, scale {transform.ScaleX:0.###}x{transform.ScaleY:0.###}.");
            UpdateStatus("Canvas ready", "Rendered the current graph view.", isError: false, visibleFrame: true);
        }
        catch (Exception ex)
        {
            LogDebug($"Render failure: {ex}");
            var safeMessage = UiExceptionBoundary.BuildActionableMessage(
                "Canvas rendering",
                "Try Fit View or reduce zoom/window size. If this keeps happening, restart the app.");
            UiExceptionBoundary.Report(ex, safeMessage, nameof(GraphCanvasControl));
            UpdateStatus("Render error", safeMessage, isError: true, visibleFrame: false);
            DrawStatusPanel(context, "Graph canvas failed to render", safeMessage, isError: true);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (ViewModel is null)
        {
            return;
        }

        try
        {
            var transform = GetCoordinateTransform();
            var interactionContext = ViewModel.CreateInteractionContext(transform.LogicalViewport);
            var point = PointerToGraph(e, transform);
            var button = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
            {
                PointerUpdateKind.LeftButtonPressed => GraphPointerButton.Left,
                PointerUpdateKind.MiddleButtonPressed => GraphPointerButton.Middle,
                PointerUpdateKind.RightButtonPressed => GraphPointerButton.Right,
                _ => GraphPointerButton.Left
            };

            if (button == GraphPointerButton.Middle && ViewModel.VisualisationState.ActiveMode == VisualisationMode.Map)
            {
                isMiddleMousePanning = true;
                isMapPanning = true;
                isDraggingOsmSelection = false;
                mapPointerDownPoint = e.GetPosition(this);
                lastMapPointerPosition = mapPointerDownPoint;
                didMapDrag = false;
                Cursor = new Cursor(StandardCursorType.SizeAll);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (button == GraphPointerButton.Right)
            {
                ShowContextMenu(ViewModel, interactionContext, point);
                e.Handled = true;
                return;
            }

            if (button == GraphPointerButton.Left &&
                ViewModel.IsFacilityPlanningMode &&
                TryToggleFacilitySelection(ViewModel, interactionContext, point))
            {
                e.Handled = true;
                return;
            }

            if (button == GraphPointerButton.Left &&
                ViewModel.IsIsochroneModeEnabled &&
                TryStartIsochroneSelection(ViewModel, interactionContext, point))
            {
                e.Handled = true;
                return;
            }

            if (button == GraphPointerButton.Left && e.ClickCount >= 2 && TryHandleWorkspaceDoubleClick(interactionContext, point))
            {
                ViewModel.NotifyVisualChanged();
                RefreshEditorSummaries(ViewModel);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (button == GraphPointerButton.Left && ViewModel.VisualisationState.ActiveMode == VisualisationMode.Sankey && TryHandleSankeySelection(ViewModel, point))
            {
                e.Handled = true;
                return;
            }

            if (button == GraphPointerButton.Left && ViewModel.VisualisationState.ActiveMode == VisualisationMode.Map)
            {
                var localPoint = e.GetPosition(this);
                mapPointerDownPoint = localPoint;
                lastMapPointerPosition = localPoint;
                didMapDrag = false;
                if (ViewModel.IsOsmAreaSelectionEnabled)
                {
                    ViewModel.BeginOsmSelection(LocalPointToMapGeo(localPoint, transform));
                    isDraggingOsmSelection = true;
                    isMapPanning = false;
                }
                else
                {
                    isMapPanning = true;
                    isDraggingOsmSelection = false;
                }

                e.Pointer.Capture(this);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            ViewModel.InteractionController.OnPointerPressed(                interactionContext,
                button,
                point,
                e.KeyModifiers.HasFlag(KeyModifiers.Shift),
                e.KeyModifiers.HasFlag(KeyModifiers.Alt),
                e.KeyModifiers.HasFlag(KeyModifiers.Control));
            ViewModel.NotifyVisualChanged();
            RefreshEditorSummaries(ViewModel);
            InvalidateVisual();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            ReportInputFailure("pointer press", ex);
            e.Handled = true;
        }
    }

    private bool TryHandleSankeySelection(WorkspaceViewModel viewModel, GraphPoint point)
    {
        var hit = sankeyRenderer.HitTest(point);
        if (hit is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(hit.Value.NodeId) && lastSankeyDiagram is not null)
        {
            var node = lastSankeyDiagram.Nodes.FirstOrDefault(n => string.Equals(n.Id, hit.Value.NodeId, StringComparison.OrdinalIgnoreCase));
            if (node is not null && sankeyNodeById.TryGetValue(node.Id, out var sourceNode) && !string.IsNullOrWhiteSpace(sourceNode.GraphNodeId))
            {
                viewModel.SelectNode(sourceNode.GraphNodeId);
                viewModel.NotifyVisualChanged();
                InvalidateVisual();
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(hit.Value.LinkId) && lastSankeyDiagram is not null)
        {
            var link = lastSankeyDiagram.Links.FirstOrDefault(l => string.Equals(l.Id, hit.Value.LinkId, StringComparison.OrdinalIgnoreCase));
            if (link is not null && sankeyLinkById.TryGetValue(link.Id, out var sourceLink))
            {
                var edgeIds = sourceLink.RouteEdgeIds;
                if (edgeIds.Count > 0)
                {
                    viewModel.HighlightRouteEdges(edgeIds);
                    viewModel.SelectEdge(edgeIds[0]);
                    viewModel.StatusText = edgeIds.Count > 1
                        ? $"Sankey route selected: {sourceLink.RouteSignature ?? string.Join(" → ", edgeIds)}"
                        : $"Sankey route selected: {edgeIds[0]}";
                }
            }
        }

        return true;
    }

    private bool TryStartIsochroneSelection(WorkspaceViewModel viewModel, GraphInteractionContext interactionContext, GraphPoint point)
    {
        var worldPoint = interactionContext.Viewport.ScreenToWorld(point, interactionContext.ViewportSize);
        var hit = new GraphHitTester().HitTest(interactionContext.Scene, worldPoint);
        if (string.IsNullOrWhiteSpace(hit.NodeId))
        {
            return false;
        }

        SafeFireAndForget(Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var threshold = await PromptIsochroneThresholdAsync(viewModel.IsochroneThresholdMinutes);
            if (!threshold.HasValue)
            {
                return;
            }

            if (viewModel.ComputeIsochrone(hit.NodeId, threshold.Value))
            {
                viewModel.NotifyVisualChanged();
                InvalidateVisual();
            }
        }), "isochrone selection");
        return true;
    }

    private bool TryHandleMapSelection(WorkspaceViewModel viewModel, Point screenPoint, GraphCanvasCoordinateTransform transform)
    {
        var geoLookup = viewModel.BuildGeoNodeLookup();
        if (geoLookup.Count == 0)
        {
            return false;
        }

        var projectionViewport = viewModel.BuildMapProjectionViewport(transform.LogicalViewport);
        var worldPoint = ScreenToWorld(screenPoint, transform);

        var nearestNode = geoLookup
            .Select(item =>
            {
                var projected = GeoToScreen(item.Value.Longitude, item.Value.Latitude, projectionViewport);
                var distance = Math.Sqrt(Math.Pow(projected.X - worldPoint.X, 2d) + Math.Pow(projected.Y - worldPoint.Y, 2d));
                return new { item.Key, Distance = distance };
            })
            .OrderBy(item => item.Distance)
            .FirstOrDefault();

        if (nearestNode is null || nearestNode.Distance > 20d)
        {
            return false;
        }

        viewModel.SelectNode(nearestNode.Key);
        viewModel.NotifyVisualChanged();
        InvalidateVisual();
        return true;
    }

    private bool TryToggleFacilitySelection(WorkspaceViewModel viewModel, GraphInteractionContext interactionContext, GraphPoint point)
    {
        var worldPoint = interactionContext.Viewport.ScreenToWorld(point, interactionContext.ViewportSize);
        var hit = new GraphHitTester().HitTest(interactionContext.Scene, worldPoint);
        if (string.IsNullOrWhiteSpace(hit.NodeId))
        {
            return false;
        }

        if (viewModel.IsFacilityOriginSelected(hit.NodeId))
        {
            return viewModel.ToggleFacilityOriginById(hit.NodeId);
        }

        SafeFireAndForget(Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var maxTravelTime = await FacilityPlanningDialogs.PromptMaxTravelTimeAsync(
                this,
                viewModel.GetFacilityNodeDisplayName(hit.NodeId),
                viewModel.IsochroneBudget);
            if (!maxTravelTime.HasValue)
            {
                return;
            }

            if (viewModel.ToggleFacilityOriginById(hit.NodeId, maxTravelTime.Value))
            {
                viewModel.NotifyVisualChanged();
                InvalidateVisual();
            }
        }), "facility selection");
        return true;
    }

    private async Task<double?> PromptIsochroneThresholdAsync(double currentThreshold)
    {
        var owner = this.GetVisualRoot() as Window;
        if (owner is null)
        {
            return null;
        }

        var result = (double?)null;
        var dialog = new Window
        {
            Width = 420,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.None,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome,
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            Title = "Isochrone threshold"
        };

        var input = new TextBox
        {
            Watermark = "Minutes",
            Background = new SolidColorBrush(AvaloniaDashboardTheme.InputBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.InputBorder),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            Padding = new Thickness(10, 6)
        };
        input.Text = currentThreshold.ToString("0.##", CultureInfo.InvariantCulture);
        var helper = new TextBlock
        {
            Text = "Enter threshold in minutes (must be 0 or greater).",
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
            TextWrapping = TextWrapping.Wrap
        };

        var applyButton = new Button
        {
            Content = "Compute",
            Background = new SolidColorBrush(AvaloniaDashboardTheme.Accent),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            Padding = new Thickness(14, 8)
        };
        applyButton.Click += (_, _) =>
        {
            if (double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0d)
            {
                result = parsed;
                dialog.Close();
            }
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ToolbarButtonBackground),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            Padding = new Thickness(14, 8)
        };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorderStrong),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Isochrone Mode",
                        FontSize = 20,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                    },
                    helper,
                    input,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { applyButton, cancelButton }
                    }
                }
            }
        };

        await dialog.ShowDialog(owner);
        return result;
    }

    public bool TryHandleWorkspaceDoubleClick(GraphInteractionContext interactionContext, GraphPoint point)
    {
        if (ViewModel is null)
        {
            return false;
        }

        var worldPoint = interactionContext.Viewport.ScreenToWorld(point, interactionContext.ViewportSize);
        var hit = new GraphHitTester().HitTest(interactionContext.Scene, worldPoint);
        if (hit.EdgeId is not null)
        {
            ViewModel.OpenRouteEditor(hit.EdgeId);
            return true;
        }

        if (hit.NodeId is not null)
        {
            return false;
        }

        var nodeId = ViewModel.AddNodeAtPosition(worldPoint);
        FullNodeEditorRequested?.Invoke(this, new GraphCanvasFullNodeEditorRequestedEventArgs
        {
            NodeId = nodeId
        });
        return true;
    }

    private void ShowContextMenu(WorkspaceViewModel viewModel, GraphInteractionContext interactionContext, GraphPoint screenPoint)
    {
        var worldPoint = interactionContext.Viewport.ScreenToWorld(screenPoint, interactionContext.ViewportSize);
        var hit = new GraphHitTester().HitTest(interactionContext.Scene, worldPoint);
        if (ContextMenu is { } existingMenu)
        {
            existingMenu.Close();
            ContextMenu = null;
        }

        var menu = new ContextMenu();
        var items = new List<MenuItem>();

        if (hit.NodeId is not null)
        {
            items.Add(BuildMenuItem("Select Node", () => viewModel.SelectNodeForEdit(hit.NodeId)));
            items.Add(BuildMenuItem("Edit Node", () => viewModel.SelectNodeForEdit(hit.NodeId)));
            items.Add(BuildMenuItem("Add Traffic Role", () =>
            {
                viewModel.SelectNodeForEdit(hit.NodeId, focusTrafficRoles: true);
                viewModel.AddNodeTrafficProfileCommand.Execute(null);
            }));
            items.Add(BuildMenuItem("Delete Node", () => viewModel.DeleteNodeById(hit.NodeId)));
            items.Add(BuildMenuItem("Start Edge From Here", () => _ = viewModel.StartEdgeFromNode(hit.NodeId)));
        }
        else if (hit.EdgeId is not null)
        {
            items.Add(BuildMenuItem("Select Route", () => viewModel.SelectRouteForEdit(hit.EdgeId)));
            items.Add(BuildMenuItem("Edit Route", () => viewModel.OpenRouteEditor(hit.EdgeId)));
            items.Add(BuildMenuItem("Delete Route", () => viewModel.DeleteRouteById(hit.EdgeId)));
        }
        else
        {
            items.Add(BuildMenuItem("Add Node Here", () => viewModel.AddNodeAtPosition(worldPoint)));
            items.Add(BuildMenuItem("Fit View", () => viewModel.FitCommand.Execute(null)));
            items.Add(BuildMenuItem("Clear Selection", viewModel.ClearSelection));
        }

        menu.ItemsSource = items;
        menu.Placement = PlacementMode.Pointer;
        menu.PlacementTarget = this;
        ContextMenu = menu;
        menu.Open(this);
    }

    private static MenuItem BuildMenuItem(string header, Action action)
    {
        return new MenuItem
        {
            Header = header,
            Command = new RelayCommand(action)
        };
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ViewModel is null)
        {
            return;
        }

        try
        {
            var transform = GetCoordinateTransform();
            if (ViewModel.VisualisationState.ActiveMode == VisualisationMode.Map)
            {
                var current = e.GetPosition(this);

                if (mapPointerDownPoint is { } downPoint &&
                    Distance(downPoint, current) > ClickDragThresholdPixels)
                {
                    didMapDrag = true;
                }

                if (isDraggingOsmSelection)
                {
                    ViewModel.UpdateOsmSelection(LocalPointToMapGeo(current, transform));
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }

                if (isMapPanning && lastMapPointerPosition is { } previous)
                {
                    var delta = Delta(previous, current);
                    if (Distance(previous, current) > 0d)
                    {
                        ViewModel.PanMap(-delta.X, -delta.Y);
                        lastMapPointerPosition = current;
                        InvalidateVisual();
                    }
                    e.Handled = true;
                    return;
                }
            }

            ViewModel.InteractionController.OnPointerMoved(
                ViewModel.CreateInteractionContext(transform.LogicalViewport),
                PointerToGraph(e, transform));
            UpdateHoveredNodeToolTip(ViewModel, transform, e);
            ViewModel.NotifyVisualChanged();
            InvalidateVisual();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            ReportInputFailure("pointer move", ex);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (ViewModel is null)
        {
            return;
        }

        try
        {
            var transform = GetCoordinateTransform();
            if (ViewModel.VisualisationState.ActiveMode == VisualisationMode.Map)
            {
                var current = e.GetPosition(this);
                if (isDraggingOsmSelection && e.InitialPressMouseButton == MouseButton.Left)
                {
                    ViewModel.EndOsmSelection(LocalPointToMapGeo(current, transform));
                }
                else if (!didMapDrag && e.InitialPressMouseButton == MouseButton.Left)
                {
                    TryHandleMapSelection(ViewModel, current, transform);
                }

                mapPointerDownPoint = null;
                lastMapPointerPosition = null;
                isMapPanning = false;
                isMiddleMousePanning = false;
                isDraggingOsmSelection = false;
                didMapDrag = false;
                Cursor = null;
                e.Pointer.Capture(null);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            var button = e.InitialPressMouseButton switch
            {
                MouseButton.Middle => GraphPointerButton.Middle,
                MouseButton.Right => GraphPointerButton.Right,
                _ => GraphPointerButton.Left
            };

            ViewModel.InteractionController.OnPointerReleased(
                ViewModel.CreateInteractionContext(transform.LogicalViewport),
                button,
                PointerToGraph(e, transform),
                e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            ViewModel.NotifyVisualChanged();
            RefreshEditorSummaries(ViewModel);
            InvalidateVisual();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            ReportInputFailure("pointer release", ex);
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (ViewModel is null)
        {
            return;
        }

        try
        {
            var transform = GetCoordinateTransform();
            if (ViewModel.VisualisationState.ActiveMode == VisualisationMode.Map)
            {
                ViewModel.ZoomMapAt(PointerToGraph(e, transform), e.Delta.Y > 0d ? 1.25d : 0.8d);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            ViewModel.InteractionController.OnPointerWheel(
                ViewModel.CreateInteractionContext(transform.LogicalViewport),
                PointerToGraph(e, transform),
                e.Delta.Y);
            ViewModel.NotifyVisualChanged();
            InvalidateVisual();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            ReportInputFailure("mouse wheel", ex);
            e.Handled = true;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        hoveredNodeId = null;
        hoveredEdgeId = null;
        ToolTip.SetTip(this, null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        isMapPanning = false;
        isMiddleMousePanning = false;
        isDraggingOsmSelection = false;
        mapPointerDownPoint = null;
        lastMapPointerPosition = null;
        didMapDrag = false;
        Cursor = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (ViewModel is null)
        {
            return;
        }

        try
        {
            var transform = GetCoordinateTransform();
            if (ViewModel.VisualisationState.ActiveMode == VisualisationMode.Map && TryHandleMapKeyDown(ViewModel, e))
            {
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (ViewModel.InteractionController.OnKeyDown(
                    ViewModel.CreateInteractionContext(transform.LogicalViewport),
                    e.Key.ToString(),
                    e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
            {
                ViewModel.NotifyVisualChanged();
                RefreshEditorSummaries(ViewModel);
                InvalidateVisual();
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            ReportInputFailure("keyboard input", ex);
            e.Handled = true;
        }
    }

    private bool TryHandleMapKeyDown(WorkspaceViewModel viewModel, KeyEventArgs e)
    {
        const double pan = 64d;
        if (e.Key == Key.Escape && viewModel.IsOsmAreaSelectionEnabled)
        {
            isDraggingOsmSelection = false;
            isMapPanning = false;
            isMiddleMousePanning = false;
            mapPointerDownPoint = null;
            lastMapPointerPosition = null;
            didMapDrag = false;
            Cursor = null;
            viewModel.ClearOsmSelection();
            return true;
        }

        if (e.Key == Key.Enter)
        {
            if (viewModel.ImportOsmSelectionCommand.CanExecute(null))
            {
                viewModel.ImportOsmSelectionCommand.Execute(null);
            }
            return true;
        }

        if (e.Key is Key.Add or Key.OemPlus)
        {
            viewModel.ZoomMapAt(new GraphPoint(viewModel.LastViewportSize.Width / 2d, viewModel.LastViewportSize.Height / 2d), 1.2d);
            return true;
        }

        if (e.Key is Key.Subtract or Key.OemMinus)
        {
            viewModel.ZoomMapAt(new GraphPoint(viewModel.LastViewportSize.Width / 2d, viewModel.LastViewportSize.Height / 2d), 0.8d);
            return true;
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var dx = e.Key == Key.Left ? -pan : e.Key == Key.Right ? pan : 0d;
            var dy = e.Key == Key.Up ? -pan : e.Key == Key.Down ? pan : 0d;
            viewModel.PanMap(dx, dy);
            return true;
        }

        return false;
    }

    public GraphCanvasCoordinateTransform GetCoordinateTransform()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var renderScale = topLevel?.RenderScaling ?? 1d;
        return GraphCanvasCoordinateTransform.Create(Bounds.Size, renderScale);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Point Delta(Point from, Point to)
    {
        return new Point(to.X - from.X, to.Y - from.Y);
    }

    private GraphPoint LocalPointToGraph(Point localPoint, GraphCanvasCoordinateTransform transform)
    {
        return transform.PointerToGraph(localPoint);
    }

    private GraphPoint ScreenToWorld(Point screenPoint, GraphCanvasCoordinateTransform transform)
    {
        var localGraphPoint = LocalPointToGraph(screenPoint, transform);
        if (ViewModel?.VisualisationState.ActiveMode == VisualisationMode.Map)
        {
            return localGraphPoint;
        }

        var interactionContext = ViewModel?.CreateInteractionContext(transform.LogicalViewport);
        return interactionContext is null
            ? localGraphPoint
            : interactionContext.Viewport.ScreenToWorld(localGraphPoint, interactionContext.ViewportSize);
    }

    private GraphPoint WorldToScreen(GraphPoint worldPoint, GraphCanvasCoordinateTransform transform)
    {
        if (ViewModel?.VisualisationState.ActiveMode == VisualisationMode.Map)
        {
            return worldPoint;
        }

        var interactionContext = ViewModel?.CreateInteractionContext(transform.LogicalViewport);
        return interactionContext is null
            ? worldPoint
            : interactionContext.Viewport.WorldToScreen(worldPoint, interactionContext.ViewportSize);
    }

    private GraphPoint GeoToWorld(double lon, double lat, MapProjectionViewport viewport)
    {
        var projected = mapProjectionService.Project(new MapGeoCoordinate(lat, lon), viewport);
        return new GraphPoint(projected.X, projected.Y);
    }

    private GraphPoint GeoToScreen(double lon, double lat, MapProjectionViewport viewport) => GeoToWorld(lon, lat, viewport);

    private GraphPoint PointerToGraph(PointerEventArgs e, GraphCanvasCoordinateTransform transform)
    {
        var local = e.GetPosition(this);
        var graphPoint = LocalPointToGraph(local, transform);

        if (local.X < 0d || local.Y < 0d || local.X > Bounds.Width || local.Y > Bounds.Height)
        {
            LogDebug($"Pointer clamped from ({local.X:0.##},{local.Y:0.##}) to ({graphPoint.X:0.##},{graphPoint.Y:0.##}) within {transform.LogicalViewport.Width:0.##}x{transform.LogicalViewport.Height:0.##}.");
        }

        return graphPoint;
    }

    private MapGeoCoordinate LocalPointToMapGeo(Point localPoint, GraphCanvasCoordinateTransform transform)
    {
        var graphPoint = ScreenToWorld(localPoint, transform);
        var mapViewport = ViewModel!.BuildMapProjectionViewport(transform.LogicalViewport);
        return mapProjectionService.Unproject(graphPoint.X, graphPoint.Y, mapViewport);
    }

    private void UpdateHoveredNodeToolTip(WorkspaceViewModel viewModel, GraphCanvasCoordinateTransform transform, PointerEventArgs e)
    {
        if (viewModel.IsSankeyMode)
        {
            var sankeyHit = sankeyRenderer.HitTest(PointerToGraph(e, transform));
            if (sankeyHit is null)
            {
                ToolTip.SetTip(this, null);
                return;
            }

            if (!string.IsNullOrWhiteSpace(sankeyHit.Value.NodeId) && sankeyNodeById.TryGetValue(sankeyHit.Value.NodeId!, out var sankeyNode))
            {
                ToolTip.SetTip(this, $"{sankeyNode.Label}\nType: node\nFlow: {sankeyNode.Value:0.##}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(sankeyHit.Value.LinkId) && sankeyLinkById.TryGetValue(sankeyHit.Value.LinkId!, out var sankeyLink))
            {
                var routeLabel = string.IsNullOrWhiteSpace(sankeyLink.RouteSignature) ? "N/A" : sankeyLink.RouteSignature;
                var unmet = sankeyLink.IsUnmetDemand ? "Yes" : "No";
                ToolTip.SetTip(this, $"{sankeyLink.SourceNodeId} → {sankeyLink.TargetNodeId}\nTraffic: {sankeyLink.TrafficType}\nFlow: {sankeyLink.Value:0.##}\nRoute: {routeLabel}\nUnmet demand: {unmet}");
                return;
            }
        }

        var interactionContext = viewModel.CreateInteractionContext(transform.LogicalViewport);
        var worldPoint = interactionContext.Viewport.ScreenToWorld(PointerToGraph(e, transform), interactionContext.ViewportSize);
        var hit = new GraphHitTester().HitTest(interactionContext.Scene, worldPoint);
        if (string.Equals(hoveredNodeId, hit.NodeId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(hoveredEdgeId, hit.EdgeId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        hoveredNodeId = hit.NodeId;
        hoveredEdgeId = hit.EdgeId;
        var tooltipText = interactionContext.Scene.FindNode(hit.NodeId)?.ToolTipText
            ?? interactionContext.Scene.FindEdge(hit.EdgeId)?.ToolTipText;
        ToolTip.SetTip(this, string.IsNullOrWhiteSpace(tooltipText) ? null : tooltipText);
    }

    private void EnsureBitmap(GraphCanvasCoordinateTransform transform)
    {
        const int maxPixelsPerAxis = 16384;
        if (transform.PixelViewport.Width <= 0 || transform.PixelViewport.Height <= 0)
        {
            throw new InvalidOperationException("Canvas pixel size is invalid.");
        }

        if (transform.PixelViewport.Width > maxPixelsPerAxis || transform.PixelViewport.Height > maxPixelsPerAxis)
        {
            throw new InvalidOperationException(
                $"Canvas resolution {transform.PixelViewport.Width}x{transform.PixelViewport.Height} is too large. Resize the window or reduce display scaling.");
        }

        if (bitmap is not null &&
            bitmap.PixelSize.Width == transform.PixelViewport.Width &&
            bitmap.PixelSize.Height == transform.PixelViewport.Height)
        {
            return;
        }

        bitmap?.Dispose();
        bitmap = new WriteableBitmap(
            transform.PixelViewport,
            new Vector(96d * transform.ScaleX, 96d * transform.ScaleY),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);
    }

    private void ReportInputFailure(string operation, Exception ex)
    {
        var safeMessage = UiExceptionBoundary.BuildActionableMessage(
            $"Canvas {operation}",
            "Try the action again. If this repeats, save your work and restart.");
        UiExceptionBoundary.Report(ex, safeMessage, nameof(GraphCanvasControl));
        UpdateStatus("Input error", safeMessage, isError: true, visibleFrame: hasVisibleFrame);
    }

    private void SafeFireAndForget(Task task, string operation)
    {
        _ = task.ContinueWith(
            t =>
            {
                if (t.Exception is null)
                {
                    return;
                }

                var ex = t.Exception.Flatten().InnerException ?? t.Exception;
                var safeMessage = UiExceptionBoundary.BuildActionableMessage(
                    $"{operation} action",
                    "Please retry. If it keeps failing, close any open dialogs and try again.");
                UiExceptionBoundary.Report(ex, safeMessage, nameof(GraphCanvasControl));
                Dispatcher.UIThread.Post(() => UpdateStatus("Action failed", safeMessage, isError: true, visibleFrame: hasVisibleFrame));
            },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void HandleAnimationTick(object? sender, EventArgs e)
    {
        if (ViewModel is null || VisualRoot is null || Bounds.Width <= 0d || Bounds.Height <= 0d)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - lastFrame).TotalSeconds;
        lastFrame = now;
        ViewModel.TickAnimation(elapsed);
        InvalidateVisual();
    }

    private static void RefreshEditorSummaries(WorkspaceViewModel viewModel)
    {
        _ = viewModel;
    }

    private void DrawBaseBackground(DrawingContext context)
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(AvaloniaDashboardTheme.CanvasBackgroundStart, 0),
                new GradientStop(AvaloniaDashboardTheme.CanvasBackgroundEnd, 1)
            }
        };

        context.DrawRectangle(
            gradient,
            new Pen(new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder), 1),
            Bounds);

        const double spacing = 32d;
        var gridPen = new Pen(new SolidColorBrush(AvaloniaDashboardTheme.CanvasGridLine, 0.35), 1);
        for (double x = 0; x <= Bounds.Width; x += spacing)
        {
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, Bounds.Height));
        }

        for (double y = 0; y <= Bounds.Height; y += spacing)
        {
            context.DrawLine(gridPen, new Point(0, y), new Point(Bounds.Width, y));
        }
    }

    private void DrawStatusPanel(DrawingContext context, string title, string detail, bool isError)
    {
        var panelRect = new Rect(
            Bounds.X + 16d,
            Bounds.Y + 16d,
            Math.Max(0d, Bounds.Width - 32d),
            Math.Max(0d, Bounds.Height - 32d));

        var fill = new SolidColorBrush(AvaloniaDashboardTheme.PanelBackground);
        var border = new Pen(new SolidColorBrush(isError ? AvaloniaDashboardTheme.Danger : AvaloniaDashboardTheme.PanelBorderStrong), 2);
        context.DrawRectangle(fill, border, panelRect);

        var titleText = CreateFormattedText(title, 24d, FontWeight.Bold, isError ? AvaloniaDashboardTheme.Danger : AvaloniaDashboardTheme.PrimaryText);
        var detailText = CreateFormattedText(detail, 14d, FontWeight.Normal, AvaloniaDashboardTheme.SecondaryText);

        context.DrawText(titleText, new Point(panelRect.X + 18d, panelRect.Y + 18d));
        context.DrawText(detailText, new Point(panelRect.X + 18d, panelRect.Y + 58d));
    }

    private static FormattedText CreateFormattedText(string text, double fontSize, FontWeight weight, Color color)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyle.Normal, weight),
            fontSize,
            new SolidColorBrush(color));
    }

    private void UpdateStatus(string title, string detail, bool isError, bool visibleFrame)
    {
        if (statusTitle == title &&
            statusDetail == detail &&
            hasError == isError &&
            hasVisibleFrame == visibleFrame)
        {
            return;
        }

        statusTitle = title;
        statusDetail = detail;
        hasError = isError;
        hasVisibleFrame = visibleFrame;
        var notificationVersion = ++statusNotificationVersion;
        var eventArgs = new GraphCanvasStatusChangedEventArgs
        {
            Title = title,
            Detail = detail,
            IsError = isError,
            HasVisibleFrame = visibleFrame
        };

        Dispatcher.UIThread.Post(() =>
        {
            if (notificationVersion != statusNotificationVersion)
            {
                return;
            }

            StatusChanged?.Invoke(this, eventArgs);
        }, DispatcherPriority.Background);
    }

    private static void LogDebug(string message)
    {
        Trace.WriteLine($"[GraphCanvasControl] {message}");
    }
}

public sealed class ShellWindow : Window
{
    private const string BrandName = "Turkey Oak";
    private const double BottomStripHeight = 300d;
    private const double BottomStripMinHeight = 220d;
    private const double BottomStripMaxHeight = 360d;
    private const double BottomStripCollapsedHeight = 86d;
    private const double ExpandedCanvasPreviewHeight = 156d;

    private readonly WorkspaceViewModel viewModel;
    private bool allowConfirmedClose;
    private Grid? workspaceGrid;
    private Grid? standardWorkspaceHost;
    private Border? trafficTypeWorkspaceHost;
    private Control? trafficTypeWorkspaceFocusTarget;
    private Border? edgeEditorWorkspaceHost;
    private Control? edgeEditorWorkspaceFocusTarget;
    private Border? scenarioEditorWorkspaceHost;
    private Control? scenarioEditorWorkspaceFocusTarget;
    private Border? osmImportWorkspaceHost;
    private Border? toolRailHost;
    private Border? canvasHost;
    private Border? inspectorHost;
    private Border? dashboardStripHost;
    private Grid? dashboardStripContentGrid;
    private Control? dashboardStripBody;
    private Grid? overlayLayer;
    private Border? overlayBackdrop;
    private Border? fullNodeEditorDrawer;
    private DashboardLayoutState dashboardLayoutState = DashboardLayoutState.Normal;
    private DashboardLayoutState previousDashboardLayoutState = DashboardLayoutState.Normal;
    private ShellWorkspaceMode shellWorkspaceMode = ShellWorkspaceMode.Standard;
    private Action? refreshToolRailState;

    private enum DashboardLayoutState
    {
        Collapsed,
        Normal,
        Expanded
    }

    private enum ShellWorkspaceMode
    {
        Standard,
        TrafficTypes,
        ScenarioEditor,
        OsmImport
    }

    private enum UnsavedChangesChoice
    {
        Save,
        Discard,
        Cancel
    }

    private sealed class InverseBoolConverter : IValueConverter
    {
        public static InverseBoolConverter Instance { get; } = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is bool boolValue ? !boolValue : AvaloniaProperty.UnsetValue;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is bool boolValue ? !boolValue : AvaloniaProperty.UnsetValue;
    }

    public ShellWindow()
    {
        viewModel = new WorkspaceViewModel();
        DataContext = viewModel;
        Width = 1760;
        Height = 1100;
        MinWidth = 1180;
        MinHeight = 720;
        Background = new SolidColorBrush(AvaloniaDashboardTheme.AppBackground);
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        SystemDecorations = SystemDecorations.None;
        CanResize = true;
        WindowState = WindowState.FullScreen;
        Title = FormatBrandedWindowTitle(viewModel.WindowTitle);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.WindowTitle))
            {
                Title = FormatBrandedWindowTitle(viewModel.WindowTitle);
            }

            if (e.PropertyName == nameof(WorkspaceViewModel.IsEditingNode) && !viewModel.IsEditingNode)
            {
                CloseFullNodeEditor();
            }

            if (e.PropertyName == nameof(WorkspaceViewModel.CurrentWorkspaceMode))
            {
                UpdateShellWorkspaceMode();
                if (viewModel.IsEdgeEditorWorkspaceMode)
                {
                    FocusEdgeEditorWorkspaceEditor();
                }
                else if (viewModel.IsScenarioEditorWorkspaceMode)
                {
                    FocusScenarioEditorWorkspace();
                }
                else if (viewModel.IsOsmImportWorkspaceMode)
                {
                    osmImportWorkspaceHost?.BringIntoView();
                }
                else
                {
                    toolRailHost?.BringIntoView();
                }
            }
        };
        viewModel.AboutRequested += HandleAboutRequested;

        Closing += HandleWindowClosing;
        KeyDown += HandleShellWindowKeyDown;
        Content = BuildLayout(viewModel);
    }

    private Control BuildLayout(WorkspaceViewModel viewModel)
    {
        var dockRoot = new DockPanel
        {
            Margin = new Thickness(14),
            LastChildFill = true
        };

        var topBarContent = BuildTopBar(viewModel);
        var topBar = BuildDashboardPanel(topBarContent, includeHeader: false, padding: new Thickness(16, 12), radius: new CornerRadius(14));
        topBar.PointerPressed += HandleTopBarPointerPressed;
        DockPanel.SetDock(topBar, Dock.Top);
        dockRoot.Children.Add(topBar);

        workspaceGrid = new Grid
        {
            Margin = new Thickness(0, 12, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("240,*,460"),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(BottomStripHeight))
                {
                    MinHeight = BottomStripMinHeight,
                    MaxHeight = BottomStripMaxHeight
                }
            }
        };

        toolRailHost = (Border)BuildToolRail(viewModel);
        canvasHost = (Border)BuildCanvasArea(viewModel);
        inspectorHost = BuildCompactInspector(viewModel);
        dashboardStripHost = BuildDashboardStrip(viewModel);

        workspaceGrid.Children.Add(toolRailHost);
        workspaceGrid.Children.Add(canvasHost);
        workspaceGrid.Children.Add(inspectorHost);
        workspaceGrid.Children.Add(dashboardStripHost);

        standardWorkspaceHost = new Grid
        {
            Children =
            {
                workspaceGrid
            }
        };
        trafficTypeWorkspaceHost = BuildTrafficTypeWorkspace(viewModel);
        trafficTypeWorkspaceHost.IsVisible = false;
        edgeEditorWorkspaceHost = BuildEdgeEditorWorkspace(viewModel);
        edgeEditorWorkspaceHost.IsVisible = false;
        scenarioEditorWorkspaceHost = BuildScenarioEditorWorkspace(viewModel);
        scenarioEditorWorkspaceHost.IsVisible = false;
        osmImportWorkspaceHost = BuildOsmImportWorkspace(viewModel);
        osmImportWorkspaceHost.IsVisible = false;

        var contentRoot = new Grid
        {
            Children =
            {
                standardWorkspaceHost,
                trafficTypeWorkspaceHost,
                edgeEditorWorkspaceHost,
                scenarioEditorWorkspaceHost,
                osmImportWorkspaceHost
            }
        };

        dockRoot.Children.Remove(workspaceGrid);
        dockRoot.Children.Add(contentRoot);

        overlayLayer = new Grid
        {
            IsHitTestVisible = false
        };
        overlayBackdrop = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(150, 20, 18, 15)),
            IsVisible = false
        };
        overlayBackdrop.PointerPressed += (_, _) => CloseFullNodeEditor();
        overlayLayer.Children.Add(overlayBackdrop);
        fullNodeEditorDrawer = BuildFullNodeEditorDrawer(viewModel);
        overlayLayer.Children.Add(fullNodeEditorDrawer);

        var root = new Grid();
        root.Children.Add(dockRoot);
        root.Children.Add(overlayLayer);
        UpdateDashboardLayout();
        UpdateShellWorkspaceMode();
        return root;
    }

    private static Border BuildDashboardPanel(Control content, string? header = null, bool includeHeader = true, Thickness? padding = null, CornerRadius? radius = null)
    {
        var layout = new Grid
        {
            RowDefinitions = includeHeader && !string.IsNullOrWhiteSpace(header)
                ? new RowDefinitions("Auto,*")
                : new RowDefinitions("*"),
            RowSpacing = includeHeader && !string.IsNullOrWhiteSpace(header) ? 8 : 0
        };

        if (includeHeader && !string.IsNullOrWhiteSpace(header))
        {
            layout.Children.Add(new Border
            {
                Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
                BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 8),
                Child = new TextBlock
                {
                    Text = header,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
                }
            });
        }

        if (includeHeader && !string.IsNullOrWhiteSpace(header))
        {
            Grid.SetRow(content, 1);
        }

        layout.Children.Add(content);

        return new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            CornerRadius = radius ?? AvaloniaDashboardTheme.PanelCornerRadius,
            Padding = padding ?? new Thickness(12),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            Child = layout
        };
    }

    private Control BuildTopBar(WorkspaceViewModel viewModel)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,*,Auto"),
            ColumnSpacing = 18
        };

        var titleStack = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                BuildBrandBadge(),
                new TextBlock
                {
                    Text = "MedW Command Workstation",
                    FontSize = 24,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                },
                new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding(nameof(WorkspaceViewModel.SessionSubtitle)),
                    FontSize = 13,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
        grid.Children.Add(titleStack);

        var centerStatus = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorderStrong),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 6),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = new Binding(nameof(WorkspaceViewModel.StatusText)),
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
                        FontWeight = FontWeight.Medium
                    },
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = new Binding(nameof(WorkspaceViewModel.ToolStatusText)),
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.MutedText),
                        FontSize = 12
                    }
                }
            }
        };
        Grid.SetColumn(centerStatus, 1);
        grid.Children.Add(centerStatus);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                BuildButton("New", new RelayCommand(() => _ = CreateBlankNetworkAsync(viewModel)), toolTip: "Create a blank network."),
                BuildButton("Open", new RelayCommand(() => _ = OpenNetworkFileAsync(viewModel)), toolTip: "Open a network JSON file."),
                BuildButton("Save", new RelayCommand(() => _ = SaveNetworkAsync(viewModel)), toolTip: "Save the current network JSON."),
                BuildButton("Import", new RelayCommand(() => _ = ImportGraphMlAsync(viewModel)), toolTip: "Import GraphML into the current workspace."),
                BuildButton("Import from OpenStreetMap", viewModel.StartOsmAreaSelectionCommand, toolTip: "Open a full-screen map workspace and drag-select an OSM area."),
                BuildButton("Export", new RelayCommand(() => _ = ExportGraphMlAsync(viewModel)), toolTip: "Export the active network as GraphML."),
                BuildButton("Run", viewModel.SimulateCommand, isPrimary: true, toolTip: "Run the simulation timeline."),
                BuildButton("Step", viewModel.StepCommand, isPrimary: true, toolTip: "Advance the simulation by one period."),
                BuildButton("Reset", viewModel.ResetTimelineCommand, toolTip: "Reset timeline to period 0."),
                BuildButton("Fit", viewModel.FitCommand, toolTip: "Fit the graph to the viewport."),
                BuildButton("About", viewModel.OpenAboutCommand, toolTip: "Show version and product details."),
                BuildButton("Exit", new RelayCommand(() => _ = CloseWithConfirmationAsync()), toolTip: "Close the workstation.")
            }
        };
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return grid;
    }

    private Control BuildToolRail(WorkspaceViewModel viewModel)
    {
        var selectButton = BuildToolButton("Select", "Click to select items, drag selected nodes, and marquee select.", viewModel.SelectToolCommand);
        var addNodeButton = BuildToolButton("Add Node", "Click the canvas to place a new node.", viewModel.AddNodeToolCommand);
        var connectButton = BuildToolButton("Connect", "Choose a source node, then a target node to create a route.", viewModel.ConnectToolCommand);
        var trafficTypesButton = BuildToolButton("Traffic Types", "Edit traffic types used by nodes and routes", new RelayCommand(EnterTrafficTypeWorkspace));
        var scenariosButton = BuildToolButton("Scenarios", "Open the full scenario workspace.", viewModel.OpenScenarioEditorCommand);
        var isochroneButton = BuildToolButton("Isochrone Mode", "Click a node and enter a minute threshold to highlight reachable nodes.", viewModel.ToggleIsochroneModeCommand);
        var facilityButton = BuildToolButton("Facility Planning", "Select multiple facilities and run a shared budget analysis.", viewModel.ToggleFacilityPlanningModeCommand);
        var deleteButton = BuildToolButton("Delete", "Delete the current selection.", viewModel.DeleteSelectionCommand);

        void RefreshToolState()
        {
            ApplyToolButtonState(selectButton, viewModel.IsSelectToolActive);
            ApplyToolButtonState(addNodeButton, viewModel.IsAddNodeToolActive);
            ApplyToolButtonState(connectButton, viewModel.IsConnectToolActive);
            ApplyToolButtonState(trafficTypesButton, shellWorkspaceMode == ShellWorkspaceMode.TrafficTypes);
            ApplyToolButtonState(scenariosButton, shellWorkspaceMode == ShellWorkspaceMode.ScenarioEditor || viewModel.IsScenarioEditorWorkspaceMode);
            ApplyToolButtonState(isochroneButton, viewModel.IsIsochroneModeEnabled);
            ApplyToolButtonState(facilityButton, viewModel.IsFacilityPlanningMode);
        }
        refreshToolRailState = RefreshToolState;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(WorkspaceViewModel.IsSelectToolActive) or nameof(WorkspaceViewModel.IsAddNodeToolActive) or nameof(WorkspaceViewModel.IsConnectToolActive) or nameof(WorkspaceViewModel.IsIsochroneModeEnabled) or nameof(WorkspaceViewModel.IsFacilityPlanningMode))
            {
                RefreshToolState();
            }
        };
        RefreshToolState();

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = AvaloniaDashboardTheme.SectionSpacing,
            MinHeight = 0
        };

        content.Children.Add(new StackPanel
        {
            Spacing = AvaloniaDashboardTheme.SectionSpacing,
            Children =
            {
                BuildSectionTitle("Tools", "Activate one operation mode for the workspace."),
                selectButton,
                addNodeButton,
                connectButton,
                trafficTypesButton,
                scenariosButton,
                isochroneButton,
                facilityButton,
                deleteButton
            }
        });

        var quickAccess = new StackPanel
        {
            Spacing = AvaloniaDashboardTheme.SectionSpacing,
            Children =
            {
                BuildSectionTitle("Quick Access", "Current network at a glance."),
                BuildQuickStat("Status", nameof(WorkspaceViewModel.StatusText)),
                BuildQuickStat("Selection", nameof(WorkspaceViewModel.SelectionSummary)),
                BuildQuickStat("Simulation", nameof(WorkspaceViewModel.SimulationSummary)),
                BuildQuickStat("Isochrone", nameof(WorkspaceViewModel.IsochroneLegendTitle)),
                BuildQuickStat("Band A", nameof(WorkspaceViewModel.IsochroneLegendStrongLabel)),
                BuildQuickStat("Band B", nameof(WorkspaceViewModel.IsochroneLegendMediumLabel)),
                BuildQuickStat("Band C", nameof(WorkspaceViewModel.IsochroneLegendLightLabel)),
                BuildQuickStat("Facilities", nameof(WorkspaceViewModel.FacilitySelectionSummary))
            }
        };
        Grid.SetRow(quickAccess, 1);
        content.Children.Add(quickAccess);

        var facilityPlanningPanel = BuildFacilityPlanningPanel(viewModel);
        Grid.SetRow(facilityPlanningPanel, 2);
        content.Children.Add(facilityPlanningPanel);

        var border = BuildDashboardPanel(
            content,
            header: "Operations Rail",
            padding: new Thickness(10),
            radius: new CornerRadius(14));
        border.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(border, 0);
        Grid.SetRow(border, 0);
        return border;
    }

    private Control BuildCanvasArea(WorkspaceViewModel viewModel)
    {
        var graphModeButton = BuildBoundButton("Graph", nameof(WorkspaceViewModel.ShowGraphModeCommand));
        graphModeButton.Classes.Add("toolbar-button");
        graphModeButton.Focusable = true;
        var sankeyModeButton = BuildBoundButton("Sankey", nameof(WorkspaceViewModel.ShowSankeyModeCommand));
        sankeyModeButton.Classes.Add("toolbar-button");
        sankeyModeButton.Focusable = true;
        var mapModeButton = BuildBoundButton("Map", nameof(WorkspaceViewModel.ShowMapModeCommand));
        mapModeButton.Classes.Add("toolbar-button");
        mapModeButton.Focusable = true;

        void RefreshModeButtons()
        {
            ApplyToolButtonState(graphModeButton, viewModel.IsGraphMode);
            ApplyToolButtonState(sankeyModeButton, viewModel.IsSankeyMode);
            ApplyToolButtonState(mapModeButton, viewModel.IsMapMode);
        }

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(WorkspaceViewModel.IsGraphMode) or nameof(WorkspaceViewModel.IsSankeyMode) or nameof(WorkspaceViewModel.IsMapMode))
            {
                RefreshModeButtons();
            }
        };
        RefreshModeButtons();

        var modeSelector = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { graphModeButton, sankeyModeButton, mapModeButton }
        };

        var sankeyOptions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            IsVisible = viewModel.IsSankeyMode,
            Children =
            {
                BuildLabeledComboBox("Traffic type", nameof(WorkspaceViewModel.TrafficTypeNameOptions), "VisualisationState.ActiveTrafficTypeFilter"),
                new CheckBox { Content = "Collapse minor flows", [!ToggleButton.IsCheckedProperty] = new Binding("VisualisationState.CollapseMinorFlows", BindingMode.TwoWay) },
                new CheckBox { Content = "Show unmet demand", [!ToggleButton.IsCheckedProperty] = new Binding("VisualisationState.ShowUnmetDemand", BindingMode.TwoWay) }
            }
        };

        var mapOptions = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
            IsVisible = viewModel.IsMapMode,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        new CheckBox { Content = "Show map background", [!ToggleButton.IsCheckedProperty] = new Binding("VisualisationState.ShowMapBackground", BindingMode.TwoWay) },
                        new CheckBox { Content = "Show capacity utilisation", [!ToggleButton.IsCheckedProperty] = new Binding("VisualisationState.ShowCapacityUtilisation", BindingMode.TwoWay) },
                        new CheckBox { Content = "Show unmet demand", [!ToggleButton.IsCheckedProperty] = new Binding("VisualisationState.ShowUnmetDemand", BindingMode.TwoWay) },
                        BuildToggleButton("Select area", viewModel.ToggleOsmAreaSelectionCommand, "Drag to select an area up to 2 square degrees."),
                        BuildButton("Import selected area", viewModel.ImportOsmSelectionCommand, toolTip: "Download and import the selected OpenStreetMap area."),
                        BuildButton("Clear selection", viewModel.ClearOsmSelectionCommand, toolTip: "Clear the selected OSM area."),
                        BuildButton("Fit to network", viewModel.FitMapToNetworkCommand, toolTip: "Fit the map camera to imported network nodes.")
                    }
                },
                BuildOsmDownloadPanel(viewModel)
            }
        };

        var lockLayoutToggle = new CheckBox
        {
            Content = "Lock layout to map",
            [!ToggleButton.IsCheckedProperty] = new Binding(nameof(WorkspaceViewModel.LockLayoutToMap), BindingMode.TwoWay),
            [!InputElement.IsEnabledProperty] = new Binding(nameof(WorkspaceViewModel.IsLockLayoutToMapEnabled))
        };
        ApplyFocusVisual(lockLayoutToggle);
        ToolTip.SetTip(lockLayoutToggle, "Keep node positions tied to their map coordinates while panning or zooming.");

        var lockLayoutHint = new TextBlock
        {
            Text = "Middle mouse pans. Mouse wheel zooms toward pointer focus.",
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
            FontSize = 12
        };
        var lockLayoutDisabledHint = new TextBlock
        {
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
            FontSize = 12
        };
        lockLayoutDisabledHint.Bind(TextBlock.TextProperty, new Binding(nameof(WorkspaceViewModel.LockLayoutToMapDisabledReason)));
        lockLayoutDisabledHint.Bind(IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.IsLockLayoutToMapEnabled))
        {
            Converter = new FuncValueConverter<bool, bool>(enabled => !enabled)
        });

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(WorkspaceViewModel.IsSankeyMode) or nameof(WorkspaceViewModel.IsMapMode))
            {
                sankeyOptions.IsVisible = viewModel.IsSankeyMode;
                mapOptions.IsVisible = viewModel.IsMapMode;
            }
        };

        var header = new Border
        {
            Padding = new Thickness(12, 8),
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Network Topology",
                        FontSize = 16,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = new Binding(nameof(WorkspaceViewModel.ActiveModeLabel)),
                        FontSize = 12,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = new Binding(nameof(WorkspaceViewModel.ToolStatusText)),
                        FontSize = 12,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };

        var graphCanvas = new GraphCanvasControl
        {
            ViewModel = viewModel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        ToolTip.SetTip(graphCanvas, "Middle mouse drag pans the canvas. Mouse wheel zooms to the pointer focus.");

        var fallbackTitle = new TextBlock
        {
            Text = "Canvas placeholder",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.Danger)
        };
        var fallbackDetail = new TextBlock
        {
            Text = "The graph could not be displayed. Reopen the file or reset the view.",
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
            TextWrapping = TextWrapping.Wrap
        };
        var fallbackPanel = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.Danger),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20),
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsVisible = false,
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6,
                Children =
                {
                    fallbackTitle,
                    fallbackDetail
                }
            }
        };

        graphCanvas.StatusChanged += (_, args) =>
        {
            fallbackTitle.Text = args.IsError ? "Graph canvas failed to render" : args.Title;
            fallbackDetail.Text = args.Detail;
            fallbackPanel.IsVisible = args.IsError;
        };
        graphCanvas.FullNodeEditorRequested += (_, _) => OpenFullNodeEditor();

        var canvasSurface = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Children =
            {
                header
            }
        };
        var controlsStrip = new StackPanel
        {
            Margin = new Thickness(2, 6, 2, 8),
            Spacing = 6,
            Children =
            {
                modeSelector,
                lockLayoutToggle,
                lockLayoutHint,
                lockLayoutDisabledHint,
                sankeyOptions,
                mapOptions
            }
        };
        Grid.SetRow(controlsStrip, 1);
        canvasSurface.Children.Add(controlsStrip);
        Grid.SetRow(graphCanvas, 2);
        Grid.SetRow(fallbackPanel, 2);
        canvasSurface.Children.Add(graphCanvas);
        canvasSurface.Children.Add(fallbackPanel);
        var canvasHost = BuildDashboardPanel(
            canvasSurface,
            header: "Workspace",
            padding: new Thickness(10),
            radius: new CornerRadius(18));
        canvasHost.MinHeight = 520;
        canvasHost.MinWidth = 760;

        Grid.SetColumn(canvasHost, 1);
        Grid.SetRow(canvasHost, 0);
        return canvasHost;
    }

    private Control BuildFacilityPlanningPanel(WorkspaceViewModel viewModel)
    {
        var budgetInput = BuildBoundTextBox(nameof(WorkspaceViewModel.IsochroneBudget), "Default max time");
        var addSelectedCommand = new RelayCommand(
            () => _ = AddSelectedFacilityOriginWithPromptAsync(viewModel),
            () => viewModel.AddFacilityOriginCommand.CanExecute(null));
        viewModel.AddFacilityOriginCommand.CanExecuteChanged += (_, _) => addSelectedCommand.NotifyCanExecuteChanged();

        var facilitiesList = new ListBox
        {
            MinHeight = 86,
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(WorkspaceViewModel.SelectedFacilityNodes)),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding(nameof(WorkspaceViewModel.SelectedFacilityNodeItem), BindingMode.TwoWay),
            ItemTemplate = new FuncDataTemplate<FacilityOriginItem>((item, _) =>
            {
                if (item is null)
                {
                    return new TextBlock();
                }

                var maxTimeInput = BuildTextBox("Max time");
                maxTimeInput.MinHeight = 32;
                maxTimeInput.Width = 98;
                maxTimeInput.Padding = new Thickness(8, 5);
                maxTimeInput.Bind(TextBox.TextProperty, new Binding(nameof(FacilityOriginItem.MaxTravelTimeText), BindingMode.TwoWay) { Source = item });

                var name = new TextBlock
                {
                    Text = item.DisplayName,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                };

                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 8,
                    Children =
                    {
                        name,
                        maxTimeInput
                    }
                };
                Grid.SetColumn(maxTimeInput, 1);
                return row;
            })
        };
        AttachFocusBorder(facilitiesList);

        var summaryGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 4,
            Children =
            {
                BuildReadOnlyRow("Reachable nodes", nameof(WorkspaceViewModel.ReachableNodeCountText)),
                BuildReadOnlyRow("Uncovered nodes", nameof(WorkspaceViewModel.UncoveredNodeCountText)),
                BuildReadOnlyRow("Overlap", nameof(WorkspaceViewModel.OverlapNodeCountText)),
                BuildReadOnlyRow("Coverage percentage", nameof(WorkspaceViewModel.CoveragePercentageText)),
                BuildReadOnlyRow("Average best travel time/cost", nameof(WorkspaceViewModel.AverageBestCostText))
            }
        };
        Grid.SetColumn(summaryGrid.Children[1], 1);
        Grid.SetRow(summaryGrid.Children[2], 1);
        Grid.SetRow(summaryGrid.Children[3], 1);
        Grid.SetColumn(summaryGrid.Children[3], 1);
        Grid.SetRow(summaryGrid.Children[4], 2);
        Grid.SetColumnSpan(summaryGrid.Children[4], 2);

        var uncoveredList = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(WorkspaceViewModel.UncoveredPlanningItems)),
            ItemTemplate = new FuncDataTemplate<UncoveredNodePlanningItem>((item, _) => new TextBlock
            {
                Text = item is null ? string.Empty : $"{item.NodeName} | Nearest: {item.NearestFacility} | Extra time: {item.ExtraBudgetNeeded}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
            })
        };

        var comparisonList = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(WorkspaceViewModel.FacilityComparisonRows)),
            ItemTemplate = new FuncDataTemplate<FacilityComparisonRowViewModel>((item, _) => new TextBlock
            {
                Text = item is null ? string.Empty : $"{item.Facility} | Covered {item.NodesCovered} | Unique {item.UniqueNodesCovered} | Avg {item.AverageCost} | Max {item.MaxCost}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
            })
        };

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                BuildSectionTitle("Facility planning", "Facilities, per-origin max time, and coverage."),
                BuildReadOnlyRow("Facilities", nameof(WorkspaceViewModel.FacilitySelectionCountText)),
                BuildLabeledRow("Default max time", budgetInput),
                facilitiesList,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        BuildButton("Run analysis", viewModel.RunMultiOriginIsochroneCommand, isPrimary: true),
                        BuildButton("Add selected", addSelectedCommand),
                        BuildButton("Remove selected", viewModel.RemoveFacilityOriginCommand),
                        BuildButton("Clear", viewModel.ClearFacilityOriginsCommand)
                    }
                },
                BuildReadOnlyRow("Message", nameof(WorkspaceViewModel.FacilityPlanningValidationText)),
                summaryGrid,
                BuildSectionTitle("Facility comparison", "Facility | Nodes covered | Unique nodes covered | Average cost | Max cost"),
                comparisonList,
                BuildSectionTitle("Uncovered nodes", "Nodes outside current budget coverage."),
                uncoveredList
            }
        };
    }

    private Control BuildOsmDownloadPanel(WorkspaceViewModel viewModel)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*,Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 5
        };

        AddCoordinate("West", nameof(WorkspaceViewModel.OsmWestText), 0);
        AddCoordinate("South", nameof(WorkspaceViewModel.OsmSouthText), 1);
        AddCoordinate("East", nameof(WorkspaceViewModel.OsmEastText), 2);
        AddCoordinate("North", nameof(WorkspaceViewModel.OsmNorthText), 3);

        var nodeSelector = new ComboBox
        {
            MinWidth = 92,
            ItemsSource = viewModel.OsmNodeImportPercentagePresets,
            [!SelectingItemsControl.SelectedItemProperty] = new Binding(nameof(WorkspaceViewModel.OsmNodeImportPercentage), BindingMode.TwoWay)
        };
        ToolTip.SetTip(nodeSelector, "Percentage of reducible OSM shape nodes to retain. Junctions, dead ends, and connectivity nodes are always kept.");
        var nodePanel = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = "Nodes to import", Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText), FontSize = 11 },
                nodeSelector
            }
        };
        Grid.SetColumn(nodePanel, 4);
        grid.Children.Add(nodePanel);

        var metrics = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(WorkspaceViewModel.OsmSelectedAreaText)), Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText), TextWrapping = TextWrapping.Wrap },
                new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(WorkspaceViewModel.OsmTileCountText)), Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText), TextWrapping = TextWrapping.Wrap },
                new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(WorkspaceViewModel.OsmValidationMessage)), Foreground = new SolidColorBrush(AvaloniaDashboardTheme.Warning), TextWrapping = TextWrapping.Wrap }
            }
        };
        Grid.SetColumn(metrics, 5);
        grid.Children.Add(metrics);

        return grid;

        void AddCoordinate(string label, string bindingPath, int column)
        {
            var input = BuildTextBox(label);
            input.MinWidth = 96;
            input.Bind(TextBox.TextProperty, new Binding(bindingPath, BindingMode.TwoWay));
            var panel = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock { Text = label, Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText), FontSize = 11 },
                    input
                }
            };
            Grid.SetColumn(panel, column);
            grid.Children.Add(panel);
        }
    }

    private ToggleButton BuildToggleButton(string text, ICommand command, string toolTip)
    {
        var button = new ToggleButton
        {
            Content = text,
            Command = command,
            Padding = new Thickness(12, 7),
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ToolbarButtonBackground),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
        };
        ToolTip.SetTip(button, toolTip);
        return button;
    }

    private async Task AddSelectedFacilityOriginWithPromptAsync(WorkspaceViewModel viewModel)
    {
        var nodeId = viewModel.Scene.Selection.SelectedNodeIds.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return;
        }

        if (viewModel.IsFacilityOriginSelected(nodeId))
        {
            viewModel.ToggleFacilityOriginById(nodeId);
            viewModel.NotifyVisualChanged();
            return;
        }

        var maxTravelTime = await FacilityPlanningDialogs.PromptMaxTravelTimeAsync(
            this,
            viewModel.GetFacilityNodeDisplayName(nodeId),
            viewModel.IsochroneBudget);
        if (!maxTravelTime.HasValue)
        {
            return;
        }

        if (viewModel.ToggleFacilityOriginById(nodeId, maxTravelTime.Value))
        {
            viewModel.NotifyVisualChanged();
        }
    }

    private async Task ShowBulkApplyTrafficRoleDialogAsync(WorkspaceViewModel viewModel)
    {
        if (!viewModel.HasAnyNodes)
        {
            viewModel.StatusText = "Add at least one node before using bulk traffic role apply.";
            return;
        }

        var selectedNodeCount = viewModel.Scene.Selection.SelectedNodeIds.Count;
        var applyToAllNodes = selectedNodeCount == 0;
        var trafficType = viewModel.TrafficTypeNameOptions.FirstOrDefault() ?? string.Empty;
        var role = viewModel.NodeRoleOptions.FirstOrDefault() ?? "Transshipment";

        var dialog = new Window
        {
            Width = 460,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.None,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome,
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            Title = "Bulk Apply Traffic Role"
        };

        var applyToAllCheck = new CheckBox
        {
            Content = "Apply to all nodes",
            IsChecked = applyToAllNodes
        };
        ToolTip.SetTip(applyToAllCheck, "Apply this role to every node in the network, not just selected nodes.");
        applyToAllCheck.IsCheckedChanged += (_, _) => applyToAllNodes = applyToAllCheck.IsChecked == true;

        var trafficTypeBox = new ComboBox
        {
            ItemsSource = viewModel.TrafficTypeNameOptions,
            SelectedItem = trafficType,
            MinWidth = 220
        };
        trafficTypeBox.SelectionChanged += (_, _) => trafficType = trafficTypeBox.SelectedItem?.ToString() ?? string.Empty;

        var roleBox = new ComboBox
        {
            ItemsSource = viewModel.NodeRoleOptions,
            SelectedItem = role,
            MinWidth = 220
        };
        roleBox.SelectionChanged += (_, _) => role = roleBox.SelectedItem?.ToString() ?? string.Empty;

        var statusText = new TextBlock
        {
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
            TextWrapping = TextWrapping.Wrap,
            Text = selectedNodeCount == 0
                ? "No nodes are selected. Apply to all nodes is enabled by default."
                : $"{selectedNodeCount} node(s) selected."
        };

        var applyButton = new Button
        {
            Content = "Apply",
            Background = new SolidColorBrush(AvaloniaDashboardTheme.Accent),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            Padding = new Thickness(14, 8)
        };

        applyButton.Click += async (_, _) =>
        {
            if (applyToAllNodes && viewModel.WouldBulkApplyTrafficRoleOverwrite(true, trafficType))
            {
                var confirmed = await ShowConfirmationDialogAsync(
                    "Apply role to all nodes?",
                    "This will overwrite existing node traffic roles.",
                    "Apply",
                    isDestructive: false);
                if (!confirmed)
                {
                    return;
                }
            }

            if (!viewModel.TryBulkApplyTrafficRole(role, trafficType, applyToAllNodes, out var message))
            {
                statusText.Text = message;
                statusText.Foreground = new SolidColorBrush(AvaloniaDashboardTheme.Danger);
                return;
            }

            dialog.Close();
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ToolbarButtonBackground),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            Padding = new Thickness(14, 8)
        };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorderStrong),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Bulk Apply Traffic Role",
                        FontSize = 20,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                    },
                    BuildLabeledRow("Traffic type", trafficTypeBox),
                    BuildLabeledRow("Role", roleBox),
                    applyToAllCheck,
                    statusText,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { applyButton, cancelButton }
                    }
                }
            }
        };

        await dialog.ShowDialog(this);
    }

    private Border BuildCompactInspector(WorkspaceViewModel viewModel)
    {
        var insightsToggle = new CheckBox
        {
            Content = "Insights",
            [!ToggleButton.IsCheckedProperty] = new Binding("VisualisationState.ShowInsights", BindingMode.TwoWay)
        };
        var insightsPanel = BuildInsightsPanel(viewModel);
        insightsPanel.IsVisible = viewModel.VisualisationState.ShowInsights;
        viewModel.VisualisationState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VisualisationState.ShowInsights))
            {
                insightsPanel.IsVisible = viewModel.VisualisationState.ShowInsights;
            }
        };

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    BuildHeadlineBlock(),
                    insightsToggle,
                    insightsPanel,
                    BuildInspectorDetails(),
                    BuildValidationBlock(nameof(WorkspaceViewModel.InspectorValidationText)),
                    BuildCompactNodeInspector(viewModel),
                    BuildCompactEdgeInspector(viewModel),
                    BuildCompactBulkInspector(viewModel),
                    BuildCompactNetworkSummary()
                }
            }
        };

        var border = BuildDashboardPanel(scrollViewer, header: "Intelligence Rail", padding: new Thickness(14));
        border.HorizontalAlignment = HorizontalAlignment.Stretch;
        border.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetColumn(border, 2);
        Grid.SetRow(border, 0);
        return border;
    }

    private static Control BuildInsightsPanel(WorkspaceViewModel viewModel)
    {
        var list = new ListBox
        {
            MinHeight = 120
        };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.NetworkInsights)));
        list.ItemTemplate = new FuncDataTemplate<NetworkInsight>((item, _) =>
        {
            if (item is null)
            {
                return new TextBlock();
            }

            var severityIcon = item.Severity switch
            {
                InsightSeverity.Critical => "⛔",
                InsightSeverity.Warning => "⚠",
                _ => "ℹ"
            };
            var target = !string.IsNullOrWhiteSpace(item.TargetNodeId) ? $"Node {item.TargetNodeId}" :
                !string.IsNullOrWhiteSpace(item.TargetEdgeId) ? $"Route {item.TargetEdgeId}" : "Network";
            var recommendation = item.Recommendations.FirstOrDefault()?.Action ?? "Review this insight.";

            var jumpButton = new Button
            {
                Content = "Jump to target",
                Focusable = true,
                Command = new RelayCommand(() => viewModel.SelectInsight(item))
            };

            return new Border
            {
                BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Child = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock { Text = $"{severityIcon} {item.Severity}", FontWeight = FontWeight.Bold },
                        new TextBlock { Text = item.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = item.Summary, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = $"Target: {target}", Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText) },
                        new TextBlock { Text = $"Top recommendation: {recommendation}", TextWrapping = TextWrapping.Wrap },
                        jumpButton
                    }
                }
            };
        });

        var emptyState = new TextBlock
        {
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
            TextWrapping = TextWrapping.Wrap
        };
        void UpdateEmptyState()
        {
            if (!string.IsNullOrWhiteSpace(viewModel.InsightsEmptyStateText))
            {
                emptyState.Text = viewModel.InsightsEmptyStateText;
            }
            else
            {
                emptyState.Text = viewModel.NetworkInsights.Count == 0 ? "No major issues found." : string.Empty;
            }
        }

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.InsightsEmptyStateText))
            {
                UpdateEmptyState();
            }
        };
        viewModel.NetworkInsights.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Insights", FontWeight = FontWeight.Bold },
                list,
                emptyState
            }
        };
    }

    private Border BuildOsmImportWorkspace(WorkspaceViewModel viewModel)
    {
        var mapCanvas = new GraphCanvasControl
        {
            ViewModel = viewModel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var rightPanel = BuildDashboardPanel(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    BuildSectionTitle("Import from OpenStreetMap", "Pan and zoom the map, then drag to select an area."),
                    BuildReadOnlyRow("West", nameof(WorkspaceViewModel.OsmWestText)),
                    BuildReadOnlyRow("South", nameof(WorkspaceViewModel.OsmSouthText)),
                    BuildReadOnlyRow("East", nameof(WorkspaceViewModel.OsmEastText)),
                    BuildReadOnlyRow("North", nameof(WorkspaceViewModel.OsmNorthText)),
                    BuildReadOnlyRow("Selected area", nameof(WorkspaceViewModel.OsmSelectedAreaText)),
                    BuildReadOnlyRow("Tile estimate", nameof(WorkspaceViewModel.OsmTileCountText)),
                    BuildReadOnlyRow("Validation", nameof(WorkspaceViewModel.OsmValidationMessage)),
                    BuildLabeledComboBox("Nodes to import", nameof(WorkspaceViewModel.OsmNodeImportPercentagePresets), nameof(WorkspaceViewModel.OsmNodeImportPercentage)),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            BuildButton("Import selected area", viewModel.ImportOsmSelectionCommand, isPrimary: true),
                            BuildButton("Clear selection", viewModel.ClearOsmSelectionCommand),
                            BuildButton("Cancel", viewModel.CancelOsmImportCommand)
                        }
                    }
                }
            },
            header: "OSM Area Import",
            padding: new Thickness(12),
            radius: new CornerRadius(14));
        rightPanel.Width = 390;

        var workspace = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Children =
            {
                mapCanvas,
                rightPanel
            }
        };
        Grid.SetColumn(rightPanel, 1);

        return new Border
        {
            Child = workspace
        };
    }

    private Border BuildTrafficTypeWorkspace(WorkspaceViewModel viewModel)
    {
        Control editorFocusTarget;
        var navigator = BuildTrafficTypeList(maxHeight: double.PositiveInfinity, onOpenEditor: FocusTrafficTypeWorkspaceEditor);
        trafficTypeWorkspaceFocusTarget = navigator;
        Grid? leftGrid = null;

        var leftColumn = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Child = leftGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 10,
                MinHeight = 0,
                Children =
                {
                    BuildSectionTitle("Traffic Types", "Browse and select the traffic type you want to edit."),
                    navigator,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            BuildBoundButton("Remove type", nameof(WorkspaceViewModel.RemoveSelectedTrafficDefinitionCommand))
                        }
                    }
                }
            }
        };
        Grid.SetRow(leftGrid.Children[1], 1);
        Grid.SetRow(leftGrid.Children[2], 2);

        Grid? editorGrid = null;
        var editorColumn = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = editorGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                RowSpacing = 12,
                MinHeight = 0,
                Children =
                {
                    BuildTrafficTypeIdentityEditors(out editorFocusTarget),
                    BuildTrafficTypeTabbedEditor()
                }
            }
        };
        Grid.SetRow(editorGrid.Children[1], 1);
        trafficTypeWorkspaceFocusTarget = editorFocusTarget;

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            Children =
            {
                new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Traffic Type Workspace",
                            FontSize = 18,
                            FontWeight = FontWeight.Bold,
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                        },
                        new TextBlock
                        {
                            Text = "Create, review, and edit traffic types in one place.",
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            }
        };

        var addTypeButton = BuildButton("Add type", new RelayCommand(() =>
        {
            viewModel.AddTrafficDefinitionCommand.Execute(null);
            FocusTrafficTypeWorkspaceEditor();
        }));
        var applyTypeButton = BuildBoundButton("Apply traffic type", nameof(WorkspaceViewModel.ApplyTrafficDefinitionCommand));
        var cancelButton = BuildButton("Back to Network", new RelayCommand(ExitTrafficTypeWorkspace));
        var doneButton = BuildButton("Done", new RelayCommand(ExitTrafficTypeWorkspace), isPrimary: true);
        Grid.SetColumn(addTypeButton, 1);
        Grid.SetColumn(applyTypeButton, 2);
        Grid.SetColumn(cancelButton, 3);
        Grid.SetColumn(doneButton, 4);
        header.Children.Add(addTypeButton);
        header.Children.Add(applyTypeButton);
        header.Children.Add(cancelButton);
        header.Children.Add(doneButton);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("320,16,*"),
            MinHeight = 0,
            Children =
            {
                leftColumn,
                editorColumn
            }
        };
        Grid.SetColumn(editorColumn, 2);

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 12,
            MinHeight = 0,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                header,
                body
            }
        };
        Grid.SetRow(layout.Children[1], 1);

        return BuildDashboardPanel(
            layout,
            header: "Traffic Workspace",
            padding: new Thickness(14),
            radius: new CornerRadius(18));
    }

    private Border BuildScenarioEditorWorkspace(WorkspaceViewModel viewModel)
    {
        var heading = new TextBlock
        {
            Text = "Scenario Workspace",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Focusable = true,
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
        };
        scenarioEditorWorkspaceFocusTarget = heading;

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            Children =
            {
                new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        heading,
                        new TextBlock
                        {
                            Text = "Create, edit, and validate scenario runs without crowding the dashboard strip.",
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            }
        };

        var saveButton = BuildBoundButton("Save scenario", "ScenarioEditor.SaveScenarioCommand");
        saveButton.Classes.Add("primary");
        var cancelButton = BuildButton("Back to Network", new RelayCommand(() => _ = CloseScenarioEditorWithConfirmationAsync()), toolTip: "Return to the network workspace.");

        Grid.SetColumn(saveButton, 1);
        Grid.SetColumn(cancelButton, 2);
        header.Children.Add(saveButton);
        header.Children.Add(cancelButton);

        // LEFT PANE: Scenario List
        var scenarioList = new ListBox { MinHeight = 220 };
        scenarioList.Bind(ItemsControl.ItemsSourceProperty, new Binding("ScenarioEditor.ScenarioDefinitions"));
        scenarioList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding("ScenarioEditor.SelectedScenarioDefinition", BindingMode.TwoWay));
        scenarioList.ItemTemplate = new FuncDataTemplate<ScenarioDefinitionModel>((item, _) =>
        {
            //Add null check 
            if (item is null)
            {
                return new TextBlock();
            }
            return new StackPanel
            {
                Spacing = 3,
                Margin = new Thickness(0, 3),
                Children =
                {
                    new TextBlock { Text = string.IsNullOrWhiteSpace(item.Name) ? "(unnamed scenario)" : item.Name, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = $"{item.Events.Count} event(s) | {item.StartTime:0.##} to {item.EndTime:0.##}", FontSize = 11, Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText) }
                }
            };
        });
        ApplyFocusVisual(scenarioList);

        var emptyState = new TextBlock
        {
            Text = "No scenarios yet. Create a scenario to test closures, demand spikes, or routing changes.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
        };
        emptyState.Bind(IsVisibleProperty, new Binding("ScenarioEditor.HasScenarios") { Converter = InverseBoolConverter.Instance });

        var leftPane = BuildScenarioEditorCard("Scenarios", "Select or create a scenario.",
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    BuildBoundButton("Create scenario", "ScenarioEditor.CreateScenarioCommand"),
                    emptyState,
                    scenarioList,
                    BuildDestructiveButton("Delete scenario", new RelayCommand(() => _ = ConfirmDeleteScenarioAsync())),
                    BuildQuickStat("Run result", nameof(WorkspaceViewModel.ScenarioResultSummary))
                }
            });

        // TAB 1: SCENARIO OVERVIEW
        var adaptiveRoutingCheckBox = BuildLabeledCheckBox("Adaptive routing", "ScenarioEditor.EnableAdaptiveRouting");
        adaptiveRoutingCheckBox.Margin = new Thickness(0, 20, 0, 0);

        var detailsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 12,
            RowSpacing = 10,
            Children =
            {
                BuildValidatedScenarioInput("Scenario name", "ScenarioEditor.NameText", "ScenarioEditor.ScenarioNameError"),
                BuildValidatedScenarioInput("Description", "ScenarioEditor.DescriptionText", string.Empty),
                BuildValidatedScenarioInput("Start time", "ScenarioEditor.StartTimeText", "ScenarioEditor.ScenarioStartTimeError"),
                BuildValidatedScenarioInput("End time", "ScenarioEditor.EndTimeText", "ScenarioEditor.ScenarioEndTimeError"),
                BuildValidatedScenarioInput("Step size", "ScenarioEditor.DeltaTimeText", "ScenarioEditor.ScenarioDeltaTimeError"),
                adaptiveRoutingCheckBox
            }
        };
        Grid.SetColumn(detailsGrid.Children[1], 1);
        Grid.SetRow(detailsGrid.Children[2], 1);
        Grid.SetRow(detailsGrid.Children[3], 1);
        Grid.SetColumn(detailsGrid.Children[3], 1);
        Grid.SetRow(detailsGrid.Children[4], 2);
        Grid.SetRow(detailsGrid.Children[5], 2);
        Grid.SetColumn(detailsGrid.Children[5], 1);

        var overviewTab = new TabItem
        {
            Header = "Overview & Settings",
            Content = new ScrollViewer
            {
                Content = BuildScenarioEditorCard("Scenario Configuration", "Configure the core timeline and rules for this scenario.", detailsGrid),
                Padding = new Thickness(0, 10, 0, 0)
            }
        };

        // TAB 2: EVENT SCHEDULE (Side-by-Side Master/Detail)
        var eventList = new ListBox { MinHeight = 180 };
        eventList.Bind(ItemsControl.ItemsSourceProperty, new Binding("ScenarioEditor.EventItems"));
        eventList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding("ScenarioEditor.SelectedEventItem", BindingMode.TwoWay));
        eventList.ItemTemplate = new FuncDataTemplate<ScenarioEventListItem>((item, _) =>
        {
            if (item is null)
            {
                return new TextBlock();
            }

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("1.5*,1.1*,1*,1*"), ColumnSpacing = 8, Margin = new Thickness(0, 4) };
            var name = new TextBlock { Text = item.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
            var target = new TextBlock { Text = item.TargetText, TextWrapping = TextWrapping.Wrap };
            var timing = new TextBlock { Text = item.TimingText, TextWrapping = TextWrapping.Wrap };
            var value = new TextBlock { Text = item.ValueStatusText, TextWrapping = TextWrapping.Wrap };

            Grid.SetColumn(target, 1);
            Grid.SetColumn(timing, 2);
            Grid.SetColumn(value, 3);

            row.Children.Add(name);
            row.Children.Add(target);
            row.Children.Add(timing);
            row.Children.Add(value);
            return row;
        });
        ApplyFocusVisual(eventList);

        var eventActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                BuildBoundButton("Add event", "ScenarioEditor.AddScenarioEventCommand"),
                BuildBoundButton("Duplicate event", "ScenarioEditor.DuplicateScenarioEventCommand"),
                BuildBoundButton("Delete event", "ScenarioEditor.DeleteScenarioEventCommand")
            }
        };

        var targetPicker = BuildLabeledComboBox("Target", "ScenarioEditor.TargetIdOptions", "ScenarioEditor.EventTargetIdText");
        targetPicker.Bind(IsVisibleProperty, new Binding("ScenarioEditor.HasSelectedEvent"));
        var trafficPicker = BuildLabeledComboBox("Traffic type", "ScenarioEditor.TrafficTypeOptions", "ScenarioEditor.EventTrafficTypeText");
        trafficPicker.Bind(IsVisibleProperty, new Binding("ScenarioEditor.EventUsesTrafficType"));
        var valueInput = BuildValidatedScenarioInput("Value", "ScenarioEditor.EventValueText", "ScenarioEditor.EventValueError");
        valueInput.Bind(IsVisibleProperty, new Binding("ScenarioEditor.EventUsesValue"));

        var eventStartTimeInput = BuildValidatedScenarioInput("Start time", "ScenarioEditor.EventStartTimeText", "ScenarioEditor.EventStartTimeError");
        var eventEndTimeInput = BuildValidatedScenarioInput("End time", "ScenarioEditor.EventEndTimeText", "ScenarioEditor.EventEndTimeError");
        Grid.SetColumn(eventEndTimeInput, 1);

        var eventTimingGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8,
            Children =
            {
                eventStartTimeInput,
                eventEndTimeInput
            }
        };

        var eventDetailsContent = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                BuildValidatedScenarioInput("Event name", "ScenarioEditor.EventNameText", "ScenarioEditor.EventNameError"),
                BuildLabeledComboBox("Event type", "ScenarioEditor.ScenarioEventKindOptions", "ScenarioEditor.EventKind"),
                targetPicker,
                trafficPicker,
                eventTimingGrid,
                valueInput,
                BuildValidatedScenarioInput("Notes", "ScenarioEditor.EventNotesText", string.Empty),
                BuildLabeledCheckBox("Event is enabled", "ScenarioEditor.EventIsEnabled"),
                BuildValidationBlock("ScenarioEditor.ValidationSummary")
            }
        };

        var eventListCard = BuildScenarioEditorCard("Event List", "Timeline of occurrences.", new StackPanel { Spacing = 10, Children = { eventActions, eventList } });
        var eventPropertiesCard = BuildScenarioEditorCard("Event Properties", "Edit the selected event.", new ScrollViewer { Content = eventDetailsContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(eventPropertiesCard, 1);

        var eventsTabBody = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.5*,1*"),
            ColumnSpacing = 16,
            Margin = new Thickness(0, 10, 0, 0),
            Children =
            {
                eventListCard,
                eventPropertiesCard
            }
        };

        var eventsTab = new TabItem { Header = "Event Schedule", Content = eventsTabBody };

        // Combine into TabControl
        var rightPaneTabs = new TabControl
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            Margin = new Thickness(0),
            Padding = new Thickness(0)
        };
        rightPaneTabs.Items.Add(overviewTab);
        rightPaneTabs.Items.Add(eventsTab);

        // MAIN BODY GRID
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("320,16,*"),
            MinHeight = 0,
            Children = { leftPane, rightPaneTabs }
        };
        Grid.SetColumn(rightPaneTabs, 2);

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 16,
            MinHeight = 0,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { header, body }
        };
        Grid.SetRow(body, 1);

        return BuildDashboardPanel(layout, header: "Scenario Workspace", padding: new Thickness(14), radius: new CornerRadius(18));
    }

    private Border BuildEdgeEditorWorkspace(WorkspaceViewModel viewModel)
    {
        var routeTypeEditor = BuildAutoCompleteTextBox("Type or choose a route type", "EdgeDraft.RouteTypeSuggestions");
        routeTypeEditor.Bind(AutoCompleteTextBox.TextProperty, new Binding("EdgeDraft.RouteTypeText", BindingMode.TwoWay));
        routeTypeEditor.Bind(AutoCompleteTextBox.SubmitCommandProperty, new Binding(nameof(WorkspaceViewModel.ApplyInspectorCommand)));
        edgeEditorWorkspaceFocusTarget = routeTypeEditor;

        var timeEditor = BuildValidatedTextBox(
            "Travel time",
            "EdgeDraft.TimeText",
            nameof(WorkspaceViewModel.EdgeTimeValidationText));
        var costEditor = BuildValidatedTextBox(
            "Cost",
            "EdgeDraft.CostText",
            nameof(WorkspaceViewModel.EdgeCostValidationText));
        var capacityEditor = BuildValidatedTextBox(
            "Capacity",
            "EdgeDraft.CapacityText",
            nameof(WorkspaceViewModel.EdgeCapacityValidationText));

        var saveButton = BuildButton("Save Route", new RelayCommand(() => { }), isPrimary: true, toolTip: "Save this route and return to the workspace.");
        saveButton.Bind(Button.CommandProperty, new Binding(nameof(WorkspaceViewModel.SaveEdgeEditorCommand)));

        var cancelButton = BuildButton("Cancel", new RelayCommand(() => { }), toolTip: "Discard in-progress route changes and return to the workspace.");
        cancelButton.Bind(Button.CommandProperty, new Binding(nameof(WorkspaceViewModel.CancelEdgeEditorCommand)));

        var deleteButton = BuildDestructiveButton("Delete Route", new RelayCommand(() => _ = ConfirmDeleteRouteAsync()), toolTip: "Delete the selected route after confirmation.");

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            Children =
            {
                new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Edit Route",
                            FontSize = 20,
                            FontWeight = FontWeight.Bold,
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                        },
                        new TextBlock
                        {
                            Text = "Update route properties, direction, and traffic permissions.",
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            }
        };
        Grid.SetColumn(saveButton, 1);
        Grid.SetColumn(cancelButton, 2);
        Grid.SetColumn(deleteButton, 3);
        header.Children.Add(saveButton);
        header.Children.Add(cancelButton);
        header.Children.Add(deleteButton);

        var leftColumn = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                BuildEditorSection(
                    "Route Basics",
                    "Core route identity and travel values.",
                    BuildReadOnlyRow("Route id", nameof(WorkspaceViewModel.SelectedEdgeIdText)),
                    BuildReadOnlyRow("Source node", nameof(WorkspaceViewModel.SelectedEdgeSourceNodeText)),
                    BuildReadOnlyRow("Target node", nameof(WorkspaceViewModel.SelectedEdgeTargetNodeText)),
                    BuildLabeledRow("Route type", routeTypeEditor),
                    timeEditor,
                    costEditor),
                BuildEditorSection(
                    "Direction and Capacity",
                    "Set directionality and any route-wide throughput limit.",
                    BuildLabeledCheckBox("Bidirectional", "EdgeDraft.IsBidirectional"),
                    capacityEditor,
                    BuildReadOnlyRow("Direction", nameof(WorkspaceViewModel.SelectedEdgeDirectionSummaryText))),
                BuildEditorSection(
                    "Traffic Permissions",
                    "Review each traffic type and override the network default where needed.",
                    BuildReadOnlyRow("Rule status", nameof(WorkspaceViewModel.SelectedEdgeRuleCountText)),
                    BuildEdgePermissionRulesEditor(viewModel)),
                BuildEditorSection(
                    "Review",
                    "Check validation and workspace context before saving.",
                    BuildReadOnlyRow("Validation", nameof(WorkspaceViewModel.SelectedEdgeValidationStatusText)),
                    BuildValidationBlock(nameof(WorkspaceViewModel.EdgeEditorValidationText)),
                    BuildQuickStat("Selection", nameof(WorkspaceViewModel.SelectionSummary)),
                    BuildQuickStat("Status", nameof(WorkspaceViewModel.StatusText)),
                    BuildQuickStat("Simulation", nameof(WorkspaceViewModel.SimulationSummary)))
            }
        };

        var rightColumn = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                BuildEdgeSummaryPanel(),
                BuildEditorSection(
                    "Live Preview",
                    "This summary updates as you edit the route.",
                    BuildReadOnlyRow("Route", nameof(WorkspaceViewModel.SelectedEdgePreviewTitleText)),
                    BuildReadOnlyRow("Travel", nameof(WorkspaceViewModel.SelectedEdgePreviewTravelText)),
                    BuildReadOnlyRow("Capacity", nameof(WorkspaceViewModel.SelectedEdgePreviewCapacityText)),
                    BuildReadOnlyRow("Direction", nameof(WorkspaceViewModel.SelectedEdgeDirectionSummaryText)),
                    BuildReadOnlyRow("Rules", nameof(WorkspaceViewModel.SelectedEdgeRuleCountText)),
                    BuildReadOnlyRow("Status", nameof(WorkspaceViewModel.SelectedEdgeValidationStatusText)))
            }
        };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.85*,16,1.15*"),
            MinHeight = 0,
            Children =
            {
                leftColumn,
                rightColumn
            }
        };
        Grid.SetColumn(rightColumn, 2);

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 0,
            Content = body
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 12,
            MinHeight = 0,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                header,
                scrollViewer
            }
        };
        Grid.SetRow(scrollViewer, 1);

        return BuildDashboardPanel(
            layout,
            header: "Route Workspace",
            padding: new Thickness(14),
            radius: new CornerRadius(18));
    }

    private static Control BuildInspectorDetails()
    {
        return new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding("Inspector.Details"),
            ItemTemplate = new FuncDataTemplate<string>((item, _) =>
                new TextBlock
                {
                    Text = item,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                    Margin = new Thickness(0, 0, 0, 4),
                    TextWrapping = TextWrapping.Wrap
                })
        };
    }

    private Control BuildCompactNodeInspector(WorkspaceViewModel viewModel)
    {
        var openFullEditorButton = BuildButton("Open full editor", new RelayCommand(OpenFullNodeEditor), isPrimary: true, toolTip: "Open the full node editor drawer.");
        openFullEditorButton.Bind(IsEnabledProperty, new Binding(nameof(WorkspaceViewModel.IsEditingNode)));

        var card = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius,
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    BuildSectionTitle("Quick Edit", "Keep fast access to the common node fields here, then open the full editor for schedules, recipes, and diagnostics."),
                    BuildQuickEditLabeledTextBox("Name", "NodeDraft.NodeNameText"),
                    BuildQuickEditLabeledAutoCompleteTextBox("Place type", "NodeDraft.PlaceTypeText", "NodeDraft.PlaceTypeSuggestions", "Type or choose a place type"),
                    BuildQuickEditLabeledTextBox("Transhipment capacity", "NodeDraft.TranshipmentCapacityText"),
                    BuildQuickEditLabeledComboBox("Node shape", nameof(WorkspaceViewModel.NodeShapeOptions), "NodeDraft.Shape"),
                    BuildValidationBlock(nameof(WorkspaceViewModel.InspectorValidationText)),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            openFullEditorButton,
                            BuildBoundButton("Apply quick changes", nameof(WorkspaceViewModel.ApplyInspectorCommand))
                        }
                    }
                }
            }
        };
        card.Bind(IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.IsEditingNode)));
        return card;
    }

    private static Control BuildCompactEdgeInspector(WorkspaceViewModel viewModel)
    {
        var openButton = BuildButton("Edit route", new RelayCommand(() => { }), isPrimary: true, toolTip: "Open the selected route in the dedicated route workspace.");
        openButton.Bind(Button.CommandProperty, new Binding(nameof(WorkspaceViewModel.OpenSelectedEdgeEditorCommand)));
        var applyQuickChangesButton = BuildBoundButton("Apply quick changes", nameof(WorkspaceViewModel.ApplyInspectorCommand));

        var card = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius,
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    BuildSectionTitle("Route Summary", "Keep the route label in sync here, or open the full route workspace for detailed access rules."),
                    BuildReadOnlyRow("Route id", nameof(WorkspaceViewModel.SelectedEdgeIdText)),
                    BuildReadOnlyRow("Source", nameof(WorkspaceViewModel.SelectedEdgeSourceNodeText)),
                    BuildReadOnlyRow("Target", nameof(WorkspaceViewModel.SelectedEdgeTargetNodeText)),
                    BuildQuickEditLabeledAutoCompleteTextBox("Route type", "EdgeDraft.RouteTypeText", "EdgeDraft.RouteTypeSuggestions", "Type or choose a route type"),
                    BuildReadOnlyRow("Direction", nameof(WorkspaceViewModel.SelectedEdgeDirectionSummaryText)),
                    BuildReadOnlyRow("Rules", nameof(WorkspaceViewModel.SelectedEdgeRuleCountText)),
                    BuildValidationBlock(nameof(WorkspaceViewModel.InspectorValidationText)),
                    BuildValidationBlock(nameof(WorkspaceViewModel.EdgeEditorValidationText)),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            applyQuickChangesButton,
                            openButton
                        }
                    }
                }
            }
        };
        card.Bind(IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.IsEditingEdge)));
        return card;
    }

    private Control BuildCompactBulkInspector(WorkspaceViewModel viewModel)
    {
        var bulkApplyRoleCommand = new RelayCommand(
            () => _ = ShowBulkApplyTrafficRoleDialogAsync(viewModel),
            () => viewModel.HasAnyNodes);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.HasAnyNodes))
            {
                bulkApplyRoleCommand.NotifyCanExecuteChanged();
            }
        };

        var card = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius,
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    BuildSectionTitle("Bulk Edit", "Shared values for multi-node selections."),
                    BuildLabeledAutoCompleteTextBox("Place type", "BulkDraft.PlaceTypeText", "BulkDraft.PlaceTypeSuggestions", "Type or choose a place type"),
                    BuildLabeledTextBox("Transhipment capacity", "BulkDraft.TranshipmentCapacityText"),
                    BuildValidationBlock(nameof(WorkspaceViewModel.InspectorValidationText)),
                    BuildBoundButton("Apply bulk changes", nameof(WorkspaceViewModel.ApplyInspectorCommand)),
                    BuildButton("Bulk Apply Traffic Role", bulkApplyRoleCommand, toolTip: "Apply one traffic role to selected nodes or to all nodes.")
                }
            }
        };
        card.Bind(IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.IsEditingSelection)));
        return card;
    }

    private static Control BuildCompactNetworkSummary()
    {
        var card = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius,
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    BuildSectionTitle("Network", "No node or route is selected."),
                    BuildQuickStat("Status", nameof(WorkspaceViewModel.StatusText)),
                    BuildQuickStat("Selection", nameof(WorkspaceViewModel.SelectionSummary)),
                    BuildQuickStat("Simulation", nameof(WorkspaceViewModel.SimulationSummary))
                }
            }
        };
        card.Bind(IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.IsEditingNetwork)));
        return card;
    }

    private Border BuildDashboardStrip(WorkspaceViewModel viewModel)
    {
        var collapseButton = BuildButton("Collapse", new RelayCommand(ToggleDashboardCollapsed), toolTip: "Collapse the dashboard strip.");
        var expandButton = BuildButton("Expand reports", new RelayCommand(ToggleDashboardExpanded), isPrimary: true, toolTip: "Expand the reports into the main workspace.");
        var restoreHint = new TextBlock
        {
            Text = "Esc restores the previous workspace layout.",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
        };

        dashboardStripContentGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10,
            MinHeight = 0
        };

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 8
        };
        headerGrid.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = "Dashboard Strip",
                    FontSize = 16,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                },
                new TextBlock
                {
                    Text = "Reports, exports, and playback stay in reach as the layout changes.",
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        });
        Grid.SetColumn(restoreHint, 1);
        Grid.SetColumn(collapseButton, 2);
        Grid.SetColumn(expandButton, 3);
        headerGrid.Children.Add(restoreHint);
        headerGrid.Children.Add(collapseButton);
        headerGrid.Children.Add(expandButton);
        dashboardStripContentGrid.Children.Add(headerGrid);

        dashboardStripBody = BuildDashboardStripTabs(viewModel);
        var body = dashboardStripBody;
        Grid.SetRow(body, 1);
        dashboardStripContentGrid.Children.Add(body);

        var strip = BuildDashboardPanel(dashboardStripContentGrid, includeHeader: false, padding: new Thickness(14, 12), radius: new CornerRadius(14));
        strip.Margin = new Thickness(12, 10, 0, 0);
        Grid.SetColumn(strip, 1);
        Grid.SetColumnSpan(strip, 2);
        Grid.SetRow(strip, 1);
        return strip;
    }

    private Border BuildFullNodeEditorDrawer(WorkspaceViewModel viewModel)
    {
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Children =
            {
                new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Full Node Editor",
                            FontSize = 20,
                            FontWeight = FontWeight.Bold,
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                        },
                        new TextBlock
                        {
                            Text = "All node attributes stay within the viewport. Only the editor body scrolls.",
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            }
        };
        var closeButton = BuildButton("Close", new RelayCommand(CloseFullNodeEditor));
        Grid.SetColumn(closeButton, 1);
        header.Children.Add(closeButton);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "Tab through sections, then press Esc to dismiss the drawer.",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                    FontSize = 12
                }
            }
        };
        var applyButton = BuildBoundButton("Apply node changes", nameof(WorkspaceViewModel.ApplyInspectorCommand));
        var closeFooterButton = BuildButton("Done", new RelayCommand(CloseFullNodeEditor), isPrimary: true);
        Grid.SetColumn(applyButton, 1);
        Grid.SetColumn(closeFooterButton, 2);
        footer.Children.Add(applyButton);
        footer.Children.Add(closeFooterButton);

        var bodyScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = BuildFullNodeEditorBody(viewModel)
        };
        Grid.SetRow(bodyScroll, 1);
        Grid.SetRow(footer, 2);

        var drawer = new Border
        {
            Width = 640,
            MaxWidth = 720,
            MinWidth = 460,
            Margin = new Thickness(0),
            Padding = new Thickness(18),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorderStrong),
            BorderThickness = new Thickness(1, 0, 0, 0),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 28,
                Color = AvaloniaDashboardTheme.PanelBorderStrong,
                OffsetX = -8,
                OffsetY = 0,
                Spread = 0
            }),
            Focusable = true,
            IsVisible = false,
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 12,
                Children =
                {
                    header,
                    bodyScroll,
                    footer
                }
            }
        };
        return drawer;
    }

    private Control BuildFullNodeEditorBody(WorkspaceViewModel viewModel)
    {
        var roleSelector = BuildComboBox();
        roleSelector.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.SelectedNodeTrafficProfiles)));
        roleSelector.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(WorkspaceViewModel.SelectedNodeTrafficProfileItem), BindingMode.TwoWay));

        return new StackPanel
        {
            Spacing = 14,
            Children =
            {
                BuildEditorSection(
                    "General",
                    "Identity, placement, and worldbuilding context.",
                    BuildLabeledTextBox("Node id", "NodeDraft.NodeIdText"),
                    BuildLabeledTextBox("Name", "NodeDraft.NodeNameText"),
                    BuildCoordinateEditors(),
                    BuildLabeledAutoCompleteTextBox("Place type", "NodeDraft.PlaceTypeText", "NodeDraft.PlaceTypeSuggestions", "Type or choose a place type", nameof(WorkspaceViewModel.ApplyInspectorCommand)),
                    BuildLabeledTextBox("Description", "NodeDraft.DescriptionText"),
                    BuildLabeledTextBox("Controlling actor", "NodeDraft.ControllingActorText"),
                    BuildLabeledTextBox("Tags", "NodeDraft.TagsText"),
                    BuildReadOnlyRow("Template id", "NodeDraft.TemplateIdText")),
                BuildEditorSection(
                    "Capabilities",
                    "Node shape, relay capacity, and child-network exposure.",
                    BuildLabeledTextBox("Transhipment capacity", "NodeDraft.TranshipmentCapacityText"),
                    BuildLabeledComboBox("Node shape", nameof(WorkspaceViewModel.NodeShapeOptions), "NodeDraft.Shape"),
                    BuildLabeledComboBox("Node kind", nameof(WorkspaceViewModel.NodeKindOptions), "NodeDraft.NodeKind"),
                    BuildLabeledAutoCompleteTextBox("Child network", "NodeDraft.ReferencedSubnetworkIdText", nameof(WorkspaceViewModel.SubnetworkIdSuggestions), "Type or choose a child network"),
                    BuildLabeledCheckBox("External-facing interface", "NodeDraft.IsExternalInterface"),
                    BuildLabeledAutoCompleteTextBox("Interface name", "NodeDraft.InterfaceNameText", nameof(WorkspaceViewModel.InterfaceNameSuggestions), "Type or choose an interface name")),
                BuildEditorSection(
                    "Traffic roles",
                    "Manage the selected role and its core quantities.",
                    BuildLabeledRow("Selected role", roleSelector),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            BuildBoundButton("Add role", nameof(WorkspaceViewModel.AddNodeTrafficProfileCommand)),
                            BuildBoundButton("Duplicate role", nameof(WorkspaceViewModel.DuplicateSelectedNodeTrafficProfileCommand)),
                            BuildBoundButton("Delete role", nameof(WorkspaceViewModel.RemoveSelectedNodeTrafficProfileCommand))
                        }
                    },
                    BuildLabeledRow("Traffic type", BuildBoundComboBox(nameof(WorkspaceViewModel.TrafficTypeOptions), nameof(WorkspaceViewModel.SelectedTrafficType))),
                    BuildLabeledRow("Role", BuildBoundComboBox(nameof(WorkspaceViewModel.NodeRoleOptions), nameof(WorkspaceViewModel.NodeTrafficRoleText))),
                    BuildLabeledTextBox("Production", nameof(WorkspaceViewModel.NodeProductionText)),
                    BuildLabeledTextBox("Consumption", nameof(WorkspaceViewModel.NodeConsumptionText)),
                    BuildLabeledTextBox("Consumer premium", nameof(WorkspaceViewModel.NodeConsumerPremiumText)),
                    BuildLabeledCheckBox("Can transship", nameof(WorkspaceViewModel.NodeCanTransship)),
                    BuildLabeledCheckBox("Store enabled", nameof(WorkspaceViewModel.NodeStoreEnabled)),
                    BuildLabeledTextBox("Store capacity", nameof(WorkspaceViewModel.NodeStoreCapacityText), nameof(WorkspaceViewModel.IsNodeStoreCapacityEnabled))),
                BuildEditorSection(
                    "Schedules",
                    "Legacy start/end fields plus explicit production and consumption windows.",
                    BuildScheduleFields(),
                    BuildWindowEditor(
                        "Production windows",
                        nameof(WorkspaceViewModel.SelectedNodeProductionWindows),
                        nameof(WorkspaceViewModel.SelectedNodeProductionWindowItem),
                        nameof(WorkspaceViewModel.AddNodeProductionWindowCommand),
                        nameof(WorkspaceViewModel.RemoveSelectedNodeProductionWindowCommand)),
                    BuildWindowEditor(
                        "Consumption windows",
                        nameof(WorkspaceViewModel.SelectedNodeConsumptionWindows),
                        nameof(WorkspaceViewModel.SelectedNodeConsumptionWindowItem),
                        nameof(WorkspaceViewModel.AddNodeConsumptionWindowCommand),
                        nameof(WorkspaceViewModel.RemoveSelectedNodeConsumptionWindowCommand))),
                BuildEditorSection(
                    "Transformation / local inputs",
                    "Define local precursor recipes for the active traffic role.",
                    BuildInputRequirementEditor(viewModel)),
                BuildEditorSection(
                    "Diagnostics",
                    "Validation and live workspace context.",
                    BuildValidationBlock(nameof(WorkspaceViewModel.NodeTrafficRoleValidationText)),
                    BuildQuickStat("Selection", nameof(WorkspaceViewModel.SelectionSummary)),
                    BuildQuickStat("Status", nameof(WorkspaceViewModel.StatusText)),
                    BuildQuickStat("Simulation", nameof(WorkspaceViewModel.SimulationSummary)))
            }
        };
    }

    private static Control BuildEditorSection(string title, string summary, params Control[] content)
    {
        var stack = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                BuildSectionTitle(title, summary)
            }
        };

        foreach (var control in content)
        {
            stack.Children.Add(control);
        }

        return new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Child = stack
        };
    }

    private static Control BuildCoordinateEditors()
    {
        var xEditor = BuildLabeledTextBox("X", "NodeDraft.NodeXText");
        var yEditor = BuildLabeledTextBox("Y", "NodeDraft.NodeYText");
        Grid.SetColumn(yEditor, 2);
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
            Children =
            {
                xEditor,
                yEditor
            }
        };
    }

    private static ComboBox BuildBoundComboBox(string itemsPropertyName, string selectedPropertyName)
    {
        var comboBox = BuildComboBox();
        comboBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(itemsPropertyName));
        comboBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(selectedPropertyName, BindingMode.TwoWay));
        return comboBox;
    }

    private static Control BuildScheduleFields()
    {
        var prodStart = BuildLabeledTextBox("Prod start", nameof(WorkspaceViewModel.NodeProductionStartText));
        var prodEnd = BuildLabeledTextBox("Prod end", nameof(WorkspaceViewModel.NodeProductionEndText));
        var consStart = BuildLabeledTextBox("Cons start", nameof(WorkspaceViewModel.NodeConsumptionStartText));
        var consEnd = BuildLabeledTextBox("Cons end", nameof(WorkspaceViewModel.NodeConsumptionEndText));
        Grid.SetColumn(prodEnd, 2);
        Grid.SetRow(consStart, 2);
        Grid.SetRow(consEnd, 2);
        Grid.SetColumn(consEnd, 2);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
            RowDefinitions = new RowDefinitions("Auto,10,Auto")
        };
        grid.Children.Add(prodStart);
        grid.Children.Add(prodEnd);
        grid.Children.Add(consStart);
        grid.Children.Add(consEnd);
        return grid;
    }

    private static Control BuildWindowEditor(string title, string itemsPropertyName, string selectedPropertyName, string addCommandPropertyName, string removeCommandPropertyName)
    {
        var listBox = new ListBox
        {
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(AvaloniaDashboardTheme.InputBackground),
            ItemTemplate = new FuncDataTemplate<PeriodWindowEditorRow>((row, _) =>
            {
                var start = BuildTextBox("Start");
                start.Bind(TextBox.TextProperty, new Binding(nameof(PeriodWindowEditorRow.StartText), BindingMode.TwoWay));
                var end = BuildTextBox("End");
                end.Bind(TextBox.TextProperty, new Binding(nameof(PeriodWindowEditorRow.EndText), BindingMode.TwoWay));
                var startRow = BuildLabeledRow("Start", start);
                var endRow = BuildLabeledRow("End", end);
                Grid.SetColumn(endRow, 2);

                return new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,8,*"),
                    Margin = new Thickness(0, 0, 0, 8),
                    Children =
                    {
                        startRow,
                        endRow
                    }
                };
            })
        };
        listBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(itemsPropertyName));
        listBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(selectedPropertyName, BindingMode.TwoWay));
        ApplyFocusVisual(listBox);

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                },
                listBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        BuildBoundButton("Add window", addCommandPropertyName),
                        BuildBoundButton("Remove selected", removeCommandPropertyName)
                    }
                }
            }
        };
    }

    private static Control BuildInputRequirementEditor(WorkspaceViewModel viewModel)
    {
        var listBox = new ListBox
        {
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(AvaloniaDashboardTheme.InputBackground),
            ItemTemplate = new FuncDataTemplate<InputRequirementEditorRow>((row, _) =>
            {
                var traffic = BuildAutoCompleteTextBox("Traffic", nameof(WorkspaceViewModel.TrafficTypeNameOptions));
                traffic.Bind(AutoCompleteTextBox.TextProperty, new Binding(nameof(InputRequirementEditorRow.TrafficType), BindingMode.TwoWay));
                traffic.Bind(AutoCompleteTextBox.SuggestionsProperty, new Binding(nameof(WorkspaceViewModel.TrafficTypeNameOptions)) { Source = viewModel });
                var input = BuildTextBox("Input");
                input.Bind(TextBox.TextProperty, new Binding(nameof(InputRequirementEditorRow.InputQuantityText), BindingMode.TwoWay));
                var output = BuildTextBox("Output");
                output.Bind(TextBox.TextProperty, new Binding(nameof(InputRequirementEditorRow.OutputQuantityText), BindingMode.TwoWay));
                var trafficRow = BuildLabeledRow("Traffic type", traffic);
                var inputRow = BuildLabeledRow("Input", input);
                var outputRow = BuildLabeledRow("Output", output);
                Grid.SetColumn(inputRow, 2);
                Grid.SetColumn(outputRow, 4);

                return new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("2*,8,*,8,*"),
                    Margin = new Thickness(0, 0, 0, 8),
                    Children =
                    {
                        trafficRow,
                        inputRow,
                        outputRow
                    }
                };
            })
        };
        listBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.SelectedNodeInputRequirements)));
        listBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(WorkspaceViewModel.SelectedNodeInputRequirementItem), BindingMode.TwoWay));
        ApplyFocusVisual(listBox);

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                listBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        BuildBoundButton("Add input", nameof(WorkspaceViewModel.AddNodeInputRequirementCommand)),
                        BuildBoundButton("Remove selected", nameof(WorkspaceViewModel.RemoveSelectedNodeInputRequirementCommand))
                    }
                }
            }
        };
    }

    private static Control BuildReadOnlyRow(string label, string propertyName)
    {
        var value = new TextBlock
        {
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            TextWrapping = TextWrapping.Wrap
        };
        value.Bind(TextBlock.TextProperty, new Binding(propertyName));
        return BuildLabeledRow(label, value);
    }

    private Control BuildDashboardStripTabs(WorkspaceViewModel viewModel)
    {
        var metrics = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(WorkspaceViewModel.ReportMetrics)),
            ItemTemplate = new FuncDataTemplate<ReportMetricViewModel>((metric, _) => BuildButton($"{metric.Label}  {metric.Value}", new RelayCommand(metric.Activate), toolTip: $"Open {metric.Label} report details."))
        };

        var playbackGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*"),
            RowDefinitions = new RowDefinitions("Auto")
        };
        playbackGrid.Children.Add(BuildButton("Run", viewModel.SimulateCommand, isPrimary: true, toolTip: "Run timeline simulation."));
        playbackGrid.Children.Add(BuildButton("Step", viewModel.StepCommand, 1, isPrimary: true, toolTip: "Advance one period."));
        playbackGrid.Children.Add(BuildButton("Reset", viewModel.ResetTimelineCommand, 2, toolTip: "Reset timeline to start."));
        playbackGrid.Children.Add(BuildButton("Fit", viewModel.FitCommand, 3, toolTip: "Fit graph on canvas."));

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 12,
            Margin = new Thickness(12, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        slider.Bind(RangeBase.ValueProperty, new Binding(nameof(WorkspaceViewModel.TimelinePosition), BindingMode.TwoWay));
        Grid.SetColumn(slider, 4);
        ApplyFocusVisual(slider);
        playbackGrid.Children.Add(slider);

        var tabControl = new TabControl
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelBackground),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinHeight = 0
        };
        tabControl.Items.Add(new TabItem
        {
            Header = "Playback",
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = playbackGrid
            }
        });

        var reportsGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10,
            MinHeight = 0
        };

        var reportsHeader = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 10,
            MinHeight = 0
        };
        reportsHeader.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                BuildButton("Export Current (HTML)", new RelayCommand(() => _ = ExportCurrentReportAsync(viewModel, ReportExportFormat.Html))),
                BuildButton("Export Timeline (CSV)", new RelayCommand(() => _ = ExportTimelineReportAsync(viewModel, ReportExportFormat.Csv)))
            }
        });
        var metricsScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = metrics
        };
        Grid.SetRow(metricsScroll, 1);
        reportsHeader.Children.Add(metricsScroll);
        reportsGrid.Children.Add(reportsHeader);

        var reportsScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinHeight = 0,
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    BuildTrafficReportSection(),
                    BuildReportSection<RouteReportRowViewModel>(
                        "Route Summary",
                        new[]
                        {
                            ("Route", 1.0),
                            ("From -> to", 1.5),
                            ("Flow", 1.0),
                            ("Capacity", 0.9),
                            ("Utilisation", 0.9),
                            ("Pressure", 1.2)
                        },
                        nameof(WorkspaceViewModel.RouteReports),
                        static row => new[]
                        {
                            row.RouteId,
                            row.FromTo,
                            row.CurrentFlow,
                            row.Capacity,
                            row.Utilisation,
                            row.Pressure
                        }),
                    BuildReportSection<NodePressureReportRowViewModel>(
                        "Node Pressure Summary",
                        new[]
                        {
                            ("Node", 1.3),
                            ("Pressure", 0.8),
                            ("Top cause", 1.2),
                            ("Unmet need", 1.0)
                        },
                        nameof(WorkspaceViewModel.NodePressureReports),
                        static row => new[]
                        {
                            row.Node,
                            row.PressureScore,
                            row.TopCause,
                            row.UnmetNeed
                        })
                }
            }
        };
        Grid.SetRow(reportsScroll, 1);
        reportsGrid.Children.Add(reportsScroll);
        tabControl.Items.Add(new TabItem
        {
            Header = "Reports",
            Content = reportsGrid
        });
        tabControl.Items.Add(new TabItem
        {
            Header = "Status",
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        BuildQuickStat("Status", nameof(WorkspaceViewModel.StatusText)),
                        BuildQuickStat("Selection", nameof(WorkspaceViewModel.SelectionSummary)),
                        BuildQuickStat("Simulation", nameof(WorkspaceViewModel.SimulationSummary))
                    }
                }
            }
        });
        tabControl.Items.Add(new TabItem
        {
            Header = "Layers",
            Content = BuildLayersPanel(viewModel)
        });
        tabControl.Items.Add(new TabItem
        {
            Header = "Scenarios",
            Content = BuildScenarioPanel(viewModel)
        });
        tabControl.Items.Add(new TabItem
        {
            Header = "Top Issues",
            Content = BuildTopIssuesPanel(viewModel)
        });
        tabControl.Items.Add(new TabItem
        {
            Header = "Explanation",
            Content = BuildExplanationPanel()
        });

        return tabControl;
    }

    private static Control BuildLayersPanel(WorkspaceViewModel viewModel)
    {
        var list = new ListBox { MinHeight = 180 };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.LayerItems)));
        list.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(WorkspaceViewModel.SelectedLayerItem), BindingMode.TwoWay));
        list.ItemTemplate = new FuncDataTemplate<LayerListItemViewModel>((item, _) =>
        {
            // Add the early exit for null items
            if (item is null)
            {
                return new TextBlock();
            }

            return new StackPanel
            {
                Spacing = 4,
                Children =
        {
            new TextBlock { Text = $"{item.Name} ({item.TypeLabel})", FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = $"Nodes {item.NodeCount} · Edges {item.EdgeCount}", FontSize = 11, Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText) },
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new CheckBox
                    {
                        Content = "Visible",
                        [!ToggleButton.IsCheckedProperty] = new Binding(nameof(LayerListItemViewModel.IsVisible), BindingMode.TwoWay)
                    },
                    new CheckBox
                    {
                        Content = "Locked",
                        [!ToggleButton.IsCheckedProperty] = new Binding(nameof(LayerListItemViewModel.IsLocked), BindingMode.TwoWay)
                    },
                    new TextBlock { Text = $"State: {item.VisibilityLabel} · {item.LockLabel}", FontSize = 11, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        }
            };
        });

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(WorkspaceViewModel.SelectedLayerHelperText)), TextWrapping = TextWrapping.Wrap },
                list,
                BuildLabeledInput("Selected layer name", nameof(WorkspaceViewModel.SelectedLayerNameText)),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        BuildButton("Add Physical layer", viewModel.AddPhysicalLayerCommand, toolTip: "Physical layers model real routes such as roads, rail, or paths."),
                        BuildButton("Add Logical layer", viewModel.AddLogicalLayerCommand, toolTip: "Logical layers model supply and demand relationships."),
                        BuildButton("Add Policy layer", viewModel.AddPolicyLayerCommand, toolTip: "Policy layers model rules that block, allow, or change movement."),
                        BuildButton("Rename selected layer", viewModel.RenameLayerCommand),
                        BuildButton("Delete layer", viewModel.DeleteLayerCommand),
                        BuildButton("Toggle visibility", viewModel.ToggleLayerVisibilityCommand),
                        BuildButton("Toggle lock", viewModel.ToggleLayerLockCommand)
                    }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        BuildButton("Set selected node(s) to layer", viewModel.AssignSelectedNodesToLayerCommand),
                        BuildButton("Set selected edge(s) to layer", viewModel.AssignSelectedEdgesToLayerCommand),
                        BuildButton("Show all layers", viewModel.ShowAllLayersCommand),
                        BuildButton("Hide non-selected", viewModel.HideNonSelectedLayersCommand),
                        BuildButton("Lock non-selected", viewModel.LockNonSelectedLayersCommand),
                        BuildButton("Unlock all layers", viewModel.UnlockAllLayersCommand)
                    }
                }
            }
        };
    }

    private static Control BuildScenarioPanel(WorkspaceViewModel viewModel)
    {
        var scenarioList = new ComboBox();
        scenarioList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.ScenarioDefinitions)));
        scenarioList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(WorkspaceViewModel.SelectedScenarioDefinition), BindingMode.TwoWay));
        var eventList = new ListBox { MinHeight = 140 };
        eventList.Bind(ItemsControl.ItemsSourceProperty, new Binding("SelectedScenarioDefinition.Events"));
        eventList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(WorkspaceViewModel.SelectedScenarioEvent), BindingMode.TwoWay));
        eventList.ItemTemplate = new FuncDataTemplate<ScenarioEventModel>((item, _) =>
        {
            // Add the early exit for null items
            if (item is null)
            {
                return new TextBlock();
            }

            return new StackPanel
            {
                Spacing = 2,
                Children =
        {
            new TextBlock { Text = $"{(item.IsEnabled ? "☑" : "☐")} {item.Kind} · {item.Name}", FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = $"Target {item.TargetId ?? "None"} · Traffic {(string.IsNullOrWhiteSpace(item.TrafficTypeIdOrName) ? "N/A" : item.TrafficTypeIdOrName)} · Start {item.Time:0.##} · End {(item.EndTime?.ToString("0.##") ?? "None")} · Value {item.Value:0.##}", FontSize = 11 },
            new TextBlock { Text = item.IsEnabled ? "Status: enabled" : "Status: disabled", FontSize = 11, Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText) }
        }
            };
        });
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Scenarios test what happens when demand, cost, or network availability changes.", TextWrapping = TextWrapping.Wrap },
                BuildButton("Open scenario editor", viewModel.OpenScenarioEditorCommand, isPrimary: true, toolTip: "Open the full-screen scenario workspace."),
                new TextBlock { Text = "No scenarios yet. Create a scenario to test failures, closures, demand spikes, or cost changes.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText) },
                scenarioList,
                BuildQuickStat("Result", nameof(WorkspaceViewModel.ScenarioResultSummary)),
                new TextBlock { Text = "Events are summarized here. Use the full editor to create or change event details.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText) },
                eventList,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { BuildButton("Run scenario", viewModel.RunScenarioCommand, isPrimary: true), BuildButton("Edit selected event", viewModel.EditScenarioEventCommand) } },
                new ItemsControl { [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(WorkspaceViewModel.ScenarioWarnings)) }
            }
        };
    }

    private static Control BuildTopIssuesPanel(WorkspaceViewModel viewModel)
    {
        var actionableList = new ListBox();
        actionableList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.TopIssues)));
        actionableList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(WorkspaceViewModel.SelectedTopIssue), BindingMode.TwoWay));
        actionableList.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space &&
                actionableList.SelectedItem is TopIssueViewModel selectedIssue &&
                viewModel.SelectTopIssueCommand.CanExecute(selectedIssue))
            {
                viewModel.SelectTopIssueCommand.Execute(selectedIssue);
                e.Handled = true;
            }
        };
        actionableList.ItemTemplate = new FuncDataTemplate<TopIssueViewModel>((item, _) =>
        {
            if (item is null)
            {
                return new TextBlock();
            }

            var nodeLabel = string.IsNullOrWhiteSpace(item.NodeDisplayName) ? item.NodeId ?? "Unknown node" : item.NodeDisplayName;
            var fromNodeName = string.IsNullOrWhiteSpace(item.FromNodeName) ? "?" : item.FromNodeName;
            var toNodeName = string.IsNullOrWhiteSpace(item.ToNodeName) ? "?" : item.ToNodeName;
            var hasAmbiguousRouteNames = string.Equals(fromNodeName, toNodeName, StringComparison.OrdinalIgnoreCase);
            var shouldShowEdgeId = string.IsNullOrWhiteSpace(item.FromNodeName) ||
                                   string.IsNullOrWhiteSpace(item.ToNodeName) ||
                                   hasAmbiguousRouteNames;

            var targetText = item.TargetKind switch
            {
                TopIssueTargetKind.Node => $"Node: {nodeLabel}",
                TopIssueTargetKind.Edge => $"Route: {fromNodeName} → {toNodeName}",
                _ => "Target: Unspecified"
            };

            var targetTooltip = item.TargetKind switch
            {
                TopIssueTargetKind.Node => $"Selects node: {nodeLabel} ({item.NodeId ?? "Unknown"})",
                TopIssueTargetKind.Edge => $"Selects route: {fromNodeName} → {toNodeName} ({item.EdgeId ?? "Unknown route"})",
                _ => "Selects issue target."
            };

            var issueButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Command = viewModel.SelectTopIssueCommand,
                CommandParameter = item,
                Padding = new Thickness(10, 8),
                Content = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock { Text = item.Title, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = item.Detail, TextWrapping = TextWrapping.Wrap },
                        new TextBlock
                        {
                            Text = targetText,
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
                            FontWeight = FontWeight.SemiBold,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = $"Route ID: {item.EdgeId ?? "Unknown route"}",
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                            FontSize = 11,
                            IsVisible = item.TargetKind == TopIssueTargetKind.Edge && shouldShowEdgeId,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = item.Breadcrumb,
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                            FontSize = 11
                        }
                    }
                }
            };
            ToolTip.SetTip(issueButton, targetTooltip);
            issueButton.KeyDown += (_, keyEvent) =>
            {
                if (keyEvent.Key is Key.Enter or Key.Space && viewModel.SelectTopIssueCommand.CanExecute(item))
                {
                    viewModel.SelectTopIssueCommand.Execute(item);
                    keyEvent.Handled = true;
                }
            };
            return issueButton;
        });

        var advisoryHeader = new TextBlock
        {
            Text = "Network-wide advisories",
            FontWeight = FontWeight.SemiBold
        };
        advisoryHeader.Bind(Visual.IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.HasTopIssueAdvisories)));
        var advisoryList = new ItemsControl();
        advisoryList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.TopIssueAdvisories)));
        advisoryList.Bind(Visual.IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.HasTopIssueAdvisories)));
        advisoryList.ItemTemplate = new FuncDataTemplate<string>((item, _) =>
            new TextBlock
            {
                Text = $"• {item}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
            });

        var unmappedSummary = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
        };
        unmappedSummary.Bind(TextBlock.TextProperty, new Binding(nameof(WorkspaceViewModel.TopIssueUnmappedSummary)));
        var emptyState = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
        };
        emptyState.Bind(TextBlock.TextProperty, new Binding(nameof(WorkspaceViewModel.TopIssueEmptyStateText)));

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                actionableList,
                emptyState,
                advisoryHeader,
                advisoryList,
                unmappedSummary,
                BuildQuickStat("Location", nameof(WorkspaceViewModel.SelectedIssueBreadcrumb))
            }
        };
    }

    private static Control BuildExplanationPanel()
    {
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                BuildQuickStat("Section", nameof(WorkspaceViewModel.ExplanationTitle)),
                BuildQuickStat("Summary", nameof(WorkspaceViewModel.ExplanationSummary)),
                new ItemsControl { [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(WorkspaceViewModel.ExplanationCauses)) },
                new ItemsControl { [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(WorkspaceViewModel.ExplanationActions)) },
                new ItemsControl { [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(WorkspaceViewModel.ExplanationRelatedIssues)) }
            }
        };
    }

    private void ToggleDashboardCollapsed()
    {
        if (dashboardLayoutState == DashboardLayoutState.Collapsed)
        {
            dashboardLayoutState = DashboardLayoutState.Normal;
        }
        else if (dashboardLayoutState == DashboardLayoutState.Expanded)
        {
            RestorePreviousDashboardLayout();
            dashboardLayoutState = DashboardLayoutState.Collapsed;
        }
        else
        {
            dashboardLayoutState = DashboardLayoutState.Collapsed;
        }

        UpdateDashboardLayout();
    }

    private void ToggleDashboardExpanded()
    {
        if (dashboardLayoutState == DashboardLayoutState.Expanded)
        {
            RestorePreviousDashboardLayout();
        }
        else
        {
            previousDashboardLayoutState = dashboardLayoutState;
            dashboardLayoutState = DashboardLayoutState.Expanded;
        }

        UpdateDashboardLayout();
    }

    private void RestorePreviousDashboardLayout()
    {
        dashboardLayoutState = previousDashboardLayoutState == DashboardLayoutState.Expanded
            ? DashboardLayoutState.Normal
            : previousDashboardLayoutState;
    }

    private void UpdateDashboardLayout()
    {
        if (workspaceGrid is null || toolRailHost is null || canvasHost is null || inspectorHost is null || dashboardStripHost is null)
        {
            return;
        }

        var isExpanded = dashboardLayoutState == DashboardLayoutState.Expanded;
        var isCollapsed = dashboardLayoutState == DashboardLayoutState.Collapsed;

        workspaceGrid.ColumnDefinitions[0].Width = isExpanded ? new GridLength(0) : new GridLength(240);
        workspaceGrid.ColumnDefinitions[2].Width = isExpanded ? new GridLength(0) : new GridLength(460);
        toolRailHost.IsVisible = !isExpanded;
        inspectorHost.IsVisible = !isExpanded;

        workspaceGrid.RowDefinitions[0].Height = isExpanded
            ? GridLength.Auto
            : GridLength.Star;
        workspaceGrid.RowDefinitions[1].Height = isCollapsed
            ? new GridLength(BottomStripCollapsedHeight)
            : isExpanded
                ? GridLength.Star
                : new GridLength(BottomStripHeight);
        workspaceGrid.RowDefinitions[1].MinHeight = isCollapsed ? BottomStripCollapsedHeight : BottomStripMinHeight;
        workspaceGrid.RowDefinitions[1].MaxHeight = isExpanded ? double.PositiveInfinity : isCollapsed ? BottomStripCollapsedHeight : BottomStripMaxHeight;

        dashboardStripBody!.IsVisible = !isCollapsed;

        Grid.SetRow(canvasHost, 0);
        Grid.SetColumn(canvasHost, 1);
        Grid.SetColumnSpan(canvasHost, 1);
        canvasHost.MaxHeight = isExpanded ? ExpandedCanvasPreviewHeight : double.PositiveInfinity;
        canvasHost.MinHeight = isExpanded ? ExpandedCanvasPreviewHeight : 520;

        Grid.SetColumn(dashboardStripHost, isExpanded ? 0 : 1);
        Grid.SetColumnSpan(dashboardStripHost, isExpanded ? 3 : 2);
    }

    private void OpenFullNodeEditor()
    {
        if (!viewModel.IsEditingNode || fullNodeEditorDrawer is null || overlayLayer is null)
        {
            return;
        }

        if (overlayBackdrop is not null)
        {
            overlayBackdrop.IsVisible = true;
        }

        fullNodeEditorDrawer.IsVisible = true;
        overlayLayer.IsHitTestVisible = true;
        fullNodeEditorDrawer.Focus();
    }

    private void CloseFullNodeEditor()
    {
        if (fullNodeEditorDrawer is null || overlayLayer is null)
        {
            return;
        }

        if (overlayBackdrop is not null)
        {
            overlayBackdrop.IsVisible = false;
        }

        fullNodeEditorDrawer.IsVisible = false;
        overlayLayer.IsHitTestVisible = false;
    }

    private void EnterTrafficTypeWorkspace()
    {
        if (viewModel.SelectedTrafficDefinitionItem is null)
        {
            viewModel.SelectedTrafficDefinitionItem = viewModel.TrafficDefinitions.FirstOrDefault();
        }

        shellWorkspaceMode = ShellWorkspaceMode.TrafficTypes;
        UpdateShellWorkspaceMode();
        FocusTrafficTypeWorkspaceEditor();
    }

    private void ExitTrafficTypeWorkspace()
    {
        shellWorkspaceMode = ShellWorkspaceMode.Standard;
        UpdateShellWorkspaceMode();
        toolRailHost?.BringIntoView();
    }

    private void UpdateShellWorkspaceMode()
    {
        if (standardWorkspaceHost is null || trafficTypeWorkspaceHost is null || edgeEditorWorkspaceHost is null || scenarioEditorWorkspaceHost is null || osmImportWorkspaceHost is null)
        {
            return;
        }

        var isTrafficTypeWorkspace = shellWorkspaceMode == ShellWorkspaceMode.TrafficTypes;
        var isScenarioEditorWorkspace = shellWorkspaceMode == ShellWorkspaceMode.ScenarioEditor || viewModel.IsScenarioEditorWorkspaceMode;
        var isOsmImportWorkspace = shellWorkspaceMode == ShellWorkspaceMode.OsmImport || viewModel.IsOsmImportWorkspaceMode;
        var isEdgeEditorWorkspace = !isTrafficTypeWorkspace && !isScenarioEditorWorkspace && !isOsmImportWorkspace && viewModel.IsEdgeEditorWorkspaceMode;
        standardWorkspaceHost.IsVisible = !isTrafficTypeWorkspace && !isEdgeEditorWorkspace && !isScenarioEditorWorkspace && !isOsmImportWorkspace;
        trafficTypeWorkspaceHost.IsVisible = isTrafficTypeWorkspace;
        edgeEditorWorkspaceHost.IsVisible = isEdgeEditorWorkspace;
        scenarioEditorWorkspaceHost.IsVisible = isScenarioEditorWorkspace;
        osmImportWorkspaceHost.IsVisible = isOsmImportWorkspace;
        refreshToolRailState?.Invoke();
    }

    private void FocusTrafficTypeWorkspaceEditor()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (trafficTypeWorkspaceHost?.IsVisible != true)
            {
                return;
            }

            trafficTypeWorkspaceHost.BringIntoView();
            if (trafficTypeWorkspaceFocusTarget is { IsVisible: true, IsEnabled: true })
            {
                trafficTypeWorkspaceFocusTarget.Focus();
            }
            else
            {
                trafficTypeWorkspaceHost.Focus();
            }
        }, DispatcherPriority.Background);
    }

    private void FocusEdgeEditorWorkspaceEditor()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (edgeEditorWorkspaceHost?.IsVisible != true)
            {
                return;
            }

            edgeEditorWorkspaceHost.BringIntoView();
            if (edgeEditorWorkspaceFocusTarget is { IsVisible: true, IsEnabled: true })
            {
                edgeEditorWorkspaceFocusTarget.Focus();
            }
            else
            {
                edgeEditorWorkspaceHost.Focus();
            }
        }, DispatcherPriority.Background);
    }

    private void FocusScenarioEditorWorkspace()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (scenarioEditorWorkspaceHost?.IsVisible != true)
            {
                return;
            }

            scenarioEditorWorkspaceHost.BringIntoView();
            if (scenarioEditorWorkspaceFocusTarget is { IsVisible: true, IsEnabled: true })
            {
                scenarioEditorWorkspaceFocusTarget.Focus();
            }
            else
            {
                scenarioEditorWorkspaceHost.Focus();
            }
        }, DispatcherPriority.Background);
    }

    private void HandleShellWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (viewModel.IsScenarioEditorWorkspaceMode && e.Key == Key.Escape)
        {
            _ = CloseScenarioEditorWithConfirmationAsync();
            e.Handled = true;
            return;
        }

        if (viewModel.IsOsmImportWorkspaceMode && e.Key == Key.Escape)
        {
            viewModel.CancelOsmImportCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (viewModel.IsEdgeEditorWorkspaceMode &&
            e.Key == Key.Enter &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (viewModel.SaveEdgeEditorCommand.CanExecute(null))
            {
                viewModel.SaveEdgeEditorCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (viewModel.IsEdgeEditorWorkspaceMode && e.Key == Key.Escape)
        {
            viewModel.CancelEdgeEditorCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        if (fullNodeEditorDrawer?.IsVisible == true)
        {
            CloseFullNodeEditor();
            e.Handled = true;
            return;
        }

        if (shellWorkspaceMode == ShellWorkspaceMode.TrafficTypes)
        {
            ExitTrafficTypeWorkspace();
            e.Handled = true;
            return;
        }

        if (dashboardLayoutState == DashboardLayoutState.Expanded)
        {
            RestorePreviousDashboardLayout();
            UpdateDashboardLayout();
            e.Handled = true;
        }
    }

    private static Control BuildInspector(WorkspaceViewModel viewModel)
    {
        var tabs = new TabControl
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelBackground),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        tabs.Items.Add(new TabItem { Header = "Selection", Content = BuildSelectionInspector(viewModel) });
        tabs.Items.Add(new TabItem { Header = "Traffic Types", Content = BuildTrafficDefinitionEditor(viewModel) });
        tabs.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(WorkspaceViewModel.SelectedInspectorTabIndex), BindingMode.TwoWay));

        var summaryBlock = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorderStrong),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    BuildQuickStat("Status", nameof(WorkspaceViewModel.StatusText)),
                    BuildQuickStat("Selection", nameof(WorkspaceViewModel.SelectionSummary)),
                    BuildQuickStat("Simulation", nameof(WorkspaceViewModel.SimulationSummary)),
                    BuildQuickStat("Tool", nameof(WorkspaceViewModel.ToolStatusText))
                }
            }
        };

        var inspectorGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        inspectorGrid.Children.Add(summaryBlock);
        Grid.SetRow(tabs, 1);
        inspectorGrid.Children.Add(tabs);

        var border = BuildDashboardPanel(inspectorGrid, header: "Intelligence Rail", padding: new Thickness(14));
        border.HorizontalAlignment = HorizontalAlignment.Stretch;
        border.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetColumn(border, 2);
        Grid.SetRow(border, 0);
        return border;
    }

    private static Control BuildSelectionInspector(WorkspaceViewModel viewModel)
    {
        var details = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding("Inspector.Details"),
            ItemTemplate = new FuncDataTemplate<string>((item, _) =>
                new TextBlock
                {
                    Text = item,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                    Margin = new Thickness(0, 0, 0, 6),
                    TextWrapping = TextWrapping.Wrap
                })
        };

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    BuildHeadlineBlock(),
                    details,
                    BuildValidationBlock(nameof(WorkspaceViewModel.InspectorValidationText)),
                    BuildNetworkEditor(),
                    BuildNodeEditor(viewModel),
                    BuildEdgeEditor(viewModel),
                    BuildBulkEditor(),
                    BuildApplyRow(viewModel.ApplyInspectorCommand)
                }
            }
        };
    }

    private static Control BuildTrafficDefinitionEditor(WorkspaceViewModel viewModel)
    {
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    BuildSectionTitle("Traffic Types", "Choose a traffic type, update its routing settings, and manage default route access."),
                    BuildTrafficTypeList(maxHeight: 180),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            BuildButton("Add type", viewModel.AddTrafficDefinitionCommand),
                            BuildButton("Remove type", viewModel.RemoveSelectedTrafficDefinitionCommand),
                            BuildButton("Apply traffic type", viewModel.ApplyTrafficDefinitionCommand)
                        }
                    },
                    BuildTrafficTypeIdentityEditors(),
                    BuildTrafficTypeTabbedEditor()
                }
            }
        };
    }

    private Control BuildBottomStrip(WorkspaceViewModel viewModel)
    {
        var metrics = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(WorkspaceViewModel.ReportMetrics)),
            ItemTemplate = new FuncDataTemplate<ReportMetricViewModel>((metric, _) => BuildButton($"{metric.Label}  {metric.Value}", new RelayCommand(metric.Activate), toolTip: $"Open {metric.Label} report details."))
        };

        var playbackGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*"),
            RowDefinitions = new RowDefinitions("Auto")
        };
        playbackGrid.Children.Add(BuildButton("Run", viewModel.SimulateCommand, isPrimary: true, toolTip: "Run timeline simulation."));
        playbackGrid.Children.Add(BuildButton("Step", viewModel.StepCommand, 1, isPrimary: true, toolTip: "Advance one period."));
        playbackGrid.Children.Add(BuildButton("Reset", viewModel.ResetTimelineCommand, 2, toolTip: "Reset timeline to start."));
        playbackGrid.Children.Add(BuildButton("Fit", viewModel.FitCommand, 3, toolTip: "Fit graph on canvas."));

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 12,
            Margin = new Thickness(12, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        slider.Bind(RangeBase.ValueProperty, new Binding(nameof(WorkspaceViewModel.TimelinePosition), BindingMode.TwoWay));
        Grid.SetColumn(slider, 4);
        ApplyFocusVisual(slider);
        playbackGrid.Children.Add(slider);

        var tabControl = new TabControl
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelBackground),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Height = BottomStripHeight - 56d,
            MinHeight = BottomStripMinHeight - 56d,
            MaxHeight = BottomStripMaxHeight - 56d
        };
        tabControl.Items.Add(new TabItem
        {
            Header = "Playback",
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = playbackGrid
            }
        });
        var reportsGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10
        };
        reportsGrid.Children.Add(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        BuildButton("Export Current (HTML)", new RelayCommand(() => _ = ExportCurrentReportAsync(viewModel, ReportExportFormat.Html))),
                        BuildButton("Export Timeline (CSV)", new RelayCommand(() => _ = ExportTimelineReportAsync(viewModel, ReportExportFormat.Csv)))
                    }
                },
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = metrics
                }
            }
        });
        tabControl.Items.Add(new TabItem
        {
            Header = "Reports",
            Content = reportsGrid
        });
        var reportsScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    BuildTrafficReportSection(),
                    BuildReportSection<RouteReportRowViewModel>(
                        "Route Summary",
                        new[]
                        {
                            ("Route", 1.0),
                            ("From -> to", 1.5),
                            ("Flow", 1.0),
                            ("Capacity", 0.9),
                            ("Utilisation", 0.9),
                            ("Pressure", 1.2)
                        },
                        nameof(WorkspaceViewModel.RouteReports),
                        static row => new[]
                        {
                            row.RouteId,
                            row.FromTo,
                            row.CurrentFlow,
                            row.Capacity,
                            row.Utilisation,
                            row.Pressure
                        }),
                    BuildReportSection<NodePressureReportRowViewModel>(
                        "Node Pressure Summary",
                        new[]
                        {
                            ("Node", 1.3),
                            ("Pressure", 0.8),
                            ("Top cause", 1.2),
                            ("Unmet need", 1.0)
                        },
                        nameof(WorkspaceViewModel.NodePressureReports),
                        static row => new[]
                        {
                            row.Node,
                            row.PressureScore,
                            row.TopCause,
                            row.UnmetNeed
                        })
                }
            }
        };
        Grid.SetRow(reportsScroll, 1);
        reportsGrid.Children.Add(reportsScroll);
        tabControl.Items.Add(new TabItem
        {
            Header = "Status",
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        BuildQuickStat("Status", nameof(WorkspaceViewModel.StatusText)),
                        BuildQuickStat("Selection", nameof(WorkspaceViewModel.SelectionSummary)),
                        BuildQuickStat("Simulation", nameof(WorkspaceViewModel.SimulationSummary))
                    }
                }
            }
        });

        var strip = BuildDashboardPanel(tabControl, header: "Dashboard Strip", padding: new Thickness(14, 10), radius: new CornerRadius(14));
        strip.Margin = new Thickness(12, 10, 0, 0);
        strip.MinHeight = BottomStripMinHeight;
        strip.MaxHeight = BottomStripMaxHeight;
        strip.Height = BottomStripHeight;
        strip.VerticalAlignment = VerticalAlignment.Stretch;

        Grid.SetColumn(strip, 1);
        Grid.SetColumnSpan(strip, 2);
        Grid.SetRow(strip, 1);
        return strip;
    }

    private static Control BuildHeadlineBlock()
    {
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding("Inspector.Headline"),
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                },
                new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding("Inspector.Summary"),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
                }
            }
        };
    }

    private static Control BuildNetworkEditor()
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius,
            Padding = new Thickness(10),
            IsVisible = false,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    BuildSectionTitle("Network", "Edit the network name, description, and loop length."),
                    BuildLabeledTextBox("Network name", nameof(WorkspaceViewModel.NetworkNameText)),
                    BuildLabeledTextBox("Description", nameof(WorkspaceViewModel.NetworkDescriptionText)),
                    BuildLabeledTextBox("Loop length (periods)", nameof(WorkspaceViewModel.NetworkTimelineLoopLengthText))
                }
            }
        };
        panel.Bind(IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.IsEditingNetwork)));
        return panel;
    }

    private static Control BuildNodeEditor(WorkspaceViewModel viewModel)
    {
        var profileList = new ListBox
        {
            Height = 88,
            SelectionMode = SelectionMode.Single
        };
        profileList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.SelectedNodeTrafficProfiles)));
        profileList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(WorkspaceViewModel.SelectedNodeTrafficProfileItem), BindingMode.TwoWay));
        ApplyFocusVisual(profileList);

        var trafficRoleEditor = BuildTrafficRoleEditor(out var trafficRoleEditorFocusTarget);

        var panel = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius,
            Padding = new Thickness(10),
            IsVisible = false,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    BuildSectionTitle("Node", "Edit node details and traffic roles."),
                    BuildLabeledTextBox("Name", "NodeDraft.NodeNameText"),
                    BuildLabeledAutoCompleteTextBox("Place type", "NodeDraft.PlaceTypeText", "NodeDraft.PlaceTypeSuggestions", "Type or choose a place type"),
                    BuildLabeledTextBox("Description", "NodeDraft.DescriptionText"),
                    BuildLabeledTextBox("Transhipment capacity", "NodeDraft.TranshipmentCapacityText"),
                    BuildLabeledComboBox("Node shape", nameof(WorkspaceViewModel.NodeShapeOptions), "NodeDraft.Shape"),
                    BuildLabeledComboBox("Node kind", nameof(WorkspaceViewModel.NodeKindOptions), "NodeDraft.NodeKind"),
                    BuildSectionTitle("Traffic Roles", "Select a role, then edit traffic, supply, demand, storage, and timing right below."),
                    profileList,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            BuildBoundButton("Add Role", nameof(WorkspaceViewModel.AddNodeTrafficProfileCommand)),
                            BuildBoundButton("Duplicate Role", nameof(WorkspaceViewModel.DuplicateSelectedNodeTrafficProfileCommand)),
                            BuildBoundButton("Delete Role", nameof(WorkspaceViewModel.RemoveSelectedNodeTrafficProfileCommand))
                        }
                    },
                    BuildValidationBlock(nameof(WorkspaceViewModel.NodeTrafficRoleValidationText)),
                    BuildTrafficRoleEmptyState(viewModel),
                    trafficRoleEditor
                }
            }
        };
        panel.Bind(IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.IsEditingNode)));
        HookInspectorSectionFocus(viewModel, panel, trafficRoleEditor, trafficRoleEditorFocusTarget, null, null);
        return panel;
    }

    private static Control BuildEdgeEditor(WorkspaceViewModel viewModel)
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius,
            Padding = new Thickness(10),
            IsVisible = false,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    BuildSectionTitle("Route", "Edit route values and access rules."),
                    BuildLabeledAutoCompleteTextBox("Route label", "EdgeDraft.RouteTypeText", "EdgeDraft.RouteTypeSuggestions", "Type or choose a route type", nameof(WorkspaceViewModel.ApplyInspectorCommand)),
                    BuildLabeledTextBox("Travel time", "EdgeDraft.TimeText"),
                    BuildLabeledTextBox("Travel cost", "EdgeDraft.CostText"),
                    BuildLabeledTextBox("Capacity", "EdgeDraft.CapacityText"),
                    BuildLabeledCheckBox("Bidirectional", "EdgeDraft.IsBidirectional"),
                    BuildPermissionEditor(
                        "Route Access",
                        "Override the network default for this route when you need a different access rule.",
                        nameof(WorkspaceViewModel.SelectedEdgePermissionRows),
                        "EdgeDraft.CapacityText")
                }
            }
        };
        panel.Bind(IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.IsEditingEdge)));
        HookInspectorSectionFocus(viewModel, panel, null, null, panel, panel);
        return panel;
    }

    private static Control BuildBulkEditor()
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius,
            Padding = new Thickness(10),
            IsVisible = false,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    BuildSectionTitle("Bulk Edit", "Apply shared values across selected nodes."),
                    BuildLabeledAutoCompleteTextBox("Place type", "BulkDraft.PlaceTypeText", "BulkDraft.PlaceTypeSuggestions", "Type or choose a place type"),
                    BuildLabeledTextBox("Transhipment capacity", "BulkDraft.TranshipmentCapacityText")
                }
            }
        };
        panel.Bind(IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.IsEditingSelection)));
        return panel;
    }

    private static Control BuildApplyRow(ICommand applyCommand)
    {
        var button = BuildButton("Apply Changes", applyCommand);
        button.Bind(ContentControl.ContentProperty, new Binding(nameof(WorkspaceViewModel.ApplyInspectorLabel)));
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { button } };
    }

    private static Control BuildPermissionEditor(string title, string summary, string rowsPropertyName, string? edgeCapacityPropertyName)
    {
        var rows = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding(rowsPropertyName),
            ItemTemplate = new FuncDataTemplate<PermissionRuleEditorRow>((row, _) =>
            {
                var wrap = new StackPanel
                {
                    Spacing = 6,
                    Margin = new Thickness(0, 0, 0, 10),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = row.TrafficType,
                            FontWeight = FontWeight.Bold,
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                        }
                    }
                };

                if (row.SupportsOverrideToggle)
                {
                    var overrideBox = BuildCheckBox("Override network default");
                    ToolTip.SetTip(overrideBox, "Enable to apply a route-specific access rule instead of the network default.");
                    overrideBox.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(PermissionRuleEditorRow.IsActive), BindingMode.TwoWay));
                    wrap.Children.Add(overrideBox);
                }

                var modeBox = BuildComboBox();
                modeBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.PermissionModeOptions)) { Source = Application.Current?.ApplicationLifetime is not null ? null : null });
                modeBox.ItemsSource = Enum.GetValues<EdgeTrafficPermissionMode>();
                modeBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(PermissionRuleEditorRow.Mode), BindingMode.TwoWay));
                ToolTip.SetTip(modeBox, "Permission mode selects permitted, blocked, or limited route access.");
                wrap.Children.Add(BuildLabeledRow("Permission", modeBox));

                var limitKind = BuildComboBox();
                limitKind.ItemsSource = Enum.GetValues<EdgeTrafficLimitKind>();
                limitKind.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(PermissionRuleEditorRow.LimitKind), BindingMode.TwoWay));
                ToolTip.SetTip(limitKind, "Limit kind sets absolute units or percent-of-capacity limits.");
                wrap.Children.Add(BuildLabeledRow("Limit type", limitKind));

                var limitValue = BuildTextBox("Enter a limit");
                limitValue.Bind(TextBox.TextProperty, new Binding(nameof(PermissionRuleEditorRow.LimitValueText), BindingMode.TwoWay));
                ToolTip.SetTip(limitValue, "When mode is Limited, specify the allowed quantity.");
                wrap.Children.Add(BuildLabeledRow("Limit value", limitValue));

                var effective = new TextBlock
                {
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                    TextWrapping = TextWrapping.Wrap
                };
                effective.Bind(TextBlock.TextProperty, new Binding(nameof(PermissionRuleEditorRow.EffectiveSummary)));
                wrap.Children.Add(effective);

                var validation = new TextBlock
                {
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.Danger),
                    TextWrapping = TextWrapping.Wrap
                };
                validation.Bind(TextBlock.TextProperty, new Binding(nameof(PermissionRuleEditorRow.ValidationMessage)));
                wrap.Children.Add(validation);

                return wrap;
            })
        };

        return new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    BuildSectionTitle(title, summary),
                    rows
                }
            }
        };
    }

    private static Control BuildSectionTitle(string title, string summary)
    {
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                },
                new TextBlock
                {
                    Text = summary,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                    FontSize = 12
                }
            }
        };
    }

    private static ListBox BuildTrafficTypeList(double maxHeight, Action? onOpenEditor = null)
    {
        var definitionList = new ListBox
        {
            MaxHeight = maxHeight,
            MinHeight = 96,
            SelectionMode = SelectionMode.Single,
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(AvaloniaDashboardTheme.InputBackground),
            ItemTemplate = new FuncDataTemplate<TrafficDefinitionListItem>((item, _) =>
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
                    ColumnSpacing = 10,
                    RowSpacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            [!TextBlock.TextProperty] = new Binding(nameof(TrafficDefinitionListItem.Name)),
                            FontWeight = FontWeight.Bold,
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            [!TextBlock.TextProperty] = new Binding(nameof(TrafficDefinitionListItem.RoutingPreferenceText)),
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                            HorizontalAlignment = HorizontalAlignment.Right,
                            [Grid.ColumnProperty] = 1
                        },
                        new TextBlock
                        {
                            [!TextBlock.TextProperty] = new Binding(nameof(TrafficDefinitionListItem.AllocationModeText)),
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                            FontSize = 12,
                            [Grid.RowProperty] = 1,
                            [Grid.ColumnSpanProperty] = 2
                        },
                        new TextBlock
                        {
                            [!TextBlock.TextProperty] = new Binding(nameof(TrafficDefinitionListItem.SummaryBadgeText)),
                            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.MutedText),
                            FontSize = 11,
                            TextWrapping = TextWrapping.Wrap,
                            [Grid.RowProperty] = 2,
                            [Grid.ColumnSpanProperty] = 2
                        }
                    }
                };

                return new Border
                {
                    Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
                    BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 8),
                    Margin = new Thickness(0, 0, 0, 6),
                    Child = row
                };
            })
        };
        definitionList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.TrafficDefinitions)));
        definitionList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(WorkspaceViewModel.SelectedTrafficDefinitionItem), BindingMode.TwoWay));
        ApplyFocusVisual(definitionList);
        if (onOpenEditor is not null)
        {
            definitionList.DoubleTapped += (_, _) => onOpenEditor();
        }

        return definitionList;
    }

    private static Control BuildTrafficTypeTabbedEditor()
    {
        var tabControl = new TabControl
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelBackground),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinHeight = 0
        };
        ApplyFocusVisual(tabControl);

        tabControl.Items.Add(new TabItem { Header = "Summary", Content = BuildScrollableTabBody(BuildTrafficTypeSummaryTab()) });
        tabControl.Items.Add(new TabItem { Header = "Routing", Content = BuildScrollableTabBody(BuildTrafficTypeRoutingTab()) });
        tabControl.Items.Add(new TabItem { Header = "Economics", Content = BuildScrollableTabBody(BuildTrafficTypeEconomicsTab()) });
        tabControl.Items.Add(new TabItem { Header = "Default Route Access", Content = BuildScrollableTabBody(BuildTrafficTypeDefaultAccessTab()) });
        tabControl.Items.Add(new TabItem { Header = "Validation", Content = BuildScrollableTabBody(BuildTrafficTypeValidationTab()) });
        return tabControl;
    }

    private static Control BuildTrafficTypeIdentityEditors()
    {
        return BuildTrafficTypeIdentityEditors(out _);
    }

    private static Control BuildTrafficTypeIdentityEditors(out Control focusTarget)
    {
        var nameEditor = BuildTextBox("Traffic type name");
        nameEditor.Bind(TextBox.TextProperty, new Binding(nameof(WorkspaceViewModel.TrafficNameText), BindingMode.TwoWay));
        focusTarget = nameEditor;

        var descriptionEditor = BuildTextBox("Description");
        descriptionEditor.Bind(TextBox.TextProperty, new Binding(nameof(WorkspaceViewModel.TrafficDescriptionText), BindingMode.TwoWay));

        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                BuildLabeledRow("Traffic type name", nameEditor),
                BuildLabeledRow("Description", descriptionEditor)
            }
        };
    }

    private static Control BuildScrollableTabBody(Control content)
    {
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content
        };
    }

    private static Control BuildTrafficTypeSummaryTab()
    {
        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new Border
                {
                    Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
                    BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10),
                    Child = new TextBlock
                    {
                        [!TextBlock.TextProperty] = new Binding(nameof(WorkspaceViewModel.SelectedTrafficTypeSummaryText)),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                    }
                },
                BuildReadOnlyRow("Name", nameof(WorkspaceViewModel.TrafficNameText)),
                BuildReadOnlyRow("Description", nameof(WorkspaceViewModel.TrafficDescriptionText)),
                BuildReadOnlyRow("Routing preference", nameof(WorkspaceViewModel.TrafficRoutingPreference)),
                BuildReadOnlyRow("Allocation mode", nameof(WorkspaceViewModel.TrafficAllocationMode)),
                BuildReadOnlyRow("Route choice model", nameof(WorkspaceViewModel.TrafficRouteChoiceModel)),
                BuildReadOnlyRow("Flow split policy", nameof(WorkspaceViewModel.TrafficFlowSplitPolicy)),
                BuildReadOnlyRow("Capacity bid per unit", nameof(WorkspaceViewModel.TrafficCapacityBidText)),
                BuildReadOnlyRow("Perishability periods", nameof(WorkspaceViewModel.TrafficPerishabilityText)),
                BuildReadOnlyRow("Default route access", nameof(WorkspaceViewModel.SelectedTrafficTypeDefaultAccessSummaryText))
            }
        };
    }

    private static Control BuildTrafficTypeRoutingTab()
    {
        var routingPreference = BuildLabeledComboBox("Routing preference", nameof(WorkspaceViewModel.RoutingPreferenceOptions), nameof(WorkspaceViewModel.TrafficRoutingPreference));
        ToolTip.SetTip(routingPreference, "Routing preference affects how route costs are prioritized.");
        var allocationMode = BuildLabeledComboBox("Allocation mode", nameof(WorkspaceViewModel.AllocationModeOptions), nameof(WorkspaceViewModel.TrafficAllocationMode));
        ToolTip.SetTip(allocationMode, "Allocation mode controls how available route capacity is assigned.");
        var routeChoiceModel = BuildLabeledComboBox("Route choice model", nameof(WorkspaceViewModel.RouteChoiceModelOptions), nameof(WorkspaceViewModel.TrafficRouteChoiceModel));
        ToolTip.SetTip(routeChoiceModel, "Route choice model defines deterministic or adaptive route behavior.");
        var flowSplitPolicy = BuildLabeledComboBox("Flow split policy", nameof(WorkspaceViewModel.FlowSplitPolicyOptions), nameof(WorkspaceViewModel.TrafficFlowSplitPolicy));
        ToolTip.SetTip(flowSplitPolicy, "Flow split policy controls how flow is divided across eligible routes.");

        return new StackPanel
        {
            Spacing = 10,
            Children = { routingPreference, allocationMode, routeChoiceModel, flowSplitPolicy }
        };
    }

    private static Control BuildTrafficTypeEconomicsTab()
    {
        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                BuildLabeledTextBox("Capacity bid per unit", nameof(WorkspaceViewModel.TrafficCapacityBidText)),
                new TextBlock
                {
                    Text = "Bid per unit: used when competing for constrained route capacity.",
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                },
                BuildLabeledTextBox("Perishability periods", nameof(WorkspaceViewModel.TrafficPerishabilityText)),
                new TextBlock
                {
                    Text = "Perishability: maximum periods before the traffic expires.",
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }

    private static Control BuildTrafficTypeDefaultAccessTab()
    {
        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                BuildReadOnlyRow("Selected type", nameof(WorkspaceViewModel.TrafficNameText)),
                BuildReadOnlyRow("Access summary", nameof(WorkspaceViewModel.SelectedTrafficTypeDefaultAccessSummaryText)),
                BuildPermissionEditor(
                    "Default Route Access",
                    "All traffic types are shown here. Use the selected type summary above to keep the current row obvious.",
                    nameof(WorkspaceViewModel.DefaultTrafficPermissionRows),
                    edgeCapacityPropertyName: null)
            }
        };
    }

    private static Control BuildTrafficTypeValidationTab()
    {
        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                BuildReadOnlyRow("Status", nameof(WorkspaceViewModel.SelectedTrafficTypeStatusText)),
                BuildValidationBlock(nameof(WorkspaceViewModel.TrafficValidationText))
            }
        };
    }

    private static Control BuildTrafficRoleEditor(out Control focusTarget)
    {
        var trafficTypeBox = BuildComboBox();
        trafficTypeBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.TrafficTypeOptions)));
        trafficTypeBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(WorkspaceViewModel.SelectedTrafficType), BindingMode.TwoWay));
        focusTarget = trafficTypeBox;

        var roleBox = BuildComboBox();
        roleBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.NodeRoleOptions)));
        roleBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(WorkspaceViewModel.NodeTrafficRoleText), BindingMode.TwoWay));

        var stack = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                BuildSectionTitle("Role Details", "Edit traffic, supply, demand, storage, and timing for the selected role."),
                BuildSectionTitle("Identity", "Type and role"),
                BuildLabeledRow("Traffic", trafficTypeBox),
                BuildLabeledRow("Role", roleBox),
                BuildSectionTitle("Flow", "Supply and demand"),
                BuildLabeledTextBox("Production", nameof(WorkspaceViewModel.NodeProductionText)),
                BuildLabeledTextBox("Consumption", nameof(WorkspaceViewModel.NodeConsumptionText)),
                BuildLabeledTextBox("Consumer premium", nameof(WorkspaceViewModel.NodeConsumerPremiumText)),
                BuildSectionTitle("Timing", "Active periods"),
                BuildLabeledTextBox("Prod start", nameof(WorkspaceViewModel.NodeProductionStartText)),
                BuildLabeledTextBox("Prod end", nameof(WorkspaceViewModel.NodeProductionEndText)),
                BuildLabeledTextBox("Cons start", nameof(WorkspaceViewModel.NodeConsumptionStartText)),
                BuildLabeledTextBox("Cons end", nameof(WorkspaceViewModel.NodeConsumptionEndText)),
                BuildSectionTitle("Storage and relay", "Movement and storage"),
                BuildLabeledCheckBox("Can transship", nameof(WorkspaceViewModel.NodeCanTransship)),
                BuildLabeledCheckBox("Store enabled", nameof(WorkspaceViewModel.NodeStoreEnabled)),
                BuildLabeledTextBox("Store capacity", nameof(WorkspaceViewModel.NodeStoreCapacityText), nameof(WorkspaceViewModel.IsNodeStoreCapacityEnabled))
            }
        };
        var card = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius,
            Padding = new Thickness(12),
            Child = stack
        };
        card.Bind(IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.IsNodeTrafficRoleSelected)));
        return card;
    }

    private static Control BuildTrafficRoleEmptyState(WorkspaceViewModel viewModel)
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "No traffic role selected.", Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText) },
                    BuildButton("Add role", viewModel.AddNodeTrafficProfileCommand)
                }
            }
        };
        panel.Bind(IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.IsNodeTrafficRoleSelected))
        {
            Converter = InverseBoolConverter.Instance
        });
        return panel;
    }

    private static void HookInspectorSectionFocus(
        WorkspaceViewModel viewModel,
        Control nodeContainer,
        Control? roleTarget,
        Control? roleFocusTarget,
        Control? routeTarget,
        Control? routeFocusTarget)
    {
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.SelectedNodeTrafficProfileItem) &&
                roleTarget is not null &&
                viewModel.IsEditingNode &&
                viewModel.SelectedNodeTrafficProfileItem is not null)
            {
                BringIntoViewAndFocus(roleTarget, roleFocusTarget);
                return;
            }

            if (e.PropertyName != nameof(WorkspaceViewModel.SelectedInspectorSection))
            {
                return;
            }

            if (viewModel.SelectedInspectorSection == InspectorSectionTarget.TrafficRoles && roleTarget is not null)
            {
                BringIntoViewAndFocus(roleTarget, roleFocusTarget);
            }
            else if (viewModel.SelectedInspectorSection == InspectorSectionTarget.Node)
            {
                BringIntoViewAndFocus(nodeContainer, null);
            }
            else if (viewModel.SelectedInspectorSection == InspectorSectionTarget.Route && routeTarget is not null)
            {
                BringIntoViewAndFocus(routeTarget, routeFocusTarget);
            }
        };
    }

    private static void BringIntoViewAndFocus(Control container, Control? focusTarget)
    {
        Dispatcher.UIThread.Post(() =>
        {
            container.BringIntoView();
            if (focusTarget is not null && focusTarget.IsVisible && focusTarget.IsEnabled)
            {
                focusTarget.Focus();
            }
        }, DispatcherPriority.Background);
    }

    private static TextBlock BuildBoundText(string propertyName, int column)
    {
        var text = new TextBlock
        {
            Margin = new Thickness(column == 0 ? 0 : 18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(column == 0 ? AvaloniaDashboardTheme.PrimaryText : AvaloniaDashboardTheme.SecondaryText),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(text, column);
        text.Bind(TextBlock.TextProperty, new Binding(propertyName));
        return text;
    }

    private static Button BuildButton(string label, ICommand command, int column = -1, bool isPrimary = false, string? toolTip = null)
    {
        var button = new Button
        {
            Content = label,
            Command = command,
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius
        };
        button.Classes.Add("toolbar-button");
        if (isPrimary)
        {
            button.Classes.Add("primary");
        }

        if (!string.IsNullOrWhiteSpace(toolTip))
        {
            ToolTip.SetTip(button, toolTip);
        }

        if (column >= 0)
        {
            button.SetValue(Grid.ColumnProperty, column);
        }

        return button;
    }

    private static Button BuildBoundButton(string label, string commandPropertyName)
    {
        var button = BuildButton(label, new RelayCommand(() => { }));
        button.Bind(Button.CommandProperty, new Binding(commandPropertyName));
        return button;
    }

    private static Button BuildToolButton(string text, string toolTip, ICommand command)
    {
        var button = new Button
        {
            Content = text,
            Tag = text,
            Command = command,
            FontWeight = FontWeight.Bold,
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius
        };
        button.Classes.Add("toolbar-button");
        button.Classes.Add("tool-button");
        ToolTip.SetTip(button, toolTip);
        return button;
    }

    private static void ApplyToolButtonState(Button button, bool isActive)
    {
        var label = button.Tag as string ?? button.Content?.ToString() ?? string.Empty;
        button.Content = isActive ? $"● {label}" : label;
        button.FontWeight = isActive ? FontWeight.ExtraBold : FontWeight.Bold;
        button.Classes.Set("active", isActive);
    }

    private static string FormatBrandedWindowTitle(string windowTitle) => $"{BrandName} — {windowTitle}";

    private static Control BuildBrandBadge()
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                BuildBrandLogo(18d),
                new TextBlock
                {
                    Text = BrandName,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    private static Control BuildBrandLogo(double size)
    {
        var fallback = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2d),
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorderStrong),
            Child = new TextBlock
            {
                Text = "T",
                FontWeight = FontWeight.Bold,
                FontSize = Math.Max(10d, size * 0.55d),
                Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        try
        {
            using var logoStream = AssetLoader.Open(new Uri("avares://MedWNetworkSim.App.Avalonia/Assets/logo.jpg"));
            var bitmap = new Bitmap(logoStream);
            return new Image
            {
                Width = size,
                Height = size,
                Source = bitmap,
                Stretch = Stretch.UniformToFill,
                ClipToBounds = true
            };
        }
        catch
        {
            return fallback;
        }
    }

    private async void HandleAboutRequested(object? sender, EventArgs e)
    {
        await ShowAboutDialogAsync();
    }

    private async Task ShowAboutDialogAsync()
    {
        var dialog = new AboutDialog();
        await dialog.ShowDialog(this);
    }

    private static Control BuildLabeledTextBox(string label, string propertyName)
    {
        var textBox = BuildTextBox(label);
        textBox.Bind(TextBox.TextProperty, new Binding(propertyName, BindingMode.TwoWay));
        return BuildLabeledRow(label, textBox);
    }

    private static Control BuildLabeledInput(string label, string bindingPath)
    {
        var textBox = new TextBox
        {
            Watermark = label,
            MinHeight = 36
        };

        textBox.Bind(TextBox.TextProperty, new Binding(bindingPath)
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });

        AttachFocusBorder(textBox);

        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.MutedText),
                    TextWrapping = TextWrapping.Wrap
                },
                textBox
            }
        };
    }

    private static TextBox BuildBoundTextBox(string propertyName, string watermark)
    {
        var textBox = BuildTextBox(watermark);
        textBox.Bind(TextBox.TextProperty, new Binding(propertyName, BindingMode.TwoWay));
        return textBox;
    }

    private static Control BuildValidatedTextBox(string label, string propertyName, string validationPropertyName)
    {
        var textBox = BuildTextBox(label);
        textBox.Bind(TextBox.TextProperty, new Binding(propertyName, BindingMode.TwoWay));
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                BuildLabeledRow(label, textBox),
                BuildValidationBlock(validationPropertyName)
            }
        };
    }

    private static Border BuildScenarioEditorCard(string title, string summary, Control content)
    {
        return new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    BuildSectionTitle(title, summary),
                    content
                }
            }
        };
    }

    private static Control BuildValidatedScenarioInput(string label, string propertyName, string validationPropertyName)
    {
        var textBox = BuildTextBox(label);
        textBox.MinHeight = 44;
        textBox.Bind(TextBox.TextProperty, new Binding(propertyName, BindingMode.TwoWay));
        var panel = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                BuildLabeledRow(label, textBox)
            }
        };

        if (!string.IsNullOrWhiteSpace(validationPropertyName))
        {
            panel.Children.Add(BuildValidationBlock(validationPropertyName));
        }

        return panel;
    }

    private static Control BuildLabeledTextBox(string label, string propertyName, string isEnabledPropertyName)
    {
        var textBox = BuildTextBox(label);
        textBox.Bind(TextBox.TextProperty, new Binding(propertyName, BindingMode.TwoWay));
        textBox.Bind(IsEnabledProperty, new Binding(isEnabledPropertyName));
        return BuildLabeledRow(label, textBox);
    }

    private static Control BuildLabeledAutoCompleteTextBox(string label, string propertyName, string suggestionsPropertyName, string watermark, string? submitCommandPropertyName = null)
    {
        var textBox = BuildAutoCompleteTextBox(watermark, suggestionsPropertyName);
        textBox.Bind(AutoCompleteTextBox.TextProperty, new Binding(propertyName, BindingMode.TwoWay));
        if (!string.IsNullOrWhiteSpace(submitCommandPropertyName))
        {
            textBox.Bind(AutoCompleteTextBox.SubmitCommandProperty, new Binding(submitCommandPropertyName));
        }

        return BuildLabeledRow(label, textBox);
    }

    private static Control BuildQuickEditLabeledTextBox(string label, string propertyName)
    {
        var textBox = BuildTextBox(label);
        textBox.Bind(TextBox.TextProperty, new Binding(propertyName, BindingMode.TwoWay));
        AttachQuickEditTextBoxBehavior(textBox);
        return BuildLabeledRow(label, textBox);
    }

    private static Control BuildQuickEditLabeledAutoCompleteTextBox(string label, string propertyName, string suggestionsPropertyName, string watermark)
    {
        var textBox = BuildAutoCompleteTextBox(watermark, suggestionsPropertyName);
        textBox.Bind(AutoCompleteTextBox.TextProperty, new Binding(propertyName, BindingMode.TwoWay));
        textBox.Bind(AutoCompleteTextBox.SubmitCommandProperty, new Binding(nameof(WorkspaceViewModel.ApplyInspectorCommand)));
        textBox.SetCurrentValue(AutoCompleteTextBox.RestoreTextOnEscapeProperty, true);
        return BuildLabeledRow(label, textBox);
    }

    private static Control BuildQuickEditLabeledComboBox(string label, string itemsPropertyName, string selectedPropertyName)
    {
        var comboBox = BuildComboBox();
        comboBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(itemsPropertyName));
        comboBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(selectedPropertyName, BindingMode.TwoWay));
        AttachQuickEditComboBoxBehavior(comboBox);
        return BuildLabeledRow(label, comboBox);
    }

    private static Control BuildLabeledComboBox(string label, string itemsPropertyName, string selectedPropertyName)
    {
        var comboBox = BuildComboBox();
        comboBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(itemsPropertyName));
        comboBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(selectedPropertyName, BindingMode.TwoWay));
        return BuildLabeledRow(label, comboBox);
    }

    private static Control BuildLabeledCheckBox(string label, string propertyName)
    {
        var checkBox = BuildCheckBox(label);
        checkBox.Bind(ToggleButton.IsCheckedProperty, new Binding(propertyName, BindingMode.TwoWay));
        return checkBox;
    }

    private static Control BuildLabeledRow(string label, Control editor)
    {
        return new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
                },
                editor
            }
        };
    }

    private static TextBox BuildTextBox(string watermark)
    {
        var textBox = new TextBox
        {
            Watermark = watermark,
            MinHeight = 40,
            Padding = new Thickness(10, 8),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.InputBorder),
            BorderThickness = new Thickness(1.2),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            Background = new SolidColorBrush(AvaloniaDashboardTheme.InputBackground),
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius
        };
        ApplyFocusVisual(textBox);
        return textBox;
    }

    private static void AttachQuickEditTextBoxBehavior(TextBox textBox)
    {
        var initialText = string.Empty;
        textBox.GotFocus += (_, _) => initialText = textBox.Text ?? string.Empty;
        textBox.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter when TryExecuteApplyInspectorCommand(textBox):
                    e.Handled = true;
                    break;

                case Key.Escape:
                    textBox.Text = initialText;
                    e.Handled = true;
                    break;
            }
        };
    }

    private static void AttachQuickEditComboBoxBehavior(ComboBox comboBox)
    {
        object? initialSelection = null;
        comboBox.GotFocus += (_, _) => initialSelection = comboBox.SelectedItem;
        comboBox.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter when TryExecuteApplyInspectorCommand(comboBox):
                    e.Handled = true;
                    break;

                case Key.Escape:
                    comboBox.SelectedItem = initialSelection;
                    e.Handled = true;
                    break;
            }
        };
    }

    private static AutoCompleteTextBox BuildAutoCompleteTextBox(string watermark, string suggestionsPropertyName)
    {
        var textBox = new AutoCompleteTextBox
        {
            Watermark = watermark
        };
        textBox.Bind(AutoCompleteTextBox.SuggestionsProperty, new Binding(suggestionsPropertyName));
        ApplyFocusVisual(textBox);
        return textBox;
    }

    private static bool TryExecuteApplyInspectorCommand(Control control)
    {
        if (control.DataContext is not WorkspaceViewModel viewModel || !viewModel.ApplyInspectorCommand.CanExecute(null))
        {
            return false;
        }

        viewModel.ApplyInspectorCommand.Execute(null);
        return true;
    }

    private static ComboBox BuildComboBox()
    {
        var comboBox = new ComboBox
        {
            MinHeight = 40,
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.InputBorder),
            BorderThickness = new Thickness(1.2),
            Background = new SolidColorBrush(AvaloniaDashboardTheme.InputBackground),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
        };
        ApplyFocusVisual(comboBox);
        return comboBox;
    }

    private static CheckBox BuildCheckBox(string label)
    {
        var checkBox = new CheckBox
        {
            Content = label,
            MinHeight = 40,
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
        };
        ApplyFocusVisual(checkBox);
        return checkBox;
    }

    private static Control BuildValidationBlock(string propertyName)
    {
        var textBlock = new TextBlock
        {
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.Danger),
            TextWrapping = TextWrapping.Wrap
        };
        textBlock.Bind(TextBlock.TextProperty, new Binding(propertyName));
        return textBlock;
    }

    private static Button BuildDestructiveButton(string label, ICommand command, string? toolTip = null)
    {
        var button = BuildButton(label, command, toolTip: toolTip);
        button.Background = new SolidColorBrush(AvaloniaDashboardTheme.Danger);
        button.BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.Danger);
        button.Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText);
        return button;
    }

    private static Control BuildEdgePermissionRulesEditor(WorkspaceViewModel viewModel)
    {
        var addRuleButton = BuildBoundButton("Add rule", nameof(WorkspaceViewModel.AddEdgePermissionRuleCommand));

        var rows = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(WorkspaceViewModel.VisibleEdgePermissionRows)),
            ItemTemplate = new FuncDataTemplate<PermissionRuleEditorRow>((row, _) =>
            {
                var trafficTypeBox = BuildComboBox();
                trafficTypeBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.TrafficTypeNameOptions)) { Source = viewModel });
                trafficTypeBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(PermissionRuleEditorRow.TrafficType), BindingMode.TwoWay));
                ToolTip.SetTip(trafficTypeBox, "Choose which traffic type this route rule applies to.");

                var removeButton = BuildButton("Remove rule", new RelayCommand(() => viewModel.RemoveEdgePermissionRule(row)), toolTip: "Remove this explicit route rule.");

                var wrap = new Border
                {
                    Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelBackground),
                    BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 10),
                    Child = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new Grid
                            {
                                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                                ColumnSpacing = 8,
                                Children =
                                {
                                    BuildLabeledRow("Traffic type", trafficTypeBox),
                                    removeButton
                                }
                            }
                        }
                    }
                };
                Grid.SetColumn(removeButton, 1);

                var content = (StackPanel)wrap.Child!;

                if (row.SupportsOverrideToggle)
                {
                    var overrideBox = BuildCheckBox("Override network default");
                    ToolTip.SetTip(overrideBox, "Enable to apply a route-specific access rule instead of the network default.");
                    overrideBox.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(PermissionRuleEditorRow.IsActive), BindingMode.TwoWay));
                    content.Children.Add(overrideBox);
                }

                var modeBox = BuildComboBox();
                modeBox.ItemsSource = Enum.GetValues<EdgeTrafficPermissionMode>();
                modeBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(PermissionRuleEditorRow.Mode), BindingMode.TwoWay));
                content.Children.Add(BuildLabeledRow("Permission", modeBox));

                var limitKind = BuildComboBox();
                limitKind.ItemsSource = Enum.GetValues<EdgeTrafficLimitKind>();
                limitKind.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(PermissionRuleEditorRow.LimitKind), BindingMode.TwoWay));
                content.Children.Add(BuildLabeledRow("Limit type", limitKind));

                var limitValue = BuildTextBox("Enter a limit");
                limitValue.Bind(TextBox.TextProperty, new Binding(nameof(PermissionRuleEditorRow.LimitValueText), BindingMode.TwoWay));
                content.Children.Add(BuildLabeledRow("Limit value", limitValue));

                var effective = new TextBlock
                {
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
                    TextWrapping = TextWrapping.Wrap
                };
                effective.Bind(TextBlock.TextProperty, new Binding(nameof(PermissionRuleEditorRow.EffectiveSummary)));
                content.Children.Add(effective);

                var validation = new TextBlock
                {
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.Danger),
                    TextWrapping = TextWrapping.Wrap
                };
                validation.Bind(TextBlock.TextProperty, new Binding(nameof(PermissionRuleEditorRow.ValidationMessage)));
                content.Children.Add(validation);

                return wrap;
            })
        };

        var emptyState = new TextBlock
        {
            Text = "No explicit route rules yet. Add a rule to override the network default for a traffic type.",
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),
            TextWrapping = TextWrapping.Wrap
        };
        emptyState.Bind(IsVisibleProperty, new Binding(nameof(WorkspaceViewModel.VisibleEdgePermissionRows.Count))
        {
            Source = viewModel,
            Converter = new FuncValueConverter<int, bool>(count => count == 0)
        });

        return new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    BuildSectionTitle("Permissions", "Add explicit route rules for the traffic types that need route-specific access."),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            addRuleButton
                        }
                    },
                    emptyState,
                    rows
                }
            }
        };
    }

    private static Control BuildEdgeSummaryPanel()
    {
        return BuildEditorSection(
            "Route Summary",
            "Read-only context for safe route edits.",
            BuildReadOnlyRow("Route id", nameof(WorkspaceViewModel.SelectedEdgeIdText)),
            BuildReadOnlyRow("Source node", nameof(WorkspaceViewModel.SelectedEdgeSourceNodeText)),
            BuildReadOnlyRow("Target node", nameof(WorkspaceViewModel.SelectedEdgeTargetNodeText)),
            BuildReadOnlyRow("Direction", nameof(WorkspaceViewModel.SelectedEdgeDirectionSummaryText)),
            BuildReadOnlyRow("Rule status", nameof(WorkspaceViewModel.SelectedEdgeRuleCountText)),
            BuildReadOnlyRow("Validation", nameof(WorkspaceViewModel.SelectedEdgeValidationStatusText)));
    }

    private static void ApplyFocusVisual(Control control)
    {
        void Apply(bool focused)
        {
            var border = new SolidColorBrush(focused ? AvaloniaDashboardTheme.FocusBorder : AvaloniaDashboardTheme.InputBorder);
            var thickness = new Thickness(focused ? 2.3 : 1.2);

            switch (control)
            {
                case TextBox textBox:
                    textBox.BorderBrush = border;
                    textBox.BorderThickness = thickness;
                    break;

                case ComboBox comboBox:
                    comboBox.BorderBrush = border;
                    comboBox.BorderThickness = thickness;
                    break;

                case Slider slider:
                    slider.BorderBrush = border;
                    slider.BorderThickness = thickness;
                    break;

                case ListBox listBox:
                    listBox.BorderBrush = border;
                    listBox.BorderThickness = thickness;
                    break;
            }
        }

        control.GotFocus += (_, _) => Apply(true);
        control.LostFocus += (_, _) => Apply(false);
    }

    private static void AttachFocusBorder(Control control) => ApplyFocusVisual(control);

    private static Control BuildQuickStat(string label, string bindingProperty)
    {
        var value = new TextBlock
        {
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            TextWrapping = TextWrapping.Wrap
        };
        value.Bind(TextBlock.TextProperty, new Binding(bindingProperty));

        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush(AvaloniaDashboardTheme.MutedText),
                    FontSize = 11
                },
                value
            }
        };
    }

    private static Control BuildReportSection<TItem>(
        string title,
        IReadOnlyList<(string Header, double Width)> columns,
        string itemsPropertyName,
        Func<TItem, IReadOnlyList<string>> valueSelector)
    {
        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", columns.Select(column => $"{column.Width}*"))),
            ColumnSpacing = 10
        };

        for (var index = 0; index < columns.Count; index++)
        {
            headerGrid.Children.Add(new TextBlock
            {
                Text = columns[index].Header,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(AvaloniaDashboardTheme.MutedText),
                TextWrapping = TextWrapping.Wrap,
                [Grid.ColumnProperty] = index
            });
        }

        var items = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding(itemsPropertyName),
            ItemTemplate = new FuncDataTemplate<TItem>((item, _) =>
            {
                var values = valueSelector(item);
                var rowGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions(string.Join(",", columns.Select(column => $"{column.Width}*"))),
                    ColumnSpacing = 10,
                    Margin = new Thickness(0, 6, 0, 0)
                };

                for (var index = 0; index < Math.Min(values.Count, columns.Count); index++)
                {
                    rowGrid.Children.Add(new TextBlock
                    {
                        Text = values[index],
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
                        TextWrapping = TextWrapping.Wrap,
                        [Grid.ColumnProperty] = index
                    });
                }

                return new Border
                {
                    Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground),
                    BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 8),
                    Child = rowGrid
                };
            })
        };

        return new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    BuildSectionTitle(title, "Simulation-backed report rows refresh as you run or step the timeline."),
                    headerGrid,
                    items
                }
            }
        };
    }

    private static Control BuildTrafficReportSection()
    {
        var columns = new (string Header, double Width)[]
        {
            ("Traffic", 1.2),
            ("Planned / moved", 1.1),
            (string.Empty, 1.0),
            ("Unmet demand", 1.0),
            ("Backlog", 1.0)
        };
        var section = (Border)BuildReportSection<TrafficReportRowViewModel>(
            "Traffic Summary",
            columns,
            nameof(WorkspaceViewModel.TrafficReports),
            static row => new[]
            {
                row.TrafficType,
                row.PlannedQuantity,
                row.DeliveredQuantity,
                row.UnmetDemand,
                row.Backlog
            });

        var content = (StackPanel)section.Child!;
        var headerGrid = (Grid)content.Children[1];
        if (headerGrid.Children[2] is TextBlock startedHeader)
        {
            startedHeader.Bind(TextBlock.TextProperty, new Binding(nameof(WorkspaceViewModel.TrafficDeliveredColumnLabel)));
        }

        return section;
    }

    private void HandleTopBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        if (e.Source is Visual visual &&
            (visual.FindAncestorOfType<Button>() is not null ||
             visual.FindAncestorOfType<TextBox>() is not null ||
             visual.FindAncestorOfType<ComboBox>() is not null ||
             visual.FindAncestorOfType<Slider>() is not null))
        {
            return;
        }

        BeginMoveDrag(e);
    }

    private async void HandleWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (allowConfirmedClose)
        {
            return;
        }

        e.Cancel = true;
        if (await ConfirmDiscardOrSaveChangesAsync("Save changes before exiting?"))
        {
            allowConfirmedClose = true;
            Close();
        }
    }

    private async Task CloseWithConfirmationAsync()
    {
        if (!await ConfirmDiscardOrSaveChangesAsync("Save changes before exiting?"))
        {
            return;
        }

        allowConfirmedClose = true;
        Close();
    }

    private async Task CreateBlankNetworkAsync(WorkspaceViewModel viewModel)
    {
        if (!await ConfirmDiscardOrSaveChangesAsync("Save changes before creating a new network?"))
        {
            return;
        }

        viewModel.NewCommand.Execute(null);
    }

    private async Task<bool> ConfirmDiscardOrSaveChangesAsync(string message)
    {
        if (!viewModel.HasUnsavedChanges)
        {
            return true;
        }

        var choice = await ShowUnsavedChangesDialogAsync(message);
        return choice switch
        {
            UnsavedChangesChoice.Save => await SaveNetworkAsync(viewModel),
            UnsavedChangesChoice.Discard => true,
            _ => false
        };
    }

    private async Task<UnsavedChangesChoice> ShowUnsavedChangesDialogAsync(string message)
    {
        var dialog = new Window
        {
            Width = 420,
            CanResize = false,
            SystemDecorations = SystemDecorations.None,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome,
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = "Unsaved changes"
        };

        var saveButton = BuildButton("Save", new RelayCommand(() => dialog.Close(UnsavedChangesChoice.Save)), isPrimary: true);
        var discardButton = BuildButton("Discard", new RelayCommand(() => dialog.Close(UnsavedChangesChoice.Discard)));
        var cancelButton = BuildButton("Cancel", new RelayCommand(() => dialog.Close(UnsavedChangesChoice.Cancel)));

        dialog.Content = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorderStrong),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Unsaved changes",
                        FontSize = 20,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                    },
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { saveButton, discardButton, cancelButton }
                    }
                }
            }
        };

        return await dialog.ShowDialog<UnsavedChangesChoice>(this);
    }

    private async Task ConfirmDeleteRouteAsync()
    {
        if (!viewModel.CanDeleteSelectedEdgeEditor)
        {
            return;
        }

        var routeLabel = viewModel.SelectedEdgePreviewTitleText;
        var confirmed = await ShowConfirmationDialogAsync(
            "Delete route",
            $"Delete route '{routeLabel}'? This removes the route and returns to the normal workspace.",
            "Delete Route",
            isDestructive: true);
        if (!confirmed)
        {
            return;
        }

        viewModel.DeleteSelectedEdgeEditorCommand.Execute(null);
    }

    private async Task CloseScenarioEditorWithConfirmationAsync()
    {
        if (!viewModel.IsScenarioEditorWorkspaceMode)
        {
            return;
        }

        if (viewModel.ScenarioEditor.IsDirty)
        {
            var confirmed = await ShowConfirmationDialogAsync(
                "Unsaved scenario changes",
                "Discard unsaved scenario edits and return to the network workspace?",
                "Discard Changes",
                isDestructive: true);
            if (!confirmed)
            {
                return;
            }
        }

        viewModel.CloseScenarioEditorCommand.Execute(null);
        toolRailHost?.Focus();
    }

    private async Task ConfirmDeleteScenarioAsync()
    {
        if (viewModel.ScenarioEditor.SelectedScenarioDefinition is null)
        {
            return;
        }

        var scenarioName = string.IsNullOrWhiteSpace(viewModel.ScenarioEditor.NameText)
            ? "this scenario"
            : viewModel.ScenarioEditor.NameText.Trim();
        var confirmed = await ShowConfirmationDialogAsync(
            "Delete scenario",
            $"Delete '{scenarioName}' and its events? This cannot be undone.",
            "Delete Scenario",
            isDestructive: true);
        if (!confirmed)
        {
            return;
        }

        viewModel.ScenarioEditor.DeleteScenarioCommand.Execute(null);
    }

    private async Task<bool> ShowConfirmationDialogAsync(string title, string message, string confirmLabel, bool isDestructive)
    {
        var dialog = new Window
        {
            Width = 440,
            CanResize = false,
            SystemDecorations = SystemDecorations.None,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome,
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = title
        };

        var confirmButton = isDestructive
            ? BuildDestructiveButton(confirmLabel, new RelayCommand(() => dialog.Close(true)))
            : BuildButton(confirmLabel, new RelayCommand(() => dialog.Close(true)), isPrimary: true);
        var cancelButton = BuildButton("Cancel", new RelayCommand(() => dialog.Close(false)));

        dialog.Content = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorderStrong),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 20,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                    },
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { confirmButton, cancelButton }
                    }
                }
            }
        };

        return await dialog.ShowDialog<bool>(this);
    }

    private async Task ShowErrorDialogAsync(string title, string message)
    {
        var dialog = new Window
        {
            Width = 440,
            CanResize = false,
            SystemDecorations = SystemDecorations.None,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome,
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = title
        };

        var closeButton = BuildButton("OK", new RelayCommand(dialog.Close), isPrimary: true);
        dialog.Content = new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorderStrong),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 20,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.Danger)
                    },
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { closeButton }
                    }
                }
            }
        };

        await dialog.ShowDialog(this);
    }

    private async Task OpenNetworkFileAsync(WorkspaceViewModel viewModel)
    {
        if (!await ConfirmDiscardOrSaveChangesAsync("Save changes before opening another network?"))
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Open network",
            FileTypeFilter =
            [
                new FilePickerFileType("Network JSON") { Patterns = ["*.json"] }
            ]
        });

        var selected = files.FirstOrDefault();
        if (selected is null)
        {
            return;
        }

        try
        {
            viewModel.OpenNetwork(selected.Path.LocalPath);
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("Open failed", ex.Message);
        }
    }

    private async Task<bool> SaveNetworkAsync(WorkspaceViewModel viewModel)
    {
        var path = viewModel.CurrentFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = await PickSaveNetworkPathAsync(viewModel);
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
        }

        try
        {
            viewModel.SaveNetwork(path);
            return true;
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("Save failed", ex.Message);
            return false;
        }
    }

    private async Task<string?> PickSaveNetworkPathAsync(WorkspaceViewModel viewModel)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save network",
            DefaultExtension = "json",
            SuggestedFileName = viewModel.SuggestedFileName,
            FileTypeChoices =
            [
                new FilePickerFileType("Network JSON") { Patterns = ["*.json"] }
            ]
        });

        return file?.Path.LocalPath;
    }

    private async Task ImportGraphMlAsync(WorkspaceViewModel viewModel)
    {
        if (!await ConfirmDiscardOrSaveChangesAsync("Save changes before importing GraphML?"))
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Import GraphML",
            FileTypeFilter =
            [
                new FilePickerFileType("GraphML") { Patterns = ["*.graphml", "*.xml"] }
            ]
        });
        var selected = files.FirstOrDefault();
        if (selected is null)
        {
            return;
        }

        try
        {
            viewModel.ImportGraphMl(selected.Path.LocalPath);
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("Import failed", ex.Message);
        }
    }

    private async Task ExportGraphMlAsync(WorkspaceViewModel viewModel)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export GraphML",
            DefaultExtension = "graphml",
            FileTypeChoices =
            [
                new FilePickerFileType("GraphML") { Patterns = ["*.graphml"] }
            ]
        });
        if (file is null)
        {
            return;
        }

        viewModel.ExportGraphMl(file.Path.LocalPath);
    }

    private async Task ExportCurrentReportAsync(WorkspaceViewModel viewModel, ReportExportFormat format)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export current report",
            DefaultExtension = format == ReportExportFormat.Csv ? "csv" : format == ReportExportFormat.Json ? "json" : "html"
        });
        if (file is null)
        {
            return;
        }

        viewModel.ExportCurrentReport(file.Path.LocalPath, format);
    }

    private async Task ExportTimelineReportAsync(WorkspaceViewModel viewModel, ReportExportFormat format)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export timeline report",
            DefaultExtension = format == ReportExportFormat.Csv ? "csv" : format == ReportExportFormat.Json ? "json" : "html"
        });
        if (file is null)
        {
            return;
        }

        viewModel.ExportTimelineReport(file.Path.LocalPath, 12, format);
    }
}

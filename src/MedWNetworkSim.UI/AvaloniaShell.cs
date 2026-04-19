using System.Diagnostics;
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
using MedWNetworkSim.App.Models;
using MedWNetworkSim.Interaction;
using MedWNetworkSim.Presentation;
using MedWNetworkSim.Rendering;
using SkiaSharp;

namespace MedWNetworkSim.UI;

public sealed class GraphCanvasStatusChangedEventArgs : EventArgs
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required bool IsError { get; init; }
    public required bool HasVisibleFrame { get; init; }
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

public sealed class GraphCanvasControl : Control
{
    public static readonly StyledProperty<WorkspaceViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<GraphCanvasControl, WorkspaceViewModel?>(nameof(ViewModel));

    private readonly GraphRenderer renderer = new();
    private readonly DispatcherTimer animationTimer;
    private WriteableBitmap? bitmap;
    private DateTimeOffset lastFrame = DateTimeOffset.UtcNow;
    private string statusTitle = "Canvas placeholder";
    private string statusDetail = "Waiting for the graph scene.";
    private bool hasVisibleFrame;
    private bool hasError;
    private int statusNotificationVersion;
    private string? hoveredNodeId;

    public GraphCanvasControl()
    {
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
        DetachedFromVisualTree += (_, _) => animationTimer.Stop();
    }

    public event EventHandler<GraphCanvasStatusChangedEventArgs>? StatusChanged;

    public WorkspaceViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        DrawBaseBackground(context);

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
            renderer.Render(surface.Canvas, interactionContext.Scene, interactionContext.Viewport, transform.LogicalViewport);
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
            UpdateStatus("Render error", ex.Message, isError: true, visibleFrame: false);
            DrawStatusPanel(context, "Graph canvas failed to render", ex.Message, isError: true);
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

        if (button == GraphPointerButton.Right)
        {
            ShowContextMenu(ViewModel, interactionContext, point);
            e.Handled = true;
            return;
        }

        ViewModel.InteractionController.OnPointerPressed(
            interactionContext,
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
            items.Add(BuildMenuItem("Edit Route", () => viewModel.SelectRouteForEdit(hit.EdgeId)));
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

        var transform = GetCoordinateTransform();
        ViewModel.InteractionController.OnPointerMoved(
            ViewModel.CreateInteractionContext(transform.LogicalViewport),
            PointerToGraph(e, transform));
        UpdateHoveredNodeToolTip(ViewModel, transform, e);
        ViewModel.NotifyVisualChanged();
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (ViewModel is null)
        {
            return;
        }

        var transform = GetCoordinateTransform();
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
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (ViewModel is null)
        {
            return;
        }

        var transform = GetCoordinateTransform();
        ViewModel.InteractionController.OnPointerWheel(
            ViewModel.CreateInteractionContext(transform.LogicalViewport),
            PointerToGraph(e, transform),
            e.Delta.Y);
        ViewModel.NotifyVisualChanged();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        hoveredNodeId = null;
        ToolTip.SetTip(this, null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (ViewModel is null)
        {
            return;
        }

        var transform = GetCoordinateTransform();
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

    public GraphCanvasCoordinateTransform GetCoordinateTransform()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var renderScale = topLevel?.RenderScaling ?? 1d;
        return GraphCanvasCoordinateTransform.Create(Bounds.Size, renderScale);
    }

    private GraphPoint PointerToGraph(PointerEventArgs e, GraphCanvasCoordinateTransform transform)
    {
        var local = e.GetPosition(this);
        var graphPoint = transform.PointerToGraph(local);

        if (local.X < 0d || local.Y < 0d || local.X > Bounds.Width || local.Y > Bounds.Height)
        {
            LogDebug($"Pointer clamped from ({local.X:0.##},{local.Y:0.##}) to ({graphPoint.X:0.##},{graphPoint.Y:0.##}) within {transform.LogicalViewport.Width:0.##}x{transform.LogicalViewport.Height:0.##}.");
        }

        return graphPoint;
    }

    private void UpdateHoveredNodeToolTip(WorkspaceViewModel viewModel, GraphCanvasCoordinateTransform transform, PointerEventArgs e)
    {
        var interactionContext = viewModel.CreateInteractionContext(transform.LogicalViewport);
        var worldPoint = interactionContext.Viewport.ScreenToWorld(PointerToGraph(e, transform), interactionContext.ViewportSize);
        var hit = new GraphHitTester().HitTest(interactionContext.Scene, worldPoint);
        if (string.Equals(hoveredNodeId, hit.NodeId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        hoveredNodeId = hit.NodeId;
        var tooltipText = interactionContext.Scene.FindNode(hit.NodeId)?.ToolTipText;
        ToolTip.SetTip(this, string.IsNullOrWhiteSpace(tooltipText) ? null : tooltipText);
    }

    private void EnsureBitmap(GraphCanvasCoordinateTransform transform)
    {
        if (bitmap is not null &&
            bitmap.PixelSize.Width == transform.PixelViewport.Width &&
            bitmap.PixelSize.Height == transform.PixelViewport.Height)
        {
            return;
        }

        bitmap = new WriteableBitmap(
            transform.PixelViewport,
            new Vector(96d * transform.ScaleX, 96d * transform.ScaleY),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);
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
        var viewModel = new WorkspaceViewModel();
        DataContext = viewModel;
        Width = 1760;
        Height = 1100;
        MinWidth = 1380;
        MinHeight = 900;
        Background = new SolidColorBrush(AvaloniaDashboardTheme.AppBackground);
        Title = viewModel.WindowTitle;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.WindowTitle))
            {
                Title = viewModel.WindowTitle;
            }
        };

        Content = BuildLayout(viewModel);
    }

    private Control BuildLayout(WorkspaceViewModel viewModel)
    {
        var root = new DockPanel
        {
            Margin = new Thickness(14),
            LastChildFill = true
        };

        var topBar = BuildDashboardPanel(BuildTopBar(viewModel), includeHeader: false, padding: new Thickness(16, 12), radius: new CornerRadius(14));
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);

        var grid = new Grid
        {
            Margin = new Thickness(0, 12, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("240,*,460"),
            RowDefinitions = new RowDefinitions("*,212")
        };

        grid.Children.Add(BuildToolRail(viewModel));
        grid.Children.Add(BuildCanvasArea(viewModel));
        grid.Children.Add(BuildInspector(viewModel));
        grid.Children.Add(BuildBottomStrip(viewModel));

        root.Children.Add(grid);
        return root;
    }

    private static Border BuildDashboardPanel(Control content, string? header = null, bool includeHeader = true, Thickness? padding = null, CornerRadius? radius = null)
    {
        var stack = new StackPanel { Spacing = 8 };
        if (includeHeader && !string.IsNullOrWhiteSpace(header))
        {
            stack.Children.Add(new Border
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

        stack.Children.Add(content);

        return new Border
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground),
            CornerRadius = radius ?? AvaloniaDashboardTheme.PanelCornerRadius,
            Padding = padding ?? new Thickness(12),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            Child = stack
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
                BuildButton("New", viewModel.NewCommand, toolTip: "Create a blank network."),
                BuildButton("Open", new RelayCommand(() => _ = OpenNetworkFileAsync(viewModel)), toolTip: "Open a network JSON file."),
                BuildButton("Save", new RelayCommand(() => _ = SaveNetworkFileAsync(viewModel)), toolTip: "Save the current network JSON."),
                BuildButton("Import", new RelayCommand(() => _ = ImportGraphMlAsync(viewModel)), toolTip: "Import GraphML into the current workspace."),
                BuildButton("Export", new RelayCommand(() => _ = ExportGraphMlAsync(viewModel)), toolTip: "Export the active network as GraphML."),
                BuildButton("Run", viewModel.SimulateCommand, isPrimary: true, toolTip: "Run the simulation timeline."),
                BuildButton("Step", viewModel.StepCommand, isPrimary: true, toolTip: "Advance the simulation by one period."),
                BuildButton("Reset", viewModel.ResetTimelineCommand, toolTip: "Reset timeline to period 0."),
                BuildButton("Fit", viewModel.FitCommand, toolTip: "Fit the graph to the viewport.")
            }
        };
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return grid;
    }

    private static Control BuildToolRail(WorkspaceViewModel viewModel)
    {
        var selectButton = BuildToolButton("Select", "Click to select items, drag selected nodes, and marquee select.", viewModel.SelectToolCommand);
        var addNodeButton = BuildToolButton("Add Node", "Click the canvas to place a new node.", viewModel.AddNodeToolCommand);
        var connectButton = BuildToolButton("Connect", "Choose a source node, then a target node to create a route.", viewModel.ConnectToolCommand);
        var deleteButton = BuildToolButton("Delete", "Delete the current selection.", viewModel.DeleteSelectionCommand);

        void RefreshToolState()
        {
            ApplyToolButtonState(selectButton, viewModel.IsSelectToolActive);
            ApplyToolButtonState(addNodeButton, viewModel.IsAddNodeToolActive);
            ApplyToolButtonState(connectButton, viewModel.IsConnectToolActive);
        }

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(WorkspaceViewModel.IsSelectToolActive) or nameof(WorkspaceViewModel.IsAddNodeToolActive) or nameof(WorkspaceViewModel.IsConnectToolActive))
            {
                RefreshToolState();
            }
        };
        RefreshToolState();

        var border = BuildDashboardPanel(
            new StackPanel
            {
                Spacing = AvaloniaDashboardTheme.SectionSpacing,
                Children =
                {
                    BuildSectionTitle("Tools", "Activate one operation mode for the workspace."),
                    selectButton,
                    addNodeButton,
                    connectButton,
                    deleteButton,
                    BuildSectionTitle("Quick Access", "Current network at a glance."),
                    BuildQuickStat("Status", nameof(WorkspaceViewModel.StatusText)),
                    BuildQuickStat("Selection", nameof(WorkspaceViewModel.SelectionSummary)),
                    BuildQuickStat("Simulation", nameof(WorkspaceViewModel.SimulationSummary))
                }
            },
            header: "Operations Rail",
            padding: new Thickness(10),
            radius: new CornerRadius(14));
        border.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(border, 0);
        return border;
    }

    private static Control BuildCanvasArea(WorkspaceViewModel viewModel)
    {
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

        var canvasSurface = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                header
            }
        };
        Grid.SetRow(graphCanvas, 1);
        Grid.SetRow(fallbackPanel, 1);
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
        return canvasHost;
    }

    private static Control BuildInspector(WorkspaceViewModel viewModel)
    {
        var tabs = new TabControl
        {
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelBackground),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4)
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

        var border = BuildDashboardPanel(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                summaryBlock,
                tabs
            }
        }, header: "Intelligence Rail", padding: new Thickness(14));
        Grid.SetColumn(border, 2);
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
        var definitionList = new ListBox
        {
            Height = 180,
            SelectionMode = SelectionMode.Single
        };
        definitionList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.TrafficDefinitions)));
        definitionList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(WorkspaceViewModel.SelectedTrafficDefinitionItem), BindingMode.TwoWay));
        ApplyFocusVisual(definitionList);

        var routingPreference = BuildLabeledComboBox("Routing preference", nameof(WorkspaceViewModel.RoutingPreferenceOptions), nameof(WorkspaceViewModel.TrafficRoutingPreference));
        ToolTip.SetTip(routingPreference, "Routing preference affects how route costs are prioritized.");
        var allocationMode = BuildLabeledComboBox("Allocation mode", nameof(WorkspaceViewModel.AllocationModeOptions), nameof(WorkspaceViewModel.TrafficAllocationMode));
        ToolTip.SetTip(allocationMode, "Allocation mode controls how available route capacity is assigned.");
        var routeChoiceModel = BuildLabeledComboBox("Route choice model", nameof(WorkspaceViewModel.RouteChoiceModelOptions), nameof(WorkspaceViewModel.TrafficRouteChoiceModel));
        ToolTip.SetTip(routeChoiceModel, "Route choice model defines deterministic or adaptive route behavior.");
        var flowSplitPolicy = BuildLabeledComboBox("Flow split policy", nameof(WorkspaceViewModel.FlowSplitPolicyOptions), nameof(WorkspaceViewModel.TrafficFlowSplitPolicy));
        ToolTip.SetTip(flowSplitPolicy, "Flow split policy controls how flow is divided across eligible routes.");
        var capacityBid = BuildLabeledTextBox("Capacity bid per unit", nameof(WorkspaceViewModel.TrafficCapacityBidText));
        ToolTip.SetTip(capacityBid, "Capacity bid influences route competition when capacity is constrained.");
        var perishability = BuildLabeledTextBox("Perishability periods", nameof(WorkspaceViewModel.TrafficPerishabilityText));
        ToolTip.SetTip(perishability, "Perishability periods is how many periods traffic can remain viable.");

        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    BuildSectionTitle("Traffic Types", "Choose a traffic type, update its routing settings, and manage default route access."),
                    definitionList,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            BuildButton("Add Traffic Type", viewModel.AddTrafficDefinitionCommand),
                            BuildButton("Remove Traffic Type", viewModel.RemoveSelectedTrafficDefinitionCommand),
                            BuildButton("Apply Traffic Type", viewModel.ApplyTrafficDefinitionCommand)
                        }
                    },
                    BuildLabeledTextBox("Traffic type name", nameof(WorkspaceViewModel.TrafficNameText)),
                    BuildLabeledTextBox("Description", nameof(WorkspaceViewModel.TrafficDescriptionText)),
                    routingPreference,
                    allocationMode,
                    routeChoiceModel,
                    flowSplitPolicy,
                    capacityBid,
                    perishability,
                    BuildValidationBlock(nameof(WorkspaceViewModel.TrafficValidationText)),
                    BuildPermissionEditor(
                        "Default Route Access",
                        "Set the default rule each traffic type uses on new and unchanged routes.",
                        nameof(WorkspaceViewModel.DefaultTrafficPermissionRows),
                        edgeCapacityPropertyName: null)
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
            Padding = new Thickness(4)
        };
        tabControl.Items.Add(new TabItem { Header = "Playback", Content = playbackGrid });
        tabControl.Items.Add(new TabItem
        {
            Header = "Reports",
            Content = new StackPanel
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
                        MaxHeight = 128,
                        Content = metrics
                    }
                }
            }
        });
        tabControl.Items.Add(new TabItem
        {
            Header = "Status",
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
        });

        var strip = BuildDashboardPanel(tabControl, header: "Dashboard Strip", padding: new Thickness(14, 10), radius: new CornerRadius(14));
        strip.Margin = new Thickness(12, 10, 0, 0);

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
                    BuildLabeledTextBox("Name", nameof(WorkspaceViewModel.NodeNameText)),
                    BuildLabeledTextBox("Place type", nameof(WorkspaceViewModel.NodePlaceTypeText)),
                    BuildLabeledTextBox("Description", nameof(WorkspaceViewModel.NodeDescriptionText)),
                    BuildLabeledTextBox("Transhipment capacity", nameof(WorkspaceViewModel.NodeTranshipmentCapacityText)),
                    BuildLabeledComboBox("Node shape", nameof(WorkspaceViewModel.NodeShapeOptions), nameof(WorkspaceViewModel.NodeShape)),
                    BuildLabeledComboBox("Node kind", nameof(WorkspaceViewModel.NodeKindOptions), nameof(WorkspaceViewModel.NodeKind)),
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
                    BuildLabeledTextBox("Route label", nameof(WorkspaceViewModel.EdgeRouteTypeText)),
                    BuildLabeledTextBox("Travel time", nameof(WorkspaceViewModel.EdgeTimeText)),
                    BuildLabeledTextBox("Travel cost", nameof(WorkspaceViewModel.EdgeCostText)),
                    BuildLabeledTextBox("Capacity", nameof(WorkspaceViewModel.EdgeCapacityText)),
                    BuildLabeledCheckBox("Bidirectional", nameof(WorkspaceViewModel.EdgeIsBidirectional)),
                    BuildPermissionEditor(
                        "Route Access",
                        "Override the network default for this route when you need a different access rule.",
                        nameof(WorkspaceViewModel.SelectedEdgePermissionRows),
                        nameof(WorkspaceViewModel.EdgeCapacityText))
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
                    BuildLabeledTextBox("Place type", nameof(WorkspaceViewModel.BulkPlaceTypeText)),
                    BuildLabeledTextBox("Transhipment capacity", nameof(WorkspaceViewModel.BulkTranshipmentCapacityText))
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

    private static Control BuildTrafficRoleEditor(out Control focusTarget)
    {
        var trafficTypeBox = BuildComboBox();
        trafficTypeBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.TrafficTypeNameOptions)));
        trafficTypeBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(WorkspaceViewModel.NodeTrafficTypeText), BindingMode.TwoWay));
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
            Padding = new Thickness(12, 9),
            MinHeight = 42,
            Background = new SolidColorBrush(isPrimary ? AvaloniaDashboardTheme.ToolbarButtonPrimaryBackground : AvaloniaDashboardTheme.ToolbarButtonBackground),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.ToolbarButtonBorder),
            BorderThickness = new Thickness(1.2),
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius
        };
        ApplyFocusVisual(button);
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
            Height = 58,
            Content = new TextBlock
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
            },
            Command = command,
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorderStrong),
            BorderThickness = new Thickness(1.2),
            Background = new SolidColorBrush(AvaloniaDashboardTheme.ToolbarButtonBackground),
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius
        };
        ToolTip.SetTip(button, toolTip);
        ApplyFocusVisual(button);
        return button;
    }

    private static void ApplyToolButtonState(Button button, bool isActive)
    {
        button.Background = new SolidColorBrush(isActive ? AvaloniaDashboardTheme.SelectedBackground : AvaloniaDashboardTheme.ToolbarButtonBackground);
        button.BorderBrush = new SolidColorBrush(isActive ? AvaloniaDashboardTheme.Accent : AvaloniaDashboardTheme.PanelBorderStrong);
        button.BorderThickness = new Thickness(isActive ? 2.2 : 1.2);
    }

    private static Control BuildLabeledTextBox(string label, string propertyName)
    {
        var textBox = BuildTextBox(label);
        textBox.Bind(TextBox.TextProperty, new Binding(propertyName, BindingMode.TwoWay));
        return BuildLabeledRow(label, textBox);
    }

    private static Control BuildLabeledTextBox(string label, string propertyName, string isEnabledPropertyName)
    {
        var textBox = BuildTextBox(label);
        textBox.Bind(TextBox.TextProperty, new Binding(propertyName, BindingMode.TwoWay));
        textBox.Bind(IsEnabledProperty, new Binding(isEnabledPropertyName));
        return BuildLabeledRow(label, textBox);
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

    private static void ApplyFocusVisual(Control control)
    {
        void Apply(bool focused)
        {
            var border = new SolidColorBrush(focused ? AvaloniaDashboardTheme.FocusBorder : AvaloniaDashboardTheme.InputBorder);
            var thickness = new Thickness(focused ? 2.3 : 1.2);

            switch (control)
            {
                case Button button:
                    button.BorderBrush = border;
                    button.BorderThickness = thickness;
                    break;

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

    private async Task OpenNetworkFileAsync(WorkspaceViewModel viewModel)
    {
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

        viewModel.OpenNetwork(selected.Path.LocalPath);
    }

    private async Task SaveNetworkFileAsync(WorkspaceViewModel viewModel)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save network",
            DefaultExtension = "json",
            FileTypeChoices =
            [
                new FilePickerFileType("Network JSON") { Patterns = ["*.json"] }
            ]
        });

        if (file is null)
        {
            return;
        }

        viewModel.SaveNetwork(file.Path.LocalPath);
    }

    private async Task ImportGraphMlAsync(WorkspaceViewModel viewModel)
    {
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

        viewModel.ImportGraphMl(selected.Path.LocalPath);
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

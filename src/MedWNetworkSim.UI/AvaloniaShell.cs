using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
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

    public GraphCanvasControl()
    {
        Focusable = true;
        ClipToBounds = true;
        MinHeight = 420;
        MinWidth = 720;

        animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, HandleAnimationTick);
        AttachedToVisualTree += (_, _) =>
        {
            LogDebug("Attached to visual tree.");
            if (!animationTimer.IsEnabled)
            {
                animationTimer.Start();
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            LogDebug("Detached from visual tree.");
            animationTimer.Stop();
        };
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

        LogDebug(
            $"Render() called. Bounds={Bounds.Width:0.##}x{Bounds.Height:0.##}, ViewModelNull={ViewModel is null}, BitmapReady={bitmap is not null}.");

        DrawBaseBackground(context);

        if (ViewModel is null)
        {
            UpdateStatus("Waiting for scene", "ViewModel is null. The shell rendered, but the graph scene has not been attached yet.", isError: false, visibleFrame: false);
            DrawStatusPanel(context, statusTitle, statusDetail, isError: false);
            return;
        }

        if (Bounds.Width <= 0d || Bounds.Height <= 0d)
        {
            UpdateStatus("Canvas placeholder", $"Waiting for layout. Bounds are {Bounds.Width:0.##} x {Bounds.Height:0.##}.", isError: false, visibleFrame: false);
            DrawStatusPanel(context, statusTitle, statusDetail, isError: false);
            return;
        }

        try
        {
            var pixelWidth = Math.Max(1, (int)Math.Ceiling(Bounds.Width));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(Bounds.Height));
            EnsureBitmap(pixelWidth, pixelHeight);
            if (bitmap is null)
            {
                UpdateStatus("Canvas placeholder", "WriteableBitmap creation did not succeed.", isError: true, visibleFrame: false);
                DrawStatusPanel(context, statusTitle, statusDetail, isError: true);
                return;
            }

            var viewportSize = new GraphSize(Bounds.Width, Bounds.Height);
            var interactionContext = ViewModel.CreateInteractionContext(viewportSize);

            using var locked = bitmap.Lock();
            var imageInfo = new SKImageInfo(
                locked.Size.Width,
                locked.Size.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul);

            using var surface = SKSurface.Create(imageInfo, locked.Address, locked.RowBytes);
            if (surface is null)
            {
                throw new InvalidOperationException($"SKSurface.Create returned null for {locked.Size.Width}x{locked.Size.Height} with row bytes {locked.RowBytes}.");
            }

            renderer.Render(surface.Canvas, interactionContext.Scene, interactionContext.Viewport, viewportSize);
            surface.Canvas.Flush();

            context.DrawImage(bitmap, new Rect(0d, 0d, bitmap.PixelSize.Width, bitmap.PixelSize.Height), Bounds);
            UpdateStatus("Canvas rendered", $"Rendered {bitmap.PixelSize.Width} x {bitmap.PixelSize.Height}.", isError: false, visibleFrame: true);
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

        var interactionContext = ViewModel.CreateInteractionContext(new GraphSize(Bounds.Width, Bounds.Height));
        var point = e.GetPosition(this);
        var button = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => GraphPointerButton.Left,
            PointerUpdateKind.MiddleButtonPressed => GraphPointerButton.Middle,
            PointerUpdateKind.RightButtonPressed => GraphPointerButton.Right,
            _ => GraphPointerButton.Left
        };

        ViewModel.InteractionController.OnPointerPressed(
            interactionContext,
            button,
            new GraphPoint(point.X, point.Y),
            e.KeyModifiers.HasFlag(KeyModifiers.Shift),
            e.KeyModifiers.HasFlag(KeyModifiers.Alt),
            e.KeyModifiers.HasFlag(KeyModifiers.Control));
        ViewModel.NotifyVisualChanged();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.InteractionController.OnPointerMoved(
            ViewModel.CreateInteractionContext(new GraphSize(Bounds.Width, Bounds.Height)),
            new GraphPoint(e.GetPosition(this).X, e.GetPosition(this).Y));
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

        var button = e.InitialPressMouseButton switch
        {
            MouseButton.Middle => GraphPointerButton.Middle,
            MouseButton.Right => GraphPointerButton.Right,
            _ => GraphPointerButton.Left
        };

        ViewModel.InteractionController.OnPointerReleased(
            ViewModel.CreateInteractionContext(new GraphSize(Bounds.Width, Bounds.Height)),
            button,
            new GraphPoint(e.GetPosition(this).X, e.GetPosition(this).Y),
            e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        ViewModel.NotifyVisualChanged();
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.InteractionController.OnPointerWheel(
            ViewModel.CreateInteractionContext(new GraphSize(Bounds.Width, Bounds.Height)),
            new GraphPoint(e.GetPosition(this).X, e.GetPosition(this).Y),
            e.Delta.Y);
        ViewModel.NotifyVisualChanged();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (ViewModel is null)
        {
            return;
        }

        if (ViewModel.InteractionController.OnKeyDown(
                ViewModel.CreateInteractionContext(new GraphSize(Bounds.Width, Bounds.Height)),
                e.Key.ToString(),
                e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            ViewModel.NotifyVisualChanged();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void EnsureBitmap(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            bitmap = null;
            LogDebug("Bitmap skipped because requested size was zero.");
            return;
        }

        if (bitmap is not null &&
            bitmap.PixelSize.Width == width &&
            bitmap.PixelSize.Height == height)
        {
            return;
        }

        bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);
        LogDebug($"Bitmap created successfully at {width}x{height}.");
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

    private void DrawBaseBackground(DrawingContext context)
    {
        context.DrawRectangle(
            new SolidColorBrush(Color.Parse("#DCEBFA")),
            new Pen(new SolidColorBrush(Color.Parse("#7FA7C9")), 1),
            Bounds);
    }

    private void DrawStatusPanel(DrawingContext context, string title, string detail, bool isError)
    {
        var panelRect = new Rect(
            Bounds.X + 16d,
            Bounds.Y + 16d,
            Math.Max(0d, Bounds.Width - 32d),
            Math.Max(0d, Bounds.Height - 32d));

        var fill = new SolidColorBrush(Color.Parse(isError ? "#FFF0F0" : "#F7FBFF"));
        var border = new Pen(new SolidColorBrush(Color.Parse(isError ? "#CC5252" : "#4D8AC3")), 2);
        context.DrawRectangle(fill, border, panelRect);

        var titleText = CreateFormattedText(title, 24d, FontWeight.Bold, isError ? "#7F1D1D" : "#163B63");
        var detailText = CreateFormattedText(detail, 14d, FontWeight.Normal, isError ? "#993333" : "#365B7E");

        context.DrawText(titleText, new Point(panelRect.X + 18d, panelRect.Y + 18d));
        context.DrawText(detailText, new Point(panelRect.X + 18d, panelRect.Y + 58d));
    }

    private static FormattedText CreateFormattedText(string text, double fontSize, FontWeight weight, string color)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyle.Normal, weight),
            fontSize,
            new SolidColorBrush(Color.Parse(color)));
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
    private static bool UseDiagnosticCanvasIsolation =>
        string.Equals(Environment.GetEnvironmentVariable("MEDW_AVALONIA_ISOLATION"), "1", StringComparison.Ordinal);

    public ShellWindow()
    {
        var viewModel = new WorkspaceViewModel();
        DataContext = viewModel;
        Width = 1720;
        Height = 1080;
        MinWidth = 1320;
        MinHeight = 860;
        Background = new SolidColorBrush(Color.Parse("#EEF4FA"));
        Title = viewModel.WindowTitle;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.WindowTitle))
            {
                Title = viewModel.WindowTitle;
            }
        };

        try
        {
            Content = BuildLayout(viewModel);
        }
        catch (Exception ex)
        {
            Content = BuildWindowFailureSurface(ex);
        }
    }

    private Control BuildLayout(WorkspaceViewModel viewModel)
    {
        var root = new DockPanel
        {
            Margin = new Thickness(16),
            LastChildFill = true
        };

        var status = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#DAE8F5")),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 10),
            BorderBrush = new SolidColorBrush(Color.Parse("#87A9C7")),
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                Children =
                {
                    BuildBoundText(nameof(WorkspaceViewModel.StatusText), 0),
                    BuildBoundText(nameof(WorkspaceViewModel.SelectionSummary), 1),
                    BuildBoundText(nameof(WorkspaceViewModel.SimulationSummary), 2)
                }
            }
        };
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        var smokeBanner = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FFF4B3")),
            BorderBrush = new SolidColorBrush(Color.Parse("#D2A106")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 12),
            Child = new TextBlock
            {
                Text = "Ready to edit your network. If the graph cannot be displayed, this workspace will show a visible recovery panel.",
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#5E4300"))
            }
        };
        DockPanel.SetDock(smokeBanner, Dock.Top);
        root.Children.Add(smokeBanner);

        var topBar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F8FBFF")),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 14),
            BorderBrush = new SolidColorBrush(Color.Parse("#9CB9D3")),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 12, 0, 0),
            Child = BuildTopBar(viewModel)
        };
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);

        var centerGrid = new Grid
        {
            Margin = new Thickness(0, 16, 0, 16),
            ColumnDefinitions = new ColumnDefinitions("96,*,340"),
            RowDefinitions = new RowDefinitions("*,250")
        };

        centerGrid.Children.Add(BuildToolRail(viewModel));
        centerGrid.Children.Add(BuildCanvasArea(viewModel));
        centerGrid.Children.Add(BuildInspector());
        centerGrid.Children.Add(BuildBottomStrip(viewModel));

        root.Children.Add(centerGrid);
        return root;
    }

    private Control BuildTopBar(WorkspaceViewModel viewModel)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        var titleStack = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "MedW Network Sim Workstation",
                    FontSize = 28,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#16324C"))
                },
                new TextBlock
                {
                    Text = "Ready to edit your network",
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#546A7E"))
                },
                new TextBlock
                {
                    Text = viewModel.TopCommandBar,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#4D6781"))
                }
            }
        };
        grid.Children.Add(titleStack);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                BuildButton("New", viewModel.NewCommand),
                BuildButton("Open", new RelayCommand(() => _ = OpenNetworkFileAsync(viewModel))),
                BuildButton("Save", new RelayCommand(() => _ = SaveNetworkFileAsync(viewModel))),
                BuildButton("Import", new RelayCommand(() => _ = ImportGraphMlAsync(viewModel))),
                BuildButton("Export", new RelayCommand(() => _ = ExportGraphMlAsync(viewModel))),
                BuildButton("Run", viewModel.SimulateCommand),
                BuildButton("Step", viewModel.StepCommand),
                BuildButton("Reset", viewModel.ResetTimelineCommand),
                BuildButton("Fit", viewModel.FitCommand),
                BuildButton("Motion", viewModel.ToggleMotionCommand)
            }
        };
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }

    private static Control BuildToolRail(WorkspaceViewModel viewModel)
    {
        var stack = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Actions",
                    FontSize = 16,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.Parse("#16324C"))
                },
                BuildRailButton("Select", viewModel.SelectInteractionHelpCommand, "Show selection, keyboard, and connection help.", 0),
                BuildRailButton("Add Node", viewModel.AddNodeCommand, "Add a node at the current viewport center.", 1),
                BuildRailButton("Connect", viewModel.ConnectSelectedNodesCommand, "Create a connection from the two selected nodes, or explain how to do it.", 2),
                BuildRailButton("Delete", viewModel.DeleteSelectionCommand, "Delete the current selection.", 3),
                BuildRailButton("Fit", viewModel.FitCommand, "Fit the graph content into view.", 4),
                BuildRailButton("Run", viewModel.SimulateCommand, "Run a static simulation.", 5)
            }
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#E8F0F8")),
            CornerRadius = new CornerRadius(20),
            BorderBrush = new SolidColorBrush(Color.Parse("#9CB9D3")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 14, 0),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    BuildRailButton("Select", "Select and inspect nodes and edges.", viewModel.FitCommand),
                    BuildRailButton("Connect", "Connect two nodes using right-drag on the canvas.", viewModel.FitCommand),
                    BuildRailButton("Add Node", "Press N on the canvas to add a new node.", viewModel.NewCommand),
                    BuildRailButton("Run Sim", "Run static simulation and review report metrics.", viewModel.SimulateCommand),
                    BuildRailButton("Reset", "Reset timeline playback to period 0.", viewModel.ResetTimelineCommand)
                }
            }
        };
        Grid.SetColumn(border, 0);
        return border;
    }

    private static Control BuildInspector()
    {
        var details = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding("Inspector.Details"),
            ItemTemplate = new FuncDataTemplate<string>((item, _) =>
                new TextBlock
                {
                    Text = item,
                    Foreground = new SolidColorBrush(Color.Parse("#31506B")),
                    Margin = new Thickness(0, 0, 0, 8),
                    TextWrapping = TextWrapping.Wrap
                })
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F6FAFE")),
            CornerRadius = new CornerRadius(22),
            BorderBrush = new SolidColorBrush(Color.Parse("#9CB9D3")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18),
            Child = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        [!TextBlock.TextProperty] = new Binding("Inspector.Summary"),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#4D6781"))
                    },
                    details,
                    BuildInspectorEditor()
                }
            }
        };
        Grid.SetColumn(border, 2);
        return border;
    }

    private static Control BuildInspectorEditor()
    {
        var panel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = "Editing",
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#16324C"))
                },
                BuildLabeledTextBox("Name", "InspectorName"),
                BuildLabeledTextBox("Type / Description", "InspectorType"),
                BuildLabeledTextBox("Traffic Type", "SelectedTrafficType"),
                BuildLabeledTextBox("Production", "InspectorProduction"),
                BuildLabeledTextBox("Consumption", "InspectorConsumption"),
                BuildLabeledTextBox("Capacity / Timeline", "InspectorCapacity"),
                BuildLabeledTextBox("Time", "InspectorTime"),
                BuildLabeledTextBox("Cost", "InspectorCost"),
                BuildLabeledTextBox("Bulk place type", "InspectorBulkType"),
                new TextBlock
                {
                    Text = "Use Apply to update network, selected node, selected edge, or bulk selection.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse("#4D6781"))
                },
                new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding("InspectorValidationMessage"),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse("#9D2E2E"))
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        BuildBoundButton("Apply", "ApplyInspectorCommand"),
                        BuildBoundButton("Add Traffic", "AddTrafficTypeCommand"),
                        BuildBoundButton("Remove Traffic", "RemoveTrafficTypeCommand")
                    }
                }
            }
        };

        return new ScrollViewer
        {
            MaxHeight = 420,
            Content = panel
        };
    }

    private static Control BuildBottomStrip(WorkspaceViewModel viewModel)
    {
        var metrics = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding("ReportMetrics"),
            ItemTemplate = new FuncDataTemplate<ReportMetricViewModel>((metric, _) =>
                new Button
                {
                    Content = $"{metric.Label}  {metric.Value}",
                    Margin = new Thickness(0, 0, 10, 10),
                    Command = new RelayCommand(metric.Activate)
                })
        };

        var playbackGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto")
        };
        playbackGrid.Children.Add(BuildButton("Run", viewModel.SimulateCommand));
        playbackGrid.Children.Add(BuildButton("Step", viewModel.StepCommand, 1));
        playbackGrid.Children.Add(BuildButton("Reset", viewModel.ResetTimelineCommand, 2));
        playbackGrid.Children.Add(BuildButton("Fit", viewModel.FitCommand, 3));

        var timelineSlider = new Slider
        {
            Minimum = 0,
            Maximum = 12,
            Margin = new Thickness(12, 6, 0, 0)
        };
        timelineSlider.Bind(RangeBase.ValueProperty, new Binding("TimelinePosition", BindingMode.TwoWay));
        Grid.SetColumn(timelineSlider, 4);
        playbackGrid.Children.Add(timelineSlider);

        var playbackHint = new TextBlock
        {
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = new SolidColorBrush(Color.Parse("#4D6781")),
            Text = "Playback, timeline scrubber, and report entry points stay visible while debugging the canvas."
        };
        Grid.SetRow(playbackHint, 1);
        Grid.SetColumnSpan(playbackHint, 5);
        playbackGrid.Children.Add(playbackHint);

        var tabControl = new TabControl();
        tabControl.Items.Add(
            new TabItem
            {
                Header = "Playback",
                Content = playbackGrid
            });
        tabControl.Items.Add(
            new TabItem
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
                            Content = metrics
                        }
                    }
                }
            });

        var strip = new Border
        {
            Margin = new Thickness(14, 14, 0, 0),
            Background = new SolidColorBrush(Color.Parse("#F6FAFE")),
            CornerRadius = new CornerRadius(20),
            BorderBrush = new SolidColorBrush(Color.Parse("#9CB9D3")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18),
            Child = tabControl
        };

        Grid.SetColumn(strip, 1);
        Grid.SetColumnSpan(strip, 2);
        Grid.SetRow(strip, 1);
        return strip;
    }

    private static Control BuildCanvasArea(WorkspaceViewModel viewModel)
    {
        if (UseDiagnosticCanvasIsolation)
        {
            var isolated = BuildCanvasFallbackPanel(
                "Graph area isolated",
                "The staged isolation panel is active. This confirms the shell layout renders even without the live GraphCanvasControl.",
                isError: false);
            Grid.SetColumn(isolated, 1);
            return isolated;
        }

        try
        {
            var canvasHost = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#DCEAF7")),
                CornerRadius = new CornerRadius(24),
                BorderBrush = new SolidColorBrush(Color.Parse("#7FA7C9")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                MinHeight = 520,
                MinWidth = 760
            };

            var canvasHeader = new Border
            {
                Margin = new Thickness(8, 8, 8, 10),
                Padding = new Thickness(12, 10),
                Background = new SolidColorBrush(Color.Parse("#F8FCFF")),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.Parse("#93B7D7")),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Text = "Canvas ready: select, pan, zoom, drag, marquee select, connect, and keyboard actions are available.",
                    Foreground = new SolidColorBrush(Color.Parse("#284A67")),
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                }
            };

            var graphCanvas = new GraphCanvasControl
            {
                Margin = new Thickness(4),
                ViewModel = viewModel,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var fallbackTitle = new TextBlock
            {
                Text = "Canvas placeholder",
                FontSize = 22,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#7F1D1D"))
            };
            var fallbackDetail = new TextBlock
            {
                Text = "The graph could not be displayed. Try resetting the view or reopening the file.",
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = new SolidColorBrush(Color.Parse("#934040")),
                TextWrapping = TextWrapping.Wrap
            };
            var fallbackPanel = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#FFF2F2")),
                BorderBrush = new SolidColorBrush(Color.Parse("#D37474")),
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
                    canvasHeader
                }
            };
            Grid.SetRow(graphCanvas, 1);
            Grid.SetRow(fallbackPanel, 1);
            canvasSurface.Children.Add(graphCanvas);
            canvasSurface.Children.Add(fallbackPanel);
            canvasHost.Child = canvasSurface;

            Grid.SetColumn(canvasHost, 1);
            return canvasHost;
        }
        catch (Exception ex)
        {
            var failed = BuildCanvasFallbackPanel("Graph canvas failed to initialize", ex.Message, isError: true);
            Grid.SetColumn(failed, 1);
            return failed;
        }
    }

    private static Border BuildCanvasFallbackPanel(string title, string detail, bool isError)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse(isError ? "#FFF2F2" : "#F7FBFF")),
            BorderBrush = new SolidColorBrush(Color.Parse(isError ? "#D37474" : "#7FA7C9")),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(24),
            MinHeight = 520,
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 26,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse(isError ? "#7F1D1D" : "#16324C"))
                    },
                    new TextBlock
                    {
                        Text = detail,
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        MaxWidth = 640,
                        Foreground = new SolidColorBrush(Color.Parse(isError ? "#934040" : "#365B7E"))
                    }
                }
            }
        };
    }

    private static Control BuildWindowFailureSurface(Exception ex)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FFF2F2")),
            BorderBrush = new SolidColorBrush(Color.Parse("#D37474")),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Shell failed during startup",
                        FontSize = 28,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse("#7F1D1D"))
                    },
                    new TextBlock
                    {
                        Text = ex.Message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#8F4040"))
                    }
                }
            }
        };
    }

    private static TextBlock BuildBoundText(string propertyName, int column)
    {
        var text = new TextBlock
        {
            Margin = new Thickness(column == 0 ? 0 : 18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse(column == 0 ? "#16324C" : "#31506B"))
        };
        Grid.SetColumn(text, column);
        text.Bind(TextBlock.TextProperty, new Binding(propertyName));
        return text;
    }

    private static Button BuildButton(string label, ICommand command, int column = -1)
    {
        var button = new Button
        {
            Content = label,
            Command = command,
            Padding = new Thickness(12, 8),
            Background = new SolidColorBrush(Color.Parse("#D9EAF8")),
            Foreground = new SolidColorBrush(Color.Parse("#17324B")),
            BorderBrush = new SolidColorBrush(Color.Parse("#7FA7C9")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };

        if (column >= 0)
        {
            button.SetValue(Grid.ColumnProperty, column);
        }

        return button;
    }

    private static Button BuildBoundButton(string label, string commandPropertyName)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8),
            Background = new SolidColorBrush(Color.Parse("#D9EAF8")),
            Foreground = new SolidColorBrush(Color.Parse("#17324B")),
            BorderBrush = new SolidColorBrush(Color.Parse("#7FA7C9")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };
        button.Bind(Button.CommandProperty, new Binding(commandPropertyName));
        return button;
    }

    private static Button BuildRailButton(string text, string toolTip, ICommand command)
    {
        var button = new Button
        {
            Height = 52,
            Content = new TextBlock
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#355777"))
            },
            ToolTip = toolTip,
            Command = command,
            BorderBrush = new SolidColorBrush(Color.Parse("#93B7D7")),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.Parse("#F8FCFF")),
            CornerRadius = new CornerRadius(14)
        };

        button.Classes.Add("focusable-tool");
        return button;
    }

    private static Control BuildLabeledTextBox(string label, string propertyName)
    {
        var textBox = new TextBox
        {
            Watermark = label
        };
        textBox.Bind(TextBox.TextProperty, new Binding(propertyName, BindingMode.TwoWay));
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#4D6781"))
                },
                textBox
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

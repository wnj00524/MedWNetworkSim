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
using Avalonia.Threading;
using MedWNetworkSim.Interaction;
using MedWNetworkSim.Presentation;
using MedWNetworkSim.Rendering;
using SkiaSharp;
using System.Windows.Input;

namespace MedWNetworkSim.UI;

public sealed class GraphCanvasControl : Control
{
    public static readonly StyledProperty<WorkspaceViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<GraphCanvasControl, WorkspaceViewModel?>(nameof(ViewModel));

    private readonly GraphRenderer renderer = new();
    private readonly DispatcherTimer animationTimer;
    private WriteableBitmap? bitmap;
    private DateTimeOffset lastFrame = DateTimeOffset.UtcNow;

    public GraphCanvasControl()
    {
        Focusable = true;
        ClipToBounds = true;
        animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, HandleAnimationTick);
        animationTimer.Start();
    }

    public WorkspaceViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (ViewModel is null || Bounds.Width <= 0d || Bounds.Height <= 0d)
        {
            return;
        }

        EnsureBitmap((int)Math.Ceiling(Bounds.Width), (int)Math.Ceiling(Bounds.Height));
        if (bitmap is null)
        {
            return;
        }

        var viewportSize = new GraphSize(Bounds.Width, Bounds.Height);
        var interactionContext = ViewModel.CreateInteractionContext(viewportSize);
        using (var locked = bitmap.Lock())
        {
            var imageInfo = new SKImageInfo(locked.Size.Width, locked.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(imageInfo, locked.Address, locked.RowBytes);
            renderer.Render(surface.Canvas, interactionContext.Scene, interactionContext.Viewport, viewportSize);
            surface.Canvas.Flush();
        }

        context.DrawImage(bitmap, new Rect(0d, 0d, bitmap.PixelSize.Width, bitmap.PixelSize.Height), Bounds);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (ViewModel is null)
        {
            return;
        }

        var context = ViewModel.CreateInteractionContext(new GraphSize(Bounds.Width, Bounds.Height));
        var point = e.GetPosition(this);
        var button = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => GraphPointerButton.Left,
            PointerUpdateKind.MiddleButtonPressed => GraphPointerButton.Middle,
            PointerUpdateKind.RightButtonPressed => GraphPointerButton.Right,
            _ => GraphPointerButton.Left
        };

        ViewModel.InteractionController.OnPointerPressed(
            context,
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
            return;
        }

        if (bitmap?.PixelSize.Width == width && bitmap.PixelSize.Height == height)
        {
            return;
        }

        bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
    }

    private void HandleAnimationTick(object? sender, EventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - lastFrame).TotalSeconds;
        lastFrame = now;
        ViewModel.TickAnimation(elapsed);
        InvalidateVisual();
    }
}

public sealed class ShellWindow : Window
{
    public ShellWindow()
    {
        var viewModel = new WorkspaceViewModel();
        DataContext = viewModel;
        Width = 1720;
        Height = 1080;
        MinWidth = 1320;
        MinHeight = 860;
        Background = new SolidColorBrush(Color.Parse("#07111C"));
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

    private static Control BuildLayout(WorkspaceViewModel viewModel)
    {
        var root = new DockPanel
        {
            Margin = new Thickness(18),
            LastChildFill = true
        };

        var status = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0C1B2A")),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 10),
            BorderBrush = new SolidColorBrush(Color.Parse("#1C374D")),
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

        var topBar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0D1624")),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 14),
            BorderBrush = new SolidColorBrush(Color.Parse("#223B52")),
            BorderThickness = new Thickness(1),
            Child = BuildTopBar(viewModel)
        };
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);

        var centerGrid = new Grid
        {
            Margin = new Thickness(0, 18, 0, 18),
            ColumnDefinitions = new ColumnDefinitions("84,*,330"),
            RowDefinitions = new RowDefinitions("*,250")
        };

        centerGrid.Children.Add(BuildToolRail(viewModel));

        var canvasGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                new Border
                {
                    Margin = new Thickness(8, 8, 8, 10),
                    Padding = new Thickness(12, 8),
                    Background = new SolidColorBrush(Color.Parse("#0D1B2A")),
                    CornerRadius = new CornerRadius(12),
                    Child = new TextBlock
                    {
                        Text = "Skia graph canvas | Pan with middle mouse, marquee with drag, connect with right-drag, N adds, F fits, E starts keyboard connect.",
                        Foreground = new SolidColorBrush(Color.Parse("#8FAEC5")),
                        FontSize = 12
                    }
                }
            }
        };
        var graphCanvas = new GraphCanvasControl
        {
            Margin = new Thickness(4),
            ViewModel = viewModel
        };
        Grid.SetRow(graphCanvas, 1);
        canvasGrid.Children.Add(graphCanvas);

        var canvasHost = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#06101A")),
            CornerRadius = new CornerRadius(24),
            BorderBrush = new SolidColorBrush(Color.Parse("#223B52")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Child = canvasGrid
        };
        Grid.SetColumn(canvasHost, 1);
        centerGrid.Children.Add(canvasHost);

        centerGrid.Children.Add(BuildInspector(viewModel));
        centerGrid.Children.Add(BuildBottomStrip(viewModel));

        root.Children.Add(centerGrid);
        return root;
    }

    private static Control BuildTopBar(WorkspaceViewModel viewModel)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };

        var titleStack = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "MedW Network Sim Workstation",
                    FontSize = 24,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#E5EDF6"))
                },
                new TextBlock
                {
                    Text = viewModel.TopCommandBar,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#8DA6BA"))
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
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0B1724")),
            CornerRadius = new CornerRadius(20),
            BorderBrush = new SolidColorBrush(Color.Parse("#223B52")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 14, 0),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    BuildRailBadge("SHELL"),
                    BuildRailBadge("TOOLS"),
                    BuildRailBadge("FLOW"),
                    BuildRailBadge("VIEW"),
                    BuildRailBadge("SIM")
                }
            }
        };
        Grid.SetColumn(border, 0);
        return border;
    }

    private static Control BuildInspector(WorkspaceViewModel viewModel)
    {
        var details = new ItemsControl
        {
            [!ItemsControl.ItemsProperty] = new Binding("Inspector.Details"),
            ItemTemplate = new FuncDataTemplate<string>((item, _) =>
                new TextBlock
                {
                    Text = item,
                    Foreground = new SolidColorBrush(Color.Parse("#A7C0D3")),
                    Margin = new Thickness(0, 0, 0, 8),
                    TextWrapping = TextWrapping.Wrap
                })
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0B1724")),
            CornerRadius = new CornerRadius(22),
            BorderBrush = new SolidColorBrush(Color.Parse("#223B52")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Inspector",
                        FontSize = 20,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse("#E5EDF6"))
                    },
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = new Binding("Inspector.Headline"),
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#F3D38A"))
                    },
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = new Binding("Inspector.Summary"),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#8DA6BA"))
                    },
                    details
                }
            }
        };
        Grid.SetColumn(border, 2);
        return border;
    }

    private static Control BuildBottomStrip(WorkspaceViewModel viewModel)
    {
        var metrics = new ItemsControl
        {
            [!ItemsControl.ItemsProperty] = new Binding("ReportMetrics"),
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
            Foreground = new SolidColorBrush(Color.Parse("#9BB3C7")),
            Text = "Premium workstation strip: playback controls, timeline scrubber, and report entry points."
        };
        Grid.SetRow(playbackHint, 1);
        Grid.SetColumnSpan(playbackHint, 5);
        playbackGrid.Children.Add(playbackHint);

        var strip = new Border
        {
            Margin = new Thickness(14, 14, 0, 0),
            Background = new SolidColorBrush(Color.Parse("#0B1724")),
            CornerRadius = new CornerRadius(20),
            BorderBrush = new SolidColorBrush(Color.Parse("#223B52")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18),
            Child = new TabControl
            {
                Items = new[]
                {
                    new TabItem
                    {
                        Header = "Playback",
                        Content = playbackGrid
                    },
                    new TabItem
                    {
                        Header = "Reports",
                        Content = new ScrollViewer
                        {
                            Content = metrics
                        }
                    }
                }
            }
        };

        Grid.SetColumn(strip, 1);
        Grid.SetColumnSpan(strip, 2);
        Grid.SetRow(strip, 1);
        return strip;
    }

    private static TextBlock BuildBoundText(string propertyName, int column)
    {
        var text = new TextBlock
        {
            Margin = new Thickness(column == 0 ? 0 : 18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse(column == 0 ? "#E5EDF6" : "#9FB7CB"))
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
            Background = new SolidColorBrush(Color.Parse("#153047")),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#2A516D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };

        if (column >= 0)
        {
            button.SetValue(Grid.ColumnProperty, column);
        }

        return button;
    }

    private static Border BuildRailBadge(string text)
    {
        return new Border
        {
            Height = 52,
            Background = new SolidColorBrush(Color.Parse("#13283C")),
            CornerRadius = new CornerRadius(14),
            Child = new TextBlock
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#8FB6D0"))
            }
        };
    }
}

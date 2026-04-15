using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MedWNetworkSim.App.ViewModels;

namespace MedWNetworkSim.App;

public partial class MainWindow : Window
{
    private const double MinimumCanvasZoom = 0.2d;
    private const double MaximumCanvasZoom = 3.0d;
    private const double CanvasZoomStep = 1.1d;

    private Point? pendingCanvasContextMenuPosition;
    private NodeViewModel? edgeCreationSourceNode;
    private bool edgeCreationIsBidirectional = true;
    private double canvasZoom = 1d;
    private bool isPanningCanvas;
    private Point panStartViewerPosition;
    private double panStartHorizontalOffset;
    private double panStartVerticalOffset;
    private Cursor? previousCanvasCursor;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel();
        DataContext = ViewModel;
    }

    public MainWindowViewModel ViewModel { get; }

    private void NewNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (!TryConfirmDiscardOrSaveChanges("create a new world"))
        {
            return;
        }

        ExecuteWithErrorHandling(ViewModel.CreateNewNetwork);
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (!TryConfirmDiscardOrSaveChanges("open another world"))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Open network file",
            Filter = "JSON network (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ExecuteWithErrorHandling(() => ViewModel.LoadFromFile(dialog.FileName));
    }

    private void GraphMl_Click(object sender, RoutedEventArgs e)
    {
        var window = new GraphMlTransferWindow(ViewModel)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void EmbedSubnetwork_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Embed subnetwork JSON",
            Filter = "JSON network (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ExecuteWithErrorHandling(() => ViewModel.AddSubnetworkFromFile(dialog.FileName));
    }

    private void NetworkProperties_Click(object sender, RoutedEventArgs e)
    {
        var window = new NetworkPropertiesWindow(ViewModel)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void Reports_Click(object sender, RoutedEventArgs e)
    {
        var window = new ReportExportWindow(ViewModel)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        TrySaveAs();
    }

    private void QuickSave_Click(object sender, RoutedEventArgs e)
    {
        TryQuickSave();
    }

    private void LoadSample_Click(object sender, RoutedEventArgs e)
    {
        if (!TryConfirmDiscardOrSaveChanges("load the bundled sample"))
        {
            return;
        }

        ExecuteWithErrorHandling(ViewModel.LoadBundledSample);
    }

    private void LoadWorldbuilderScenario_Click(object sender, RoutedEventArgs e)
    {
        if (!TryConfirmDiscardOrSaveChanges("load another scenario"))
        {
            return;
        }

        ExecuteWithErrorHandling(ViewModel.LoadSelectedWorldbuilderScenario);
    }

    private void RunSimulation_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.RunSimulation);
    }

    private void ResetTimeline_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.ResetTimeline);
    }

    private void NextTimelinePeriod_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.AdvanceTimeline);
    }



    private void AutoArrange_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.AutoArrangeNodes);
    }

    private void ToggleCanvasOnly_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.ToggleCanvasOnlyMode);
    }

    private void ToggleLayers_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.ToggleLayersPanel);
    }

    private void ToggleInspector_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.ToggleInspectorPanel);
    }

    private void ToggleReportsDrawer_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.ToggleReportsDrawer);
    }

    private void ToggleLegend_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.ToggleLegendPanel);
    }

    private void AddTrafficType_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.AddTrafficDefinition);
    }

    private void EditTrafficTypes_Click(object sender, RoutedEventArgs e)
    {
        OpenTrafficTypeEditorWindow();
    }

    private void RemoveTrafficType_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.RemoveSelectedTrafficDefinition);
    }

    private void AddNode_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.AddNode);
    }

    private void AddNodeFromTemplate_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(() =>
        {
            ViewModel.AddNodeFromSelectedTemplate();
            OpenNodeEditorWindow();
        });
    }

    private void CanvasAddNode_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(() =>
        {
            var position = pendingCanvasContextMenuPosition;
            ViewModel.AddNodeAt(position?.X, position?.Y);
            OpenNodeEditorWindow();
        });
    }

    private void EditSelectedNode_Click(object sender, RoutedEventArgs e)
    {
        OpenNodeEditorWindow();
    }

    private void ApplyTrafficRoleToAllNodes_Click(object sender, RoutedEventArgs e)
    {
        var window = new BulkApplyTrafficRoleWindow(ViewModel)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void EditEdges_Click(object sender, RoutedEventArgs e)
    {
        OpenEdgeEditorWindow();
    }

    private void RemoveNode_Click(object sender, RoutedEventArgs e)
    {
        ConfirmAndDeleteSelectedItem();
    }

    private void AddTrafficProfile_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.AddTrafficProfileToSelectedNode);
    }

    private void RemoveTrafficProfile_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.RemoveSelectedTrafficProfileFromNode);
    }

    private void AddEdge_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.AddEdge);
    }

    private void RemoveEdge_Click(object sender, RoutedEventArgs e)
    {
        ConfirmAndDeleteSelectedItem();
    }

    private void NodeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (edgeCreationSourceNode is not null)
        {
            return;
        }

        if (sender is Thumb { DataContext: NodeViewModel node })
        {
            ViewModel.MoveNode(node, e.HorizontalChange, e.VerticalChange);
        }
    }

    private void NodeThumb_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Thumb { DataContext: NodeViewModel node })
        {
            ViewModel.SelectedNode = node;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                BeginEdgeCreation(node, isBidirectional: true);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                BeginEdgeCreation(node, isBidirectional: false);
                e.Handled = true;
            }
        }
    }

    private void EdgeVisual_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EdgeViewModel edge })
        {
            ViewModel.SelectedEdge = edge;
            e.Handled = true;
        }
    }

    private void NodeThumb_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (isPanningCanvas || edgeCreationSourceNode is null)
        {
            return;
        }
    }

    private void NetworkCanvasGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || edgeCreationSourceNode is not null)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject hitTarget)
        {
            return;
        }

        var node = FindDataContext<NodeViewModel>(hitTarget);
        if (node is not null)
        {
            ViewModel.SelectedNode = node;
            OpenNodeEditorWindow();
            e.Handled = true;
            return;
        }

        var edge = FindDataContext<EdgeViewModel>(hitTarget);
        if (edge is not null)
        {
            ViewModel.SelectedEdge = edge;
            OpenEdgeEditorWindow();
            e.Handled = true;
            return;
        }

        ExecuteWithErrorHandling(() =>
        {
            var position = e.GetPosition(NetworkCanvasGrid);
            ViewModel.AddNodeAt(Math.Max(0d, position.X), Math.Max(0d, position.Y));
            OpenNodeEditorWindow();
        });
        e.Handled = true;
    }

    private void NetworkCanvasGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not IInputElement canvasGrid)
        {
            pendingCanvasContextMenuPosition = null;
            return;
        }

        var position = e.GetPosition(canvasGrid);
        pendingCanvasContextMenuPosition = new Point(
            Math.Max(0d, position.X),
            Math.Max(0d, position.Y));

        if (e.OriginalSource is not DependencyObject hitTarget)
        {
            return;
        }

        var node = FindDataContext<NodeViewModel>(hitTarget);
        if (node is not null)
        {
            ViewModel.SelectedNode = node;
            ViewModel.SelectedEdge = null;
            OpenNodeContextMenu(node);
            e.Handled = true;
            return;
        }

        var edge = FindDataContext<EdgeViewModel>(hitTarget);
        if (edge is not null)
        {
            ViewModel.SelectedEdge = edge;
            ViewModel.SelectedNode = null;
            OpenEdgeContextMenu(edge);
            e.Handled = true;
            return;
        }

        ViewModel.SelectedNode = null;
        ViewModel.SelectedEdge = null;
        OpenBlankCanvasContextMenu();
        e.Handled = true;
    }

    private void NetworkCanvasGrid_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var previousZoom = canvasZoom;
        var zoomFactor = e.Delta > 0 ? CanvasZoomStep : 1d / CanvasZoomStep;
        var nextZoom = Math.Clamp(previousZoom * zoomFactor, MinimumCanvasZoom, MaximumCanvasZoom);
        if (Math.Abs(nextZoom - previousZoom) < 0.0001d)
        {
            e.Handled = true;
            return;
        }

        var contentPosition = e.GetPosition(NetworkCanvasGrid);
        var viewportPosition = e.GetPosition(NetworkCanvasScrollViewer);
        canvasZoom = nextZoom;
        NetworkCanvasScaleTransform.ScaleX = nextZoom;
        NetworkCanvasScaleTransform.ScaleY = nextZoom;

        NetworkCanvasScrollViewer.ScrollToHorizontalOffset((contentPosition.X * nextZoom) - viewportPosition.X);
        NetworkCanvasScrollViewer.ScrollToVerticalOffset((contentPosition.Y * nextZoom) - viewportPosition.Y);
        e.Handled = true;
    }

    private void NetworkCanvasScrollViewer_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (TryBeginCanvasPan(e))
        {
            e.Handled = true;
        }
    }

    private void NetworkCanvasScrollViewer_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (UpdateCanvasPan(e))
        {
            e.Handled = true;
        }
    }

    private void NetworkCanvasScrollViewer_OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!isPanningCanvas)
        {
            return;
        }

        if (e.ChangedButton is MouseButton.Left or MouseButton.Middle)
        {
            EndCanvasPan();
            e.Handled = true;
        }
    }

    private void NetworkCanvasScrollViewer_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        EndCanvasPan();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!TryConfirmDiscardOrSaveChanges("close the app"))
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key != Key.Delete || IsTextInputElement(e.OriginalSource))
        {
            return;
        }

        if (ConfirmAndDeleteSelectedItem())
        {
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);

        if (isPanningCanvas || edgeCreationSourceNode is null)
        {
            return;
        }

        var position = e.GetPosition(NetworkCanvasGrid);
        EdgeCreationPreviewLine.X2 = Math.Max(0d, position.X);
        EdgeCreationPreviewLine.Y2 = Math.Max(0d, position.Y);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);

        if (isPanningCanvas || edgeCreationSourceNode is null)
        {
            return;
        }

        var targetNode = FindNodeAt(e.GetPosition(NetworkCanvasGrid));
        if (targetNode is not null && !ReferenceEquals(edgeCreationSourceNode, targetNode))
        {
            var sourceNode = edgeCreationSourceNode;
            var isBidirectional = edgeCreationIsBidirectional;
            ExecuteWithErrorHandling(() => ViewModel.AddEdgeBetween(sourceNode, targetNode, isBidirectional));
        }

        EndEdgeCreation();
    }

    private void BeginEdgeCreation(NodeViewModel sourceNode, bool isBidirectional)
    {
        EndCanvasPan();
        edgeCreationSourceNode = sourceNode;
        edgeCreationIsBidirectional = isBidirectional;
        EdgeCreationPreviewLine.X1 = sourceNode.CenterX;
        EdgeCreationPreviewLine.Y1 = sourceNode.CenterY;
        EdgeCreationPreviewLine.X2 = sourceNode.CenterX;
        EdgeCreationPreviewLine.Y2 = sourceNode.CenterY;
        EdgeCreationPreviewLine.Visibility = Visibility.Visible;
        Mouse.Capture(NetworkCanvasGrid);
    }

    private void BeginEdgeCreationFromContext(NodeViewModel node)
    {
        ViewModel.SelectedNode = node;
        ViewModel.SelectedEdge = null;
        Dispatcher.BeginInvoke(new Action(() => BeginEdgeCreation(node, isBidirectional: true)));
    }

    private void EndEdgeCreation()
    {
        edgeCreationSourceNode = null;
        edgeCreationIsBidirectional = true;
        EdgeCreationPreviewLine.Visibility = Visibility.Collapsed;

        if (Mouse.Captured == NetworkCanvasGrid)
        {
            Mouse.Capture(null);
        }
    }

    private bool TryBeginCanvasPan(MouseButtonEventArgs e)
    {
        if (isPanningCanvas || edgeCreationSourceNode is not null)
        {
            return false;
        }

        var isMiddlePan = e.ChangedButton == MouseButton.Middle;
        var isSpaceLeftPan = e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space);
        if (!isMiddlePan && !isSpaceLeftPan)
        {
            return false;
        }

        if (e.OriginalSource is not DependencyObject hitTarget ||
            !IsDescendantOf(hitTarget, NetworkCanvasGrid) ||
            FindDataContext<NodeViewModel>(hitTarget) is not null ||
            FindDataContext<EdgeViewModel>(hitTarget) is not null)
        {
            return false;
        }

        isPanningCanvas = true;
        panStartViewerPosition = e.GetPosition(NetworkCanvasScrollViewer);
        panStartHorizontalOffset = NetworkCanvasScrollViewer.HorizontalOffset;
        panStartVerticalOffset = NetworkCanvasScrollViewer.VerticalOffset;
        previousCanvasCursor = NetworkCanvasScrollViewer.Cursor;
        NetworkCanvasScrollViewer.Cursor = Cursors.ScrollAll;
        Mouse.Capture(NetworkCanvasScrollViewer);
        return true;
    }

    private bool UpdateCanvasPan(MouseEventArgs e)
    {
        if (!isPanningCanvas)
        {
            return false;
        }

        var position = e.GetPosition(NetworkCanvasScrollViewer);
        var delta = position - panStartViewerPosition;
        NetworkCanvasScrollViewer.ScrollToHorizontalOffset(panStartHorizontalOffset - delta.X);
        NetworkCanvasScrollViewer.ScrollToVerticalOffset(panStartVerticalOffset - delta.Y);
        return true;
    }

    private void EndCanvasPan()
    {
        if (!isPanningCanvas)
        {
            return;
        }

        isPanningCanvas = false;
        NetworkCanvasScrollViewer.Cursor = previousCanvasCursor;
        previousCanvasCursor = null;

        if (Mouse.Captured == NetworkCanvasScrollViewer)
        {
            Mouse.Capture(null);
        }
    }

    private NodeViewModel? FindNodeAt(Point position)
    {
        var hitResult = VisualTreeHelper.HitTest(NetworkCanvasGrid, position);
        var current = hitResult?.VisualHit as DependencyObject;

        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: NodeViewModel node })
            {
                return node;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindDataContext<T>(DependencyObject? current)
        where T : class
    {
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: T target })
            {
                return target;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool IsDescendantOf(DependencyObject? current, DependencyObject ancestor)
    {
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void OpenNodeEditorWindow()
    {
        // The dedicated window keeps node-role editing simpler than trying to fit every selector into the main pane.
        var window = new NodeEditorWindow(ViewModel)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void OpenEdgeEditorWindow()
    {
        var window = new EdgeEditorWindow(new EdgeEditorViewModel(ViewModel))
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void OpenTrafficTypeEditorWindow()
    {
        var window = new TrafficTypeEditorWindow(new TrafficTypeEditorViewModel(ViewModel))
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void OpenBlankCanvasContextMenu()
    {
        OpenCanvasContextMenu(
            CreateMenuItem("Add node here", (_, _) => CanvasAddNode_Click(this, new RoutedEventArgs())));
    }

    private void OpenNodeContextMenu(NodeViewModel node)
    {
        OpenCanvasContextMenu(
            CreateMenuItem("Edit node", (_, _) => OpenNodeEditorWindow()),
            CreateMenuItem("Add edge from this node", (_, _) => BeginEdgeCreationFromContext(node)),
            CreateMenuItem("Delete node", (_, _) => ConfirmAndDeleteSelectedItem()));
    }

    private void OpenEdgeContextMenu(EdgeViewModel edge)
    {
        OpenCanvasContextMenu(
            CreateMenuItem("Edit edge", (_, _) => OpenEdgeEditorWindow()),
            CreateMenuItem("Delete edge", (_, _) => ConfirmAndDeleteSelectedItem()));
    }

    private void OpenCanvasContextMenu(params MenuItem[] items)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = NetworkCanvasGrid
        };

        foreach (var item in items)
        {
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private static MenuItem CreateMenuItem(string header, RoutedEventHandler clickHandler)
    {
        var item = new MenuItem
        {
            Header = header
        };

        item.Click += clickHandler;
        return item;
    }

    private bool ConfirmAndDeleteSelectedItem()
    {
        if (ViewModel.SelectedEdge is not null)
        {
            var edgeDescription = DescribeEdge(ViewModel.SelectedEdge);
            if (ConfirmDeletion($"Delete edge {edgeDescription}?"))
            {
                ExecuteWithErrorHandling(ViewModel.RemoveSelectedEdge);
                return true;
            }

            return false;
        }

        if (ViewModel.SelectedNode is not null)
        {
            var nodeName = string.IsNullOrWhiteSpace(ViewModel.SelectedNode.Name)
                ? ViewModel.SelectedNode.Id
                : ViewModel.SelectedNode.Name;
            if (ConfirmDeletion($"Delete node '{nodeName}' and any connected edges?"))
            {
                ExecuteWithErrorHandling(ViewModel.RemoveSelectedNode);
                return true;
            }
        }

        return false;
    }

    private bool ConfirmDeletion(string message)
    {
        return MessageBox.Show(
            this,
            message,
            "Confirm deletion",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private static string DescribeEdge(EdgeViewModel edge)
    {
        if (!string.IsNullOrWhiteSpace(edge.RouteType))
        {
            return $"'{edge.RouteType.Trim()}' ({edge.FromNodeId} to {edge.ToNodeId})";
        }

        return $"from '{edge.FromNodeId}' to '{edge.ToNodeId}'";
    }

    private bool TryConfirmDiscardOrSaveChanges(string actionDescription)
    {
        if (!ViewModel.HasUnsavedChanges)
        {
            return true;
        }

        var result = MessageBox.Show(
            this,
            $"Save changes before you {actionDescription}?",
            "Unsaved changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes => TryQuickSave(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private bool TrySaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save network file",
            Filter = "JSON network (*.json)|*.json|All files (*.*)|*.*",
            FileName = ViewModel.SuggestedFileName,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        return TrySaveToFile(dialog.FileName);
    }

    private bool TryQuickSave()
    {
        var path = ViewModel.CurrentFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = PromptForQuickSavePath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
        }

        if (File.Exists(path) &&
            MessageBox.Show(
                this,
                $"Overwrite '{Path.GetFileName(path)}'?",
                "Confirm overwrite",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return false;
        }

        return TrySaveToFile(path);
    }

    private string? PromptForQuickSavePath()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Quick save network file",
            Filter = "JSON network (*.json)|*.json|All files (*.*)|*.*",
            FileName = ViewModel.SuggestedFileName,
            OverwritePrompt = false
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private bool TrySaveToFile(string path)
    {
        try
        {
            ViewModel.SaveToFile(path);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "MedW Network Simulator",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private static bool IsTextInputElement(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is TextBoxBase or PasswordBox or ComboBox)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void ExecuteWithErrorHandling(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "MedW Network Simulator",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

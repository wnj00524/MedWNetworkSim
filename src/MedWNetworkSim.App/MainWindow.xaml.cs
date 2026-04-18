using Microsoft.Win32;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MedWNetworkSim.App.Import;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.ViewModels;

namespace MedWNetworkSim.App;

public partial class MainWindow : Window
{
    private const double MinimumCanvasZoom = 0.2d;
    private const double MaximumCanvasZoom = 3.0d;
    private const double CanvasZoomStep = 1.1d;
    private const double KeyboardMoveStep = 16d;
    private const double KeyboardPanStep = 36d;

    private Point? pendingCanvasContextMenuPosition;
    private NodeViewModel? edgeCreationSourceNode;
    private EdgeViewModel? focusedKeyboardEdge;
    private bool edgeCreationIsBidirectional = true;
    private bool isGraphKeyboardMode;
    private double canvasZoom = 1d;
    private bool isPanningCanvas;
    private Point panStartViewerPosition;
    private double panStartHorizontalOffset;
    private double panStartVerticalOffset;
    private Cursor? previousCanvasCursor;
    private readonly OsmImporter osmImporter = new();

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel();
        DataContext = ViewModel;
        SetDefaultCanvasHint();
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

        ExecuteWithErrorHandling(() => PreserveViewportAcrossWorkspaceShift(() => ViewModel.LoadFromFile(dialog.FileName)));
    }

    private async void ImportOsm_Click(object sender, RoutedEventArgs e)
    {
        if (!TryConfirmDiscardOrSaveChanges("import an OSM map"))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import OpenStreetMap file",
            Filter = "OpenStreetMap files (*.osm;*.pbf)|*.osm;*.pbf|OSM XML (*.osm)|*.osm|OSM PBF (*.pbf)|*.pbf|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var optionsWindow = new OsmImportOptionsWindow
        {
            Owner = this
        };

        if (optionsWindow.ShowDialog() != true)
        {
            return;
        }

        var importOptions = optionsWindow.ImportOptions ?? new OsmImportOptions();

        try
        {
            ViewModel.StartOsmImport();

            var progress = new Progress<OsmImportProgress>(ViewModel.ReportOsmImportProgress);
            var importedNetwork = await Task.Run(
                () => osmImporter.ImportFromFileAsync(dialog.FileName, importOptions, progress),
                CancellationToken.None);

            PreserveViewportAcrossWorkspaceShift(() => ViewModel.LoadImportedNetwork(importedNetwork, dialog.FileName));
            await Dispatcher.InvokeAsync(() =>
            {
                FitCanvasToNetwork();
                SetCanvasHint("OpenStreetMap import complete. Network auto-fit to canvas.");
            });

            MessageBox.Show(
                this,
                $"Imported '{Path.GetFileName(dialog.FileName)}' as a network with {ViewModel.Nodes.Count} place(s) and {ViewModel.Edges.Count} route(s).",
                "OpenStreetMap import complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Could not import '{Path.GetFileName(dialog.FileName)}'.{Environment.NewLine}{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}Check that the file is a valid .osm or .pbf extract. If the map is large, try a lower retention target and import again.",
                "OpenStreetMap import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ViewModel.FinishOsmImport();
        }
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

        ExecuteWithErrorHandling(() => PreserveViewportAcrossWorkspaceShift(() => ViewModel.AddSubnetworkFromFile(dialog.FileName)));
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

    private void ExportCurrentReportShell_Click(object sender, RoutedEventArgs e)
    {
        var window = new ReportExportWindow(ViewModel, ReportExportKind.Current)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void ExportTimelineReportShell_Click(object sender, RoutedEventArgs e)
    {
        var window = new ReportExportWindow(ViewModel, ReportExportKind.Timeline)
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

        ExecuteWithErrorHandling(() => PreserveViewportAcrossWorkspaceShift(ViewModel.LoadBundledSample));
    }

    private void LoadWorldbuilderScenario_Click(object sender, RoutedEventArgs e)
    {
        if (!TryConfirmDiscardOrSaveChanges("load another scenario"))
        {
            return;
        }

        ExecuteWithErrorHandling(() => PreserveViewportAcrossWorkspaceShift(ViewModel.LoadSelectedWorldbuilderScenario));
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
        ExecuteWithErrorHandling(() => PreserveViewportAcrossWorkspaceShift(ViewModel.AutoArrangeNodes));
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
        ExecuteWithErrorHandling(() => PreserveViewportAcrossWorkspaceShift(ViewModel.AddNode));
    }

    private void AddNodeFromTemplate_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(() =>
        {
            PreserveViewportAcrossWorkspaceShift(ViewModel.AddNodeFromSelectedTemplate);
            OpenNodeEditorWindow();
        });
    }

    private void CanvasAddNode_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(() =>
        {
            var position = pendingCanvasContextMenuPosition;
            PreserveViewportAcrossWorkspaceShift(() => ViewModel.AddNodeAt(position?.X, position?.Y));
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
            PreserveViewportAcrossWorkspaceShift(() => ViewModel.MoveNode(node, e.HorizontalChange, e.VerticalChange));
        }
    }

    private void NodeThumb_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Thumb { DataContext: NodeViewModel node })
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                ViewModel.ToggleBulkNodeSelection(node);
                SetCanvasHint("Bulk selection updated. Open the inspector to review shared edits or launch bulk edit.");
                e.Handled = true;
                return;
            }

            ViewModel.SelectedNode = node;
            FocusKeyboardNode(node);
            SetCanvasHint("Place selected. Enter edits. Ctrl+Arrow moves. E starts a route. Shift+F10 opens actions.");

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
            FocusKeyboardEdge(edge);
            e.Handled = true;
        }
    }

    private void NodeThumb_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not Thumb { DataContext: NodeViewModel node })
        {
            return;
        }

        FocusKeyboardNode(node);
    }

    private void NodeThumb_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Thumb { DataContext: NodeViewModel node })
        {
            return;
        }

        if (!isGraphKeyboardMode || IsTextInputElement(e.OriginalSource))
        {
            ActivateGraphKeyboardMode();
        }

        ViewModel.SelectedNode = node;
        FocusKeyboardNode(node);

        if (e.Key == Key.Enter)
        {
            OpenNodeEditorWindow();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ViewModel.ToggleBulkNodeSelection(node);
            SetCanvasHint("Bulk selection updated for the focused place.");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            SetCanvasHint("Place selected. Enter edits. E starts a route. Shift+F10 opens actions.");
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) || e.Key == Key.Apps)
        {
            OpenNodeContextMenu(node);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            (e.Key is Key.Left or Key.Right or Key.Up or Key.Down))
        {
            MoveNodeFromKeyboard(node, e.Key);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.E)
        {
            BeginEdgeCreation(node, isBidirectional: true);
            SetCanvasHint("Route creation mode. Move to a second place and press Enter or click. Esc cancels.");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            EndEdgeCreation();
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
        ActivateGraphKeyboardMode();

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
            var position = ToWorldCoordinates(e.GetPosition(NetworkCanvasGrid));
            PreserveViewportAcrossWorkspaceShift(() => ViewModel.AddNodeAt(position.X, position.Y));
            OpenNodeEditorWindow();
        });
        e.Handled = true;
    }

    private void NetworkCanvasGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        ActivateGraphKeyboardMode();

        if (sender is not IInputElement canvasGrid)
        {
            pendingCanvasContextMenuPosition = null;
            return;
        }

        pendingCanvasContextMenuPosition = ToWorldCoordinates(e.GetPosition(canvasGrid));

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

    private void NetworkCanvasGrid_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ActivateGraphKeyboardMode();
    }

    private void NetworkCanvasGrid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        ActivateGraphKeyboardMode();

        if (e.Key == Key.F6)
        {
            CycleWorkspaceRegions(reverse: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
            return;
        }

        if (HandleGraphCanvasKeyCommands(e))
        {
            e.Handled = true;
        }
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

        if (e.Key == Key.F6)
        {
            CycleWorkspaceRegions(reverse: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
            return;
        }

        if (isGraphKeyboardMode && HandleGraphCanvasKeyCommands(e))
        {
            e.Handled = true;
            return;
        }

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

        var position = ToWorldCoordinates(e.GetPosition(NetworkCanvasGrid));
        EdgeCreationPreviewLine.X2 = position.X;
        EdgeCreationPreviewLine.Y2 = position.Y;
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
        FocusKeyboardEdge(null);
        edgeCreationSourceNode = sourceNode;
        edgeCreationIsBidirectional = isBidirectional;
        EdgeCreationPreviewLine.X1 = sourceNode.CenterX;
        EdgeCreationPreviewLine.Y1 = sourceNode.CenterY;
        EdgeCreationPreviewLine.X2 = sourceNode.CenterX;
        EdgeCreationPreviewLine.Y2 = sourceNode.CenterY;
        EdgeCreationPreviewLine.Visibility = Visibility.Visible;
        Mouse.Capture(NetworkCanvasGrid);
        SetCanvasHint($"Creating route from '{sourceNode.Name}'. Focus another place and press Enter, or click destination. Esc cancels.");
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
        if (isGraphKeyboardMode)
        {
            SetCanvasHint("Canvas active. Arrow keys move between places. Ctrl+Tab cycles connected routes. Enter edits. N adds a place. E starts a route.");
        }

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

    private Point ToWorldCoordinates(Point renderedPosition)
    {
        return new Point(
            renderedPosition.X + ViewModel.WorkspaceMinX,
            renderedPosition.Y + ViewModel.WorkspaceMinY);
    }

    private void PreserveViewportAcrossWorkspaceShift(Action editAction)
    {
        ArgumentNullException.ThrowIfNull(editAction);

        var oldMinX = ViewModel.WorkspaceMinX;
        var oldMinY = ViewModel.WorkspaceMinY;

        editAction();

        var deltaMinX = ViewModel.WorkspaceMinX - oldMinX;
        var deltaMinY = ViewModel.WorkspaceMinY - oldMinY;

        var horizontalShift = deltaMinX < 0d ? -deltaMinX * canvasZoom : 0d;
        var verticalShift = deltaMinY < 0d ? -deltaMinY * canvasZoom : 0d;
        if (horizontalShift <= 0d && verticalShift <= 0d)
        {
            return;
        }

        NetworkCanvasScrollViewer.UpdateLayout();
        if (horizontalShift > 0d)
        {
            NetworkCanvasScrollViewer.ScrollToHorizontalOffset(NetworkCanvasScrollViewer.HorizontalOffset + horizontalShift);
        }

        if (verticalShift > 0d)
        {
            NetworkCanvasScrollViewer.ScrollToVerticalOffset(NetworkCanvasScrollViewer.VerticalOffset + verticalShift);
        }
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

    private void ShowKeyboardHelp_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Canvas keyboard shortcuts:\n" +
            "• F6 cycles workspace regions (top, canvas, right rail, reports)\n" +
            "• Arrow keys move between places\n" +
            "• Ctrl+Arrow moves selected place\n" +
            "• Shift+Arrow pans the canvas\n" +
            "• Enter edits selected place/route\n" +
            "• Ctrl+Tab cycles routes connected to selected place\n" +
            "• Space selects a place\n" +
            "• E starts route creation, Esc cancels\n" +
            "• N adds a place\n" +
            "• +/- zoom, 0 reset zoom\n" +
            "• Delete removes selected item\n" +
            "• Shift+F10 or Menu opens actions",
            "Keyboard controls",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ActivateGraphKeyboardMode()
    {
        isGraphKeyboardMode = true;
        SetCanvasHint("Canvas active. Arrow keys move between places. Ctrl+Tab cycles connected routes. Enter edits. N adds a place. E starts a route. Shift+F10 opens actions.");
    }

    private void SetDefaultCanvasHint()
    {
        ViewModel.GraphKeyboardHint = "Press F6 to move focus to the canvas workspace. Use Arrow keys for graph navigation and Ctrl+Tab to cycle connected routes.";
        ViewModel.FocusedEdgeStatus = "No route focused.";
    }

    private void SetCanvasHint(string hint)
    {
        ViewModel.GraphKeyboardHint = hint;
    }

    private bool HandleGraphCanvasKeyCommands(KeyEventArgs e)
    {
        if (!isGraphKeyboardMode)
        {
            return false;
        }

        if (e.Key == Key.Escape)
        {
            EndEdgeCreation();
            FocusKeyboardEdge(null);
            SetCanvasHint("Canvas active. Arrow keys move between places. Ctrl+Tab cycles connected routes. Enter edits. N adds a place.");
            return true;
        }

        if (Keyboard.Modifiers == ModifierKeys.Shift && (e.Key is Key.Left or Key.Right or Key.Up or Key.Down))
        {
            PanCanvasByKeyboard(e.Key);
            return true;
        }

        if (e.Key is Key.Add or Key.OemPlus)
        {
            ApplyZoomAtCenter(CanvasZoomStep);
            return true;
        }

        if (e.Key is Key.Subtract or Key.OemMinus)
        {
            ApplyZoomAtCenter(1d / CanvasZoomStep);
            return true;
        }

        if (e.Key == Key.D0 || e.Key == Key.NumPad0)
        {
            ResetCanvasZoom();
            return true;
        }

        if (e.Key == Key.N)
        {
            ExecuteWithErrorHandling(() => PreserveViewportAcrossWorkspaceShift(ViewModel.AddNode));
            if (ViewModel.SelectedNode is not null)
            {
                FocusKeyboardNode(ViewModel.SelectedNode);
            }

            return true;
        }

        if (e.Key == Key.E && ViewModel.SelectedNode is not null)
        {
            BeginEdgeCreation(ViewModel.SelectedNode, isBidirectional: true);
            SetCanvasHint("Route creation mode. Pick a destination place. Esc cancels.");
            return true;
        }

        if (e.Key == Key.Enter)
        {
            if (edgeCreationSourceNode is not null &&
                ViewModel.SelectedNode is not null &&
                !ReferenceEquals(edgeCreationSourceNode, ViewModel.SelectedNode))
            {
                var source = edgeCreationSourceNode;
                var target = ViewModel.SelectedNode;
                var isBidirectional = edgeCreationIsBidirectional;
                ExecuteWithErrorHandling(() => ViewModel.AddEdgeBetween(source, target, isBidirectional));
                EndEdgeCreation();
                FocusKeyboardEdge(ViewModel.SelectedEdge);
                return true;
            }

            if (focusedKeyboardEdge is not null)
            {
                ViewModel.SelectedEdge = focusedKeyboardEdge;
                OpenEdgeEditorWindow();
                return true;
            }

            if (ViewModel.SelectedNode is not null)
            {
                OpenNodeEditorWindow();
                return true;
            }
        }

        if (e.Key == Key.Delete && !IsTextInputElement(e.OriginalSource))
        {
            return ConfirmAndDeleteSelectedItem();
        }

        if ((e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) || e.Key == Key.Apps)
        {
            if (focusedKeyboardEdge is not null)
            {
                OpenEdgeContextMenu(focusedKeyboardEdge);
                return true;
            }

            if (ViewModel.SelectedNode is not null)
            {
                OpenNodeContextMenu(ViewModel.SelectedNode);
                return true;
            }

            OpenBlankCanvasContextMenu();
            return true;
        }

        if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && ViewModel.SelectedNode is not null)
        {
            FocusNextConnectedEdge(ViewModel.SelectedNode, forward: !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            return true;
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            return FocusNearbyNode(e.Key);
        }

        return false;
    }

    private void FocusKeyboardNode(NodeViewModel? node)
    {
        foreach (var candidate in ViewModel.Nodes)
        {
            candidate.IsKeyboardFocused = ReferenceEquals(candidate, node);
        }

        if (node is null)
        {
            return;
        }

        ViewModel.SelectedNode = node;
        ViewModel.SelectedEdge = null;
        FocusKeyboardEdge(null);

        var thumb = FindNodeThumb(node);
        if (thumb is not null && !thumb.IsKeyboardFocused)
        {
            thumb.Focus();
        }
    }

    private void FocusKeyboardEdge(EdgeViewModel? edge)
    {
        focusedKeyboardEdge = edge;
        foreach (var candidate in ViewModel.Edges)
        {
            candidate.IsKeyboardFocused = ReferenceEquals(candidate, edge);
        }

        if (edge is null)
        {
            ViewModel.FocusedEdgeStatus = "No route focused.";
            return;
        }

        ViewModel.SelectedEdge = edge;
        ViewModel.FocusedEdgeStatus = $"Route focused: {DescribeEdge(edge)}. Enter edits. Delete removes. Esc clears.";
        SetCanvasHint("Route selected. Enter edits. Delete removes. Esc clears selection.");
    }

    private bool FocusNearbyNode(Key key)
    {
        if (ViewModel.Nodes.Count == 0)
        {
            return false;
        }

        var current = ViewModel.SelectedNode ?? ViewModel.Nodes[0];
        var currentPoint = new Point(current.CenterX, current.CenterY);
        var direction = key switch
        {
            Key.Left => new Vector(-1, 0),
            Key.Right => new Vector(1, 0),
            Key.Up => new Vector(0, -1),
            Key.Down => new Vector(0, 1),
            _ => new Vector(0, 0)
        };

        var best = ViewModel.Nodes
            .Where(node => !ReferenceEquals(node, current))
            .Select(node => new
            {
                Node = node,
                Delta = new Vector(node.CenterX - currentPoint.X, node.CenterY - currentPoint.Y)
            })
            .Where(item => Vector.Multiply(item.Delta, direction) > 0d)
            .OrderByDescending(item => Vector.Multiply(item.Delta, direction))
            .ThenBy(item => item.Delta.LengthSquared)
            .FirstOrDefault()?.Node;

        if (best is null)
        {
            return false;
        }

        FocusKeyboardNode(best);
        SetCanvasHint($"Focused place '{best.Name}'. Enter edits. Ctrl+Arrow moves. E starts a route.");
        return true;
    }

    private void MoveNodeFromKeyboard(NodeViewModel node, Key key)
    {
        var (dx, dy) = key switch
        {
            Key.Left => (-KeyboardMoveStep, 0d),
            Key.Right => (KeyboardMoveStep, 0d),
            Key.Up => (0d, -KeyboardMoveStep),
            Key.Down => (0d, KeyboardMoveStep),
            _ => (0d, 0d)
        };

        if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)
        {
            return;
        }

        ExecuteWithErrorHandling(() => PreserveViewportAcrossWorkspaceShift(() => ViewModel.MoveNode(node, dx, dy)));
        SetCanvasHint($"Moved place '{node.Name}'. Ctrl+Arrow continues moving in grid steps.");
    }

    private void FocusNextConnectedEdge(NodeViewModel node, bool forward)
    {
        var connectedEdges = ViewModel.Edges
            .Where(edge => string.Equals(edge.FromNodeId, node.Id, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(edge.ToNodeId, node.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (connectedEdges.Count == 0)
        {
            ViewModel.FocusedEdgeStatus = $"No routes are connected to '{node.Name}'.";
            return;
        }

        var currentIndex = focusedKeyboardEdge is null
            ? -1
            : connectedEdges.FindIndex(edge => ReferenceEquals(edge, focusedKeyboardEdge));
        var offset = forward ? 1 : -1;
        var nextIndex = (currentIndex + offset + connectedEdges.Count) % connectedEdges.Count;
        FocusKeyboardEdge(connectedEdges[nextIndex]);
    }

    private Thumb? FindNodeThumb(NodeViewModel node)
    {
        return FindVisualChildren<Thumb>(NetworkCanvasGrid).FirstOrDefault(thumb => ReferenceEquals(thumb.DataContext, node));
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T matched)
            {
                yield return matched;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void PanCanvasByKeyboard(Key key)
    {
        var (dx, dy) = key switch
        {
            Key.Left => (-KeyboardPanStep, 0d),
            Key.Right => (KeyboardPanStep, 0d),
            Key.Up => (0d, -KeyboardPanStep),
            Key.Down => (0d, KeyboardPanStep),
            _ => (0d, 0d)
        };

        NetworkCanvasScrollViewer.ScrollToHorizontalOffset(Math.Max(0d, NetworkCanvasScrollViewer.HorizontalOffset + dx));
        NetworkCanvasScrollViewer.ScrollToVerticalOffset(Math.Max(0d, NetworkCanvasScrollViewer.VerticalOffset + dy));
    }

    private void ApplyZoomAtCenter(double factor)
    {
        var previousZoom = canvasZoom;
        var nextZoom = Math.Clamp(previousZoom * factor, MinimumCanvasZoom, MaximumCanvasZoom);
        if (Math.Abs(nextZoom - previousZoom) < 0.0001d)
        {
            return;
        }

        var viewportCenter = new Point(
            NetworkCanvasScrollViewer.ViewportWidth / 2d,
            NetworkCanvasScrollViewer.ViewportHeight / 2d);
        var contentX = (NetworkCanvasScrollViewer.HorizontalOffset + viewportCenter.X) / Math.Max(previousZoom, 0.001d);
        var contentY = (NetworkCanvasScrollViewer.VerticalOffset + viewportCenter.Y) / Math.Max(previousZoom, 0.001d);

        canvasZoom = nextZoom;
        NetworkCanvasScaleTransform.ScaleX = nextZoom;
        NetworkCanvasScaleTransform.ScaleY = nextZoom;
        NetworkCanvasScrollViewer.ScrollToHorizontalOffset((contentX * nextZoom) - viewportCenter.X);
        NetworkCanvasScrollViewer.ScrollToVerticalOffset((contentY * nextZoom) - viewportCenter.Y);
    }

    private void ResetCanvasZoom()
    {
        canvasZoom = 1d;
        NetworkCanvasScaleTransform.ScaleX = 1d;
        NetworkCanvasScaleTransform.ScaleY = 1d;
        SetCanvasHint("Canvas zoom reset.");
    }

    private void FitCanvasToNetwork()
    {
        if (!ViewModel.HasNetwork)
        {
            return;
        }

        NetworkCanvasScrollViewer.UpdateLayout();

        var viewportWidth = Math.Max(1d, NetworkCanvasScrollViewer.ViewportWidth);
        var viewportHeight = Math.Max(1d, NetworkCanvasScrollViewer.ViewportHeight);
        var workspaceWidth = Math.Max(1d, ViewModel.WorkspaceWidth);
        var workspaceHeight = Math.Max(1d, ViewModel.WorkspaceHeight);
        var widthZoom = viewportWidth / workspaceWidth;
        var heightZoom = viewportHeight / workspaceHeight;
        var targetZoom = Math.Clamp(Math.Min(widthZoom, heightZoom) * 0.92d, MinimumCanvasZoom, MaximumCanvasZoom);

        canvasZoom = targetZoom;
        NetworkCanvasScaleTransform.ScaleX = targetZoom;
        NetworkCanvasScaleTransform.ScaleY = targetZoom;

        var horizontalOffset = Math.Max(0d, (workspaceWidth * targetZoom - viewportWidth) / 2d);
        var verticalOffset = Math.Max(0d, (workspaceHeight * targetZoom - viewportHeight) / 2d);
        NetworkCanvasScrollViewer.ScrollToHorizontalOffset(horizontalOffset);
        NetworkCanvasScrollViewer.ScrollToVerticalOffset(verticalOffset);
    }

    private void CycleWorkspaceRegions(bool reverse)
    {
        var regions = new List<FrameworkElement> { TopControlsRegion, NetworkCanvasGrid };
        if (RightRailRegion.Visibility == Visibility.Visible)
        {
            regions.Add(RightRailRegion);
        }

        if (BottomReportsRegion.Visibility == Visibility.Visible)
        {
            regions.Add(BottomReportsRegion);
        }

        if (regions.Count == 0)
        {
            return;
        }

        var focusedElement = FocusManager.GetFocusedElement(this) as DependencyObject;
        var currentIndex = regions.FindIndex(region => focusedElement is not null && IsDescendantOf(focusedElement, region));
        var offset = reverse ? -1 : 1;
        var nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + offset + regions.Count) % regions.Count;
        regions[nextIndex].Focus();

        if (ReferenceEquals(regions[nextIndex], NetworkCanvasGrid))
        {
            ActivateGraphKeyboardMode();
        }
        else
        {
            isGraphKeyboardMode = false;
            SetDefaultCanvasHint();
        }
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
                FocusKeyboardEdge(null);
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
                FocusKeyboardEdge(null);
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

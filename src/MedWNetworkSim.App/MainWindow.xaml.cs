using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MedWNetworkSim.App.ViewModels;

namespace MedWNetworkSim.App;

public partial class MainWindow : Window
{
    private Point? pendingCanvasContextMenuPosition;
    private NodeViewModel? edgeCreationSourceNode;
    private bool edgeCreationIsBidirectional = true;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel();
        DataContext = ViewModel;
    }

    public MainWindowViewModel ViewModel { get; }

    private void NewNetwork_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.CreateNewNetwork);
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
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
        var dialog = new SaveFileDialog
        {
            Title = "Save network file",
            Filter = "JSON network (*.json)|*.json|All files (*.*)|*.*",
            FileName = ViewModel.SuggestedFileName,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ExecuteWithErrorHandling(() => ViewModel.SaveToFile(dialog.FileName));
    }

    private void LoadSample_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.LoadBundledSample);
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
        ExecuteWithErrorHandling(ViewModel.RemoveSelectedNode);
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
        ExecuteWithErrorHandling(ViewModel.RemoveSelectedEdge);
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
        if (edgeCreationSourceNode is null)
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
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);

        if (edgeCreationSourceNode is null)
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

        if (edgeCreationSourceNode is null)
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
        edgeCreationSourceNode = sourceNode;
        edgeCreationIsBidirectional = isBidirectional;
        EdgeCreationPreviewLine.X1 = sourceNode.CenterX;
        EdgeCreationPreviewLine.Y1 = sourceNode.CenterY;
        EdgeCreationPreviewLine.X2 = sourceNode.CenterX;
        EdgeCreationPreviewLine.Y2 = sourceNode.CenterY;
        EdgeCreationPreviewLine.Visibility = Visibility.Visible;
        Mouse.Capture(NetworkCanvasGrid);
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

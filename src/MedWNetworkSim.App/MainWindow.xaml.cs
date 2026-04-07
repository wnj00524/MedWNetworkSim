using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls.Primitives;
using MedWNetworkSim.App.ViewModels;

namespace MedWNetworkSim.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel();
        DataContext = ViewModel;
    }

    public MainWindowViewModel ViewModel { get; }

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

    private void AutoArrange_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWithErrorHandling(ViewModel.AutoArrangeNodes);
    }

    private void NodeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is Thumb { DataContext: NodeViewModel node })
        {
            ViewModel.MoveNode(node, e.HorizontalChange, e.VerticalChange);
        }
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

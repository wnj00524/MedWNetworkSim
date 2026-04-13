using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MedWNetworkSim.App.ViewModels;

public sealed class EdgeEditorViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel mainWindowViewModel;
    private EdgeViewModel? observedSelectedEdge;
    private bool isDisposed;

    public EdgeEditorViewModel(MainWindowViewModel mainWindowViewModel)
    {
        this.mainWindowViewModel = mainWindowViewModel;
        this.mainWindowViewModel.PropertyChanged += HandleMainWindowViewModelPropertyChanged;
    }

    public ObservableCollection<EdgeViewModel> Edges => mainWindowViewModel.Edges;

    public ObservableCollection<string> NodeIdOptions => mainWindowViewModel.NodeIdOptions;

    public UiTerminologyViewModel Terminology => mainWindowViewModel.Terminology;

    public EdgeViewModel? SelectedEdge
    {
        get => mainWindowViewModel.SelectedEdge;
        set
        {
            if (ReferenceEquals(mainWindowViewModel.SelectedEdge, value))
            {
                return;
            }

            mainWindowViewModel.SelectedEdge = value;
            ObserveSelectedEdge(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(FromInterfaceNodeOptions));
            OnPropertyChanged(nameof(ToInterfaceNodeOptions));
        }
    }

    public IReadOnlyList<string> FromInterfaceNodeOptions => mainWindowViewModel.GetInterfaceNodeOptionsForEdgeEndpoint(SelectedEdge?.FromNodeId);

    public IReadOnlyList<string> ToInterfaceNodeOptions => mainWindowViewModel.GetInterfaceNodeOptionsForEdgeEndpoint(SelectedEdge?.ToNodeId);

    public string StatusMessage => mainWindowViewModel.StatusMessage;

    public void AddEdge()
    {
        mainWindowViewModel.AddEdge();
        OnPropertyChanged(nameof(SelectedEdge));
    }

    public void RemoveSelectedEdge()
    {
        mainWindowViewModel.RemoveSelectedEdge();
        OnPropertyChanged(nameof(SelectedEdge));
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        mainWindowViewModel.PropertyChanged -= HandleMainWindowViewModelPropertyChanged;
        ObserveSelectedEdge(null);
        isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private void ObserveSelectedEdge(EdgeViewModel? edge)
    {
        if (ReferenceEquals(observedSelectedEdge, edge))
        {
            return;
        }

        if (observedSelectedEdge is not null)
        {
            observedSelectedEdge.PropertyChanged -= HandleSelectedEdgePropertyChanged;
        }

        observedSelectedEdge = edge;

        if (observedSelectedEdge is not null)
        {
            observedSelectedEdge.PropertyChanged += HandleSelectedEdgePropertyChanged;
        }
    }

    private void HandleMainWindowViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isDisposed)
        {
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.SelectedEdge))
        {
            ObserveSelectedEdge(mainWindowViewModel.SelectedEdge);
            OnPropertyChanged(nameof(SelectedEdge));
            OnPropertyChanged(nameof(FromInterfaceNodeOptions));
            OnPropertyChanged(nameof(ToInterfaceNodeOptions));
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.StatusMessage))
        {
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    private void HandleSelectedEdgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EdgeViewModel.FromNodeId))
        {
            OnPropertyChanged(nameof(FromInterfaceNodeOptions));
        }

        if (e.PropertyName is nameof(EdgeViewModel.ToNodeId))
        {
            OnPropertyChanged(nameof(ToInterfaceNodeOptions));
        }
    }
}

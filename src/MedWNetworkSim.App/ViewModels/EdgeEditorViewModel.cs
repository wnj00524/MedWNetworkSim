using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MedWNetworkSim.App.ViewModels;

public sealed class EdgeEditorViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel mainWindowViewModel;
    private bool isDisposed;

    public EdgeEditorViewModel(MainWindowViewModel mainWindowViewModel)
    {
        this.mainWindowViewModel = mainWindowViewModel;
        this.mainWindowViewModel.PropertyChanged += HandleMainWindowViewModelPropertyChanged;
    }

    public ObservableCollection<EdgeViewModel> Edges => mainWindowViewModel.Edges;

    public ObservableCollection<string> NodeIdOptions => mainWindowViewModel.NodeIdOptions;

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
            OnPropertyChanged();
        }
    }

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
        isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private void HandleMainWindowViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isDisposed)
        {
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.SelectedEdge))
        {
            OnPropertyChanged(nameof(SelectedEdge));
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.StatusMessage))
        {
            OnPropertyChanged(nameof(StatusMessage));
        }
    }
}

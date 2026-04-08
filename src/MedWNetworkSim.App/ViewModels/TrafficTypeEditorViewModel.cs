using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MedWNetworkSim.App.ViewModels;

public sealed class TrafficTypeEditorViewModel : ObservableObject
{
    private readonly MainWindowViewModel mainWindowViewModel;

    public TrafficTypeEditorViewModel(MainWindowViewModel mainWindowViewModel)
    {
        this.mainWindowViewModel = mainWindowViewModel;
        this.mainWindowViewModel.PropertyChanged += HandleMainWindowViewModelPropertyChanged;
    }

    public ObservableCollection<TrafficTypeDefinitionEditorViewModel> TrafficDefinitions => mainWindowViewModel.TrafficDefinitions;

    public Array RoutingPreferences => mainWindowViewModel.RoutingPreferences;

    public TrafficTypeDefinitionEditorViewModel? SelectedTrafficDefinition
    {
        get => mainWindowViewModel.SelectedTrafficDefinition;
        set
        {
            if (ReferenceEquals(mainWindowViewModel.SelectedTrafficDefinition, value))
            {
                return;
            }

            mainWindowViewModel.SelectedTrafficDefinition = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage => mainWindowViewModel.StatusMessage;

    public void AddTrafficDefinition()
    {
        mainWindowViewModel.AddTrafficDefinition();
        OnPropertyChanged(nameof(SelectedTrafficDefinition));
    }

    public void RemoveSelectedTrafficDefinition()
    {
        mainWindowViewModel.RemoveSelectedTrafficDefinition();
        OnPropertyChanged(nameof(SelectedTrafficDefinition));
    }

    private void HandleMainWindowViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SelectedTrafficDefinition))
        {
            OnPropertyChanged(nameof(SelectedTrafficDefinition));
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.StatusMessage))
        {
            OnPropertyChanged(nameof(StatusMessage));
        }
    }
}

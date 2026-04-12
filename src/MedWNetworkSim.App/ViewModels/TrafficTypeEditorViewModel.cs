using System.Collections.ObjectModel;
using System.ComponentModel;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class TrafficTypeEditorViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel mainWindowViewModel;
    private bool isDisposed;

    public TrafficTypeEditorViewModel(MainWindowViewModel mainWindowViewModel)
    {
        this.mainWindowViewModel = mainWindowViewModel;
        this.mainWindowViewModel.PropertyChanged += HandleMainWindowViewModelPropertyChanged;
    }

    public ObservableCollection<TrafficTypeDefinitionEditorViewModel> TrafficDefinitions => mainWindowViewModel.TrafficDefinitions;

    public UiTerminologyViewModel Terminology => mainWindowViewModel.Terminology;

    public Array RoutingPreferences => mainWindowViewModel.RoutingPreferences;

    public IReadOnlyList<AllocationModeOptionViewModel> AllocationModeOptions => mainWindowViewModel.AllocationModeOptions;

    public Array RouteChoiceModels { get; } = Enum.GetValues(typeof(RouteChoiceModel));

    public Array FlowSplitPolicies { get; } = Enum.GetValues(typeof(FlowSplitPolicy));

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

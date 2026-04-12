using System.Windows;

namespace MedWNetworkSim.App.ViewModels;

public sealed class ReportsDrawerViewModel : ObservableObject
{
    private bool isOpen;
    private object? selectedReportRow;

    public event EventHandler<RouteAllocationRowViewModel>? RouteSelected;

    public bool IsOpen
    {
        get => isOpen;
        set
        {
            if (SetProperty(ref isOpen, value))
            {
                OnPropertyChanged(nameof(Visibility));
            }
        }
    }

    public Visibility Visibility => IsOpen ? Visibility.Visible : Visibility.Collapsed;

    public object? SelectedReportRow
    {
        get => selectedReportRow;
        set
        {
            if (!SetProperty(ref selectedReportRow, value))
            {
                return;
            }

            if (value is RouteAllocationRowViewModel route)
            {
                RouteSelected?.Invoke(this, route);
            }
        }
    }

    public void Open()
    {
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
    }
}

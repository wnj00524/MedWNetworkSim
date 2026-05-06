using Avalonia;
using Avalonia.Controls;
using MedWNetworkSim.Presentation;

namespace MedWNetworkSim.UI;

public sealed class IconRail : Border
{
}

public sealed class FloatingCommandBar : Border
{
}

public sealed class SimulationTransportBar : Border
{
}

public sealed class ContextInspectorDrawer : Border
{
}

public sealed class NetworkWorkspaceView : Grid
{
}

public sealed class AnalyticsWorkspaceView : Grid
{
}

public sealed class FacilitiesWorkspaceView : Grid
{
}

public sealed class AnalyticsCanvasControl : Control
{
    public static readonly DirectProperty<AnalyticsCanvasControl, WorkspaceViewModel?> ViewModelProperty =
        AvaloniaProperty.RegisterDirect<AnalyticsCanvasControl, WorkspaceViewModel?>(
            nameof(ViewModel),
            control => control.ViewModel,
            (control, value) => control.ViewModel = value);

    private WorkspaceViewModel? viewModel;

    public WorkspaceViewModel? ViewModel
    {
        get => viewModel;
        set => SetAndRaise(ViewModelProperty, ref viewModel, value);
    }
}

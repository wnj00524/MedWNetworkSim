using Avalonia;
using Avalonia.Controls;
using MedWNetworkSim.Presentation;

namespace MedWNetworkSim.UI;
/// <summary>
/// Represents the icon rail component.
/// </summary>

public sealed class IconRail : Border
{
}
/// <summary>
/// Represents the floating command bar component.
/// </summary>

public sealed class FloatingCommandBar : Border
{
}
/// <summary>
/// Represents the simulation transport bar component.
/// </summary>

public sealed class SimulationTransportBar : Border
{
}
/// <summary>
/// Represents the context inspector drawer component.
/// </summary>

public sealed class ContextInspectorDrawer : Border
{
}
/// <summary>
/// Represents the network workspace view component.
/// </summary>

public sealed class NetworkWorkspaceView : Grid
{
}
/// <summary>
/// Represents the analytics workspace view component.
/// </summary>

public sealed class AnalyticsWorkspaceView : Grid
{
}
/// <summary>
/// Represents the facilities workspace view component.
/// </summary>

public sealed class FacilitiesWorkspaceView : Grid
{
}
/// <summary>
/// Represents the analytics canvas control component.
/// </summary>

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

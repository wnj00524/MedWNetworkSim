using Avalonia;
using Avalonia.Media;

namespace MedWNetworkSim.UI;

internal static class AvaloniaDashboardTheme
{
    public static readonly Color AppBackground = Color.Parse("#060B14");
    public static readonly Color ChromeBackground = Color.Parse("#0D1524");
    public static readonly Color PanelBackground = Color.Parse("#111C2E");
    public static readonly Color PanelHeaderBackground = Color.Parse("#16243A");
    public static readonly Color PanelBorder = Color.Parse("#2A3A56");
    public static readonly Color PanelBorderStrong = Color.Parse("#3F5C83");
    public static readonly Color PrimaryText = Color.Parse("#EAF1FB");
    public static readonly Color SecondaryText = Color.Parse("#AEC0DA");
    public static readonly Color MutedText = Color.Parse("#7E93B3");
    public static readonly Color Accent = Color.Parse("#4EA1FF");
    public static readonly Color AccentSoft = Color.Parse("#223E64");
    public static readonly Color Success = Color.Parse("#27C08A");
    public static readonly Color Warning = Color.Parse("#E8B24A");
    public static readonly Color Danger = Color.Parse("#E46868");
    public static readonly Color CanvasBackgroundStart = Color.Parse("#0B1322");
    public static readonly Color CanvasBackgroundEnd = Color.Parse("#101C32");
    public static readonly Color CanvasGridLine = Color.Parse("#233349");
    public static readonly Color HoverBackground = Color.Parse("#1A2A42");
    public static readonly Color SelectedBackground = Color.Parse("#1F3A5E");
    public static readonly Color FocusBorder = Color.Parse("#66B4FF");
    public static readonly Color InputBackground = Color.Parse("#0E1728");
    public static readonly Color InputBorder = Color.Parse("#324766");
    public static readonly Color ToolbarButtonBackground = Color.Parse("#17263C");
    public static readonly Color ToolbarButtonPrimaryBackground = Color.Parse("#2C5E95");
    public static readonly Color ToolbarButtonBorder = Color.Parse("#3C5A80");

    public static readonly double SectionSpacing = 12d;
    public static readonly CornerRadius ControlCornerRadius = new(10);
    public static readonly CornerRadius PanelCornerRadius = new(16);
}

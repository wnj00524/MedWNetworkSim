using Avalonia;
using Avalonia.Media;

namespace MedWNetworkSim.UI;
/// <summary>
/// Represents the avalonia dashboard theme component.
/// </summary>

internal static class AvaloniaDashboardTheme
{
    public static readonly Color AppBackground = Color.Parse("#050A12");
    public static readonly Color ChromeBackground = Color.Parse("#0B1423");
    public static readonly Color PanelBackground = Color.Parse("#0F1A2C");
    public static readonly Color PanelHeaderBackground = Color.Parse("#152238");
    public static readonly Color PanelBorder = Color.Parse("#324967");
    public static readonly Color PanelBorderStrong = Color.Parse("#4E6E98");
    public static readonly Color PrimaryText = Color.Parse("#EAF1FB");
    public static readonly Color SecondaryText = Color.Parse("#AEC0DA");
    public static readonly Color MutedText = Color.Parse("#7E93B3");
    public static readonly Color Accent = Color.Parse("#37A7FF");
    public static readonly Color AccentSoft = Color.Parse("#223E64");
    public static readonly Color Success = Color.Parse("#2FD38F");
    public static readonly Color Warning = Color.Parse("#E8B24A");
    public static readonly Color Danger = Color.Parse("#EF5B5B");
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

    public static readonly Color GlassBackground = Color.Parse("#1A14233A");
    public static readonly Color GlassBackgroundStrong = Color.Parse("#CC101C31");
    public static readonly Color GlassBorder = Color.Parse("#4A6A8D");
    public static readonly Color GlassBorderStrong = Color.Parse("#79A8D8");
    public static readonly Color GlowCyan = Color.Parse("#4EDCFF");
    public static readonly Color GlowAmber = Color.Parse("#FFB65C");
    public static readonly Color RouteBase = Color.Parse("#496D8F");
    public static readonly Color RouteFlow = Color.Parse("#4DDCFF");
    public static readonly Color MarketPressure = Color.Parse("#F2A255");
    public static readonly Color HealthyGreen = Color.Parse("#4EDB97");
    public static readonly Color CriticalRed = Color.Parse("#F06C6C");
    public static readonly Color CanvasGridSubtle = Color.Parse("#29455F");
    public static readonly Color ShadowSoft = Color.Parse("#66040A14");
    public static readonly double SectionSpacing = 12d;
    public static readonly CornerRadius ControlCornerRadius = new(10);
    public static readonly CornerRadius PanelCornerRadius = new(16);
}

using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public static class AppThemeManager
{
    private static AppTheme currentTheme = AppTheme.TurkeyOakCommand;

    static AppThemeManager()
    {
        SystemEvents.UserPreferenceChanged += HandleUserPreferenceChanged;
    }

    public static AppTheme CurrentTheme => currentTheme;

    public static void ApplyTheme(AppTheme theme)
    {
        currentTheme = theme;

        if (Application.Current is null)
        {
            return;
        }

        var palette = theme switch
        {
            AppTheme.System => BuildSystemPalette(),
            AppTheme.HighContrast => BuildHighContrastPalette(),
            _ => BuildTurkeyOakPalette()
        };

        SetBrushColor("WindowBrush", palette.Window);
        SetBrushColor("PanelBrush", palette.Panel);
        SetBrushColor("CanvasBrush", palette.Canvas);
        SetBrushColor("CanvasLineBrush", palette.CanvasLine);
        SetBrushColor("AccentBrush", palette.Accent);
        SetBrushColor("AccentStrongBrush", palette.AccentStrong);
        SetBrushColor("AccentSoftBrush", palette.AccentSoft);
        SetBrushColor("BorderBrush", palette.Border);
        SetBrushColor("NodeBrush", palette.Node);
        SetBrushColor("NodeBorderBrush", palette.NodeBorder);
        SetBrushColor("EdgeBrush", palette.Edge);
        SetBrushColor("EdgeLabelBrush", palette.EdgeLabel);
        SetBrushColor("InfoPanelBrush", palette.InfoPanel);
        SetBrushColor("MutedForegroundBrush", palette.MutedForeground);
        SetBrushColor("StrongForegroundBrush", palette.StrongForeground);
        SetBrushColor("InputBrush", palette.Input);
        SetBrushColor("SectionBrush", palette.Section);
        SetBrushColor("CardBrush", palette.Card);
        SetBrushColor("CardAltBrush", palette.CardAlt);
        SetBrushColor("TrackBrush", palette.Track);
        SetBrushColor("NodeWatermarkBrush", palette.Watermark);
        SetBrushColor("NodeBadgeBrush", palette.NodeBadge);
        SetBrushColor("SuccessBrush", palette.Success);
        SetBrushColor("WarningBrush", palette.Warning);
        SetBrushColor("DangerBrush", palette.Danger);
        SetBrushColor("KeyboardFocusBrush", palette.Focus);
        SetBrushColor("KeyboardFocusFillBrush", palette.FocusFill);

        Application.Current.Resources["AppFontFamily"] = palette.FontFamily;
    }

    private static void HandleUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (currentTheme == AppTheme.System)
        {
            ApplyTheme(AppTheme.System);
        }
    }

    private static ThemePalette BuildTurkeyOakPalette() => new(
        ColorFromHex("#FF040A10"),
        ColorFromHex("#FF0A131A"),
        ColorFromHex("#FF08111A"),
        ColorFromHex("#40458FA1"),
        ColorFromHex("#FF5AC9B9"),
        ColorFromHex("#FF88FFF0"),
        ColorFromHex("#2F5AC9B9"),
        ColorFromHex("#FF2A4252"),
        ColorFromHex("#FF11212B"),
        ColorFromHex("#FF63D8C7"),
        ColorFromHex("#FF9BFCEF"),
        ColorFromHex("#FF101D26"),
        ColorFromHex("#FFA7BDCA"),
        ColorFromHex("#FFF0FBFB"),
        ColorFromHex("#FF0F1B24"),
        ColorFromHex("#FF0D1821"),
        ColorFromHex("#FF11202A"),
        ColorFromHex("#FF152632"),
        ColorFromHex("#FF2D4352"),
        ColorFromHex("#6638C2B2"),
        ColorFromHex("#FF132430"),
        ColorFromHex("#FF203747"),
        ColorFromHex("#FF3FCF81"),
        ColorFromHex("#FFEAAA4F"),
        ColorFromHex("#FFE06464"),
        ColorFromHex("#FF9AFDFB"),
        ColorFromHex("#389AFDFB"),
        new FontFamily("Segoe UI Semibold"));

    private static ThemePalette BuildHighContrastPalette() => new(
        Colors.Black,
        Colors.Black,
        ColorFromHex("#FF050505"),
        ColorFromHex("#66FFFFFF"),
        ColorFromHex("#FF00FFFF"),
        ColorFromHex("#FFFFFFFF"),
        ColorFromHex("#FF78FFFF"),
        ColorFromHex("#FFFFFFFF"),
        ColorFromHex("#FFFFFFFF"),
        ColorFromHex("#FFFFFFFF"),
        ColorFromHex("#FF000000"),
        ColorFromHex("#FF101010"),
        ColorFromHex("#FFD0D0D0"),
        ColorFromHex("#FFFFFFFF"),
        ColorFromHex("#FF111111"),
        ColorFromHex("#FF0A0A0A"),
        ColorFromHex("#FF101010"),
        ColorFromHex("#FF1A1A1A"),
        ColorFromHex("#FF2C2C2C"),
        ColorFromHex("#88FFFFFF"),
        ColorFromHex("#FF161616"),
        ColorFromHex("#FF2A2A2A"),
        ColorFromHex("#FF4BFF9A"),
        ColorFromHex("#FFFFC75A"),
        ColorFromHex("#FFFF6A6A"),
        ColorFromHex("#FFFFFFFF"),
        ColorFromHex("#44FFFFFF"),
        SystemFonts.MessageFontFamily);

    private static ThemePalette BuildSystemPalette()
    {
        var window = SystemColors.ControlColor;
        var panel = SystemColors.WindowColor;
        var accent = SystemColors.HighlightColor;
        var border = SystemColors.ActiveBorderColor;
        var strongForeground = SystemColors.ControlTextColor;
        var mutedForeground = SystemColors.GrayTextColor;
        var canvasLine = Color.FromArgb(0x33, accent.R, accent.G, accent.B);

        return new ThemePalette(
            window,
            panel,
            SystemColors.ControlLightLightColor,
            canvasLine,
            accent,
            SystemColors.HotTrackColor,
            Color.FromArgb(0x33, accent.R, accent.G, accent.B),
            border,
            SystemColors.WindowColor,
            accent,
            SystemColors.ControlDarkDarkColor,
            SystemColors.InfoColor,
            SystemColors.ControlLightColor,
            mutedForeground,
            strongForeground,
            SystemColors.WindowColor,
            SystemColors.ControlLightColor,
            SystemColors.ControlLightLightColor,
            SystemColors.ControlColor,
            SystemColors.ControlDarkColor,
            Color.FromArgb(0x55, accent.R, accent.G, accent.B),
            SystemColors.ControlColor,
            ColorFromHex("#FF008744"),
            ColorFromHex("#FFB37A00"),
            ColorFromHex("#FFB00020"),
            accent,
            Color.FromArgb(0x33, accent.R, accent.G, accent.B),
            SystemFonts.MessageFontFamily);
    }

    private static void SetBrushColor(string key, Color color)
    {
        Application.Current!.Resources[key] = new SolidColorBrush(color);
    }

    private static Color ColorFromHex(string hex)
    {
        return (Color)ColorConverter.ConvertFromString(hex)!;
    }

    private readonly record struct ThemePalette(
        Color Window,
        Color Panel,
        Color Canvas,
        Color CanvasLine,
        Color Accent,
        Color AccentStrong,
        Color AccentSoft,
        Color Border,
        Color Node,
        Color NodeBorder,
        Color Edge,
        Color EdgeLabel,
        Color InfoPanel,
        Color MutedForeground,
        Color StrongForeground,
        Color Input,
        Color Section,
        Color Card,
        Color CardAlt,
        Color Track,
        Color Watermark,
        Color NodeBadge,
        Color Success,
        Color Warning,
        Color Danger,
        Color Focus,
        Color FocusFill,
        FontFamily FontFamily);
}

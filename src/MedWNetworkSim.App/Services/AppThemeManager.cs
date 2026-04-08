using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public static class AppThemeManager
{
    private static AppTheme currentTheme = AppTheme.Classic;

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

        var palette = theme == AppTheme.System
            ? BuildSystemPalette()
            : BuildPalette(theme);

        SetBrushColor("WindowBrush", palette.Window);
        SetBrushColor("PanelBrush", palette.Panel);
        SetBrushColor("CanvasBrush", palette.Canvas);
        SetBrushColor("CanvasLineBrush", palette.CanvasLine);
        SetBrushColor("AccentBrush", palette.Accent);
        SetBrushColor("AccentStrongBrush", palette.AccentStrong);
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

        Application.Current.Resources["AppFontFamily"] = palette.FontFamily;
    }

    private static void HandleUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (currentTheme == AppTheme.System)
        {
            ApplyTheme(AppTheme.System);
        }
    }

    private static ThemePalette BuildPalette(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Futuristic => new ThemePalette(
                ColorFromHex("#FF0A1220"),
                ColorFromHex("#FF111B2D"),
                ColorFromHex("#FF0E1625"),
                ColorFromHex("#3388E3FF"),
                ColorFromHex("#FF2C7BE5"),
                ColorFromHex("#FF7AF0FF"),
                ColorFromHex("#FF29405D"),
                ColorFromHex("#FF162235"),
                ColorFromHex("#FF4AA6FF"),
                ColorFromHex("#FF6EA8FF"),
                ColorFromHex("#FF8AB4FF"),
                ColorFromHex("#FF121E31"),
                ColorFromHex("#FFA6B9D5"),
                ColorFromHex("#FFF2FAFF"),
                ColorFromHex("#FF16253B"),
                ColorFromHex("#FF132035"),
                ColorFromHex("#FF101A2B"),
                ColorFromHex("#FF17263A"),
                ColorFromHex("#FF233754"),
                ColorFromHex("#662C7BE5"),
                ColorFromHex("#FF17263A"),
                new FontFamily("Bahnschrift")),
            AppTheme.Stone => new ThemePalette(
                ColorFromHex("#FFE9E4DA"),
                ColorFromHex("#FFF6F2EA"),
                ColorFromHex("#FFEEE7DC"),
                ColorFromHex("#33807A73"),
                ColorFromHex("#FF7A6750"),
                ColorFromHex("#FF54463A"),
                ColorFromHex("#FFC9C0B1"),
                ColorFromHex("#FFF7F4EE"),
                ColorFromHex("#FF9B8B76"),
                ColorFromHex("#FF6A6157"),
                ColorFromHex("#FFF0E6D9"),
                ColorFromHex("#FFF2EBE1"),
                ColorFromHex("#FF6A6157"),
                ColorFromHex("#FF2B2622"),
                ColorFromHex("#FFF8F3EB"),
                ColorFromHex("#FFF3EADF"),
                ColorFromHex("#FFF6EFE7"),
                ColorFromHex("#FFE8DED1"),
                ColorFromHex("#FFD6CABA"),
                ColorFromHex("#667A6750"),
                ColorFromHex("#FFE8DED1"),
                new FontFamily("Georgia")),
            _ => new ThemePalette(
                ColorFromHex("#FFF4EFE6"),
                ColorFromHex("#FFFFFCF7"),
                ColorFromHex("#FFF8F3E9"),
                ColorFromHex("#23A07C5F"),
                ColorFromHex("#FF9B5E33"),
                ColorFromHex("#FF6F3E1B"),
                ColorFromHex("#FFD7C7B1"),
                ColorFromHex("#FFF9F8F1"),
                ColorFromHex("#FFC7B27C"),
                ColorFromHex("#FF7A685D"),
                ColorFromHex("#FFF7EBDD"),
                ColorFromHex("#FFF8F1E7"),
                ColorFromHex("#FF6E5D51"),
                ColorFromHex("#FF2D231E"),
                ColorFromHex("#FFFDF9F3"),
                ColorFromHex("#FFF8F1E7"),
                ColorFromHex("#FFFFFBF4"),
                ColorFromHex("#FFF0E6D6"),
                ColorFromHex("#FFE7DCCB"),
                ColorFromHex("#66A67C54"),
                ColorFromHex("#FFF0E6D6"),
                new FontFamily("Segoe UI"))
        };
    }

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
        FontFamily FontFamily);
}

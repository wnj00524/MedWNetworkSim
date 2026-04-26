using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MedWNetworkSim.UI;

internal sealed class AboutDialog : Window
{
    public AboutDialog()
    {
        var brandName = "Turkey Oak";
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown";

        Width = 430;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = $"{brandName} — About";
        Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground);

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            BuildBrandLogo(24d),
                            new TextBlock
                            {
                                Text = $"{brandName} Network Simulator",
                                FontSize = 18,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        }
                    },
                    new TextBlock
                    {
                        Text = $"Version {version}",
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)
                    },
                    new TextBlock
                    {
                        Text = "A planning workstation for modeling resilient, multi-layer network operations.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)
                    }
                }
            }
        };
    }

    private static Control BuildBrandLogo(double size)
    {
        var fallback = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2d),
            Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorderStrong),
            Child = new TextBlock
            {
                Text = "T",
                FontWeight = FontWeight.Bold,
                FontSize = Math.Max(10d, size * 0.55d),
                Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        try
        {
            using var logoStream = AssetLoader.Open(new Uri("avares://MedWNetworkSim.App.Avalonia/Assets/logo.jpg"));
            var bitmap = new Bitmap(logoStream);
            return new Image
            {
                Width = size,
                Height = size,
                Source = bitmap,
                Stretch = Stretch.UniformToFill,
                ClipToBounds = true
            };
        }
        catch
        {
            return fallback;
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MedWNetworkSim.UI;

namespace MedWNetworkSim.App.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ShowSplashThenMainWindowAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void ShowSplashThenMainWindowAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var splashWindow = new SplashWindow();
        desktop.MainWindow = splashWindow;
        splashWindow.Show();

        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        var shellWindow = new ShellWindow
        {
            WindowState = WindowState.FullScreen
        };

        desktop.MainWindow = shellWindow;
        shellWindow.Show();
        splashWindow.Close();
    }
}

internal sealed class SplashWindow : Window
{
    public SplashWindow()
    {
        Width = 1365;
        Height = 768;
        Background = Brushes.Black;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var logoBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://MedWNetworkSim.App.Avalonia/Assets/logo.jpg")));
        Content = new Grid
        {
            Background = Brushes.Black,
            Children =
            {
                new Image
                {
                    Source = logoBitmap,
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                }
            }
        };
    }
}

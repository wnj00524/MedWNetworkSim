using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MedWNetworkSim.App.Agents;
using MedWNetworkSim.Presentation;
using MedWNetworkSim.UI;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace MedWNetworkSim.App.Avalonia;

public partial class App : Application
{
    private ServiceProvider? serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        serviceProvider = BuildServices();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _ = ShowSplashThenMainWindowAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task ShowSplashThenMainWindowAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        SplashWindow? splashWindow = null;
        try
        {
            try
            {
                splashWindow = new SplashWindow();
                desktop.MainWindow = splashWindow;
                splashWindow.Show();
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
            catch (Exception splashEx)
            {
                Trace.WriteLine($"[{nameof(App)}] Splash failed, continuing to main window: {splashEx}");
            }

            var shellWindow = new ShellWindow
            {
                WindowState = WindowState.FullScreen
            };
            if (serviceProvider is not null)
            {
                shellWindow = new ShellWindow(serviceProvider.GetRequiredService<WorkspaceViewModel>())
                {
                    WindowState = WindowState.FullScreen
                };
            }

            desktop.MainWindow = shellWindow;
            shellWindow.Show();
        }
        catch (Exception ex)
        {
            UiExceptionBoundary.Report(
                ex,
                UiExceptionBoundary.BuildActionableMessage(
                    "App startup",
                    "Try restarting the app. If this continues, verify app files and logo assets are intact."),
                nameof(App));
            var errorWindow = BuildStartupErrorWindow(ex.Message);
            desktop.MainWindow = errorWindow;
            errorWindow.Show();
        }
        finally
        {
            try
            {
                splashWindow?.Close();
            }
            catch (Exception closeEx)
            {
                Trace.WriteLine($"[{nameof(App)}] Failed to close splash window: {closeEx}");
            }
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgentActionLogger, AgentActionLogger>();
        services.AddSingleton(provider => new SimulationActorCoordinator(actionLogger: provider.GetRequiredService<IAgentActionLogger>()));
        services.AddSingleton(provider => new WorkspaceViewModel(
            provider.GetRequiredService<IAgentActionLogger>(),
            provider.GetRequiredService<SimulationActorCoordinator>()));
        return services.BuildServiceProvider();
    }

    private static Window BuildStartupErrorWindow(string message)
    {
        var detail = string.IsNullOrWhiteSpace(message) ? "Unknown startup error." : message;
        var closeButton = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(14, 8)
        };
        var window = new Window
        {
            Width = 620,
            Height = 260,
            Title = "MedWNetworkSim startup issue",
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new Border
            {
                Padding = new Thickness(20),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "The app could not open the main workspace.",
                            FontWeight = FontWeight.Bold,
                            FontSize = 20
                        },
                        new TextBlock
                        {
                            Text = $"{detail}\nTry restarting. If this repeats, reinstall or repair the app package.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        closeButton
                    }
                }
            }
        };
        closeButton.Click += (_, _) => window.Close();
        return window;
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

        Content = BuildSplashContent();
    }

    private static Control BuildSplashContent()
    {
        try
        {
            using var logoStream = AssetLoader.Open(new Uri("avares://MedWNetworkSim.App.Avalonia/Assets/logo.jpg"));
            var logoBitmap = new Bitmap(logoStream);
            return new Grid
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
        catch (Exception ex)
        {
            Trace.WriteLine($"[{nameof(SplashWindow)}] Logo splash asset load failed: {ex}");
            return new Grid
            {
                Background = Brushes.Black,
                Children =
                {
                    new TextBlock
                    {
                        Text = "MedWNetworkSim is starting…",
                        Foreground = Brushes.White,
                        FontSize = 28,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
        }
    }
}

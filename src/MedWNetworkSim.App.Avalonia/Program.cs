using Avalonia;

namespace MedWNetworkSim.App.Avalonia;
/// <summary>
/// Represents the program component.
/// </summary>

internal sealed class Program
{
    /// <summary>
    /// Executes the main operation.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    /// <summary>
    /// Executes the build avalonia app operation.
    /// </summary>

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}

using System.Windows;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App;

public partial class App : Application
{
    private readonly CommandLineRunService commandLineRunService = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (commandLineRunService.ShouldRunFromCommandLine(e.Args))
        {
            RunCommandLineMode(e.Args);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void RunCommandLineMode(string[] args)
    {
        try
        {
            if (commandLineRunService.IsHelpRequest(args))
            {
                MessageBox.Show(
                    commandLineRunService.GetUsageText(),
                    "MedW Network Simulator",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown(0);
                return;
            }

            var options = commandLineRunService.Parse(args);
            commandLineRunService.Run(options);
            Shutdown(0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "MedW Network Simulator",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}

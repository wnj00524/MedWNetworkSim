using System;
using System.Runtime.InteropServices;
using System.Windows;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App;

public partial class App : Application
{
    private const uint AttachParentProcess = 0xFFFFFFFF;
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
        TryAttachToParentConsole();

        try
        {
            var options = commandLineRunService.Parse(args);
            var message = commandLineRunService.Run(options);
            if (!string.IsNullOrWhiteSpace(message))
            {
                Console.Out.WriteLine(message);
            }

            Shutdown(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Shutdown(1);
        }
    }

    private static void TryAttachToParentConsole()
    {
        try
        {
            AttachConsole(AttachParentProcess);
        }
        catch
        {
            // Best effort only. If there is no parent console, writes simply have nowhere visible to go.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);
}

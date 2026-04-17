using System;
using System.Runtime.InteropServices;
using System.Windows;
using MedWNetworkSim.App.Services;

namespace MedWNetworkSim.App;

public partial class App : Application
{
    private const uint AttachParentProcess = 0xFFFFFFFF;
    private static readonly TimeSpan IntroDuration = TimeSpan.FromSeconds(1.5);
    private readonly CommandLineRunService commandLineRunService = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = RemoveIntroArguments(e.Args);
        var skipIntro = args.Length != e.Args.Length;

        if (commandLineRunService.ShouldRunFromCommandLine(args))
        {
            RunCommandLineMode(args);
            return;
        }

        if (skipIntro)
        {
            ShowMainWindow();
            return;
        }

        await ShowIntroWindowAsync();
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

    private async Task ShowIntroWindowAsync()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var introWindow = new IntroWindow();
        var introClosed = false;
        introWindow.Closed += (_, _) => introClosed = true;
        MainWindow = introWindow;
        introWindow.Show();

        await Task.Delay(IntroDuration);

        if (introClosed)
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            Shutdown();
            return;
        }

        ShowMainWindow();
        introWindow.Close();
    }

    private void ShowMainWindow()
    {
        var window = new MainWindow();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    private static string[] RemoveIntroArguments(IEnumerable<string> args)
    {
        return args.Where(arg => !IsNoIntroToken(arg)).ToArray();
    }

    private static bool IsNoIntroToken(string arg)
    {
        return string.Equals(arg, "-nointro", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--nointro", StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);
}

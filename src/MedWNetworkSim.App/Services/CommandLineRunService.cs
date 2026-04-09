using System.IO;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public sealed class CommandLineRunService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private readonly NetworkFileService networkFileService = new();
    private readonly ReportExportService reportExportService = new();

    public bool ShouldRunFromCommandLine(string[] args)
    {
        return args.Length > 0;
    }

    public bool IsHelpRequest(string[] args)
    {
        return args.Any(arg =>
            Comparer.Equals(arg, "--help") ||
            Comparer.Equals(arg, "-h") ||
            Comparer.Equals(arg, "/?"));
    }

    public string GetUsageText()
    {
        return """
MedW Network Simulator command line usage

Named form:
  MedWNetworkSim.App.exe --network <file> --mode <simulation|timeline> --report <current|timeline> --output <file> [--turns <count>]

Positional form:
  MedWNetworkSim.App.exe <network-file> <simulation|timeline> <current|timeline> <output-file> [turns]

Examples:
  MedWNetworkSim.App.exe sample-network.json simulation current report.html
  MedWNetworkSim.App.exe --network sample-network.json --mode timeline --report timeline --turns 12 --output timeline.csv

Rules:
  - Use simulation/current for the same output as pressing Run Simulation.
  - Use timeline/timeline to simulate a number of periods and export a timeline report.
  - Output format is chosen from the output file extension:
      .html or .htm -> HTML
      .csv          -> CSV
      anything else -> CSV
""";
    }

    public CommandLineOptions Parse(string[] args)
    {
        var named = new Dictionary<string, string>(Comparer);
        var positional = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    throw new InvalidOperationException($"Missing value for option '{arg}'.");
                }

                named[arg[2..]] = args[++index];
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length == 2)
            {
                if (index + 1 >= args.Length)
                {
                    throw new InvalidOperationException($"Missing value for option '{arg}'.");
                }

                var key = arg[1] switch
                {
                    'n' => "network",
                    'm' => "mode",
                    'r' => "report",
                    'o' => "output",
                    't' => "turns",
                    _ => throw new InvalidOperationException($"Unknown option '{arg}'.")
                };
                named[key] = args[++index];
                continue;
            }

            positional.Add(arg);
        }

        var networkPath = GetValue(named, positional, "network", 0);
        var modeText = GetValue(named, positional, "mode", 1);
        var reportText = GetValue(named, positional, "report", 2, allowMissing: true);
        var outputPath = GetValue(named, positional, "output", 3);
        var turnsText = GetValue(named, positional, "turns", 4, allowMissing: true);

        if (string.IsNullOrWhiteSpace(networkPath))
        {
            throw new InvalidOperationException("Provide a network file path.");
        }

        if (!File.Exists(networkPath))
        {
            throw new FileNotFoundException("The specified network file was not found.", networkPath);
        }

        var mode = ParseMode(modeText);
        var reportType = string.IsNullOrWhiteSpace(reportText)
            ? mode == CommandLineRunMode.Timeline ? CommandLineReportType.Timeline : CommandLineReportType.Current
            : ParseReportType(reportText);

        ValidateModeAndReport(mode, reportType);

        var timelinePeriods = 0;
        if (mode == CommandLineRunMode.Timeline)
        {
            if (string.IsNullOrWhiteSpace(turnsText) ||
                !int.TryParse(turnsText, out timelinePeriods) ||
                timelinePeriods <= 0)
            {
                throw new InvalidOperationException("Timeline mode requires a positive number of turns via --turns or the fifth positional argument.");
            }
        }

        var normalizedOutputPath = NormalizeOutputPath(outputPath, out var format);
        return new CommandLineOptions
        {
            NetworkPath = Path.GetFullPath(networkPath),
            Mode = mode,
            ReportType = reportType,
            TimelinePeriods = timelinePeriods,
            OutputPath = normalizedOutputPath,
            ReportFormat = format
        };
    }

    public void Run(CommandLineOptions options)
    {
        var network = networkFileService.Load(options.NetworkPath);
        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);

        if (options.ReportType == CommandLineReportType.Timeline)
        {
            reportExportService.SaveTimelineReport(network, options.OutputPath, options.TimelinePeriods, options.ReportFormat);
            return;
        }

        reportExportService.SaveCurrentReport(network, options.OutputPath, options.ReportFormat);
    }

    private static string GetValue(
        IReadOnlyDictionary<string, string> named,
        IReadOnlyList<string> positional,
        string key,
        int positionalIndex,
        bool allowMissing = false)
    {
        if (named.TryGetValue(key, out var namedValue))
        {
            return namedValue;
        }

        if (positionalIndex < positional.Count)
        {
            return positional[positionalIndex];
        }

        if (allowMissing)
        {
            return string.Empty;
        }

        throw new InvalidOperationException($"Missing required value for '{key}'.");
    }

    private static CommandLineRunMode ParseMode(string value)
    {
        if (Comparer.Equals(value, "simulation") ||
            Comparer.Equals(value, "simulate") ||
            Comparer.Equals(value, "run"))
        {
            return CommandLineRunMode.Simulation;
        }

        if (Comparer.Equals(value, "timeline") ||
            Comparer.Equals(value, "turns"))
        {
            return CommandLineRunMode.Timeline;
        }

        throw new InvalidOperationException($"Unknown mode '{value}'. Use 'simulation' or 'timeline'.");
    }

    private static CommandLineReportType ParseReportType(string value)
    {
        if (Comparer.Equals(value, "current") ||
            Comparer.Equals(value, "simulation"))
        {
            return CommandLineReportType.Current;
        }

        if (Comparer.Equals(value, "timeline"))
        {
            return CommandLineReportType.Timeline;
        }

        throw new InvalidOperationException($"Unknown report type '{value}'. Use 'current' or 'timeline'.");
    }

    private static void ValidateModeAndReport(CommandLineRunMode mode, CommandLineReportType reportType)
    {
        if (mode == CommandLineRunMode.Simulation && reportType != CommandLineReportType.Current)
        {
            throw new InvalidOperationException("Simulation mode must be paired with the current report type.");
        }

        if (mode == CommandLineRunMode.Timeline && reportType != CommandLineReportType.Timeline)
        {
            throw new InvalidOperationException("Timeline mode must be paired with the timeline report type.");
        }
    }

    private static string NormalizeOutputPath(string outputPath, out ReportExportFormat format)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new InvalidOperationException("Provide an output file path.");
        }

        var extension = Path.GetExtension(outputPath);
        if (Comparer.Equals(extension, ".html") || Comparer.Equals(extension, ".htm"))
        {
            format = ReportExportFormat.Html;
            return Path.GetFullPath(Path.ChangeExtension(outputPath, ".html"));
        }

        if (Comparer.Equals(extension, ".csv"))
        {
            format = ReportExportFormat.Csv;
            return Path.GetFullPath(outputPath);
        }

        format = ReportExportFormat.Csv;
        return Path.GetFullPath(Path.ChangeExtension(outputPath, ".csv"));
    }
}

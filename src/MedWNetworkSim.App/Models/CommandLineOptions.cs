namespace MedWNetworkSim.App.Models;

public sealed class CommandLineOptions
{
    public string NetworkPath { get; init; } = string.Empty;

    public CommandLineRunMode Mode { get; init; }

    public CommandLineReportType ReportType { get; init; }

    public int TimelinePeriods { get; init; }

    public string OutputPath { get; init; } = string.Empty;

    public ReportExportFormat ReportFormat { get; init; }
}

using System.IO;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public sealed class CommandLineRunService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private readonly NetworkFileService networkFileService = new();
    private readonly ReportExportService reportExportService = new();
    private readonly NetworkSimulationEngine networkSimulationEngine = new();
    private readonly TemporalNetworkSimulationEngine temporalNetworkSimulationEngine = new();

    public bool ShouldRunFromCommandLine(string[] args)
    {
        return args.Length > 0;
    }

    public bool IsHelpRequest(string[] args)
    {
        return args.Any(IsHelpToken);
    }

    public string GetUsageText()
    {
        return """
MedW Network Simulator CLI

Usage:
  MedWNetworkSim.App.exe help
  MedWNetworkSim.App.exe run --file <network.json> --output <report.html|report.csv> [--mode simulation|timeline] [--report current|timeline] [--turns <count>]
  MedWNetworkSim.App.exe new --file <network.json> [--name <name>] [--description <text>] [--overwrite]
  MedWNetworkSim.App.exe set-network --file <network.json> [--name <name>] [--description <text>]
  MedWNetworkSim.App.exe add-traffic --file <network.json> --name <traffic-name> [--description <text>] [--preference speed|cost|totalCost] [--bid <amount>|none]
  MedWNetworkSim.App.exe add-node --file <network.json> --id <node-id> [--name <name>] [--shape square|circle|person|car|building] [--x <number>] [--y <number>] [--transhipment-capacity <amount>|none]
  MedWNetworkSim.App.exe set-profile --file <network.json> --node <node-id> --traffic <traffic-name> [--role producer|consumer|transship|producer+consumer|producer+transship|consumer+transship|all|none] [--production <amount>] [--consumption <amount>] [--premium <amount>] [--production-start <period>|none] [--production-end <period>|none] [--consumption-start <period>|none] [--consumption-end <period>|none] [--store|--no-store] [--store-capacity <amount>|none]
  MedWNetworkSim.App.exe add-edge --file <network.json> --from <node-id> --to <node-id> [--id <edge-id>] [--time <number>] [--cost <number>] [--capacity <amount>|none] [--direction one-way|bidirectional]
  MedWNetworkSim.App.exe auto-arrange --file <network.json>

Friendly aliases:
  help, -h, -help, --help, /?

Notes:
  - `run` is the same reporting flow as the GUI export path.
  - If you omit the command and pass the older positional form, it still runs a report:
      MedWNetworkSim.App.exe <network-file> <simulation|timeline> <current|timeline> <output-file> [turns]
  - Output format is chosen from the output file extension:
      .html or .htm -> HTML
      .csv          -> CSV
      anything else -> CSV
  - `add-traffic`, `add-node`, and `add-edge` create or update existing items when the same name/id is already present.
  - Use `none` for optional capacities or schedules when you want to clear them.

Examples:
  MedWNetworkSim.App.exe run --file .\network.json --output .\report.html
  MedWNetworkSim.App.exe run --file .\network.json --mode timeline --report timeline --turns 12 --output .\timeline.csv
  MedWNetworkSim.App.exe new --file .\demo.json --name "Demo Network"
  MedWNetworkSim.App.exe add-traffic --file .\demo.json --name Waste --preference cost --bid 1.5
  MedWNetworkSim.App.exe add-node --file .\demo.json --id N1 --name "Clinic A" --shape building --x 120 --y 180
  MedWNetworkSim.App.exe set-profile --file .\demo.json --node N1 --traffic Waste --role producer --production 25
  MedWNetworkSim.App.exe add-edge --file .\demo.json --id E1 --from N1 --to N2 --time 1 --cost 4 --direction bidirectional
""";
    }

    public CommandLineOptions Parse(string[] args)
    {
        if (args.Length == 0 || IsHelpRequest(args))
        {
            return new CommandLineOptions
            {
                Command = CommandLineCommand.Help
            };
        }

        if (TryGetCommand(args[0], out var command))
        {
            var (named, positional) = ParseArguments(args.Skip(1));
            return command switch
            {
                CommandLineCommand.Run => ParseRunCommand(named, positional),
                CommandLineCommand.NewNetwork => ParseNewNetworkCommand(named, positional),
                CommandLineCommand.SetNetwork => ParseSetNetworkCommand(named, positional),
                CommandLineCommand.AddTraffic => ParseAddTrafficCommand(named, positional),
                CommandLineCommand.AddNode => ParseAddNodeCommand(named, positional),
                CommandLineCommand.SetProfile => ParseSetProfileCommand(named, positional),
                CommandLineCommand.AddEdge => ParseAddEdgeCommand(named, positional),
                CommandLineCommand.AutoArrange => ParseAutoArrangeCommand(named, positional),
                _ => new CommandLineOptions { Command = CommandLineCommand.Help }
            };
        }

        var (legacyNamed, legacyPositional) = ParseArguments(args);
        return ParseRunCommand(legacyNamed, legacyPositional);
    }

    public string Run(CommandLineOptions options)
    {
        return options.Command switch
        {
            CommandLineCommand.Help => GetUsageText(),
            CommandLineCommand.Run => ExecuteRun(options),
            CommandLineCommand.NewNetwork => ExecuteNewNetwork(options),
            CommandLineCommand.SetNetwork => ExecuteSetNetwork(options),
            CommandLineCommand.AddTraffic => ExecuteAddTraffic(options),
            CommandLineCommand.AddNode => ExecuteAddNode(options),
            CommandLineCommand.SetProfile => ExecuteSetProfile(options),
            CommandLineCommand.AddEdge => ExecuteAddEdge(options),
            CommandLineCommand.AutoArrange => ExecuteAutoArrange(options),
            _ => throw new InvalidOperationException("Unknown CLI command.")
        };
    }

    private static bool TryGetCommand(string token, out CommandLineCommand command)
    {
        if (IsHelpToken(token))
        {
            command = CommandLineCommand.Help;
            return true;
        }

        var normalized = token.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "run":
            case "report":
            case "simulate":
                command = CommandLineCommand.Run;
                return true;
            case "new":
            case "create":
            case "init":
                command = CommandLineCommand.NewNetwork;
                return true;
            case "set-network":
            case "network":
                command = CommandLineCommand.SetNetwork;
                return true;
            case "add-traffic":
            case "traffic":
                command = CommandLineCommand.AddTraffic;
                return true;
            case "add-node":
            case "node":
                command = CommandLineCommand.AddNode;
                return true;
            case "set-profile":
            case "profile":
                command = CommandLineCommand.SetProfile;
                return true;
            case "add-edge":
            case "edge":
                command = CommandLineCommand.AddEdge;
                return true;
            case "auto-arrange":
            case "arrange":
                command = CommandLineCommand.AutoArrange;
                return true;
            default:
                command = default;
                return false;
        }
    }

    private static bool IsHelpToken(string arg)
    {
        return Comparer.Equals(arg, "help") ||
            Comparer.Equals(arg, "--help") ||
            Comparer.Equals(arg, "-help") ||
            Comparer.Equals(arg, "-h") ||
            Comparer.Equals(arg, "/?");
    }

    private static (Dictionary<string, string> Named, List<string> Positional) ParseArguments(IEnumerable<string> args)
    {
        var named = new Dictionary<string, string>(Comparer);
        var positional = new List<string>();
        var values = args.ToList();

        for (var index = 0; index < values.Count; index++)
        {
            var arg = values[index];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var body = arg[2..];
                var separatorIndex = body.IndexOf('=');
                if (separatorIndex >= 0)
                {
                    named[body[..separatorIndex]] = body[(separatorIndex + 1)..];
                    continue;
                }

                if (TryParseBooleanFlag(body, out var flagKey, out var flagValue))
                {
                    named[flagKey] = flagValue;
                    continue;
                }

                if (index + 1 >= values.Count)
                {
                    throw new InvalidOperationException($"Missing value for option '--{body}'.");
                }

                named[body] = values[++index];
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length == 2)
            {
                var key = arg[1] switch
                {
                    'n' => "network",
                    'm' => "mode",
                    'r' => "report",
                    'o' => "output",
                    't' => "turns",
                    'f' => "file",
                    _ => throw new InvalidOperationException($"Unknown option '{arg}'.")
                };

                if (index + 1 >= values.Count)
                {
                    throw new InvalidOperationException($"Missing value for option '{arg}'.");
                }

                named[key] = values[++index];
                continue;
            }

            positional.Add(arg);
        }

        return (named, positional);
    }

    private static bool TryParseBooleanFlag(string name, out string key, out string value)
    {
        switch (name.ToLowerInvariant())
        {
            case "overwrite":
                key = "overwrite";
                value = "true";
                return true;
            case "store":
                key = "store";
                value = "true";
                return true;
            case "no-store":
                key = "store";
                value = "false";
                return true;
            case "one-way":
                key = "direction";
                value = "one-way";
                return true;
            case "bidirectional":
                key = "direction";
                value = "bidirectional";
                return true;
            default:
                key = string.Empty;
                value = string.Empty;
                return false;
        }
    }

    private CommandLineOptions ParseRunCommand(
        IReadOnlyDictionary<string, string> named,
        IReadOnlyList<string> positional)
    {
        var networkPath = GetRequiredValue(named, positional, 0, "file", "network");
        var outputPath = GetRequiredValue(named, positional, 3, "output");
        var modeText = GetOptionalValue(named, positional, 1, "mode");
        var reportText = GetOptionalValue(named, positional, 2, "report");
        var turnsText = GetOptionalValue(named, positional, 4, "turns");

        EnsureFileExists(networkPath);
        var mode = string.IsNullOrWhiteSpace(modeText) ? CommandLineRunMode.Simulation : ParseMode(modeText);
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
                throw new InvalidOperationException("Timeline mode requires a positive number of turns via --turns.");
            }
        }

        var normalizedOutputPath = NormalizeOutputPath(outputPath, out var format);
        return new CommandLineOptions
        {
            Command = CommandLineCommand.Run,
            NetworkPath = Path.GetFullPath(networkPath),
            Mode = mode,
            ReportType = reportType,
            TimelinePeriods = timelinePeriods,
            OutputPath = normalizedOutputPath,
            ReportFormat = format
        };
    }

    private CommandLineOptions ParseNewNetworkCommand(
        IReadOnlyDictionary<string, string> named,
        IReadOnlyList<string> positional)
    {
        var networkPath = GetRequiredValue(named, positional, 0, "file", "network");
        return new CommandLineOptions
        {
            Command = CommandLineCommand.NewNetwork,
            NetworkPath = Path.GetFullPath(networkPath),
            Overwrite = ParseBool(GetOptionalNamedValue(named, "overwrite")),
            NetworkName = GetOptionalNamedValue(named, "name"),
            NetworkDescription = GetOptionalNamedValue(named, "description"),
            HasNetworkName = HasOption(named, "name"),
            HasNetworkDescription = HasOption(named, "description")
        };
    }

    private CommandLineOptions ParseSetNetworkCommand(
        IReadOnlyDictionary<string, string> named,
        IReadOnlyList<string> positional)
    {
        var networkPath = GetRequiredValue(named, positional, 0, "file", "network");
        EnsureFileExists(networkPath);

        var hasName = HasOption(named, "name");
        var hasDescription = HasOption(named, "description");
        if (!hasName && !hasDescription)
        {
            throw new InvalidOperationException("set-network requires --name, --description, or both.");
        }

        return new CommandLineOptions
        {
            Command = CommandLineCommand.SetNetwork,
            NetworkPath = Path.GetFullPath(networkPath),
            NetworkName = GetOptionalNamedValue(named, "name"),
            NetworkDescription = GetOptionalNamedValue(named, "description"),
            HasNetworkName = hasName,
            HasNetworkDescription = hasDescription
        };
    }

    private CommandLineOptions ParseAddTrafficCommand(
        IReadOnlyDictionary<string, string> named,
        IReadOnlyList<string> positional)
    {
        var networkPath = GetRequiredValue(named, positional, 0, "file", "network");
        EnsureFileExists(networkPath);
        var trafficName = GetRequiredValue(named, positional, 1, "name", "traffic");
        return new CommandLineOptions
        {
            Command = CommandLineCommand.AddTraffic,
            NetworkPath = Path.GetFullPath(networkPath),
            TrafficName = trafficName.Trim(),
            TrafficDescription = GetOptionalNamedValue(named, "description"),
            HasTrafficDescription = HasOption(named, "description"),
            RoutingPreference = HasOption(named, "preference")
                ? ParseRoutingPreference(GetOptionalNamedValue(named, "preference"))
                : null,
            CapacityBidPerUnit = ParseOptionalDoubleOption(named, "bid"),
            HasCapacityBidPerUnit = HasOption(named, "bid")
        };
    }

    private CommandLineOptions ParseAddNodeCommand(
        IReadOnlyDictionary<string, string> named,
        IReadOnlyList<string> positional)
    {
        var networkPath = GetRequiredValue(named, positional, 0, "file", "network");
        EnsureFileExists(networkPath);
        var nodeId = GetRequiredValue(named, positional, 1, "id", "node");
        return new CommandLineOptions
        {
            Command = CommandLineCommand.AddNode,
            NetworkPath = Path.GetFullPath(networkPath),
            NodeId = nodeId.Trim(),
            NodeName = GetOptionalNamedValue(named, "name"),
            HasNodeName = HasOption(named, "name"),
            NodeShape = HasOption(named, "shape")
                ? ParseNodeShape(GetOptionalNamedValue(named, "shape"))
                : null,
            NodeX = ParseDoubleOption(named, "x"),
            HasNodeX = HasOption(named, "x"),
            NodeY = ParseDoubleOption(named, "y"),
            HasNodeY = HasOption(named, "y"),
            TranshipmentCapacity = ParseOptionalDoubleOption(named, "transhipment-capacity"),
            HasTranshipmentCapacity = HasOption(named, "transhipment-capacity")
        };
    }

    private CommandLineOptions ParseSetProfileCommand(
        IReadOnlyDictionary<string, string> named,
        IReadOnlyList<string> positional)
    {
        var networkPath = GetRequiredValue(named, positional, 0, "file", "network");
        EnsureFileExists(networkPath);
        var nodeId = GetRequiredValue(named, positional, 1, "node", "id");
        var trafficName = GetRequiredValue(named, positional, 2, "traffic", "name");

        return new CommandLineOptions
        {
            Command = CommandLineCommand.SetProfile,
            NetworkPath = Path.GetFullPath(networkPath),
            NodeId = nodeId.Trim(),
            ProfileTrafficType = trafficName.Trim(),
            RoleName = GetOptionalNamedValue(named, "role"),
            HasRoleName = HasOption(named, "role"),
            Production = ParseDoubleOption(named, "production"),
            HasProduction = HasOption(named, "production"),
            Consumption = ParseDoubleOption(named, "consumption"),
            HasConsumption = HasOption(named, "consumption"),
            ConsumerPremiumPerUnit = ParseDoubleOption(named, "premium"),
            HasConsumerPremiumPerUnit = HasOption(named, "premium"),
            ProductionStartPeriod = ParseOptionalIntOption(named, "production-start"),
            HasProductionStartPeriod = HasOption(named, "production-start"),
            ProductionEndPeriod = ParseOptionalIntOption(named, "production-end"),
            HasProductionEndPeriod = HasOption(named, "production-end"),
            ConsumptionStartPeriod = ParseOptionalIntOption(named, "consumption-start"),
            HasConsumptionStartPeriod = HasOption(named, "consumption-start"),
            ConsumptionEndPeriod = ParseOptionalIntOption(named, "consumption-end"),
            HasConsumptionEndPeriod = HasOption(named, "consumption-end"),
            IsStore = HasOption(named, "store")
                ? ParseBool(GetOptionalNamedValue(named, "store"))
                : null,
            HasIsStore = HasOption(named, "store"),
            StoreCapacity = ParseOptionalDoubleOption(named, "store-capacity"),
            HasStoreCapacity = HasOption(named, "store-capacity")
        };
    }

    private CommandLineOptions ParseAddEdgeCommand(
        IReadOnlyDictionary<string, string> named,
        IReadOnlyList<string> positional)
    {
        var networkPath = GetRequiredValue(named, positional, 0, "file", "network");
        EnsureFileExists(networkPath);
        return new CommandLineOptions
        {
            Command = CommandLineCommand.AddEdge,
            NetworkPath = Path.GetFullPath(networkPath),
            EdgeId = GetOptionalNamedValue(named, "id"),
            FromNodeId = GetRequiredValue(named, positional, 1, "from"),
            ToNodeId = GetRequiredValue(named, positional, 2, "to"),
            EdgeTime = ParseDoubleOption(named, "time") ?? 0d,
            HasEdgeTime = HasOption(named, "time"),
            EdgeCost = ParseDoubleOption(named, "cost") ?? 0d,
            HasEdgeCost = HasOption(named, "cost"),
            EdgeCapacity = ParseOptionalDoubleOption(named, "capacity"),
            HasEdgeCapacity = HasOption(named, "capacity"),
            EdgeIsBidirectional = HasOption(named, "direction")
                ? ParseDirection(GetOptionalNamedValue(named, "direction"))
                : null,
            HasEdgeIsBidirectional = HasOption(named, "direction")
        };
    }

    private CommandLineOptions ParseAutoArrangeCommand(
        IReadOnlyDictionary<string, string> named,
        IReadOnlyList<string> positional)
    {
        var networkPath = GetRequiredValue(named, positional, 0, "file", "network");
        EnsureFileExists(networkPath);
        return new CommandLineOptions
        {
            Command = CommandLineCommand.AutoArrange,
            NetworkPath = Path.GetFullPath(networkPath)
        };
    }

    private string ExecuteRun(CommandLineOptions options)
    {
        var network = networkFileService.Load(options.NetworkPath);
        EnsureParentDirectory(options.OutputPath);

        if (options.ReportType == CommandLineReportType.Timeline)
        {
            var timelineResults = RunTimeline(network, options.TimelinePeriods);
            reportExportService.SaveTimelineReport(network, timelineResults, options.OutputPath, options.ReportFormat);
            return $"Timeline report written to {options.OutputPath}";
        }

        var outcomes = networkSimulationEngine.Simulate(network);
        var consumerCosts = networkSimulationEngine.SummarizeConsumerCosts(outcomes);
        reportExportService.SaveCurrentReport(network, outcomes, consumerCosts, options.OutputPath, options.ReportFormat);
        return $"Current report written to {options.OutputPath}";
    }

    private string ExecuteNewNetwork(CommandLineOptions options)
    {
        if (File.Exists(options.NetworkPath) && !options.Overwrite)
        {
            throw new InvalidOperationException($"'{options.NetworkPath}' already exists. Use --overwrite if you want to replace it.");
        }

        var network = new NetworkModel
        {
            Name = options.HasNetworkName ? options.NetworkName : Path.GetFileNameWithoutExtension(options.NetworkPath),
            Description = options.HasNetworkDescription ? options.NetworkDescription : string.Empty
        };

        SaveNetwork(network, options.NetworkPath);
        return $"Created network file at {options.NetworkPath}";
    }

    private string ExecuteSetNetwork(CommandLineOptions options)
    {
        var network = networkFileService.Load(options.NetworkPath);
        if (options.HasNetworkName)
        {
            network.Name = options.NetworkName;
        }

        if (options.HasNetworkDescription)
        {
            network.Description = options.NetworkDescription;
        }

        SaveNetwork(network, options.NetworkPath);
        return $"Updated network metadata in {options.NetworkPath}";
    }

    private string ExecuteAddTraffic(CommandLineOptions options)
    {
        var network = networkFileService.Load(options.NetworkPath);
        var traffic = network.TrafficTypes.FirstOrDefault(item => Comparer.Equals(item.Name, options.TrafficName));
        var created = false;

        if (traffic is null)
        {
            traffic = new TrafficTypeDefinition
            {
                Name = options.TrafficName
            };
            network.TrafficTypes.Add(traffic);
            created = true;
        }

        if (options.HasTrafficDescription)
        {
            traffic.Description = options.TrafficDescription;
        }

        if (options.RoutingPreference.HasValue)
        {
            traffic.RoutingPreference = options.RoutingPreference.Value;
        }

        if (options.HasCapacityBidPerUnit)
        {
            traffic.CapacityBidPerUnit = options.CapacityBidPerUnit;
        }

        SaveNetwork(network, options.NetworkPath);
        return created
            ? $"Added traffic type '{traffic.Name}' to {options.NetworkPath}"
            : $"Updated traffic type '{traffic.Name}' in {options.NetworkPath}";
    }

    private string ExecuteAddNode(CommandLineOptions options)
    {
        var network = networkFileService.Load(options.NetworkPath);
        var node = network.Nodes.FirstOrDefault(item => Comparer.Equals(item.Id, options.NodeId));
        var created = false;

        if (node is null)
        {
            node = new NodeModel
            {
                Id = options.NodeId,
                Name = options.HasNodeName ? options.NodeName : options.NodeId
            };
            network.Nodes.Add(node);
            created = true;
        }

        if (options.HasNodeName)
        {
            node.Name = options.NodeName;
        }

        if (options.NodeShape.HasValue)
        {
            node.Shape = options.NodeShape.Value;
        }

        if (options.HasNodeX)
        {
            node.X = options.NodeX;
        }

        if (options.HasNodeY)
        {
            node.Y = options.NodeY;
        }

        if (options.HasTranshipmentCapacity)
        {
            node.TranshipmentCapacity = options.TranshipmentCapacity;
        }

        SaveNetwork(network, options.NetworkPath);
        return created
            ? $"Added node '{node.Id}' to {options.NetworkPath}"
            : $"Updated node '{node.Id}' in {options.NetworkPath}";
    }

    private string ExecuteSetProfile(CommandLineOptions options)
    {
        var network = networkFileService.Load(options.NetworkPath);
        var node = network.Nodes.FirstOrDefault(item => Comparer.Equals(item.Id, options.NodeId))
            ?? throw new InvalidOperationException($"Node '{options.NodeId}' was not found.");
        var profile = node.TrafficProfiles.FirstOrDefault(item => Comparer.Equals(item.TrafficType, options.ProfileTrafficType));
        var created = false;

        if (profile is null)
        {
            profile = new NodeTrafficProfile
            {
                TrafficType = options.ProfileTrafficType
            };
            node.TrafficProfiles.Add(profile);
            created = true;
        }

        if (options.HasRoleName)
        {
            var normalizedRoleName = NormalizeRoleName(options.RoleName);
            if (!NodeTrafficRoleCatalog.TryParseFlags(normalizedRoleName, out var flags))
            {
                throw new InvalidOperationException($"Unknown role '{options.RoleName}'.");
            }

            profile.CanTransship = flags.CanTransship;
            profile.Production = flags.IsProducer
                ? options.HasProduction
                    ? options.Production ?? 0d
                    : profile.Production > 0d ? profile.Production : 1d
                : 0d;
            profile.Consumption = flags.IsConsumer
                ? options.HasConsumption
                    ? options.Consumption ?? 0d
                    : profile.Consumption > 0d ? profile.Consumption : 1d
                : 0d;
        }
        else
        {
            if (options.HasProduction)
            {
                profile.Production = options.Production ?? 0d;
            }

            if (options.HasConsumption)
            {
                profile.Consumption = options.Consumption ?? 0d;
            }
        }

        if (options.HasConsumerPremiumPerUnit)
        {
            profile.ConsumerPremiumPerUnit = options.ConsumerPremiumPerUnit ?? 0d;
        }

        if (options.HasProductionStartPeriod)
        {
            profile.ProductionStartPeriod = options.ProductionStartPeriod;
        }

        if (options.HasProductionEndPeriod)
        {
            profile.ProductionEndPeriod = options.ProductionEndPeriod;
        }

        if (options.HasConsumptionStartPeriod)
        {
            profile.ConsumptionStartPeriod = options.ConsumptionStartPeriod;
        }

        if (options.HasConsumptionEndPeriod)
        {
            profile.ConsumptionEndPeriod = options.ConsumptionEndPeriod;
        }

        if (options.HasIsStore)
        {
            profile.IsStore = options.IsStore ?? false;
        }

        if (options.HasStoreCapacity)
        {
            profile.StoreCapacity = options.StoreCapacity;
        }

        if (IsProfileEmpty(profile))
        {
            node.TrafficProfiles.Remove(profile);
            SaveNetwork(network, options.NetworkPath);
            return $"Cleared traffic profile '{options.ProfileTrafficType}' from node '{node.Id}' in {options.NetworkPath}";
        }

        SaveNetwork(network, options.NetworkPath);
        return created
            ? $"Added traffic profile '{profile.TrafficType}' to node '{node.Id}' in {options.NetworkPath}"
            : $"Updated traffic profile '{profile.TrafficType}' on node '{node.Id}' in {options.NetworkPath}";
    }

    private string ExecuteAddEdge(CommandLineOptions options)
    {
        var network = networkFileService.Load(options.NetworkPath);
        EdgeModel? edge = null;
        var created = false;

        if (!string.IsNullOrWhiteSpace(options.EdgeId))
        {
            edge = network.Edges.FirstOrDefault(item => Comparer.Equals(item.Id, options.EdgeId));
        }

        if (edge is null)
        {
            edge = new EdgeModel
            {
                Id = options.EdgeId,
                FromNodeId = options.FromNodeId,
                ToNodeId = options.ToNodeId,
                Time = options.HasEdgeTime ? options.EdgeTime : 1d,
                Cost = options.HasEdgeCost ? options.EdgeCost : 0d
            };
            network.Edges.Add(edge);
            created = true;
        }
        else
        {
            edge.FromNodeId = options.FromNodeId;
            edge.ToNodeId = options.ToNodeId;
            if (options.HasEdgeTime)
            {
                edge.Time = options.EdgeTime;
            }

            if (options.HasEdgeCost)
            {
                edge.Cost = options.EdgeCost;
            }
        }

        if (options.HasEdgeCapacity)
        {
            edge.Capacity = options.EdgeCapacity;
        }

        if (options.HasEdgeIsBidirectional)
        {
            edge.IsBidirectional = options.EdgeIsBidirectional ?? true;
        }

        SaveNetwork(network, options.NetworkPath);
        return created
            ? $"Added edge '{(string.IsNullOrWhiteSpace(edge.Id) ? $"{edge.FromNodeId}->{edge.ToNodeId}" : edge.Id)}' to {options.NetworkPath}"
            : $"Updated edge '{edge.Id}' in {options.NetworkPath}";
    }

    private string ExecuteAutoArrange(CommandLineOptions options)
    {
        var network = networkFileService.Load(options.NetworkPath);
        var arranged = networkFileService.AutoArrange(network);
        SaveNetwork(arranged, options.NetworkPath);
        return $"Auto-arranged nodes in {options.NetworkPath}";
    }

    private static string NormalizeRoleName(string roleName)
    {
        var normalized = roleName.Trim().ToLowerInvariant().Replace(" ", string.Empty);
        return normalized switch
        {
            "producer" => NodeTrafficRoleCatalog.ProducerRole,
            "consumer" => NodeTrafficRoleCatalog.ConsumerRole,
            "transship" or "transshipment" => NodeTrafficRoleCatalog.TransshipRole,
            "producer+consumer" or "consumer+producer" => NodeTrafficRoleCatalog.ProducerConsumerRole,
            "producer+transship" or "transship+producer" or "producer+transshipment" => NodeTrafficRoleCatalog.ProducerTransshipRole,
            "consumer+transship" or "transship+consumer" or "consumer+transshipment" => NodeTrafficRoleCatalog.ConsumerTransshipRole,
            "all" or "producer+consumer+transship" or "producer+consumer+transshipment" => NodeTrafficRoleCatalog.ProducerConsumerTransshipRole,
            "none" => NodeTrafficRoleCatalog.NoTrafficRole,
            _ => roleName.Trim()
        };
    }

    private static bool IsProfileEmpty(NodeTrafficProfile profile)
    {
        return profile.Production <= 0d &&
            profile.Consumption <= 0d &&
            profile.ConsumerPremiumPerUnit <= 0d &&
            !profile.CanTransship &&
            !profile.IsStore &&
            !profile.ProductionStartPeriod.HasValue &&
            !profile.ProductionEndPeriod.HasValue &&
            !profile.ConsumptionStartPeriod.HasValue &&
            !profile.ConsumptionEndPeriod.HasValue &&
            !profile.StoreCapacity.HasValue;
    }

    private void SaveNetwork(NetworkModel network, string path)
    {
        EnsureParentDirectory(path);
        networkFileService.Save(network, path);
    }

    private static void EnsureParentDirectory(string path)
    {
        var outputDirectory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
    }

    private static void EnsureFileExists(string networkPath)
    {
        if (!File.Exists(networkPath))
        {
            throw new FileNotFoundException("The specified network file was not found.", networkPath);
        }
    }

    private static string GetRequiredValue(
        IReadOnlyDictionary<string, string> named,
        IReadOnlyList<string> positional,
        int positionalIndex,
        params string[] keys)
    {
        var value = GetOptionalValue(named, positional, positionalIndex, keys);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required value for '{keys[0]}'.");
        }

        return value;
    }

    private static string GetOptionalValue(
        IReadOnlyDictionary<string, string> named,
        IReadOnlyList<string> positional,
        int positionalIndex,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (named.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return positionalIndex < positional.Count ? positional[positionalIndex] : string.Empty;
    }

    private static string GetOptionalNamedValue(IReadOnlyDictionary<string, string> named, string key)
    {
        return named.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static bool HasOption(IReadOnlyDictionary<string, string> named, params string[] keys)
    {
        return keys.Any(named.ContainsKey);
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

    private static RoutingPreference ParseRoutingPreference(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "speed" => RoutingPreference.Speed,
            "cost" => RoutingPreference.Cost,
            "totalcost" or "total-cost" or "total" => RoutingPreference.TotalCost,
            _ => throw new InvalidOperationException($"Unknown routing preference '{value}'. Use 'speed', 'cost', or 'totalCost'.")
        };
    }

    private static NodeVisualShape ParseNodeShape(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "square" => NodeVisualShape.Square,
            "circle" => NodeVisualShape.Circle,
            "person" => NodeVisualShape.Person,
            "car" => NodeVisualShape.Car,
            "building" => NodeVisualShape.Building,
            _ => throw new InvalidOperationException($"Unknown node shape '{value}'. Use square, circle, person, car, or building.")
        };
    }

    private static bool ParseDirection(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "bidirectional" or "two-way" or "twoway" => true,
            "one-way" or "oneway" => false,
            _ => throw new InvalidOperationException($"Unknown direction '{value}'. Use 'one-way' or 'bidirectional'.")
        };
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

    private static double? ParseDoubleOption(IReadOnlyDictionary<string, string> named, string key)
    {
        if (!named.TryGetValue(key, out var value))
        {
            return null;
        }

        if (!double.TryParse(value, out var result))
        {
            throw new InvalidOperationException($"'{value}' is not a valid number for --{key}.");
        }

        return result;
    }

    private static double? ParseOptionalDoubleOption(IReadOnlyDictionary<string, string> named, string key)
    {
        if (!named.TryGetValue(key, out var value))
        {
            return null;
        }

        if (Comparer.Equals(value, "none") || Comparer.Equals(value, "unlimited"))
        {
            return null;
        }

        if (!double.TryParse(value, out var result))
        {
            throw new InvalidOperationException($"'{value}' is not a valid number for --{key}.");
        }

        return result;
    }

    private static int? ParseOptionalIntOption(IReadOnlyDictionary<string, string> named, string key)
    {
        if (!named.TryGetValue(key, out var value))
        {
            return null;
        }

        if (Comparer.Equals(value, "none"))
        {
            return null;
        }

        if (!int.TryParse(value, out var result))
        {
            throw new InvalidOperationException($"'{value}' is not a valid integer for --{key}.");
        }

        return result;
    }

    private static bool ParseBool(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (Comparer.Equals(value, "true") ||
            Comparer.Equals(value, "yes") ||
            Comparer.Equals(value, "1"))
        {
            return true;
        }

        if (Comparer.Equals(value, "false") ||
            Comparer.Equals(value, "no") ||
            Comparer.Equals(value, "0"))
        {
            return false;
        }

        throw new InvalidOperationException($"'{value}' is not a valid boolean value.");
    }

    private IReadOnlyList<TemporalNetworkSimulationEngine.TemporalSimulationStepResult> RunTimeline(NetworkModel network, int periods)
    {
        var state = temporalNetworkSimulationEngine.Initialize(network);
        var results = new List<TemporalNetworkSimulationEngine.TemporalSimulationStepResult>(periods);

        for (var period = 0; period < periods; period++)
        {
            results.Add(temporalNetworkSimulationEngine.Advance(network, state));
        }

        return results;
    }
}

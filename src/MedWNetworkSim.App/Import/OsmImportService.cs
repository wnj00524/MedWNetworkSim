using System.IO;
using SimulationNetwork = MedWNetworkSim.App.Models.NetworkModel;

namespace MedWNetworkSim.App.Import;

public sealed class OsmImportService
{
    private readonly IReadOnlyList<IOsmGraphParser> parsers;
    private readonly GraphSimplifier simplifier;
    private readonly OsmToSimulationMapper mapper;

    public OsmImportService()
        : this([new OsmXmlParser(), new OsmPbfParser()], new GraphSimplifier(), new OsmToSimulationMapper())
    {
    }

    public OsmImportService(
        IReadOnlyList<IOsmGraphParser> parsers,
        GraphSimplifier simplifier,
        OsmToSimulationMapper mapper)
    {
        this.parsers = parsers;
        this.simplifier = simplifier;
        this.mapper = mapper;
    }

    public SimulationNetwork ImportFromFile(string path)
    {
        return ImportFromFileAsync(path, options: null).GetAwaiter().GetResult();
    }

    public SimulationNetwork ImportFromFile(string path, OsmImportOptions? options)
    {
        return ImportFromFileAsync(path, options: options).GetAwaiter().GetResult();
    }

    public async Task<SimulationNetwork> ImportFromFileAsync(
        string path,
        OsmImportOptions? options = null,
        IProgress<OsmImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new OsmImportProgress(0.02d, "Preparing import…"));

        var parser = ResolveParser(path);

        OsmParseResult parsed;
        try
        {
            parsed = await parser.ParseAsync(path, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (parser is OsmPbfParser && ex is not OperationCanceledException)
        {
            throw new InvalidDataException("The file could not be read as a valid OpenStreetMap PBF file.", ex);
        }

        if (parsed.Graph.Edges.Count == 0)
        {
            throw new InvalidDataException("This file did not contain any supported road data to import.");
        }

        progress?.Report(new OsmImportProgress(0.72d, "Simplifying network…"));
        var simplificationOptions = GraphSimplificationOptions.FromImportOptions(options);
        var simplified = simplifier.Simplify(parsed.Graph, simplificationOptions);

        if (simplified.Edges.Count == 0)
        {
            throw new InvalidDataException("This file did not contain any supported road data to import after simplification.");
        }

        progress?.Report(new OsmImportProgress(0.86d, "Building simulation network…"));
        var retentionSummary = simplified.Stats is null
            ? null
            : new OsmRetentionSummary(
                simplified.Stats.MandatoryKeptNodes,
                simplified.Stats.OptionalKeptNodes,
                simplified.Nodes.Count,
                simplified.Stats.RequestedPercentage,
                simplified.Stats.EffectivePercentage,
                simplified.Stats.BaselineNodeCount);

        var summary = new OsmImportSummary(parsed.Summary, simplified.Nodes.Count, simplified.Edges.Count, retentionSummary);
        var network = mapper.Map(simplified, Path.GetFileName(path), summary);

        var warningSuffix = parsed.Summary.Warnings.Count > 0
            ? $" Warnings: {string.Join(" ", parsed.Summary.Warnings)}"
            : string.Empty;

        progress?.Report(new OsmImportProgress(
            1d,
            BuildCompletedMessage(simplified, warningSuffix)));

        return network;
    }

    private static string BuildCompletedMessage(GraphSimplifier.SimplifiedGraph simplified, string warningSuffix)
    {
        if (simplified.Stats is null)
        {
            return $"Imported {simplified.Nodes.Count:N0} nodes and {simplified.Edges.Count:N0} edges from road data.{warningSuffix}";
        }

        return $"Imported {simplified.Nodes.Count:N0} nodes and {simplified.Edges.Count:N0} edges from road data " +
               $"(requested retention: {simplified.Stats.RequestedPercentage}%; effective: {simplified.Stats.EffectivePercentage}%).{warningSuffix}";
    }

    public IOsmGraphParser ResolveParser(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("OSM file path is required.", nameof(path));
        }

        var extension = Path.GetExtension(path);
        var parser = parsers.FirstOrDefault(candidate => candidate.CanParseExtension(extension));
        if (parser is null)
        {
            throw new NotSupportedException("Supported OpenStreetMap formats are .osm and .pbf.");
        }

        return parser;
    }
}

using System.IO;
using SimulationNetwork = MedWNetworkSim.App.Models.NetworkModel;

namespace MedWNetworkSim.App.Import;

public sealed class OsmImportService
{
    private readonly IReadOnlyList<IOsmSourceParser> parsers;
    private readonly GraphSimplifier simplifier;
    private readonly OsmToSimulationMapper mapper;

    public OsmImportService()
        : this([new OsmXmlParser(), new OsmPbfParser()], new GraphSimplifier(), new OsmToSimulationMapper())
    {
    }

    public OsmImportService(
        IReadOnlyList<IOsmSourceParser> parsers,
        GraphSimplifier simplifier,
        OsmToSimulationMapper mapper)
    {
        this.parsers = parsers;
        this.simplifier = simplifier;
        this.mapper = mapper;
    }

    public SimulationNetwork ImportFromFile(string path)
    {
        return ImportFromFileAsync(path).GetAwaiter().GetResult();
    }

    public async Task<SimulationNetwork> ImportFromFileAsync(
        string path,
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
        var simplified = simplifier.Simplify(parsed.Graph);

        if (simplified.Edges.Count == 0)
        {
            throw new InvalidDataException("This file did not contain any supported road data to import after simplification.");
        }

        progress?.Report(new OsmImportProgress(0.86d, "Building simulation network…"));
        var summary = new OsmImportSummary(parsed.Summary, simplified.Nodes.Count, simplified.Edges.Count);
        var network = mapper.Map(simplified, Path.GetFileName(path), summary);

        var warningSuffix = parsed.Summary.Warnings.Count > 0
            ? $" Warnings: {string.Join(" ", parsed.Summary.Warnings)}"
            : string.Empty;

        progress?.Report(new OsmImportProgress(
            1d,
            $"Imported {simplified.Nodes.Count:N0} nodes and {simplified.Edges.Count:N0} edges from road data.{warningSuffix}"));

        return network;
    }

    public IOsmSourceParser ResolveParser(string path)
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

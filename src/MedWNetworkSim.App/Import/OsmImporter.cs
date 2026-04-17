using SimulationNetwork = MedWNetworkSim.App.Models.NetworkModel;

namespace MedWNetworkSim.App.Import;

public sealed class OsmImporter
{
    private readonly OsmParser parser;
    private readonly GraphSimplifier simplifier;
    private readonly OsmToSimulationMapper mapper;

    public OsmImporter()
        : this(new OsmParser(), new GraphSimplifier(), new OsmToSimulationMapper())
    {
    }

    public OsmImporter(OsmParser parser, GraphSimplifier simplifier, OsmToSimulationMapper mapper)
    {
        this.parser = parser;
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
        progress?.Report(new OsmImportProgress(0.01d, "Preparing import..."));

        var parsed = await parser.ParseAsync(path, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new OsmImportProgress(0.70d, "Simplifying road network..."));
        var simplified = simplifier.Simplify(parsed);

        progress?.Report(new OsmImportProgress(0.88d, "Converting to simulation network..."));
        var network = mapper.Map(simplified, Path.GetFileName(path));

        progress?.Report(new OsmImportProgress(1d, "OSM import complete."));
        return network;
    }
}

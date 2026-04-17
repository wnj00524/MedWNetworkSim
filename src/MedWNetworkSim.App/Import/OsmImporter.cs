using SimulationNetwork = MedWNetworkSim.App.Models.NetworkModel;

namespace MedWNetworkSim.App.Import;

public sealed class OsmImporter
{
    private readonly OsmImportService importService;

    public OsmImporter()
        : this(new OsmImportService())
    {
    }

    public OsmImporter(OsmImportService importService)
    {
        this.importService = importService;
    }

    public SimulationNetwork ImportFromFile(string path)
    {
        return importService.ImportFromFile(path);
    }

    public Task<SimulationNetwork> ImportFromFileAsync(
        string path,
        IProgress<OsmImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return importService.ImportFromFileAsync(path, progress, cancellationToken);
    }
}

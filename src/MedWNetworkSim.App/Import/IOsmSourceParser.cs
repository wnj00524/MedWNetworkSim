namespace MedWNetworkSim.App.Import;

public interface IOsmSourceParser
{
    bool CanParseExtension(string extension);

    Task<OsmParseResult> ParseAsync(
        string path,
        IProgress<OsmImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

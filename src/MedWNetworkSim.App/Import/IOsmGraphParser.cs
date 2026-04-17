namespace MedWNetworkSim.App.Import;

/// <summary>
/// Parses OpenStreetMap sources into a raw traversable graph that can be simplified for simulation.
/// </summary>
public interface IOsmGraphParser
{
    bool CanParseExtension(string extension);

    Task<OsmParseResult> ParseAsync(
        string path,
        IProgress<OsmImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

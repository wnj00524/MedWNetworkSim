using System.Globalization;
using System.IO;
using System.Xml;

namespace MedWNetworkSim.App.Import;

public sealed class OsmXmlParser : IOsmSourceParser
{
    public bool CanParseExtension(string extension)
    {
        return ".osm".Equals(extension, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<OsmParseResult> ParseAsync(
        string path,
        IProgress<OsmImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(path, "XML");

        var requiredNodeIds = new HashSet<long>();
        var parsedEdges = new List<OsmParsedEdge>();
        var warnings = new List<string>();
        long rawWayCount = 0;

        progress?.Report(new OsmImportProgress(0.10d, "Reading OSM XML file…"));
        await ScanWaysAsync(path, requiredNodeIds, parsedEdges, cancellationToken, waysSeen => rawWayCount = waysSeen).ConfigureAwait(false);

        var parsedNodes = new Dictionary<long, OsmParsedNode>();
        long rawNodeCount = 0;
        progress?.Report(new OsmImportProgress(0.35d, "Resolving referenced road nodes…"));
        await LoadReferencedNodesAsync(
            path,
            requiredNodeIds,
            parsedNodes,
            cancellationToken,
            nodesSeen => rawNodeCount = nodesSeen).ConfigureAwait(false);

        var filteredEdges = parsedEdges
            .Where(edge => parsedNodes.ContainsKey(edge.FromNodeId) && parsedNodes.ContainsKey(edge.ToNodeId) && edge.FromNodeId != edge.ToNodeId)
            .ToList();

        var missingNodeReferenceCount = parsedEdges.Count - filteredEdges.Count;
        if (missingNodeReferenceCount > 0)
        {
            warnings.Add($"Skipped {missingNodeReferenceCount:N0} road segments due to missing node references.");
        }

        progress?.Report(new OsmImportProgress(0.60d, "Filtering road data…"));

        return new OsmParseResult(
            new OsmParsedGraph(parsedNodes, filteredEdges),
            new OsmParseSummary(
                RawNodeCount: rawNodeCount,
                RawWayCount: rawWayCount,
                RetainedWayCount: CountDistinctWays(filteredEdges),
                SkippedEntityCount: 0,
                MissingNodeReferenceCount: missingNodeReferenceCount,
                Warnings: warnings));
    }

    private static int CountDistinctWays(IReadOnlyList<OsmParsedEdge> edges)
    {
        // XML parser flattens ways into segments and does not preserve way IDs currently.
        // For parity with legacy behavior, we treat retained way count as number of road segments.
        return edges.Count;
    }

    private static void ValidateInputPath(string path, string formatName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("OSM file path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The selected OpenStreetMap file does not exist.", path);
        }

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length == 0)
        {
            throw new InvalidDataException($"The selected {formatName} file is empty.");
        }
    }

    private static async Task ScanWaysAsync(
        string path,
        HashSet<long> requiredNodeIds,
        List<OsmParsedEdge> parsedEdges,
        CancellationToken cancellationToken,
        Action<long> rawWayCountReporter)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreWhitespace = true,
            IgnoreComments = true
        };

        long rawWayCount = 0;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 * 64, useAsync: true);
        using var reader = XmlReader.Create(stream, settings);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element || !reader.Name.Equals("way", StringComparison.Ordinal))
            {
                continue;
            }

            rawWayCount++;

            var refs = new List<long>();
            string? highwayType = null;

            if (reader.IsEmptyElement)
            {
                continue;
            }

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.NodeType == XmlNodeType.EndElement && reader.Name.Equals("way", StringComparison.Ordinal))
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.Name.Equals("nd", StringComparison.Ordinal))
                {
                    var refValue = reader.GetAttribute("ref");
                    if (long.TryParse(refValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nodeRef))
                    {
                        refs.Add(nodeRef);
                    }

                    continue;
                }

                if (reader.Name.Equals("tag", StringComparison.Ordinal))
                {
                    var key = reader.GetAttribute("k");
                    if (!"highway".Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    highwayType = reader.GetAttribute("v")?.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(highwayType) || refs.Count < 2)
            {
                continue;
            }

            for (var index = 0; index < refs.Count - 1; index++)
            {
                var fromNodeId = refs[index];
                var toNodeId = refs[index + 1];
                if (fromNodeId == toNodeId)
                {
                    continue;
                }

                requiredNodeIds.Add(fromNodeId);
                requiredNodeIds.Add(toNodeId);
                parsedEdges.Add(new OsmParsedEdge(fromNodeId, toNodeId, highwayType));
            }
        }

        rawWayCountReporter(rawWayCount);
    }

    private static async Task LoadReferencedNodesAsync(
        string path,
        HashSet<long> requiredNodeIds,
        Dictionary<long, OsmParsedNode> parsedNodes,
        CancellationToken cancellationToken,
        Action<long> rawNodeCountReporter)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreWhitespace = true,
            IgnoreComments = true
        };

        long rawNodeCount = 0;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 * 64, useAsync: true);
        using var reader = XmlReader.Create(stream, settings);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element || !reader.Name.Equals("node", StringComparison.Ordinal))
            {
                continue;
            }

            rawNodeCount++;

            var idValue = reader.GetAttribute("id");
            var latValue = reader.GetAttribute("lat");
            var lonValue = reader.GetAttribute("lon");
            if (!long.TryParse(idValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nodeId) ||
                !requiredNodeIds.Contains(nodeId) ||
                !double.TryParse(latValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(lonValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                continue;
            }

            parsedNodes[nodeId] = new OsmParsedNode(nodeId, latitude, longitude);
        }

        rawNodeCountReporter(rawNodeCount);
    }
}

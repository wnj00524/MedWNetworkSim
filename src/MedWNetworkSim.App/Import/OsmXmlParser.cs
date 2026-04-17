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
        var distinctWayCount = edges.Select(edge => edge.WayId).Where(wayId => wayId.HasValue).Distinct().Count();
        return distinctWayCount > 0 ? distinctWayCount : edges.Count;
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
            var wayId = TryParseLong(reader.GetAttribute("id"));
            var refs = new List<long>();
            string? highwayType = null;
            OsmNameTags? wayNameTags = null;

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
                    var nodeRef = TryParseLong(reader.GetAttribute("ref"));
                    if (nodeRef.HasValue)
                    {
                        refs.Add(nodeRef.Value);
                    }

                    continue;
                }

                if (reader.Name.Equals("tag", StringComparison.Ordinal))
                {
                    var key = reader.GetAttribute("k");
                    var value = reader.GetAttribute("v");
                    if ("highway".Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        highwayType = value?.Trim();
                    }
                    else
                    {
                        wayNameTags = MergeNameTag(key, value, wayNameTags);
                    }
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
                parsedEdges.Add(new OsmParsedEdge(fromNodeId, toNodeId, highwayType, wayId, wayNameTags));
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

            var nodeId = TryParseLong(reader.GetAttribute("id"));
            var latValue = reader.GetAttribute("lat");
            var lonValue = reader.GetAttribute("lon");
            if (!nodeId.HasValue ||
                !requiredNodeIds.Contains(nodeId.Value) ||
                !double.TryParse(latValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(lonValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                continue;
            }

            var nameTags = await ReadNodeTagsAsync(reader, cancellationToken).ConfigureAwait(false);
            parsedNodes[nodeId.Value] = new OsmParsedNode(nodeId.Value, latitude, longitude, nameTags);
        }

        rawNodeCountReporter(rawNodeCount);
    }

    private static async Task<OsmNameTags?> ReadNodeTagsAsync(XmlReader reader, CancellationToken cancellationToken)
    {
        OsmNameTags? nameTags = null;
        if (reader.IsEmptyElement)
        {
            return null;
        }

        var depth = reader.Depth;
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth && reader.Name.Equals("node", StringComparison.Ordinal))
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element || !reader.Name.Equals("tag", StringComparison.Ordinal))
            {
                continue;
            }

            nameTags = MergeNameTag(reader.GetAttribute("k"), reader.GetAttribute("v"), nameTags);
        }

        return nameTags;
    }

    private static long? TryParseLong(string? value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static OsmNameTags? MergeNameTag(string? key, string? value, OsmNameTags? current)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
        {
            return current;
        }

        var existing = current ?? new OsmNameTags(null, null, null, null);
        var trimmed = value.Trim();
        return key.Trim().ToLowerInvariant() switch
        {
            "name" => existing with { Name = trimmed },
            "ref" => existing with { Ref = trimmed },
            "junction:name" => existing with { JunctionName = trimmed },
            "official_name" => existing with { OfficialName = trimmed },
            _ => existing
        };
    }
}

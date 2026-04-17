using System.Globalization;
using System.IO;
using System.Xml;

namespace MedWNetworkSim.App.Import;

public sealed class OsmParser
{
    public sealed record ParsedNode(long Id, double Latitude, double Longitude);

    public sealed record ParsedEdge(long FromNodeId, long ToNodeId, string HighwayType);

    public sealed record ParsedGraph(
        IReadOnlyDictionary<long, ParsedNode> Nodes,
        IReadOnlyList<ParsedEdge> Edges);

    public async Task<ParsedGraph> ParseAsync(
        string path,
        IProgress<OsmImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("OSM file path is required.", nameof(path));
        }

        var extension = Path.GetExtension(path);
        if (!extension.Equals(".osm", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Only XML .osm files are supported right now. PBF support can be added later.");
        }

        var requiredNodeIds = new HashSet<long>();
        var parsedEdges = new List<ParsedEdge>();

        progress?.Report(new OsmImportProgress(0.05d, "Scanning OSM ways..."));
        await ScanWaysAsync(path, requiredNodeIds, parsedEdges, cancellationToken).ConfigureAwait(false);

        var parsedNodes = new Dictionary<long, ParsedNode>();
        progress?.Report(new OsmImportProgress(0.45d, "Loading referenced OSM nodes..."));
        await LoadReferencedNodesAsync(path, requiredNodeIds, parsedNodes, cancellationToken).ConfigureAwait(false);

        var filteredEdges = parsedEdges
            .Where(edge => parsedNodes.ContainsKey(edge.FromNodeId) && parsedNodes.ContainsKey(edge.ToNodeId) && edge.FromNodeId != edge.ToNodeId)
            .ToList();

        progress?.Report(new OsmImportProgress(0.55d, $"Parsed {parsedNodes.Count:N0} nodes and {filteredEdges.Count:N0} road segments."));
        return new ParsedGraph(parsedNodes, filteredEdges);
    }

    private static async Task ScanWaysAsync(
        string path,
        HashSet<long> requiredNodeIds,
        List<ParsedEdge> parsedEdges,
        CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreWhitespace = true,
            IgnoreComments = true
        };

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 * 64, useAsync: true);
        using var reader = XmlReader.Create(stream, settings);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element || !reader.Name.Equals("way", StringComparison.Ordinal))
            {
                continue;
            }

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
                parsedEdges.Add(new ParsedEdge(fromNodeId, toNodeId, highwayType));
            }
        }
    }

    private static async Task LoadReferencedNodesAsync(
        string path,
        HashSet<long> requiredNodeIds,
        Dictionary<long, ParsedNode> parsedNodes,
        CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreWhitespace = true,
            IgnoreComments = true
        };

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 * 64, useAsync: true);
        using var reader = XmlReader.Create(stream, settings);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element || !reader.Name.Equals("node", StringComparison.Ordinal))
            {
                continue;
            }

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

            parsedNodes[nodeId] = new ParsedNode(nodeId, latitude, longitude);
        }
    }
}

public sealed record OsmImportProgress(double Fraction, string Message);

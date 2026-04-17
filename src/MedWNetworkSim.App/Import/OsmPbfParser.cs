using OsmSharp;
using OsmSharp.Streams;
using System.IO;

namespace MedWNetworkSim.App.Import;

public sealed class OsmPbfParser : IOsmSourceParser
{
    public bool CanParseExtension(string extension)
    {
        return ".pbf".Equals(extension, StringComparison.OrdinalIgnoreCase);
    }

    public Task<OsmParseResult> ParseAsync(
        string path,
        IProgress<OsmImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(path);

        return Task.Run(() =>
        {
            progress?.Report(new OsmImportProgress(0.10d, "Reading OSM PBF file…"));

            var roadWays = new List<Way>();
            var requiredNodeIds = new HashSet<long>();
            var warnings = new List<string>();
            long rawNodeCount = 0;
            long rawWayCount = 0;
            long skippedEntities = 0;

            using (var stream = File.OpenRead(path))
            {
                var source = new PBFOsmStreamSource(stream);
                foreach (var osmGeo in source)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    switch (osmGeo.Type)
                    {
                        case OsmGeoType.Node:
                            rawNodeCount++;
                            break;
                        case OsmGeoType.Way:
                        {
                            rawWayCount++;
                            var way = (Way)osmGeo;
                            if (!IsSupportedRoad(way))
                            {
                                continue;
                            }

                            if (way.Nodes is null || way.Nodes.Length < 2)
                            {
                                skippedEntities++;
                                continue;
                            }

                            roadWays.Add(way);
                            foreach (var nodeId in way.Nodes)
                            {
                                requiredNodeIds.Add(nodeId);
                            }

                            break;
                        }
                        default:
                            skippedEntities++;
                            break;
                    }
                }
            }

            progress?.Report(new OsmImportProgress(0.35d, "Resolving road node references…"));

            var nodes = new Dictionary<long, OsmParsedNode>(requiredNodeIds.Count);
            using (var stream = File.OpenRead(path))
            {
                var source = new PBFOsmStreamSource(stream);
                foreach (var osmGeo in source)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (osmGeo.Type != OsmGeoType.Node)
                    {
                        continue;
                    }

                    var node = (Node)osmGeo;
                    if (!node.Id.HasValue || !node.Latitude.HasValue || !node.Longitude.HasValue)
                    {
                        skippedEntities++;
                        continue;
                    }

                    var nodeId = node.Id.Value;
                    if (!requiredNodeIds.Contains(nodeId))
                    {
                        continue;
                    }

                    nodes[nodeId] = new OsmParsedNode(nodeId, node.Latitude.Value, node.Longitude.Value);
                }
            }

            progress?.Report(new OsmImportProgress(0.60d, "Filtering road data…"));

            var edges = new List<OsmParsedEdge>();
            long missingNodeRefs = 0;
            foreach (var way in roadWays)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var highwayType = GetHighwayType(way);
                if (string.IsNullOrWhiteSpace(highwayType) || way.Nodes is null)
                {
                    continue;
                }

                for (var index = 0; index < way.Nodes.Length - 1; index++)
                {
                    var fromNodeId = way.Nodes[index];
                    var toNodeId = way.Nodes[index + 1];
                    if (fromNodeId == toNodeId)
                    {
                        continue;
                    }

                    if (!nodes.ContainsKey(fromNodeId) || !nodes.ContainsKey(toNodeId))
                    {
                        missingNodeRefs++;
                        continue;
                    }

                    edges.Add(new OsmParsedEdge(fromNodeId, toNodeId, highwayType));
                }
            }

            if (missingNodeRefs > 0)
            {
                warnings.Add($"Skipped {missingNodeRefs:N0} road segments due to missing node references.");
            }

            return new OsmParseResult(
                new OsmParsedGraph(nodes, edges),
                new OsmParseSummary(
                    RawNodeCount: rawNodeCount,
                    RawWayCount: rawWayCount,
                    RetainedWayCount: roadWays.Count,
                    SkippedEntityCount: skippedEntities,
                    MissingNodeReferenceCount: missingNodeRefs,
                    Warnings: warnings));
        }, cancellationToken);
    }

    private static void ValidateInputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("OSM file path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The selected OpenStreetMap PBF file does not exist.", path);
        }

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length == 0)
        {
            throw new InvalidDataException("The selected PBF file is empty.");
        }
    }

    private static bool IsSupportedRoad(Way way)
    {
        return !string.IsNullOrWhiteSpace(GetHighwayType(way));
    }

    private static string? GetHighwayType(Way way)
    {
        return way.Tags?.TryGetValue("highway", out var highwayType) == true
            ? highwayType?.Trim()
            : null;
    }
}

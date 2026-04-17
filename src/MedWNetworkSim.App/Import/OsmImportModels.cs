namespace MedWNetworkSim.App.Import;

public sealed record OsmNameTags(
    string? Name,
    string? Ref,
    string? JunctionName,
    string? OfficialName);

public sealed record OsmParsedNode(
    long Id,
    double Latitude,
    double Longitude,
    OsmNameTags? NameTags = null);

public sealed record OsmParsedEdge(
    long FromNodeId,
    long ToNodeId,
    string HighwayType,
    long? WayId = null,
    OsmNameTags? WayNameTags = null);

public sealed record OsmParsedGraph(
    IReadOnlyDictionary<long, OsmParsedNode> Nodes,
    IReadOnlyList<OsmParsedEdge> Edges);

public sealed record OsmParseSummary(
    long RawNodeCount,
    long RawWayCount,
    long RetainedWayCount,
    long SkippedEntityCount,
    long MissingNodeReferenceCount,
    IReadOnlyList<string> Warnings);

public sealed record OsmParseResult(OsmParsedGraph Graph, OsmParseSummary Summary);

public sealed record OsmImportSummary(
    OsmParseSummary Parse,
    int SimplifiedNodeCount,
    int SimplifiedEdgeCount);

public sealed record OsmImportProgress(double Fraction, string Message);

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

public enum OsmRetentionStrategy
{
    Balanced,
    PreserveShape,
    PreserveJunctionImportance
}

public sealed record OsmImportOptions(
    bool EnableNodeRetentionTarget = false,
    int TargetRetainedNodePercentage = 100,
    OsmRetentionStrategy RetentionStrategy = OsmRetentionStrategy.Balanced,
    bool AlwaysKeepNamedRoadTransitions = true,
    bool PreserveShapeOnLongSegments = true)
{
    public int ClampedTargetRetainedNodePercentage => Math.Clamp(TargetRetainedNodePercentage, 1, 100);
}

public sealed record GraphSimplificationOptions(
    bool EnableNodeRetentionTarget = false,
    int TargetRetainedNodePercentage = 100,
    OsmRetentionStrategy RetentionStrategy = OsmRetentionStrategy.Balanced,
    bool AlwaysKeepNamedRoadTransitions = true,
    bool PreserveShapeOnLongSegments = true)
{
    public static GraphSimplificationOptions FromImportOptions(OsmImportOptions? options)
    {
        if (options is null)
        {
            return new GraphSimplificationOptions();
        }

        return new GraphSimplificationOptions(
            options.EnableNodeRetentionTarget,
            options.ClampedTargetRetainedNodePercentage,
            options.RetentionStrategy,
            options.AlwaysKeepNamedRoadTransitions,
            options.PreserveShapeOnLongSegments);
    }
}

public sealed record OsmRetentionSummary(
    int MandatoryKeptNodes,
    int OptionalKeptNodes,
    int FinalRetainedNodeCount,
    int RequestedPercentage,
    int EffectivePercentage,
    int BaselineSimplifiedNodeCount);

public sealed record OsmImportSummary(
    OsmParseSummary Parse,
    int SimplifiedNodeCount,
    int SimplifiedEdgeCount,
    OsmRetentionSummary? Retention = null);

public sealed record OsmImportProgress(double Fraction, string Message);

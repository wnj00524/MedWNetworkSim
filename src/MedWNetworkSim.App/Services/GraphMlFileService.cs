using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

/// <summary>
/// Loads and saves GraphML files while preserving MedW-specific metadata inside standard GraphML data keys.
/// </summary>
public sealed class GraphMlFileService
{
    private static readonly XNamespace GraphMlNamespace = "http://graphml.graphdrawing.org/xmlns";
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private readonly NetworkFileService networkFileService = new();
    private readonly JsonSerializerOptions serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public NetworkModel Load(string path, GraphMlTransferOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);

        var document = XDocument.Load(path);
        var graphElement = document.Root?.Element(GraphMlNamespace + "graph")
            ?? throw new InvalidOperationException("The selected GraphML file does not contain a <graph> element.");

        var keyDefinitions = BuildKeyLookup(document);
        var graphData = ReadDataValues(graphElement, keyDefinitions);
        var hasExplicitTrafficTypes = TryDeserializeData(
            graphData,
            out List<TrafficTypeDefinition>? trafficTypes,
            "trafficTypesJson",
            "medwTrafficTypesJson");

        var directedByDefault = !string.Equals(
            graphElement.Attribute("edgedefault")?.Value,
            "undirected",
            StringComparison.OrdinalIgnoreCase);

        var nodes = graphElement.Elements(GraphMlNamespace + "node")
            .Select(node => ReadNode(node, keyDefinitions, options))
            .ToList();

        var edges = graphElement.Elements(GraphMlNamespace + "edge")
            .Select(edge => ReadEdge(edge, keyDefinitions, directedByDefault))
            .ToList();

        var graphName = GetFirstValue(graphData, "name", "networkName")
            ?? graphElement.Attribute("id")?.Value
            ?? Path.GetFileNameWithoutExtension(path);

        var graphDescription = GetFirstValue(graphData, "description")
            ?? graphElement.Element(GraphMlNamespace + "desc")?.Value
            ?? string.Empty;

        var model = new NetworkModel
        {
            Name = graphName,
            Description = graphDescription.Trim(),
            TrafficTypes = hasExplicitTrafficTypes ? trafficTypes ?? [] : [],
            Nodes = nodes,
            Edges = edges
        };

        return networkFileService.NormalizeAndValidate(model);
    }

    public void Save(NetworkModel model, string path, GraphMlTransferOptions options)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);

        var normalized = networkFileService.NormalizeAndValidate(model);
        var document = BuildDocument(normalized, options);

        using var writer = XmlWriter.Create(
            path,
            new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  "
            });

        document.Save(writer);
    }

    private XDocument BuildDocument(NetworkModel model, GraphMlTransferOptions options)
    {
        var root = new XElement(
            GraphMlNamespace + "graphml",
            new XAttribute(XNamespace.Xmlns + "xsi", XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance")),
            new XAttribute(
                XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance") + "schemaLocation",
                "http://graphml.graphdrawing.org/xmlns http://graphml.graphdrawing.org/xmlns/1.0/graphml.xsd"));

        AddKey(root, "graph_name", "graph", "name", "string");
        AddKey(root, "graph_description", "graph", "description", "string");
        AddKey(root, "graph_traffic_types_json", "graph", "trafficTypesJson", "string");

        AddKey(root, "node_name", "node", "name", "string");
        AddKey(root, "node_x", "node", "x", "double");
        AddKey(root, "node_y", "node", "y", "double");
        AddKey(root, "node_traffic_type", "node", "trafficType", "string");
        AddKey(root, "node_role", "node", "role", "string");
        AddKey(root, "node_capacity", "node", "capacity", "double");
        AddKey(root, "node_profiles_json", "node", "trafficProfilesJson", "string");

        AddKey(root, "edge_id", "edge", "id", "string");
        AddKey(root, "edge_time", "edge", "time", "double");
        AddKey(root, "edge_cost", "edge", "cost", "double");
        AddKey(root, "edge_capacity", "edge", "capacity", "double");
        AddKey(root, "edge_is_bidirectional", "edge", "isBidirectional", "boolean");

        var graph = new XElement(
            GraphMlNamespace + "graph",
            new XAttribute("id", SanitizeIdentifier(model.Name, "network")),
            new XAttribute("edgedefault", "directed"));

        AddData(graph, "graph_name", model.Name);
        if (!string.IsNullOrWhiteSpace(model.Description))
        {
            AddData(graph, "graph_description", model.Description);
        }

        AddData(graph, "graph_traffic_types_json", JsonSerializer.Serialize(model.TrafficTypes, serializerOptions));

        foreach (var node in model.Nodes)
        {
            var nodeElement = new XElement(
                GraphMlNamespace + "node",
                new XAttribute("id", node.Id));

            AddData(nodeElement, "node_name", node.Name);

            if (node.X.HasValue)
            {
                AddData(nodeElement, "node_x", node.X.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (node.Y.HasValue)
            {
                AddData(nodeElement, "node_y", node.Y.Value.ToString(CultureInfo.InvariantCulture));
            }

            AddData(nodeElement, "node_profiles_json", JsonSerializer.Serialize(node.TrafficProfiles, serializerOptions));

            var preferredProfile = SelectPreferredProfile(node, options.DefaultTrafficType);
            var exportedTrafficType = preferredProfile?.TrafficType ?? NormalizeOptionalString(options.DefaultTrafficType);
            var exportedRole = preferredProfile is not null
                ? NodeTrafficRoleCatalog.GetRoleName(preferredProfile)
                : options.DefaultRoleName;
            var exportedCapacity = preferredProfile is not null
                ? NodeTrafficRoleCatalog.GetRepresentativeCapacity(preferredProfile)
                : options.DefaultNodeCapacity;

            if (!string.IsNullOrWhiteSpace(exportedTrafficType))
            {
                AddData(nodeElement, "node_traffic_type", exportedTrafficType);
            }

            if (NodeTrafficRoleCatalog.TryParseFlags(exportedRole, out var roleFlags) &&
                (roleFlags.IsProducer || roleFlags.IsConsumer || roleFlags.CanTransship))
            {
                AddData(nodeElement, "node_role", exportedRole);
            }

            if (exportedCapacity.HasValue)
            {
                AddData(nodeElement, "node_capacity", exportedCapacity.Value.ToString(CultureInfo.InvariantCulture));
            }

            graph.Add(nodeElement);
        }

        foreach (var edge in model.Edges)
        {
            var edgeElement = new XElement(
                GraphMlNamespace + "edge",
                new XAttribute("id", edge.Id),
                new XAttribute("source", edge.FromNodeId),
                new XAttribute("target", edge.ToNodeId));

            if (edge.IsBidirectional)
            {
                edgeElement.Add(new XAttribute("directed", "false"));
            }

            AddData(edgeElement, "edge_id", edge.Id);
            AddData(edgeElement, "edge_time", edge.Time.ToString(CultureInfo.InvariantCulture));
            AddData(edgeElement, "edge_cost", edge.Cost.ToString(CultureInfo.InvariantCulture));
            AddData(edgeElement, "edge_is_bidirectional", edge.IsBidirectional ? "true" : "false");

            if (edge.Capacity.HasValue)
            {
                AddData(edgeElement, "edge_capacity", edge.Capacity.Value.ToString(CultureInfo.InvariantCulture));
            }

            graph.Add(edgeElement);
        }

        root.Add(graph);
        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    private NodeModel ReadNode(
        XElement nodeElement,
        IReadOnlyDictionary<string, GraphMlKeyDefinition> keyDefinitions,
        GraphMlTransferOptions options)
    {
        var nodeData = ReadDataValues(nodeElement, keyDefinitions);
        var nodeId = nodeElement.Attribute("id")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new InvalidOperationException("Each GraphML node must have a non-empty id.");
        }

        var hasExplicitProfiles = TryDeserializeData(
            nodeData,
            out List<NodeTrafficProfile>? explicitProfiles,
            "trafficProfilesJson",
            "medwTrafficProfilesJson");

        var profiles = hasExplicitProfiles
            ? explicitProfiles ?? []
            : BuildDefaultProfiles(nodeData, options);

        return new NodeModel
        {
            Id = nodeId,
            Name = GetFirstValue(nodeData, "name", "label") ?? nodeId,
            X = TryGetDouble(nodeData, "x"),
            Y = TryGetDouble(nodeData, "y"),
            TrafficProfiles = profiles
        };
    }

    private static EdgeModel ReadEdge(
        XElement edgeElement,
        IReadOnlyDictionary<string, GraphMlKeyDefinition> keyDefinitions,
        bool directedByDefault)
    {
        var edgeData = ReadDataValues(edgeElement, keyDefinitions);
        var sourceId = edgeElement.Attribute("source")?.Value?.Trim();
        var targetId = edgeElement.Attribute("target")?.Value?.Trim();

        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
        {
            throw new InvalidOperationException("Each GraphML edge must define source and target nodes.");
        }

        var edgeId = edgeElement.Attribute("id")?.Value?.Trim()
            ?? GetFirstValue(edgeData, "id");

        var directedOverride = edgeElement.Attribute("directed")?.Value;
        var isBidirectional = directedOverride is not null
            ? !TryParseBoolean(directedOverride, defaultValue: true)
            : !directedByDefault;

        if (edgeData.TryGetValue("isBidirectional", out var bidirectionalText))
        {
            isBidirectional = TryParseBoolean(bidirectionalText, defaultValue: isBidirectional);
        }

        return new EdgeModel
        {
            Id = edgeId ?? string.Empty,
            FromNodeId = sourceId,
            ToNodeId = targetId,
            Time = TryGetDouble(edgeData, "time") ?? 1d,
            Cost = TryGetDouble(edgeData, "cost") ?? 1d,
            Capacity = TryGetDouble(edgeData, "capacity"),
            IsBidirectional = isBidirectional
        };
    }

    private List<NodeTrafficProfile> BuildDefaultProfiles(
        IReadOnlyDictionary<string, string> nodeData,
        GraphMlTransferOptions options)
    {
        var trafficType = NormalizeOptionalString(GetFirstValue(nodeData, "trafficType"))
            ?? NormalizeOptionalString(options.DefaultTrafficType);
        var roleName = NormalizeOptionalString(GetFirstValue(nodeData, "role"))
            ?? options.DefaultRoleName;
        var capacity = TryGetDouble(nodeData, "capacity") ?? options.DefaultNodeCapacity;

        var profile = NodeTrafficRoleCatalog.CreateDefaultProfile(trafficType, roleName, capacity);
        return profile is null ? [] : [profile];
    }

    private static NodeTrafficProfile? SelectPreferredProfile(NodeModel node, string? preferredTrafficType)
    {
        ArgumentNullException.ThrowIfNull(node);

        var normalizedPreferredTrafficType = NormalizeOptionalString(preferredTrafficType);
        if (!string.IsNullOrWhiteSpace(normalizedPreferredTrafficType))
        {
            var matchedProfile = node.TrafficProfiles.FirstOrDefault(
                profile => Comparer.Equals(profile.TrafficType, normalizedPreferredTrafficType));
            if (matchedProfile is not null)
            {
                return matchedProfile;
            }
        }

        return node.TrafficProfiles.Count == 1
            ? node.TrafficProfiles[0]
            : null;
    }

    private static IReadOnlyDictionary<string, GraphMlKeyDefinition> BuildKeyLookup(XDocument document)
    {
        return document.Root?
            .Elements(GraphMlNamespace + "key")
            .Select(element => new GraphMlKeyDefinition(
                element.Attribute("id")?.Value ?? string.Empty,
                element.Attribute("for")?.Value,
                element.Attribute("attr.name")?.Value ?? string.Empty))
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Id))
            .ToDictionary(definition => definition.Id, definition => definition, Comparer)
            ?? new Dictionary<string, GraphMlKeyDefinition>(Comparer);
    }

    private static Dictionary<string, string> ReadDataValues(
        XElement parent,
        IReadOnlyDictionary<string, GraphMlKeyDefinition> keyDefinitions)
    {
        var data = new Dictionary<string, string>(Comparer);

        foreach (var dataElement in parent.Elements(GraphMlNamespace + "data"))
        {
            var keyId = dataElement.Attribute("key")?.Value;
            if (string.IsNullOrWhiteSpace(keyId))
            {
                continue;
            }

            var rawValue = dataElement.Value?.Trim();
            if (rawValue is null)
            {
                continue;
            }

            data[keyId] = rawValue;

            if (!keyDefinitions.TryGetValue(keyId, out var keyDefinition))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(keyDefinition.Name))
            {
                data[keyDefinition.Name] = rawValue;
            }
        }

        return data;
    }

    private bool TryDeserializeData<T>(
        IReadOnlyDictionary<string, string> data,
        out T? value,
        params string[] candidateNames)
    {
        foreach (var candidateName in candidateNames)
        {
            if (!data.TryGetValue(candidateName, out var rawValue))
            {
                continue;
            }

            value = JsonSerializer.Deserialize<T>(rawValue, serializerOptions);
            return true;
        }

        value = default;
        return false;
    }

    private static string? GetFirstValue(IReadOnlyDictionary<string, string> data, params string[] candidateNames)
    {
        foreach (var candidateName in candidateNames)
        {
            if (data.TryGetValue(candidateName, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static double? TryGetDouble(IReadOnlyDictionary<string, string> data, params string[] candidateNames)
    {
        foreach (var candidateName in candidateNames)
        {
            if (!data.TryGetValue(candidateName, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            if (double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var invariantValue))
            {
                return invariantValue;
            }

            if (double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var currentCultureValue))
            {
                return currentCultureValue;
            }
        }

        return null;
    }

    private static bool TryParseBoolean(string? rawValue, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (bool.TryParse(rawValue, out var parsedBoolean))
        {
            return parsedBoolean;
        }

        if (string.Equals(rawValue, "1", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(rawValue, "0", StringComparison.Ordinal))
        {
            return false;
        }

        return defaultValue;
    }

    private static string? NormalizeOptionalString(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string SanitizeIdentifier(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var characters = value.Trim()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();

        var normalized = new string(characters).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static void AddKey(XElement root, string id, string target, string name, string type)
    {
        root.Add(
            new XElement(
                GraphMlNamespace + "key",
                new XAttribute("id", id),
                new XAttribute("for", target),
                new XAttribute("attr.name", name),
                new XAttribute("attr.type", type)));
    }

    private static void AddData(XElement parent, string key, string value)
    {
        parent.Add(
            new XElement(
                GraphMlNamespace + "data",
                new XAttribute("key", key),
                value));
    }

    private sealed record GraphMlKeyDefinition(string Id, string? Target, string Name);
}

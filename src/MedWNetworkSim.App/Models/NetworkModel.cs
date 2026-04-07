using System.Collections.Generic;

namespace MedWNetworkSim.App.Models;

public sealed class NetworkModel
{
    public string Name { get; set; } = "Untitled Network";

    public string Description { get; set; } = string.Empty;

    public List<TrafficTypeDefinition> TrafficTypes { get; set; } = [];

    public List<NodeModel> Nodes { get; set; } = [];

    public List<EdgeModel> Edges { get; set; } = [];
}

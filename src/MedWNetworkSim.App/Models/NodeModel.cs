using System.Collections.Generic;

namespace MedWNetworkSim.App.Models;

public sealed class NodeModel
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public double? X { get; set; }

    public double? Y { get; set; }

    public List<NodeTrafficProfile> TrafficProfiles { get; set; } = [];
}

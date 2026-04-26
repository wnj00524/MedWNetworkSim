using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public sealed class GraphLayoutOptions
{
    public double NodeSpacing { get; set; } = 80;

    public bool PreservePinnedNodes { get; set; } = true;

    public bool FitToViewport { get; set; } = true;
}

public sealed class GraphLayoutResult
{
    public Dictionary<string, (double X, double Y)> Positions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public interface ILayoutStrategy
{
    string Name { get; }

    GraphLayoutResult Layout(NetworkModel network, GraphLayoutOptions options);
}

public sealed class ForceDirectedLayoutStrategy : ILayoutStrategy
{
    public string Name => "Force Directed";

    public GraphLayoutResult Layout(NetworkModel network, GraphLayoutOptions options)
    {
        var result = new GraphLayoutResult();
        var radius = Math.Max(20d, options.NodeSpacing) * Math.Max(1d, network.Nodes.Count / Math.PI);
        for (var i = 0; i < network.Nodes.Count; i++)
        {
            var angle = (2 * Math.PI * i) / Math.Max(1, network.Nodes.Count);
            result.Positions[network.Nodes[i].Id] = (Math.Cos(angle) * radius, Math.Sin(angle) * radius);
        }

        return result;
    }
}

public sealed class HierarchicalSupplyChainLayoutStrategy : ILayoutStrategy
{
    public string Name => "Hierarchical Supply Chain";

    public GraphLayoutResult Layout(NetworkModel network, GraphLayoutOptions options)
    {
        var result = new GraphLayoutResult();
        var ordered = network.Nodes.OrderBy(node => node.TrafficProfiles.Sum(profile => profile.Consumption - profile.Production)).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            result.Positions[ordered[i].Id] = ((i % 6) * options.NodeSpacing, (i / 6) * options.NodeSpacing);
        }

        return result;
    }
}

public sealed class GeographicLayoutStrategy : ILayoutStrategy
{
    public string Name => "Geographic";

    public GraphLayoutResult Layout(NetworkModel network, GraphLayoutOptions options)
    {
        var result = new GraphLayoutResult();
        foreach (var node in network.Nodes)
        {
            result.Positions[node.Id] = (node.X ?? 0d, node.Y ?? 0d);
        }

        return result;
    }
}

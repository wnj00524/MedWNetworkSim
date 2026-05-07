using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;
/// <summary>
/// Represents the graph layout options component.
/// </summary>

public sealed class GraphLayoutOptions
{
    /// <summary>
    /// Gets or sets the node spacing.
    /// </summary>
    public double NodeSpacing { get; set; } = 80;
    /// <summary>
    /// Gets a value indicating whether preserve pinned nodes is enabled or active.
    /// </summary>

    public bool PreservePinnedNodes { get; set; } = true;
    /// <summary>
    /// Gets a value indicating whether fit to viewport is enabled or active.
    /// </summary>

    public bool FitToViewport { get; set; } = true;
}
/// <summary>
/// Represents the graph layout result component.
/// </summary>

public sealed class GraphLayoutResult
{
    public Dictionary<string, (double X, double Y)> Positions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
/// <summary>
/// Defines the contract and required members for ilayout strategy implementations.
/// </summary>

public interface ILayoutStrategy
{
    string Name { get; }

    GraphLayoutResult Layout(NetworkModel network, GraphLayoutOptions options);
}
/// <summary>
/// Represents the force directed layout strategy component.
/// </summary>

public sealed class ForceDirectedLayoutStrategy : ILayoutStrategy
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "Force Directed";
    /// <summary>
    /// Executes the layout operation.
    /// </summary>

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
/// <summary>
/// Represents the hierarchical supply chain layout strategy component.
/// </summary>

public sealed class HierarchicalSupplyChainLayoutStrategy : ILayoutStrategy
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "Hierarchical Supply Chain";
    /// <summary>
    /// Executes the layout operation.
    /// </summary>

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
/// <summary>
/// Represents the geographic layout strategy component.
/// </summary>

public sealed class GeographicLayoutStrategy : ILayoutStrategy
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "Geographic";
    /// <summary>
    /// Executes the layout operation.
    /// </summary>

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

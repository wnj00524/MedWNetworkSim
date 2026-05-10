using System.Collections.Generic;
using Xunit;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.App.Agents;

namespace MedWNetworkSim.Tests;

public class NetworkModelCloneUtilityTests
{
    [Fact]
    public void Clone_ReturnsNewInstance()
    {
        var original = new NetworkModel();
        var cloned = NetworkModelCloneUtility.Clone(original);

        Assert.NotSame(original, cloned);
    }

    [Fact]
    public void Clone_CopiesSerializedProperties()
    {
        var original = new NetworkModel
        {
            Name = "Test Network",
            Description = "A test network",
            SimulationSeed = 42,
            Nodes = new List<NodeModel>
            {
                new NodeModel { Id = "Node1", Name = "First Node" }
            },
            Edges = new List<EdgeModel>
            {
                new EdgeModel { Id = "Edge1", FromNodeId = "Node1", ToNodeId = "Node2" }
            }
        };

        var cloned = NetworkModelCloneUtility.Clone(original);

        Assert.Equal("Test Network", cloned.Name);
        Assert.Equal("A test network", cloned.Description);
        Assert.Equal(42, cloned.SimulationSeed);

        Assert.Single(cloned.Nodes);
        Assert.Equal("Node1", cloned.Nodes[0].Id);
        Assert.Equal("First Node", cloned.Nodes[0].Name);

        Assert.Single(cloned.Edges);
        Assert.Equal("Edge1", cloned.Edges[0].Id);
        Assert.Equal("Node1", cloned.Edges[0].FromNodeId);
        Assert.Equal("Node2", cloned.Edges[0].ToNodeId);

        // Ensure lists are new instances too
        Assert.NotSame(original.Nodes, cloned.Nodes);
        Assert.NotSame(original.Edges, cloned.Edges);
    }

    [Fact]
    public void Clone_IgnoresJsonIgnoreProperties()
    {
        var original = new NetworkModel
        {
            AgentMode = AgentMode.SellLocal,
            ActorTick = 100,
            Actors = new List<SimulationActorState>
            {
                new SimulationActorState { Id = "Actor1" }
            }
        };

        var cloned = NetworkModelCloneUtility.Clone(original);

        // Verify [JsonIgnore] properties are NOT cloned and retain their default values
        Assert.Equal(AgentMode.Off, cloned.AgentMode); // Default is Off
        Assert.Equal(0, cloned.ActorTick); // Default is 0
        Assert.Empty(cloned.Actors); // Default is empty list
    }
}

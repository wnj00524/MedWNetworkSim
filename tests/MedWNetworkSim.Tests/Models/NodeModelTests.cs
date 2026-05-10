using MedWNetworkSim.App.Models;
using Xunit;

namespace MedWNetworkSim.Tests.Models;

public class NodeModelTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Act
        var node = new NodeModel();

        // Assert
        Assert.Equal(string.Empty, node.Id);
        Assert.Equal(string.Empty, node.Name);
        Assert.Equal(NodeVisualShape.Square, node.Shape);
        Assert.Equal(NodeKind.Ordinary, node.NodeKind);
        Assert.False(node.IsExternalInterface);
        Assert.False(node.IsFacility);
        Assert.NotNull(node.Tags);
        Assert.Empty(node.Tags);
        Assert.NotNull(node.TrafficProfiles);
        Assert.Empty(node.TrafficProfiles);
        Assert.False(node.IsCompositeSubnetwork);
    }

    [Fact]
    public void IsCompositeSubnetwork_ReturnsTrue_WhenKindIsCompositeSubnetwork()
    {
        // Arrange
        var node = new NodeModel { NodeKind = NodeKind.CompositeSubnetwork };

        // Act & Assert
        Assert.True(node.IsCompositeSubnetwork);
    }

    [Fact]
    public void IsCompositeSubnetwork_ReturnsFalse_WhenKindIsOrdinary()
    {
        // Arrange
        var node = new NodeModel { NodeKind = NodeKind.Ordinary };

        // Act & Assert
        Assert.False(node.IsCompositeSubnetwork);
    }
}

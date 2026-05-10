using System;
using MedWNetworkSim.App.Models;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class NetworkLayerModelTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Arrange & Act
        var model = new NetworkLayerModel();

        // Assert
        Assert.NotEqual(Guid.Empty, model.Id);
        Assert.Equal(NetworkLayerType.Physical, model.Type);
        Assert.Equal("Physical", model.Name);
        Assert.Equal(0, model.Order);
        Assert.True(model.IsVisible);
        Assert.False(model.IsLocked);
    }

    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var model = new NetworkLayerModel();
        var id = Guid.NewGuid();
        var type = NetworkLayerType.Logical;
        var name = "Logical";
        var order = 1;
        var isVisible = false;
        var isLocked = true;

        // Act
        model.Id = id;
        model.Type = type;
        model.Name = name;
        model.Order = order;
        model.IsVisible = isVisible;
        model.IsLocked = isLocked;

        // Assert
        Assert.Equal(id, model.Id);
        Assert.Equal(type, model.Type);
        Assert.Equal(name, model.Name);
        Assert.Equal(order, model.Order);
        Assert.Equal(isVisible, model.IsVisible);
        Assert.Equal(isLocked, model.IsLocked);
    }
}

using System;
using System.Collections.Generic;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using Xunit;

namespace MedWNetworkSim.Tests.App.Services;

public class EdgeTrafficPermissionResolverTests
{
    private readonly EdgeTrafficPermissionResolver _sut = new();

    [Fact]
    public void Resolve_NullNetwork_ThrowsArgumentNullException()
    {
        // Arrange
        var edge = new EdgeModel { Id = "E1" };
        var trafficType = "Car";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _sut.Resolve(null!, edge, trafficType));
    }

    [Fact]
    public void Resolve_NullEdge_ThrowsArgumentNullException()
    {
        // Arrange
        var network = new NetworkModel();
        var trafficType = "Car";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _sut.Resolve(network, null!, trafficType));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_InvalidTrafficType_ThrowsArgumentException(string invalidTrafficType)
    {
        // Arrange
        var network = new NetworkModel();
        var edge = new EdgeModel { Id = "E1" };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.Resolve(network, edge, invalidTrafficType));
    }

    [Fact]
    public void Resolve_NullTrafficType_ThrowsArgumentNullException()
    {
        // Arrange
        var network = new NetworkModel();
        var edge = new EdgeModel { Id = "E1" };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _sut.Resolve(network, edge, null!));
    }

    [Fact]
    public void Resolve_HasActiveEdgeOverride_ReturnsEdgeOverride()
    {
        // Arrange
        var network = new NetworkModel
        {
            EdgeTrafficPermissionDefaults =
            [
                new EdgeTrafficPermissionRule { TrafficType = "Car", Mode = EdgeTrafficPermissionMode.Permitted }
            ]
        };

        var edge = new EdgeModel
        {
            Id = "E1",
            TrafficPermissions =
            [
                new EdgeTrafficPermissionRule
                {
                    TrafficType = "Car",
                    Mode = EdgeTrafficPermissionMode.Blocked,
                    IsActive = true
                }
            ]
        };

        // Act
        var result = _sut.Resolve(network, edge, "Car");

        // Assert
        Assert.Equal("Car", result.TrafficType);
        Assert.Equal(EdgeTrafficPermissionMode.Blocked, result.Mode);
        Assert.Equal(PermissionRuleSource.EdgeOverride, result.Source);
    }

    [Fact]
    public void Resolve_HasInactiveEdgeOverride_FallsBackToNetworkDefault()
    {
        // Arrange
        var network = new NetworkModel
        {
            EdgeTrafficPermissionDefaults =
            [
                new EdgeTrafficPermissionRule
                {
                    TrafficType = "Car",
                    Mode = EdgeTrafficPermissionMode.Limited,
                    LimitKind = EdgeTrafficLimitKind.AbsoluteUnits,
                    LimitValue = 50
                }
            ]
        };

        var edge = new EdgeModel
        {
            Id = "E1",
            TrafficPermissions =
            [
                new EdgeTrafficPermissionRule
                {
                    TrafficType = "Car",
                    Mode = EdgeTrafficPermissionMode.Blocked,
                    IsActive = false // Inactive override
                }
            ]
        };

        // Act
        var result = _sut.Resolve(network, edge, "Car");

        // Assert
        Assert.Equal("Car", result.TrafficType);
        Assert.Equal(EdgeTrafficPermissionMode.Limited, result.Mode);
        Assert.Equal(50, result.LimitValue);
        Assert.Equal(PermissionRuleSource.NetworkDefault, result.Source);
    }

    [Fact]
    public void Resolve_NoOverrideAndNoDefault_ReturnsImplicitPermit()
    {
        // Arrange
        var network = new NetworkModel();
        var edge = new EdgeModel { Id = "E1" };

        // Act
        var result = _sut.Resolve(network, edge, "Car");

        // Assert
        Assert.Equal("Car", result.TrafficType);
        Assert.Equal(EdgeTrafficPermissionMode.Permitted, result.Mode);
        Assert.Equal(PermissionRuleSource.ImplicitPermit, result.Source);
    }

    [Fact]
    public void GetAllowedCapacity_NullEdge_ThrowsArgumentNullException()
    {
        // Arrange
        var permission = new EffectiveEdgeTrafficPermission(
            "Car",
            EdgeTrafficPermissionMode.Permitted,
            EdgeTrafficLimitKind.AbsoluteUnits,
            null,
            PermissionRuleSource.ImplicitPermit,
            "Permitted");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _sut.GetAllowedCapacity(null!, permission));
    }

    [Fact]
    public void GetAllowedCapacity_ModeBlocked_ReturnsZero()
    {
        // Arrange
        var edge = new EdgeModel { Id = "E1", Capacity = 100 };
        var permission = new EffectiveEdgeTrafficPermission(
            "Car",
            EdgeTrafficPermissionMode.Blocked,
            EdgeTrafficLimitKind.AbsoluteUnits,
            null,
            PermissionRuleSource.ImplicitPermit,
            "Blocked");

        // Act
        var result = _sut.GetAllowedCapacity(edge, permission);

        // Assert
        Assert.Equal(0d, result);
    }

    [Fact]
    public void GetAllowedCapacity_ModePermitted_ReturnsPositiveInfinity()
    {
        // Arrange
        var edge = new EdgeModel { Id = "E1", Capacity = 100 };
        var permission = new EffectiveEdgeTrafficPermission(
            "Car",
            EdgeTrafficPermissionMode.Permitted,
            EdgeTrafficLimitKind.AbsoluteUnits,
            null,
            PermissionRuleSource.ImplicitPermit,
            "Permitted");

        // Act
        var result = _sut.GetAllowedCapacity(edge, permission);

        // Assert
        Assert.Equal(double.PositiveInfinity, result);
    }

    [Fact]
    public void GetAllowedCapacity_ModeLimitedAbsoluteUnits_ReturnsLimitValue()
    {
        // Arrange
        var edge = new EdgeModel { Id = "E1", Capacity = 100 };
        var permission = new EffectiveEdgeTrafficPermission(
            "Car",
            EdgeTrafficPermissionMode.Limited,
            EdgeTrafficLimitKind.AbsoluteUnits,
            25,
            PermissionRuleSource.ImplicitPermit,
            "Limited");

        // Act
        var result = _sut.GetAllowedCapacity(edge, permission);

        // Assert
        Assert.Equal(25d, result);
    }

    [Fact]
    public void GetAllowedCapacity_ModeLimitedAbsoluteUnits_NullLimit_ReturnsZero()
    {
        // Arrange
        var edge = new EdgeModel { Id = "E1", Capacity = 100 };
        var permission = new EffectiveEdgeTrafficPermission(
            "Car",
            EdgeTrafficPermissionMode.Limited,
            EdgeTrafficLimitKind.AbsoluteUnits,
            null, // null limit value
            PermissionRuleSource.ImplicitPermit,
            "Limited");

        // Act
        var result = _sut.GetAllowedCapacity(edge, permission);

        // Assert
        Assert.Equal(0d, result);
    }

    [Fact]
    public void GetAllowedCapacity_ModeLimitedPercentWithEdgeCapacity_ReturnsCalculatedValue()
    {
        // Arrange
        var edge = new EdgeModel { Id = "E1", Capacity = 200 };
        var permission = new EffectiveEdgeTrafficPermission(
            "Car",
            EdgeTrafficPermissionMode.Limited,
            EdgeTrafficLimitKind.PercentOfEdgeCapacity,
            30, // 30%
            PermissionRuleSource.ImplicitPermit,
            "Limited");

        // Act
        var result = _sut.GetAllowedCapacity(edge, permission);

        // Assert
        Assert.Equal(60d, result); // 30% of 200
    }

    [Fact]
    public void GetAllowedCapacity_ModeLimitedPercentWithoutEdgeCapacity_ReturnsPositiveInfinity()
    {
        // Arrange
        var edge = new EdgeModel { Id = "E1", Capacity = null }; // No capacity
        var permission = new EffectiveEdgeTrafficPermission(
            "Car",
            EdgeTrafficPermissionMode.Limited,
            EdgeTrafficLimitKind.PercentOfEdgeCapacity,
            30, // 30%
            PermissionRuleSource.ImplicitPermit,
            "Limited");

        // Act
        var result = _sut.GetAllowedCapacity(edge, permission);

        // Assert
        Assert.Equal(double.PositiveInfinity, result);
    }

    [Fact]
    public void BuildInitialRemainingAllowances_CalculatesCorrectly()
    {
        // Arrange
        var network = new NetworkModel
        {
            Edges =
            [
                new EdgeModel { Id = "E1", Capacity = 100 },
                new EdgeModel { Id = "E2", Capacity = 200 }
            ],
            EdgeTrafficPermissionDefaults =
            [
                new EdgeTrafficPermissionRule
                {
                    TrafficType = "Car",
                    Mode = EdgeTrafficPermissionMode.Limited,
                    LimitKind = EdgeTrafficLimitKind.AbsoluteUnits,
                    LimitValue = 50
                },
                new EdgeTrafficPermissionRule
                {
                    TrafficType = "Truck",
                    Mode = EdgeTrafficPermissionMode.Blocked
                }
            ]
        };

        var trafficTypes = new[] { "Car", "Truck", "Bike" };

        var occupied = new Dictionary<EdgeTrafficResourceKey, double>(EdgeTrafficResourceKey.Comparer)
        {
            { new EdgeTrafficResourceKey("E1", "Car"), 20 },
            { new EdgeTrafficResourceKey("E2", "Car"), 60 } // More than allowed 50
        };

        // Act
        var result = _sut.BuildInitialRemainingAllowances(network, trafficTypes, occupied);

        // Assert
        Assert.Equal(6, result.Count); // 2 edges * 3 traffic types

        // E1, Car: Limited to 50, Occupied 20 -> 30
        Assert.Equal(30, result[new EdgeTrafficResourceKey("E1", "Car")]);
        // E2, Car: Limited to 50, Occupied 60 -> 0
        Assert.Equal(0, result[new EdgeTrafficResourceKey("E2", "Car")]);

        // E1, Truck: Blocked -> 0
        Assert.Equal(0, result[new EdgeTrafficResourceKey("E1", "Truck")]);
        // E2, Truck: Blocked -> 0
        Assert.Equal(0, result[new EdgeTrafficResourceKey("E2", "Truck")]);

        // E1, Bike: Implicit Permit -> Infinity
        Assert.Equal(double.PositiveInfinity, result[new EdgeTrafficResourceKey("E1", "Bike")]);
        // E2, Bike: Implicit Permit -> Infinity
        Assert.Equal(double.PositiveInfinity, result[new EdgeTrafficResourceKey("E2", "Bike")]);
    }

    [Theory]
    [InlineData(EdgeTrafficPermissionMode.Blocked, EdgeTrafficLimitKind.AbsoluteUnits, 10.0, "Effective: Blocked")]
    [InlineData(EdgeTrafficPermissionMode.Permitted, EdgeTrafficLimitKind.AbsoluteUnits, 10.0, "Effective: Permitted")]
    [InlineData(EdgeTrafficPermissionMode.Limited, EdgeTrafficLimitKind.AbsoluteUnits, 15.5, "Effective: Limited to 15.5 unit(s)")]
    [InlineData(EdgeTrafficPermissionMode.Limited, EdgeTrafficLimitKind.AbsoluteUnits, null, "Effective: Limited to 0 unit(s)")]
    [InlineData(EdgeTrafficPermissionMode.Limited, EdgeTrafficLimitKind.PercentOfEdgeCapacity, 50.0, "Effective: Limited to 50% of edge capacity")]
    public void FormatSummary_ReturnsExpectedString(
        EdgeTrafficPermissionMode mode,
        EdgeTrafficLimitKind limitKind,
        double? limitValue,
        string expected)
    {
        // Act
        var result = EdgeTrafficPermissionResolver.FormatSummary(mode, limitKind, limitValue);

        // Assert
        Assert.Equal(expected, result);
    }
}

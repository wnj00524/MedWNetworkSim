using System;
using System.Linq;
using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using Xunit;

namespace MedWNetworkSim.Tests;

public class ScenarioValidationServiceTests
{
    private readonly ScenarioValidationService _sut;

    public ScenarioValidationServiceTests()
    {
        _sut = new ScenarioValidationService();
    }

    // ValidateScenario Tests

    [Fact]
    public void ValidateScenario_WithValidScenario_ReturnsNoErrors()
    {
        var scenario = new ScenarioDefinitionModel
        {
            Name = "Valid Scenario",
            StartTime = 0d,
            EndTime = 10d,
            DeltaTime = 1d
        };

        var errors = _sut.ValidateScenario(scenario);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void ValidateScenario_WithMissingName_ReturnsError(string? name)
    {
        var scenario = new ScenarioDefinitionModel
        {
            Name = name ?? string.Empty,
            StartTime = 0d,
            EndTime = 10d,
            DeltaTime = 1d
        };

        var errors = _sut.ValidateScenario(scenario);

        Assert.Single(errors, "Enter a scenario name.");
    }

    [Fact]
    public void ValidateScenario_WithNegativeStartTime_ReturnsError()
    {
        var scenario = new ScenarioDefinitionModel
        {
            Name = "Valid",
            StartTime = -1d,
            EndTime = 10d,
            DeltaTime = 1d
        };

        var errors = _sut.ValidateScenario(scenario);

        Assert.Single(errors, "Start time must be zero or greater.");
    }

    [Theory]
    [InlineData(10d, 10d)]
    [InlineData(10d, 5d)]
    public void ValidateScenario_WithEndTimeLessOrEqualStartTime_ReturnsError(double startTime, double endTime)
    {
        var scenario = new ScenarioDefinitionModel
        {
            Name = "Valid",
            StartTime = startTime,
            EndTime = endTime,
            DeltaTime = 1d
        };

        var errors = _sut.ValidateScenario(scenario);

        Assert.Single(errors, "End time must be after start time.");
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void ValidateScenario_WithInvalidDeltaTime_ReturnsError(double deltaTime)
    {
        var scenario = new ScenarioDefinitionModel
        {
            Name = "Valid",
            StartTime = 0d,
            EndTime = 10d,
            DeltaTime = deltaTime
        };

        var errors = _sut.ValidateScenario(scenario);

        Assert.Single(errors, "Step size must be greater than zero.");
    }

    [Fact]
    public void ValidateScenario_WithMultipleErrors_ReturnsAllErrors()
    {
        var scenario = new ScenarioDefinitionModel
        {
            Name = "",
            StartTime = -1d,
            EndTime = -2d,
            DeltaTime = 0d
        };

        var errors = _sut.ValidateScenario(scenario);

        Assert.Equal(4, errors.Count);
        Assert.Contains("Enter a scenario name.", errors);
        Assert.Contains("Start time must be zero or greater.", errors);
        Assert.Contains("End time must be after start time.", errors);
        Assert.Contains("Step size must be greater than zero.", errors);
    }

    // ValidateEvent Tests

    [Fact]
    public void ValidateEvent_WithValidEvent_ReturnsNoErrors()
    {
        var evt = new ScenarioEventModel
        {
            Name = "Valid Event",
            Time = 0d,
            Kind = ScenarioEventKind.NodeFailure,
            TargetKind = ScenarioTargetKind.Node,
            TargetId = "node1"
        };

        var errors = _sut.ValidateEvent(evt);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void ValidateEvent_WithMissingName_ReturnsError(string? name)
    {
        var evt = new ScenarioEventModel
        {
            Name = name ?? string.Empty,
            Time = 0d,
            Kind = ScenarioEventKind.NodeFailure,
            TargetKind = ScenarioTargetKind.Node,
            TargetId = "node1"
        };

        var errors = _sut.ValidateEvent(evt);

        Assert.Single(errors, "Enter an event name.");
    }

    [Fact]
    public void ValidateEvent_WithNegativeTime_ReturnsError()
    {
        var evt = new ScenarioEventModel
        {
            Name = "Valid Event",
            Time = -1d,
            Kind = ScenarioEventKind.NodeFailure,
            TargetKind = ScenarioTargetKind.Node,
            TargetId = "node1"
        };

        var errors = _sut.ValidateEvent(evt);

        Assert.Single(errors, "Start time must be zero or greater.");
    }

    [Theory]
    [InlineData(10d, 10d)]
    [InlineData(10d, 5d)]
    public void ValidateEvent_WithEndTimeLessOrEqualTime_ReturnsError(double time, double endTime)
    {
        var evt = new ScenarioEventModel
        {
            Name = "Valid Event",
            Time = time,
            EndTime = endTime,
            Kind = ScenarioEventKind.NodeFailure,
            TargetKind = ScenarioTargetKind.Node,
            TargetId = "node1"
        };

        var errors = _sut.ValidateEvent(evt);

        Assert.Single(errors, "End time must be after start time.");
    }

    [Theory]
    [InlineData(ScenarioTargetKind.Edge, "node1")]
    [InlineData(ScenarioTargetKind.Node, null)]
    public void ValidateEvent_NodeFailure_WithInvalidTarget_ReturnsError(ScenarioTargetKind targetKind, string? targetId)
    {
        var evt = new ScenarioEventModel
        {
            Name = "Valid Event",
            Time = 0d,
            Kind = ScenarioEventKind.NodeFailure,
            TargetKind = targetKind,
            TargetId = targetId
        };

        var errors = _sut.ValidateEvent(evt);

        Assert.Single(errors, "Choose a node for this event.");
    }

    [Theory]
    [InlineData(ScenarioEventKind.EdgeClosure, ScenarioTargetKind.Node, "edge1")]
    [InlineData(ScenarioEventKind.EdgeClosure, ScenarioTargetKind.Edge, null)]
    [InlineData(ScenarioEventKind.EdgeCostChange, ScenarioTargetKind.Node, "edge1")]
    [InlineData(ScenarioEventKind.EdgeCostChange, ScenarioTargetKind.Edge, null)]
    public void ValidateEvent_EdgeEvent_WithInvalidTarget_ReturnsError(ScenarioEventKind kind, ScenarioTargetKind targetKind, string? targetId)
    {
        var evt = new ScenarioEventModel
        {
            Name = "Valid Event",
            Time = 0d,
            Kind = kind,
            TargetKind = targetKind,
            TargetId = targetId,
            Value = 1d
        };

        var errors = _sut.ValidateEvent(evt);

        Assert.Single(errors, "Choose an edge for this event.");
    }

    [Theory]
    [InlineData(ScenarioEventKind.DemandSpike)]
    [InlineData(ScenarioEventKind.ProductionMultiplier)]
    [InlineData(ScenarioEventKind.ConsumptionMultiplier)]
    public void ValidateEvent_NodeMultipliers_WithInvalidTarget_ReturnsError(ScenarioEventKind kind)
    {
        var evt = new ScenarioEventModel
        {
            Name = "Valid Event",
            Time = 0d,
            Kind = kind,
            TargetKind = ScenarioTargetKind.Edge, // Invalid target
            TargetId = "node1",
            TrafficTypeIdOrName = "traffic1",
            Value = 1d
        };

        var errors = _sut.ValidateEvent(evt);

        Assert.Single(errors, "Choose a node for this event.");
    }

    [Theory]
    [InlineData(ScenarioEventKind.DemandSpike)]
    [InlineData(ScenarioEventKind.ProductionMultiplier)]
    [InlineData(ScenarioEventKind.ConsumptionMultiplier)]
    public void ValidateEvent_NodeMultipliers_WithMissingTrafficType_ReturnsError(ScenarioEventKind kind)
    {
        var evt = new ScenarioEventModel
        {
            Name = "Valid Event",
            Time = 0d,
            Kind = kind,
            TargetKind = ScenarioTargetKind.Node,
            TargetId = "node1",
            TrafficTypeIdOrName = null, // Invalid traffic type
            Value = 1d
        };

        var errors = _sut.ValidateEvent(evt);

        Assert.Single(errors, "Choose a traffic type for this event.");
    }

    [Theory]
    [InlineData(ScenarioEventKind.EdgeCostChange)]
    [InlineData(ScenarioEventKind.DemandSpike)]
    [InlineData(ScenarioEventKind.ProductionMultiplier)]
    [InlineData(ScenarioEventKind.ConsumptionMultiplier)]
    [InlineData(ScenarioEventKind.RouteCostMultiplier)]
    public void ValidateEvent_MultiplierEvents_WithInvalidValue_ReturnsError(ScenarioEventKind kind)
    {
        var evt = new ScenarioEventModel
        {
            Name = "Valid Event",
            Time = 0d,
            Kind = kind,
            TargetKind = kind == ScenarioEventKind.EdgeCostChange || kind == ScenarioEventKind.RouteCostMultiplier ? ScenarioTargetKind.Edge : ScenarioTargetKind.Node,
            TargetId = "target1",
            TrafficTypeIdOrName = "traffic1",
            Value = 0d // Invalid value
        };

        var errors = _sut.ValidateEvent(evt);

        Assert.Single(errors, "Value must be greater than zero.");
    }

    [Fact]
    public void ValidateEvent_RouteCostMultiplier_WithMissingTrafficType_ReturnsNoTrafficTypeError()
    {
        // Traffic type is optional for RouteCostMultiplier
        var evt = new ScenarioEventModel
        {
            Name = "Valid Event",
            Time = 0d,
            Kind = ScenarioEventKind.RouteCostMultiplier,
            TargetKind = ScenarioTargetKind.Edge,
            TargetId = "edge1",
            TrafficTypeIdOrName = null,
            Value = 1d
        };

        var errors = _sut.ValidateEvent(evt);

        Assert.Empty(errors);
    }
}

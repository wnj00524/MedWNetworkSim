using System.Collections.Generic;

namespace MedWNetworkSim.App.Models;
/// <summary>
/// Represents the command line options component.
/// </summary>

public sealed class CommandLineOptions
{
    /// <summary>
    /// Gets or sets the command.
    /// </summary>
    public CommandLineCommand Command { get; init; }
    /// <summary>
    /// Gets or sets the network path.
    /// </summary>

    public string NetworkPath { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the mode.
    /// </summary>

    public CommandLineRunMode Mode { get; init; }
    /// <summary>
    /// Gets or sets the report type.
    /// </summary>

    public CommandLineReportType ReportType { get; init; }
    /// <summary>
    /// Gets or sets the timeline periods.
    /// </summary>

    public int TimelinePeriods { get; init; }
    /// <summary>
    /// Gets or sets the output path.
    /// </summary>

    public string OutputPath { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the report format.
    /// </summary>

    public ReportExportFormat ReportFormat { get; init; }
    /// <summary>
    /// Gets a value indicating whether overwrite is enabled or active.
    /// </summary>

    public bool Overwrite { get; init; }
    /// <summary>
    /// Gets or sets the network name.
    /// </summary>

    public string NetworkName { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the network description.
    /// </summary>

    public string NetworkDescription { get; init; } = string.Empty;
    /// <summary>
    /// Gets a value indicating whether has network name is enabled or active.
    /// </summary>

    public bool HasNetworkName { get; init; }
    /// <summary>
    /// Gets a value indicating whether has network description is enabled or active.
    /// </summary>

    public bool HasNetworkDescription { get; init; }
    /// <summary>
    /// Gets or sets the timeline loop length.
    /// </summary>

    public int? TimelineLoopLength { get; init; }
    /// <summary>
    /// Gets a value indicating whether has timeline loop length is enabled or active.
    /// </summary>

    public bool HasTimelineLoopLength { get; init; }
    /// <summary>
    /// Gets or sets the traffic name.
    /// </summary>

    public string TrafficName { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the traffic description.
    /// </summary>

    public string TrafficDescription { get; init; } = string.Empty;
    /// <summary>
    /// Gets a value indicating whether has traffic description is enabled or active.
    /// </summary>

    public bool HasTrafficDescription { get; init; }
    /// <summary>
    /// Gets or sets the routing preference.
    /// </summary>

    public RoutingPreference? RoutingPreference { get; init; }
    /// <summary>
    /// Gets or sets the capacity bid per unit.
    /// </summary>

    public double? CapacityBidPerUnit { get; init; }
    /// <summary>
    /// Gets a value indicating whether has capacity bid per unit is enabled or active.
    /// </summary>

    public bool HasCapacityBidPerUnit { get; init; }
    /// <summary>
    /// Gets or sets the node id.
    /// </summary>

    public string NodeId { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the node name.
    /// </summary>

    public string NodeName { get; init; } = string.Empty;
    /// <summary>
    /// Gets a value indicating whether has node name is enabled or active.
    /// </summary>

    public bool HasNodeName { get; init; }
    /// <summary>
    /// Gets or sets the node shape.
    /// </summary>

    public NodeVisualShape? NodeShape { get; init; }
    /// <summary>
    /// Gets or sets the node x.
    /// </summary>

    public double? NodeX { get; init; }
    /// <summary>
    /// Gets a value indicating whether has node x is enabled or active.
    /// </summary>

    public bool HasNodeX { get; init; }
    /// <summary>
    /// Gets or sets the node y.
    /// </summary>

    public double? NodeY { get; init; }
    /// <summary>
    /// Gets a value indicating whether has node y is enabled or active.
    /// </summary>

    public bool HasNodeY { get; init; }
    /// <summary>
    /// Gets or sets the transhipment capacity.
    /// </summary>

    public double? TranshipmentCapacity { get; init; }
    /// <summary>
    /// Gets a value indicating whether has transhipment capacity is enabled or active.
    /// </summary>

    public bool HasTranshipmentCapacity { get; init; }
    /// <summary>
    /// Gets or sets the profile traffic type.
    /// </summary>

    public string ProfileTrafficType { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the role name.
    /// </summary>

    public string RoleName { get; init; } = string.Empty;
    /// <summary>
    /// Gets a value indicating whether has role name is enabled or active.
    /// </summary>

    public bool HasRoleName { get; init; }
    /// <summary>
    /// Gets or sets the production.
    /// </summary>

    public double? Production { get; init; }
    /// <summary>
    /// Gets a value indicating whether has production is enabled or active.
    /// </summary>

    public bool HasProduction { get; init; }
    /// <summary>
    /// Gets or sets the consumption.
    /// </summary>

    public double? Consumption { get; init; }
    /// <summary>
    /// Gets a value indicating whether has consumption is enabled or active.
    /// </summary>

    public bool HasConsumption { get; init; }
    /// <summary>
    /// Gets or sets the consumer premium per unit.
    /// </summary>

    public double? ConsumerPremiumPerUnit { get; init; }
    /// <summary>
    /// Gets a value indicating whether has consumer premium per unit is enabled or active.
    /// </summary>

    public bool HasConsumerPremiumPerUnit { get; init; }
    /// <summary>
    /// Gets or sets the production start period.
    /// </summary>

    public int? ProductionStartPeriod { get; init; }
    /// <summary>
    /// Gets a value indicating whether has production start period is enabled or active.
    /// </summary>

    public bool HasProductionStartPeriod { get; init; }
    /// <summary>
    /// Gets or sets the production end period.
    /// </summary>

    public int? ProductionEndPeriod { get; init; }
    /// <summary>
    /// Gets a value indicating whether has production end period is enabled or active.
    /// </summary>

    public bool HasProductionEndPeriod { get; init; }
    /// <summary>
    /// Gets or sets the consumption start period.
    /// </summary>

    public int? ConsumptionStartPeriod { get; init; }
    /// <summary>
    /// Gets a value indicating whether has consumption start period is enabled or active.
    /// </summary>

    public bool HasConsumptionStartPeriod { get; init; }
    /// <summary>
    /// Gets or sets the consumption end period.
    /// </summary>

    public int? ConsumptionEndPeriod { get; init; }
    /// <summary>
    /// Gets a value indicating whether has consumption end period is enabled or active.
    /// </summary>

    public bool HasConsumptionEndPeriod { get; init; }
    /// <summary>
    /// Gets the collection of production windows associated with this entity.
    /// </summary>

    public IReadOnlyList<PeriodWindow> ProductionWindows { get; init; } = [];
    /// <summary>
    /// Gets a value indicating whether has production windows is enabled or active.
    /// </summary>

    public bool HasProductionWindows { get; init; }
    /// <summary>
    /// Gets a value indicating whether clear production windows is enabled or active.
    /// </summary>

    public bool ClearProductionWindows { get; init; }
    /// <summary>
    /// Gets the collection of consumption windows associated with this entity.
    /// </summary>

    public IReadOnlyList<PeriodWindow> ConsumptionWindows { get; init; } = [];
    /// <summary>
    /// Gets a value indicating whether has consumption windows is enabled or active.
    /// </summary>

    public bool HasConsumptionWindows { get; init; }
    /// <summary>
    /// Gets a value indicating whether clear consumption windows is enabled or active.
    /// </summary>

    public bool ClearConsumptionWindows { get; init; }
    /// <summary>
    /// Gets the collection of input requirements associated with this entity.
    /// </summary>

    public IReadOnlyList<ProductionInputRequirement> InputRequirements { get; init; } = [];
    /// <summary>
    /// Gets a value indicating whether has input requirements is enabled or active.
    /// </summary>

    public bool HasInputRequirements { get; init; }
    /// <summary>
    /// Gets a value indicating whether clear input requirements is enabled or active.
    /// </summary>

    public bool ClearInputRequirements { get; init; }
    /// <summary>
    /// Gets or sets the is store.
    /// </summary>

    public bool? IsStore { get; init; }
    /// <summary>
    /// Gets a value indicating whether has is store is enabled or active.
    /// </summary>

    public bool HasIsStore { get; init; }
    /// <summary>
    /// Gets or sets the store capacity.
    /// </summary>

    public double? StoreCapacity { get; init; }
    /// <summary>
    /// Gets a value indicating whether has store capacity is enabled or active.
    /// </summary>

    public bool HasStoreCapacity { get; init; }
    /// <summary>
    /// Gets or sets the initial inventory.
    /// </summary>

    public double? Inventory { get; init; }
    /// <summary>
    /// Gets a value indicating whether initial inventory was provided.
    /// </summary>

    public bool HasInventory { get; init; }
    /// <summary>
    /// Gets or sets the edge id.
    /// </summary>

    public string EdgeId { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the from node id.
    /// </summary>

    public string FromNodeId { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the to node id.
    /// </summary>

    public string ToNodeId { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the edge time.
    /// </summary>

    public double EdgeTime { get; init; }
    /// <summary>
    /// Gets a value indicating whether has edge time is enabled or active.
    /// </summary>

    public bool HasEdgeTime { get; init; }
    /// <summary>
    /// Gets or sets the edge cost.
    /// </summary>

    public double EdgeCost { get; init; }
    /// <summary>
    /// Gets a value indicating whether has edge cost is enabled or active.
    /// </summary>

    public bool HasEdgeCost { get; init; }
    /// <summary>
    /// Gets or sets the edge capacity.
    /// </summary>

    public double? EdgeCapacity { get; init; }
    /// <summary>
    /// Gets a value indicating whether has edge capacity is enabled or active.
    /// </summary>

    public bool HasEdgeCapacity { get; init; }
    /// <summary>
    /// Gets or sets the edge is bidirectional.
    /// </summary>

    public bool? EdgeIsBidirectional { get; init; }
    /// <summary>
    /// Gets a value indicating whether has edge is bidirectional is enabled or active.
    /// </summary>

    public bool HasEdgeIsBidirectional { get; init; }
}

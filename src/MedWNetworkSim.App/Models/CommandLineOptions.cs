namespace MedWNetworkSim.App.Models;

public sealed class CommandLineOptions
{
    public CommandLineCommand Command { get; init; }

    public string NetworkPath { get; init; } = string.Empty;

    public CommandLineRunMode Mode { get; init; }

    public CommandLineReportType ReportType { get; init; }

    public int TimelinePeriods { get; init; }

    public string OutputPath { get; init; } = string.Empty;

    public ReportExportFormat ReportFormat { get; init; }

    public bool Overwrite { get; init; }

    public string NetworkName { get; init; } = string.Empty;

    public string NetworkDescription { get; init; } = string.Empty;

    public bool HasNetworkName { get; init; }

    public bool HasNetworkDescription { get; init; }

    public string TrafficName { get; init; } = string.Empty;

    public string TrafficDescription { get; init; } = string.Empty;

    public bool HasTrafficDescription { get; init; }

    public RoutingPreference? RoutingPreference { get; init; }

    public double? CapacityBidPerUnit { get; init; }

    public bool HasCapacityBidPerUnit { get; init; }

    public string NodeId { get; init; } = string.Empty;

    public string NodeName { get; init; } = string.Empty;

    public bool HasNodeName { get; init; }

    public NodeVisualShape? NodeShape { get; init; }

    public double? NodeX { get; init; }

    public bool HasNodeX { get; init; }

    public double? NodeY { get; init; }

    public bool HasNodeY { get; init; }

    public double? TranshipmentCapacity { get; init; }

    public bool HasTranshipmentCapacity { get; init; }

    public string ProfileTrafficType { get; init; } = string.Empty;

    public string RoleName { get; init; } = string.Empty;

    public bool HasRoleName { get; init; }

    public double? Production { get; init; }

    public bool HasProduction { get; init; }

    public double? Consumption { get; init; }

    public bool HasConsumption { get; init; }

    public double? ConsumerPremiumPerUnit { get; init; }

    public bool HasConsumerPremiumPerUnit { get; init; }

    public int? ProductionStartPeriod { get; init; }

    public bool HasProductionStartPeriod { get; init; }

    public int? ProductionEndPeriod { get; init; }

    public bool HasProductionEndPeriod { get; init; }

    public int? ConsumptionStartPeriod { get; init; }

    public bool HasConsumptionStartPeriod { get; init; }

    public int? ConsumptionEndPeriod { get; init; }

    public bool HasConsumptionEndPeriod { get; init; }

    public bool? IsStore { get; init; }

    public bool HasIsStore { get; init; }

    public double? StoreCapacity { get; init; }

    public bool HasStoreCapacity { get; init; }

    public string EdgeId { get; init; } = string.Empty;

    public string FromNodeId { get; init; } = string.Empty;

    public string ToNodeId { get; init; } = string.Empty;

    public double EdgeTime { get; init; }

    public bool HasEdgeTime { get; init; }

    public double EdgeCost { get; init; }

    public bool HasEdgeCost { get; init; }

    public double? EdgeCapacity { get; init; }

    public bool HasEdgeCapacity { get; init; }

    public bool? EdgeIsBidirectional { get; init; }

    public bool HasEdgeIsBidirectional { get; init; }
}

using System.Text.Json.Serialization;

namespace MedWNetworkSim.App.Models;

/// <summary>
/// Represents an inclusive period range where a scheduled profile action is active.
/// </summary>
public sealed class PeriodWindow
{
    public int? StartPeriod { get; set; }

    public int? EndPeriod { get; set; }
}

/// <summary>
/// Defines one local precursor input required to produce output traffic.
/// </summary>
public sealed class ProductionInputRequirement
{
    public string TrafficType { get; set; } = string.Empty;

    public double InputQuantity { get; set; } = 1d;

    public double OutputQuantity { get; set; } = 1d;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? QuantityPerOutputUnit { get; set; }

    [JsonIgnore]
    public double InputPerOutputUnit => OutputQuantity > 0d ? InputQuantity / OutputQuantity : 0d;
}

/// <summary>
/// Describes how a single node participates in one traffic type.
/// </summary>
public sealed class NodeTrafficProfile
{
    public string TrafficType { get; set; } = string.Empty;

    public double Production { get; set; }

    public double Consumption { get; set; }

    public double ConsumerPremiumPerUnit { get; set; }

    public bool CanTransship { get; set; }

    public int? ProductionStartPeriod { get; set; }

    public int? ProductionEndPeriod { get; set; }

    public int? ConsumptionStartPeriod { get; set; }

    public int? ConsumptionEndPeriod { get; set; }

    public List<PeriodWindow> ProductionWindows { get; set; } = [];

    public List<PeriodWindow> ConsumptionWindows { get; set; } = [];

    public List<ProductionInputRequirement> InputRequirements { get; set; } = [];

    public bool IsStore { get; set; }

    public double? StoreCapacity { get; set; }

    public double Inventory { get; set; }

    public double UnitPrice { get; set; }

    public double HoldingCostPerTime { get; set; }

    public double Revenue { get; set; }

    public double Profit { get; set; }

    public double ShortagePenalty { get; set; }
}

using System.Text.Json.Serialization;

namespace MedWNetworkSim.App.Models;

/// <summary>
/// Represents an inclusive period range where a scheduled profile action is active.
/// </summary>
public sealed class PeriodWindow
{
    /// <summary>
    /// Gets or sets the start period.
    /// </summary>
    public int? StartPeriod { get; set; }
    /// <summary>
    /// Gets or sets the end period.
    /// </summary>

    public int? EndPeriod { get; set; }
}

/// <summary>
/// Defines one local precursor input required to produce output traffic.
/// </summary>
public sealed class ProductionInputRequirement
{
    /// <summary>
    /// Gets or sets the traffic type.
    /// </summary>
    public string TrafficType { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the input quantity.
    /// </summary>

    public double InputQuantity { get; set; } = 1d;
    /// <summary>
    /// Gets or sets the output quantity.
    /// </summary>

    public double OutputQuantity { get; set; } = 1d;
    /// <summary>
    /// Gets or sets the quantity per output unit.
    /// </summary>

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? QuantityPerOutputUnit { get; set; }
    /// <summary>
    /// Gets or sets the input per output unit.
    /// </summary>

    [JsonIgnore]
    public double InputPerOutputUnit => OutputQuantity > 0d ? InputQuantity / OutputQuantity : 0d;
}

/// <summary>
/// Describes how a single node participates in one traffic type.
/// </summary>
public sealed class NodeTrafficProfile
{
    /// <summary>
    /// Gets or sets the traffic type.
    /// </summary>
    public string TrafficType { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the production.
    /// </summary>

    public double Production { get; set; }
    /// <summary>
    /// Gets or sets the consumption.
    /// </summary>

    public double Consumption { get; set; }
    /// <summary>
    /// Gets or sets the consumer premium per unit.
    /// </summary>

    public double ConsumerPremiumPerUnit { get; set; }
    /// <summary>
    /// Gets a value indicating whether can transship is enabled or active.
    /// </summary>

    public bool CanTransship { get; set; }
    /// <summary>
    /// Gets or sets the production start period.
    /// </summary>

    public int? ProductionStartPeriod { get; set; }
    /// <summary>
    /// Gets or sets the production end period.
    /// </summary>

    public int? ProductionEndPeriod { get; set; }
    /// <summary>
    /// Gets or sets the consumption start period.
    /// </summary>

    public int? ConsumptionStartPeriod { get; set; }
    /// <summary>
    /// Gets or sets the consumption end period.
    /// </summary>

    public int? ConsumptionEndPeriod { get; set; }
    /// <summary>
    /// Gets the collection of production windows associated with this entity.
    /// </summary>

    public List<PeriodWindow> ProductionWindows { get; set; } = [];
    /// <summary>
    /// Gets the collection of consumption windows associated with this entity.
    /// </summary>

    public List<PeriodWindow> ConsumptionWindows { get; set; } = [];
    /// <summary>
    /// Gets the collection of input requirements associated with this entity.
    /// </summary>

    public List<ProductionInputRequirement> InputRequirements { get; set; } = [];
    /// <summary>
    /// Gets a value indicating whether is store is enabled or active.
    /// </summary>

    public bool IsStore { get; set; }
    /// <summary>
    /// Gets or sets the store capacity.
    /// </summary>

    public double? StoreCapacity { get; set; }
    /// <summary>
    /// Gets or sets the inventory.
    /// </summary>

    public double Inventory { get; set; }
    /// <summary>
    /// Gets or sets the unit price.
    /// </summary>

    public double UnitPrice { get; set; }
    /// <summary>
    /// Gets or sets the production cost per unit.
    /// </summary>

    public double? ProductionCostPerUnit { get; set; }
    /// <summary>
    /// Gets or sets the sales tax rate.
    /// </summary>

    public double? SalesTaxRate { get; set; }
    /// <summary>
    /// Gets or sets the holding cost per time.
    /// </summary>

    public double HoldingCostPerTime { get; set; }
    /// <summary>
    /// Gets or sets the revenue.
    /// </summary>

    public double Revenue { get; set; }
    /// <summary>
    /// Gets or sets the profit.
    /// </summary>

    public double Profit { get; set; }
    /// <summary>
    /// Gets or sets the shortage penalty.
    /// </summary>

    public double ShortagePenalty { get; set; }
}

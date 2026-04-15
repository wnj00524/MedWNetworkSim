using System.Text.Json.Serialization;

namespace MedWNetworkSim.App.Models;

/// <summary>
/// Represents an inclusive period range where a scheduled profile action is active.
/// </summary>
public sealed class PeriodWindow
{
    /// <summary>
    /// Gets or sets the first active period. Null means active from the beginning.
    /// </summary>
    public int? StartPeriod { get; set; }

    /// <summary>
    /// Gets or sets the last active period. Null means no upper bound.
    /// </summary>
    public int? EndPeriod { get; set; }
}

/// <summary>
/// Defines one local precursor input required to produce output traffic.
/// </summary>
public sealed class ProductionInputRequirement
{
    /// <summary>
    /// Gets or sets the precursor traffic type consumed at the producing node.
    /// </summary>
    public string TrafficType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the amount of precursor input consumed by this recipe row.
    /// </summary>
    public double InputQuantity { get; set; } = 1d;

    /// <summary>
    /// Gets or sets the amount of output produced by this recipe row.
    /// </summary>
    public double OutputQuantity { get; set; } = 1d;

    /// <summary>
    /// Legacy compatibility field. When present in older JSON this represents X:1.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? QuantityPerOutputUnit { get; set; }

    /// <summary>
    /// Gets the normalized input-per-output ratio represented by this requirement.
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
    /// Gets or sets the traffic type this profile applies to.
    /// </summary>
    public string TrafficType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the amount of this traffic type produced at the node.
    /// </summary>
    public double Production { get; set; }

    /// <summary>
    /// Gets or sets the amount of this traffic type consumed at the node.
    /// </summary>
    public double Consumption { get; set; }

    /// <summary>
    /// Gets or sets the optional extra per-unit premium this consumer will pay to prioritize this traffic type.
    /// </summary>
    public double ConsumerPremiumPerUnit { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this node may be used as an intermediate transhipment point.
    /// </summary>
    public bool CanTransship { get; set; }

    /// <summary>
    /// Gets or sets the first period in which production is active. Null means active from the beginning.
    /// </summary>
    public int? ProductionStartPeriod { get; set; }

    /// <summary>
    /// Gets or sets the last period in which production is active. Null means no upper bound.
    /// </summary>
    public int? ProductionEndPeriod { get; set; }

    /// <summary>
    /// Gets or sets the first period in which consumption is active. Null means active from the beginning.
    /// </summary>
    public int? ConsumptionStartPeriod { get; set; }

    /// <summary>
    /// Gets or sets the last period in which consumption is active. Null means no upper bound.
    /// </summary>
    public int? ConsumptionEndPeriod { get; set; }

    /// <summary>
    /// Gets or sets the inclusive production windows for this profile. Empty uses the legacy single-window fields.
    /// </summary>
    public List<PeriodWindow> ProductionWindows { get; set; } = [];

    /// <summary>
    /// Gets or sets the inclusive consumption windows for this profile. Empty uses the legacy single-window fields.
    /// </summary>
    public List<PeriodWindow> ConsumptionWindows { get; set; } = [];

    /// <summary>
    /// Gets or sets local precursor traffic required to produce this profile's output.
    /// </summary>
    public List<ProductionInputRequirement> InputRequirements { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether this traffic profile stores received traffic in inventory instead of destroying it.
    /// </summary>
    public bool IsStore { get; set; }

    /// <summary>
    /// Gets or sets the optional maximum amount of this traffic type that can be stored at the node.
    /// </summary>
    public double? StoreCapacity { get; set; }
}
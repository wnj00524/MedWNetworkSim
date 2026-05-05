namespace MedWNetworkSim.App.Models;

/// <summary>
/// Defines how one traffic type behaves when the simulator routes it through the network.
/// </summary>
public sealed class TrafficTypeDefinition
{
    /// <summary>
    /// Gets or sets the name of the traffic type.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional description shown in the editor.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the route-scoring preference for this traffic type.
    /// </summary>
    public RoutingPreference RoutingPreference { get; set; } = RoutingPreference.TotalCost;

    /// <summary>
    /// Gets or sets how supply is allocated across feasible routes for this traffic type.
    /// </summary>
    public AllocationMode AllocationMode { get; set; } = AllocationMode.GreedyBestRoute;

    /// <summary>
    /// Gets or sets the active route-choice regime for this traffic type.
    /// </summary>
    public RouteChoiceModel RouteChoiceModel { get; set; } = RouteChoiceModel.StochasticUserResponsive;

    /// <summary>
    /// Gets or sets whether this traffic type concentrates flow on one path or splits across alternatives.
    /// </summary>
    public FlowSplitPolicy FlowSplitPolicy { get; set; } = FlowSplitPolicy.MultiPath;

    /// <summary>
    /// Gets or sets route-choice, congestion, and priority tuning for this traffic type.
    /// </summary>
    public RouteChoiceSettings RouteChoiceSettings { get; set; } = new();

    /// <summary>
    /// Gets or sets the optional per-unit bid used when competing for constrained edge or node transhipment capacity.
    /// </summary>
    public double? CapacityBidPerUnit { get; set; }

    public double DefaultUnitSalePrice { get; set; }

    public double DefaultUnitProductionCost { get; set; }

    public double SalesTaxRate { get; set; }

    public double RouteTaxRate { get; set; }

    /// <summary>
    /// Gets or sets how many timeline periods this traffic type can remain in the network
    /// before it expires. Null means it does not perish.
    /// </summary>
    public int? PerishabilityPeriods { get; set; }
}

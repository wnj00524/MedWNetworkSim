namespace MedWNetworkSim.App.Models;
/// <summary>
/// Represents the economic summary component.
/// </summary>

public sealed class EconomicSummary
{
    /// <summary>
    /// Gets or sets the total revenue.
    /// </summary>
    public double TotalRevenue { get; set; }
    /// <summary>
    /// Gets or sets the total sales revenue.
    /// </summary>

    public double TotalSalesRevenue { get; set; }
    /// <summary>
    /// Gets or sets the total transport cost.
    /// </summary>

    public double TotalTransportCost { get; set; }
    /// <summary>
    /// Gets or sets the total production cost.
    /// </summary>

    public double TotalProductionCost { get; set; }
    /// <summary>
    /// Gets or sets the total tax.
    /// </summary>

    public double TotalTax { get; set; }
    /// <summary>
    /// Gets or sets the total holding cost.
    /// </summary>

    public double TotalHoldingCost { get; set; }
    /// <summary>
    /// Gets or sets the total shortage penalty.
    /// </summary>

    public double TotalShortagePenalty { get; set; }
    /// <summary>
    /// Gets or sets the total profit.
    /// </summary>

    public double TotalProfit { get; set; }
}

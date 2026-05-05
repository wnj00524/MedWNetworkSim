namespace MedWNetworkSim.App.Models;

public sealed class EconomicSummary
{
    public double TotalRevenue { get; set; }

    public double TotalSalesRevenue { get; set; }

    public double TotalTransportCost { get; set; }

    public double TotalProductionCost { get; set; }

    public double TotalTax { get; set; }

    public double TotalHoldingCost { get; set; }

    public double TotalShortagePenalty { get; set; }

    public double TotalProfit { get; set; }
}

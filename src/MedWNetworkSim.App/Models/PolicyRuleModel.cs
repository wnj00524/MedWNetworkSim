namespace MedWNetworkSim.App.Models;

public enum PolicyRuleEffect
{
    BlockTraffic,
    AllowOnlyTraffic,
    CostMultiplier,
    CapacityMultiplier
}

public sealed class PolicyRuleModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public PolicyRuleEffect Effect { get; set; }

    public string? TrafficTypeIdOrName { get; set; }

    public string? TargetNodeId { get; set; }

    public string? TargetEdgeId { get; set; }

    public double Value { get; set; } = 1d;

    public bool IsEnabled { get; set; } = true;
}

namespace MedWNetworkSim.App.Models;

public sealed record BulkApplyTrafficRoleOptions(
    string TrafficType,
    string RoleName,
    double ProductionAmount,
    double ConsumptionAmount,
    bool ApplyTranshipmentCapacity,
    double? TranshipmentCapacity);

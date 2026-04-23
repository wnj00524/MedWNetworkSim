namespace MedWNetworkSim.App.Models;

public sealed record BulkApplyTrafficRoleOptions(
    bool ApplyPlaceType,
    string? PlaceType,
    bool ApplyTrafficRole,
    string? TrafficType,
    string RoleName,
    double? ProductionAmount,
    double? ConsumptionAmount,
    bool ApplyTranshipmentCapacity,
    double? TranshipmentCapacity);

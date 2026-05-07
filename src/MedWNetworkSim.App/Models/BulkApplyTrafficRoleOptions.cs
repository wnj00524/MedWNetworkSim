namespace MedWNetworkSim.App.Models;
/// <summary>
/// Represents the bulk apply traffic role options component.
/// </summary>

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

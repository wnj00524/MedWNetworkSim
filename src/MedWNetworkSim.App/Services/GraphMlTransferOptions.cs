namespace MedWNetworkSim.App.Services;

/// <summary>
/// User-selected GraphML defaults used when generic GraphML nodes omit MedW-specific traffic data.
/// </summary>
public sealed record GraphMlTransferOptions(
    string? DefaultTrafficType,
    string DefaultRoleName,
    double? DefaultNodeCapacity);

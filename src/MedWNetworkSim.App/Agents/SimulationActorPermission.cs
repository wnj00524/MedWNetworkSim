namespace MedWNetworkSim.App.Agents;

public sealed class SimulationActorPermission
{
    public SimulationActorActionKind ActionKind { get; set; }
    public string? TrafficType { get; set; }
    public string? NodeId { get; set; }
    public string? EdgeId { get; set; }
    public bool IsAllowed { get; set; } = true;
}

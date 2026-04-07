namespace MedWNetworkSim.App.Models;

public sealed class EdgeModel
{
    public string Id { get; set; } = string.Empty;

    public string FromNodeId { get; set; } = string.Empty;

    public string ToNodeId { get; set; } = string.Empty;

    public double Time { get; set; }

    public double Cost { get; set; }

    public bool IsBidirectional { get; set; } = true;
}

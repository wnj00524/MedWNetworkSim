namespace MedWNetworkSim.App.Models;

public sealed class NetworkLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public NetworkLayerType Type { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Order { get; set; }
}

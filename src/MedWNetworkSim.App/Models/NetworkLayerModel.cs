namespace MedWNetworkSim.App.Models;

public sealed class NetworkLayerModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public NetworkLayerType Type { get; set; } = NetworkLayerType.Physical;

    public string Name { get; set; } = "Physical";

    public int Order { get; set; }

    public bool IsVisible { get; set; } = true;

    public bool IsLocked { get; set; }
}

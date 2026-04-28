using System.Text.Json;
using System.Text.Json.Serialization;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

public static class NetworkModelCloneUtility
{
    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static NetworkModel Clone(NetworkModel network)
    {
        var json = JsonSerializer.Serialize(network, CloneOptions);
        return JsonSerializer.Deserialize<NetworkModel>(json, CloneOptions) ?? new NetworkModel();
    }
}

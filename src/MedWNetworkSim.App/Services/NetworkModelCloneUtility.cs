using System.Text.Json;
using System.Text.Json.Serialization;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;
/// <summary>
/// Represents a data model for network clone utility entities within the simulation.
/// </summary>

public static class NetworkModelCloneUtility
{
    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    /// <summary>
    /// Executes the clone operation.
    /// </summary>

    public static NetworkModel Clone(NetworkModel network)
    {
        var json = JsonSerializer.Serialize(network, CloneOptions);
        return JsonSerializer.Deserialize<NetworkModel>(json, CloneOptions) ?? new NetworkModel();
    }
}

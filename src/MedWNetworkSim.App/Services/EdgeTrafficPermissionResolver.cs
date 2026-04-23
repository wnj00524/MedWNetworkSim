using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.Services;

/// <summary>
/// Resolves effective edge traffic permissions and turns them into numeric allowances for routing.
/// </summary>
public sealed class EdgeTrafficPermissionResolver
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public EffectiveEdgeTrafficPermission Resolve(NetworkModel network, EdgeModel edge, string trafficType)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentException.ThrowIfNullOrWhiteSpace(trafficType);

        var edgeOverride = (edge.TrafficPermissions ?? [])
            .FirstOrDefault(rule => rule.IsActive && Comparer.Equals(rule.TrafficType, trafficType));
        if (edgeOverride is not null)
        {
            return CreateEffectivePermission(edgeOverride, PermissionRuleSource.EdgeOverride);
        }

        var networkDefault = (network.EdgeTrafficPermissionDefaults ?? [])
            .FirstOrDefault(rule => Comparer.Equals(rule.TrafficType, trafficType));
        if (networkDefault is not null)
        {
            return CreateEffectivePermission(networkDefault, PermissionRuleSource.NetworkDefault);
        }

        return new EffectiveEdgeTrafficPermission(
            trafficType,
            EdgeTrafficPermissionMode.Permitted,
            EdgeTrafficLimitKind.AbsoluteUnits,
            null,
            PermissionRuleSource.ImplicitPermit,
            "Effective: Permitted");
    }

    public double GetAllowedCapacity(EdgeModel edge, EffectiveEdgeTrafficPermission permission)
    {
        ArgumentNullException.ThrowIfNull(edge);

        return permission.Mode switch
        {
            EdgeTrafficPermissionMode.Blocked => 0d,
            EdgeTrafficPermissionMode.Limited => permission.LimitKind switch
            {
                EdgeTrafficLimitKind.AbsoluteUnits => Math.Max(0d, permission.LimitValue.GetValueOrDefault()),
                EdgeTrafficLimitKind.PercentOfEdgeCapacity => edge.Capacity.HasValue
                    ? Math.Max(0d, edge.Capacity.Value * permission.LimitValue.GetValueOrDefault() / 100d)
                    : double.PositiveInfinity,
                _ => double.PositiveInfinity
            },
            _ => double.PositiveInfinity
        };
    }

    public Dictionary<EdgeTrafficResourceKey, double> BuildInitialRemainingAllowances(
        NetworkModel network,
        IEnumerable<string> trafficTypes,
        IReadOnlyDictionary<EdgeTrafficResourceKey, double>? occupiedTrafficByEdge = null)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(trafficTypes);

        var result = new Dictionary<EdgeTrafficResourceKey, double>(EdgeTrafficResourceKey.Comparer);
        var uniqueTrafficTypes = trafficTypes
            .Where(trafficType => !string.IsNullOrWhiteSpace(trafficType))
            .Distinct(Comparer)
            .ToList();

        foreach (var edge in network.Edges)
        {
            foreach (var trafficType in uniqueTrafficTypes)
            {
                var key = new EdgeTrafficResourceKey(edge.Id, trafficType);
                var allowance = GetAllowedCapacity(edge, Resolve(network, edge, trafficType));
                var occupied = occupiedTrafficByEdge?.TryGetValue(key, out var used) == true
                    ? Math.Max(0d, used)
                    : 0d;

                result[key] = double.IsPositiveInfinity(allowance)
                    ? double.PositiveInfinity
                    : Math.Max(0d, allowance - occupied);
            }
        }

        return result;
    }

    public static string FormatSummary(EdgeTrafficPermissionMode mode, EdgeTrafficLimitKind limitKind, double? limitValue)
    {
        return mode switch
        {
            EdgeTrafficPermissionMode.Blocked => "Effective: Blocked",
            EdgeTrafficPermissionMode.Limited => limitKind == EdgeTrafficLimitKind.PercentOfEdgeCapacity
                ? $"Effective: Limited to {FormatValue(limitValue)}% of edge capacity"
                : $"Effective: Limited to {FormatValue(limitValue)} unit(s)",
            _ => "Effective: Permitted"
        };
    }

    private static EffectiveEdgeTrafficPermission CreateEffectivePermission(EdgeTrafficPermissionRule rule, PermissionRuleSource source)
    {
        return new EffectiveEdgeTrafficPermission(
            rule.TrafficType,
            rule.Mode,
            rule.LimitKind,
            rule.LimitValue,
            source,
            FormatSummary(rule.Mode, rule.LimitKind, rule.LimitValue));
    }

    private static string FormatValue(double? value)
    {
        if (!value.HasValue || double.IsPositiveInfinity(value.Value))
        {
            return "0";
        }

        return value.Value.ToString("0.##");
    }
}

public enum PermissionRuleSource
{
    EdgeOverride,
    NetworkDefault,
    ImplicitPermit
}

public readonly record struct EffectiveEdgeTrafficPermission(
    string TrafficType,
    EdgeTrafficPermissionMode Mode,
    EdgeTrafficLimitKind LimitKind,
    double? LimitValue,
    PermissionRuleSource Source,
    string Summary);

public readonly record struct EdgeTrafficResourceKey(string EdgeId, string TrafficType)
{
    public static IEqualityComparer<EdgeTrafficResourceKey> Comparer { get; } = new EdgeTrafficResourceKeyComparer();

    private sealed class EdgeTrafficResourceKeyComparer : IEqualityComparer<EdgeTrafficResourceKey>
    {
        public bool Equals(EdgeTrafficResourceKey x, EdgeTrafficResourceKey y)
        {
            return string.Equals(x.EdgeId, y.EdgeId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.TrafficType, y.TrafficType, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(EdgeTrafficResourceKey obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.EdgeId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TrafficType));
        }
    }
}

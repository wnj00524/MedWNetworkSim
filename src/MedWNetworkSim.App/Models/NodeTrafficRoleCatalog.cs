namespace MedWNetworkSim.App.Models;

/// <summary>
/// Centralizes the user-facing traffic-role names used throughout the app.
/// </summary>
public static class NodeTrafficRoleCatalog
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public const string NoTrafficRole = "No Traffic Role";
    public const string ProducerRole = "Producer";
    public const string ConsumerRole = "Consumer";
    public const string TransshipRole = "Transship";
    public const string ProducerConsumerRole = "Producer + Consumer";
    public const string ProducerTransshipRole = "Producer + Transship";
    public const string ConsumerTransshipRole = "Consumer + Transship";
    public const string ProducerConsumerTransshipRole = "Producer + Consumer + Transship";

    public static IReadOnlyList<string> RoleOptions { get; } =
    [
        NoTrafficRole,
        ProducerRole,
        ConsumerRole,
        TransshipRole,
        ProducerConsumerRole,
        ProducerTransshipRole,
        ConsumerTransshipRole,
        ProducerConsumerTransshipRole
    ];

    /// <summary>
    /// Returns the display name for a role based on the profile flags.
    /// </summary>
    public static string GetRoleName(bool isProducer, bool isConsumer, bool canTransship)
    {
        return (isProducer, isConsumer, canTransship) switch
        {
            (false, false, false) => NoTrafficRole,
            (true, false, false) => ProducerRole,
            (false, true, false) => ConsumerRole,
            (false, false, true) => TransshipRole,
            (true, true, false) => ProducerConsumerRole,
            (true, false, true) => ProducerTransshipRole,
            (false, true, true) => ConsumerTransshipRole,
            (true, true, true) => ProducerConsumerTransshipRole
        };
    }

    /// <summary>
    /// Returns the current role label for the supplied profile.
    /// </summary>
    public static string GetRoleName(NodeTrafficProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return GetRoleName(profile.Production > 0d, profile.Consumption > 0d, profile.CanTransship);
    }

    /// <summary>
    /// Indicates whether the named role needs a positive quantity to be represented by the simulator.
    /// </summary>
    public static bool RequiresCapacity(string? roleName)
    {
        return TryParseFlags(roleName, out var flags) && (flags.IsProducer || flags.IsConsumer);
    }

    /// <summary>
    /// Creates a single traffic profile from a GraphML-style traffic type, role, and optional capacity.
    /// </summary>
    public static NodeTrafficProfile? CreateDefaultProfile(string? trafficType, string? roleName, double? capacity)
    {
        if (string.IsNullOrWhiteSpace(trafficType) || !TryParseFlags(roleName, out var flags))
        {
            return null;
        }

        if (!flags.IsProducer && !flags.IsConsumer && !flags.CanTransship)
        {
            return null;
        }

        var normalizedTrafficType = trafficType.Trim();
        var quantity = flags.IsProducer || flags.IsConsumer
            ? capacity is > 0d ? capacity.Value : 1d
            : 0d;

        return new NodeTrafficProfile
        {
            TrafficType = normalizedTrafficType,
            Production = flags.IsProducer ? quantity : 0d,
            Consumption = flags.IsConsumer ? quantity : 0d,
            CanTransship = flags.CanTransship
        };
    }

    /// <summary>
    /// Returns a single representative quantity for generic GraphML exports.
    /// </summary>
    public static double? GetRepresentativeCapacity(NodeTrafficProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Production > 0d && profile.Consumption > 0d)
        {
            return Math.Max(profile.Production, profile.Consumption);
        }

        if (profile.Production > 0d)
        {
            return profile.Production;
        }

        if (profile.Consumption > 0d)
        {
            return profile.Consumption;
        }

        return null;
    }

    /// <summary>
    /// Applies the named role to a live profile.
    /// </summary>
    public static void ApplyRoleSelection(
        NodeTrafficProfileViewModelAdapter adapter,
        string roleName)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        if (!TryParseFlags(roleName, out var flags))
        {
            flags = default;
        }

        adapter.Production = flags.IsProducer ? Math.Max(adapter.Production, 1d) : 0d;
        adapter.Consumption = flags.IsConsumer ? Math.Max(adapter.Consumption, 1d) : 0d;
        adapter.CanTransship = flags.CanTransship;
    }

    public static bool TryParseFlags(string? roleName, out NodeTrafficRoleFlags flags)
    {
        var normalized = roleName?.Trim();

        if (string.IsNullOrWhiteSpace(normalized) ||
            Comparer.Equals(normalized, NoTrafficRole) ||
            Comparer.Equals(normalized, "None"))
        {
            flags = default;
            return true;
        }

        if (Comparer.Equals(normalized, ProducerRole))
        {
            flags = new NodeTrafficRoleFlags(true, false, false);
            return true;
        }

        if (Comparer.Equals(normalized, ConsumerRole))
        {
            flags = new NodeTrafficRoleFlags(false, true, false);
            return true;
        }

        if (Comparer.Equals(normalized, TransshipRole))
        {
            flags = new NodeTrafficRoleFlags(false, false, true);
            return true;
        }

        if (Comparer.Equals(normalized, ProducerConsumerRole))
        {
            flags = new NodeTrafficRoleFlags(true, true, false);
            return true;
        }

        if (Comparer.Equals(normalized, ProducerTransshipRole))
        {
            flags = new NodeTrafficRoleFlags(true, false, true);
            return true;
        }

        if (Comparer.Equals(normalized, ConsumerTransshipRole))
        {
            flags = new NodeTrafficRoleFlags(false, true, true);
            return true;
        }

        if (Comparer.Equals(normalized, ProducerConsumerTransshipRole))
        {
            flags = new NodeTrafficRoleFlags(true, true, true);
            return true;
        }

        flags = default;
        return false;
    }

    public readonly record struct NodeTrafficRoleFlags(bool IsProducer, bool IsConsumer, bool CanTransship);

    /// <summary>
    /// Minimal adapter so the catalog can update view models without owning UI classes.
    /// </summary>
    public interface NodeTrafficProfileViewModelAdapter
    {
        double Production { get; set; }

        double Consumption { get; set; }

        bool CanTransship { get; set; }
    }
}

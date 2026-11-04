using Pragmatic.Persistence.Entity;

namespace Pragmatic.Authorization.Management.Stores;

/// <summary>
///     Whether a temporal row is in force: the one rule the actions, the permission provider and the role store apply to what
///     <see cref="IAuthorizationManagementStore" /> reads, on every engine.
/// </summary>
internal static class TemporalValidity
{
    /// <summary>Started at or before <paramref name="now" />, and not ended at it.</summary>
    public static bool IsInForceAt(this ITemporalRelation row, DateTimeOffset now)
        => row.ValidFrom <= now && (row.ValidTo is null || row.ValidTo > now);
}

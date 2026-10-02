namespace Pragmatic.Authorization.Stores;

/// <summary>
///     Extends <see cref="IGroupRoleStore"/> with point-in-time role resolution.
///     Forward-compatible interface for L3 temporal authorization.
/// </summary>
public interface ITemporalGroupRoleStore : IGroupRoleStore
{
    /// <summary>Gets roles for a group as they were at the specified point in time.</summary>
    ValueTask<IReadOnlySet<string>> GetRolesForGroupAsync(
        string groupName, DateTimeOffset asOf, CancellationToken ct = default);
}

namespace Pragmatic.Authorization.Stores;

/// <summary>
///     Resolves roles assigned to a group.
///     Pluggable: in-memory for dev/test, database-backed for production.
/// </summary>
public interface IGroupRoleStore
{
    /// <summary>Gets all roles assigned to the specified group.</summary>
    ValueTask<IReadOnlySet<string>> GetRolesForGroupAsync(
        string groupName, CancellationToken ct = default);

    /// <summary>Gets all known groups.</summary>
    ValueTask<IReadOnlyList<string>> GetAllGroupsAsync(CancellationToken ct = default);
}

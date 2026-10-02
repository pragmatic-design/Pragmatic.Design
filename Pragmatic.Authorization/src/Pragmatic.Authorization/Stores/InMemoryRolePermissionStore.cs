using System.Collections.Concurrent;

namespace Pragmatic.Authorization.Stores;

/// <summary>
///     In-memory <see cref="IRolePermissionStore"/> for development, testing, and configuration-driven setups.
///     Populate via <c>AuthorizationBuilder.MapRole</c> or programmatically.
/// </summary>
public sealed class InMemoryRolePermissionStore : IRolePermissionStore
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _rolePermissions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds a role with its permissions. Thread-safe.</summary>
    public void AddRole(string roleName, IEnumerable<string> permissions)
    {
        var set = _rolePermissions.GetOrAdd(
            roleName,
            static _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        // Lock on the individual set to allow concurrent adds to different roles
        lock (set)
        {
            foreach (var p in permissions)
                set.Add(p);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlySet<string>> GetPermissionsForRoleAsync(
        string roleName, CancellationToken ct = default)
    {
        if (_rolePermissions.TryGetValue(roleName, out var perms))
        {
            // Snapshot under lock to avoid concurrent modification during enumeration
            lock (perms)
            {
                IReadOnlySet<string> snapshot = new HashSet<string>(perms, StringComparer.OrdinalIgnoreCase);
                return ValueTask.FromResult(snapshot);
            }
        }

        return ValueTask.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<string>> GetAllRolesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<string> roles = _rolePermissions.Keys.ToList();
        return ValueTask.FromResult(roles);
    }
}

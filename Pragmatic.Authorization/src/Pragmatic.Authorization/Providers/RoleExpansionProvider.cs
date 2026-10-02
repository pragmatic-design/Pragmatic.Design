using Pragmatic.Authorization.Stores;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Providers;

/// <summary>
///     Expands user roles into permissions via <see cref="IRolePermissionStore"/>.
///     Runs after claims-based providers (Order = 100).
/// </summary>
public sealed class RoleExpansionProvider(IRolePermissionStore store) : IPermissionProvider
{
    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
        ICurrentUser user, CancellationToken ct = default)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!user.Claims.TryGetValue("role", out var roles))
            return merged;

        foreach (var role in roles)
        {
            var permissions = await store.GetPermissionsForRoleAsync(role, ct).ConfigureAwait(false);
            foreach (var p in permissions)
                merged.Add(p);
        }

        return merged;
    }
}

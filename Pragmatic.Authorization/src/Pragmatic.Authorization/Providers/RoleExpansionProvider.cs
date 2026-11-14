using Microsoft.Extensions.Options;
using Pragmatic.Authorization.Configuration;
using Pragmatic.Authorization.Stores;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Providers;

/// <summary>
///     Expands user roles into permissions via <see cref="IRolePermissionStore"/>.
///     Runs after claims-based providers (Order = 100).
/// </summary>
/// <remarks>
///     An unauthenticated caller's role is <see cref="AuthorizationOptions.AnonymousRole" />, when one is configured (#722).
/// </remarks>
public sealed class RoleExpansionProvider(IRolePermissionStore store, IOptions<AuthorizationOptions>? options = null) : IPermissionProvider
{
    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
        ICurrentUser user, CancellationToken ct = default)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> roles = user.Claims.TryGetValue("role", out var claimed) ? claimed : [];
        if (options?.Value.AnonymousRoleFor(user) is { } anonymous)
            roles = roles.Append(anonymous);

        foreach (var role in roles)
        {
            var permissions = await store.GetPermissionsForRoleAsync(role, ct).ConfigureAwait(false);
            foreach (var p in permissions)
                merged.Add(p);
        }

        return merged;
    }
}

using Pragmatic.Identity;

namespace Pragmatic.Authorization.Providers;

/// <summary>
///     Reads permissions from the user's claims (claim type "permission").
///     First in the provider chain (Order = 0).
/// </summary>
public sealed class ClaimsPermissionProvider : IPermissionProvider
{
    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
        ICurrentUser user, CancellationToken ct = default)
    {
        if (user.Claims.TryGetValue("permission", out var permissions))
        {
            var set = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            return ValueTask.FromResult<IReadOnlySet<string>>(set);
        }

        return ValueTask.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }
}

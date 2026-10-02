using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Pragmatic.Identity.Oidc;

/// <summary>
///     Maps the external IdP's role claim (<see cref="OidcOptions.RoleClaim"/>) onto the role claim type the
///     Pragmatic authorization pipeline reads (<see cref="IdentityOptions.RoleClaimType"/>) so it sees the
///     user's roles. The role claim may appear as multiple claims (one per role) or a single
///     JSON-array-valued claim — both are handled. Idempotent: re-running adds nothing already present.
/// </summary>
public sealed class OidcRoleClaimsTransformer(
    IOptions<OidcOptions> options,
    IOptions<IdentityOptions> identityOptions) : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var roleClaim = options.Value.RoleClaim;
        if (string.IsNullOrEmpty(roleClaim) || principal.Identity is not ClaimsIdentity identity)
            return Task.FromResult(principal);

        // Write to the configured role claim type rather than ClaimTypes.Role — the rest of the
        // pipeline (NormalizeClaimType / CachedPermissionResolver) keys off IdentityOptions.RoleClaimType.
        var roleClaimType = identityOptions.Value.RoleClaimType;
        var opts = options.Value;

        foreach (var claim in principal.FindAll(roleClaim).ToList())
            foreach (var role in ExtractRoles(claim.Value))
                if (IsRoleAllowed(role, opts) && !identity.HasClaim(roleClaimType, role))
                    identity.AddClaim(new Claim(roleClaimType, role));

        return Task.FromResult(principal);
    }

    // Optional constraint for roles from a shared/mis-scoped external IdP. Null/empty AllowedRoles and null
    // RolePrefix (the defaults) let every role through; when set, a role must satisfy BOTH filters.
    private static bool IsRoleAllowed(string role, OidcOptions options)
    {
        if (options.RolePrefix is { Length: > 0 } prefix
            && !role.StartsWith(prefix, System.StringComparison.Ordinal))
            return false;

        return options.AllowedRoles is not { Count: > 0 } allowed || allowed.Contains(role);
    }

    /// <summary>A role claim value is either a single role or a JSON array of roles.</summary>
    private static IReadOnlyList<string> ExtractRoles(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        if (value.TrimStart().StartsWith("[", System.StringComparison.Ordinal))
        {
            try
            {
                var roles = JsonSerializer.Deserialize(value, global::Pragmatic.Serialization.PragmaticCommonJsonContext.Default.StringArray);
                if (roles is not null)
                    return roles.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
            }
            catch (JsonException)
            {
                // Not a JSON array after all — fall through to the single-value case.
            }
        }

        return [value];
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Pragmatic.Identity.Keycloak;

/// <summary>
///     Maps Keycloak's <c>realm_access</c> roles onto the role claim type the Pragmatic authorization
///     pipeline reads (<see cref="IdentityOptions.RoleClaimType"/>). Unlike a flat OIDC role claim, Keycloak
///     emits <c>realm_access</c> as a JSON object — <c>{"roles":["admin","user"]}</c> — so the nested array
///     must be read out. Idempotent.
/// </summary>
public sealed class KeycloakRoleClaimsTransformer(
    IOptions<IdentityOptions> identityOptions,
    IOptions<KeycloakOptions> keycloakOptions)
    : IClaimsTransformation
{
    private const string RealmAccessClaim = "realm_access";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity)
            return Task.FromResult(principal);

        var realmAccess = principal.FindFirst(RealmAccessClaim);
        if (realmAccess is null)
            return Task.FromResult(principal);

        // Write to the configured role claim type rather than ClaimTypes.Role — the rest of the
        // pipeline (NormalizeClaimType / CachedPermissionResolver) keys off IdentityOptions.RoleClaimType.
        var roleClaimType = identityOptions.Value.RoleClaimType;
        var opts = keycloakOptions.Value;

        foreach (var role in ExtractRealmRoles(realmAccess.Value))
            if (IsRoleAllowed(role, opts) && !identity.HasClaim(roleClaimType, role))
                identity.AddClaim(new Claim(roleClaimType, role));

        return Task.FromResult(principal);
    }

    // Optional constraint for roles from a shared Keycloak realm. Null/empty AllowedRoles and null
    // RolePrefix (the defaults) let every realm role through; when set, a role must satisfy BOTH filters.
    private static bool IsRoleAllowed(string role, KeycloakOptions options)
    {
        if (options.RolePrefix is { Length: > 0 } prefix
            && !role.StartsWith(prefix, System.StringComparison.Ordinal))
            return false;

        return options.AllowedRoles is not { Count: > 0 } allowed || allowed.Contains(role);
    }

    private static IReadOnlyList<string> ExtractRealmRoles(string realmAccessJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(realmAccessJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("roles", out var roles) &&
                roles.ValueKind == JsonValueKind.Array)
            {
                return roles.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Malformed realm_access — treat as no roles rather than throwing during auth.
        }

        return [];
    }
}

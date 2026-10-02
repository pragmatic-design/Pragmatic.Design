using Pragmatic.Identity;

namespace Pragmatic.Authorization.Scopes;

/// <summary>
///     Default scope resolver that expands user identity into standard scope identifiers.
///     Produces: <c>"user:{id}"</c>, <c>"role:{roleName}"</c> for each role,
///     and <c>"scope:{scopeName}"</c> for each explicit data scope claim.
/// </summary>
public sealed class DefaultUserScopeResolver : IUserScopeResolver
{
    private const string DataScopeClaimType = "data-scope";

    public Task<IReadOnlySet<string>> ResolveAccessScopesAsync(
        ICurrentUser user,
        CancellationToken ct = default)
    {
        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The spellings come from ScopeIdentifiers, not from an interpolation here: ScopeInterceptor
        // writes user:{id} onto a row at insert, this expands a principal into the set a row is matched
        // against, and a disagreement between the two is not an error anywhere — it is a row nobody can
        // see.
        var userId = user.IdOrNull();
        if (userId is not null)
            scopes.Add(ScopeIdentifiers.ForUser(userId));

        foreach (var role in user.Authorization.Roles)
            scopes.Add(ScopeIdentifiers.ForRole(role));

        // Explicit data-scope claims.
        var dataScopeValues = user.GetClaims(DataScopeClaimType);
        foreach (var value in dataScopeValues)
            scopes.Add(ScopeIdentifiers.ForScope(value));

        return Task.FromResult<IReadOnlySet<string>>(scopes);
    }
}

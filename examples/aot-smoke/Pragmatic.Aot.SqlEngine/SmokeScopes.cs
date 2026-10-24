using Pragmatic.Authorization;
using Pragmatic.Identity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>The caller's scopes.</summary>
internal sealed class SmokeScopes(params string[] scopes) : IUserScopeResolver
{
    public Task<IReadOnlySet<string>> ResolveAccessScopesAsync(ICurrentUser user, CancellationToken ct = default) => Task.FromResult(ResolveAccessScopes(user));

    public IReadOnlySet<string> ResolveAccessScopes(ICurrentUser user) => scopes.ToHashSet();
}

using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using Pragmatic.Authorization.Configuration;
using Pragmatic.Authorization.Diagnostics;
using Pragmatic.Caching;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Authorization.Evaluation;

/// <summary>
///     Request-scoped lazy resolver that combines all <see cref="IPermissionProvider"/>
///     results and caches the resolved permission set for the lifetime of the scope.
///     Optionally uses <see cref="ICacheStack"/> for cross-request caching.
///     Implements <see cref="IUserAuthorization"/> — this is the runtime bridge.
/// </summary>
public sealed class CachedPermissionResolver : IUserAuthorization
{
    private readonly IReadOnlyList<IPermissionProvider> _providers;
    private readonly ICurrentUser _currentUser;
    private readonly ICacheStack? _cache;
    private readonly PermissionCacheOptions? _cacheOptions;

    // ⚠️ Whether the answer can depend on the token. With the claims trusted, the permission set is a
    // function of what the request carried; with them not trusted it is a function of the person, and
    // the token is irrelevant. The cache key has to say which of the two it is.
    private readonly bool _trustsClaims;

    // ⚠️ The tenant the request is being SERVED for, which is not always the tenant on the principal.
    // ICurrentUser.TenantId is the `tenant_id` claim; an application that resolves tenants by header,
    // route or subdomain and issues no such claim has it null on every request. Keying the permission
    // cache off the claim alone made every workspace share one entry per person — measured, and the
    // second workspace inherited the first one's permissions. Optional because an application with no
    // multi-tenancy registers no ITenantContext, and the claim is then the only thing there is.
    private readonly ITenantContext? _tenantContext;
    private IReadOnlySet<string>? _resolvedPermissions;
    // Cached subset of wildcard patterns; null until first resolution.
    private IReadOnlyList<string>? _wildcardPermissions;

    // ⚠️ Reentrancy guard. IPermissionProvider documents reading from "an external source (JWT claims,
    // database, policy server)", and reading this application's own database is what a per-tenant
    // permission store has to do — but a repository read evaluates the query filters, and a
    // permission-based filter asks for the caller's permissions, which lands back here. The cache
    // below is only assigned once the providers have finished, so nothing broke that cycle: the
    // process died of a stack overflow, with no exception and no log. Returning the empty set instead
    // makes the provider fail closed and leaves a diagnostic to count, which is a defined behaviour a
    // consumer can find. It does not make the read work — a provider that needs its own data must
    // read it past the filter pipeline (IQueryFilterToggle) and apply the tenant itself.
    private bool _resolving;

    public CachedPermissionResolver(
        IEnumerable<IPermissionProvider> providers,
        ICurrentUser currentUser,
        IOptions<AuthorizationOptions>? options = null,
        ICacheStack? cache = null,
        ITenantContext? tenantContext = null)
    {
        _providers = providers.OrderBy(p => p.Order).ToList();
        _currentUser = currentUser;
        _cacheOptions = options?.Value.CacheOptions;
        _trustsClaims = options?.Value.TrustPermissionClaims ?? false;
        _cache = cache;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles => _currentUser.Claims.TryGetValue("role", out var roles)
        ? (IReadOnlyCollection<string>)roles
        : Array.Empty<string>();

    /// <inheritdoc />
    public IReadOnlySet<string> Permissions => ResolvePermissions();

    /// <inheritdoc />
    public IReadOnlyCollection<string> Groups => _currentUser.Claims.TryGetValue("group", out var groups)
        ? (IReadOnlyCollection<string>)groups
        : Array.Empty<string>();

    /// <inheritdoc />
    public IReadOnlyCollection<string> Scopes => _currentUser.Claims.TryGetValue("scope", out var scopes)
        ? (IReadOnlyCollection<string>)scopes
        : Array.Empty<string>();

    /// <inheritdoc />
    public bool HasPermission(string permission)
    {
        AuthorizationDiagnostics.PermissionChecks.Add(1);
        var result = MatchesPermission(permission);
        if (!result)
            AuthorizationDiagnostics.PermissionDenied.Add(1);
        return result;
    }

    /// <inheritdoc />
    public bool HasAnyPermission(IEnumerable<string> permissions)
    {
        AuthorizationDiagnostics.PermissionChecks.Add(1);
        var result = permissions.Any(MatchesPermission);
        if (!result)
            AuthorizationDiagnostics.PermissionDenied.Add(1);
        return result;
    }

    /// <inheritdoc />
    public bool HasAllPermissions(IEnumerable<string> permissions)
    {
        AuthorizationDiagnostics.PermissionChecks.Add(1);
        var result = permissions.All(MatchesPermission);
        if (!result)
            AuthorizationDiagnostics.PermissionDenied.Add(1);
        return result;
    }

    // ── Async (non-blocking) variants — override the IUserAuthorization defaults to await the store
    //    instead of blocking a thread-pool thread on a cache miss. ─────────────────────────────────

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<string>> GetPermissionsAsync(CancellationToken cancellationToken = default)
        => await ResolvePermissionsAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default)
    {
        AuthorizationDiagnostics.PermissionChecks.Add(1);
        await ResolvePermissionsAsync(cancellationToken).ConfigureAwait(false);
        var result = MatchesResolved(permission);
        if (!result)
            AuthorizationDiagnostics.PermissionDenied.Add(1);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<bool> HasAnyPermissionAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        AuthorizationDiagnostics.PermissionChecks.Add(1);
        await ResolvePermissionsAsync(cancellationToken).ConfigureAwait(false);
        var result = permissions.Any(MatchesResolved);
        if (!result)
            AuthorizationDiagnostics.PermissionDenied.Add(1);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<bool> HasAllPermissionsAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        AuthorizationDiagnostics.PermissionChecks.Add(1);
        await ResolvePermissionsAsync(cancellationToken).ConfigureAwait(false);
        var result = permissions.All(MatchesResolved);
        if (!result)
            AuthorizationDiagnostics.PermissionDenied.Add(1);
        return result;
    }

    private bool MatchesPermission(string required)
    {
        ResolvePermissions();
        return MatchesResolved(required);
    }

    /// <summary>Matches against the already-resolved permission set (no resolution). Caller must have resolved first.</summary>
    private bool MatchesResolved(string required)
    {
        var resolved = _resolvedPermissions!;

        // Fast path: exact match (most common case)
        if (resolved.Contains(required))
            return true;

        // Avoid iterating all resolved permissions on every call; only iterate the
        // pre-extracted wildcard subset (typically much smaller).
        var wildcards = _wildcardPermissions;
        if (wildcards is null || wildcards.Count == 0)
            return false;

        foreach (var granted in wildcards)
        {
            if (WildcardMatcher.Matches(granted, required))
                return true;
        }

        return false;
    }

    /// <inheritdoc />
    public bool IsInRole(string role)
        => Roles.Contains(role);

    /// <inheritdoc />
    public bool IsInGroup(string group)
        => Groups.Contains(group);

    /// <inheritdoc />
    public bool HasScope(string scope)
        => Scopes.Contains(scope);

    /// <inheritdoc />
    /// <remarks>
    ///     The same flag the re-entrancy guard reads, exposed so the query-filter pipeline can skip
    ///     permission filters while this is true instead of asking the provider to lift them by hand.
    /// </remarks>
    public bool IsResolvingPermissions => _resolving;

    /// <summary>
    ///     What a provider gets when it asks for the permissions it is itself resolving.
    /// </summary>
    /// <remarks>
    ///     Empty rather than partial: handing back what the earlier providers happened to contribute
    ///     would make one provider's answer depend on its position in the chain, which is worse than
    ///     an answer that is plainly nothing. The set is not cached — <c>_resolvedPermissions</c> is
    ///     left alone, so the outer resolution still completes and stores the real answer.
    /// </remarks>
    private static IReadOnlySet<string> ReentrantEmptySet()
    {
        AuthorizationDiagnostics.ReentrantResolutions.Add(1);
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlySet<string> ResolvePermissions()
    {
        if (_resolvedPermissions is not null)
        {
            AuthorizationDiagnostics.CacheHits.Add(1);
            return _resolvedPermissions;
        }

        if (_resolving)
            return ReentrantEmptySet();

        AuthorizationDiagnostics.CacheMisses.Add(1);

        _resolving = true;
        try
        {
            _resolvedPermissions = CanUseCrossRequestCache()
                ? ResolveCached()
                : ResolveFromProviders();
        }
        finally
        {
            _resolving = false;
        }

        // Pre-extract wildcard patterns so MatchesPermission iterates only the small subset.
        _wildcardPermissions = _resolvedPermissions
            .Where(p => p.Contains('*'))
            .ToList();

        return _resolvedPermissions;
    }

    private bool CanUseCrossRequestCache()
        => _cache is not null && _cacheOptions is not null && _currentUser.IsAuthenticated;

    private IReadOnlySet<string> ResolveCached()
    {
        var key = BuildCacheKey();
        var options = new CacheEntryOptions
        {
            Duration = _cacheOptions!.Expiration,
            Tags = BuildCacheTags()
        };

        // ICacheStack.GetOrSetAsync is synchronously awaited here because
        // IUserAuthorization.Permissions is a synchronous property.
        // Use Task.Run to detach from any ambient SynchronizationContext to
        // avoid deadlock if an async cache provider captures it (ASP.NET
        // Classic, WinForms, WPF, xUnit SyncContext).
        var task = _cache!.GetOrSetAsync<HashSet<string>>(
            key,
            async ct => await ResolveFromProvidersAsync().ConfigureAwait(false),
            options);

        if (task.IsCompleted)
            return task.Result;

        var pending = task.AsTask();
        return Task.Run(() => pending).GetAwaiter().GetResult();
    }

    private async ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(CancellationToken cancellationToken)
    {
        if (_resolvedPermissions is not null)
        {
            AuthorizationDiagnostics.CacheHits.Add(1);
            return _resolvedPermissions;
        }

        if (_resolving)
            return ReentrantEmptySet();

        AuthorizationDiagnostics.CacheMisses.Add(1);

        _resolving = true;
        try
        {
            _resolvedPermissions = CanUseCrossRequestCache()
                ? await ResolveCachedAsync(cancellationToken).ConfigureAwait(false)
                : await ResolveFromProvidersAsync().ConfigureAwait(false);
        }
        finally
        {
            _resolving = false;
        }

        _wildcardPermissions = _resolvedPermissions
            .Where(p => p.Contains('*'))
            .ToList();

        return _resolvedPermissions;
    }

    private async ValueTask<IReadOnlySet<string>> ResolveCachedAsync(CancellationToken cancellationToken)
    {
        var key = BuildCacheKey();
        var options = new CacheEntryOptions
        {
            Duration = _cacheOptions!.Expiration,
            Tags = BuildCacheTags()
        };

        // No blocking: this is the async path consumed by the request pipeline.
        return await _cache!.GetOrSetAsync<HashSet<string>>(
            key,
            async ct => await ResolveFromProvidersAsync().ConfigureAwait(false),
            options).ConfigureAwait(false);
    }

    private string BuildCacheKey()
    {
        var prefix = _cacheOptions!.KeyPrefix;
        // Explicit scope markers prevent cross-tenant collisions:
        // - "t:{id}:u:{uid}" for multi-tenant requests
        // - "g:u:{uid}" for global / single-tenant requests
        // Without the marker, a missing TenantId would collapse to the same key
        // as a multi-tenant user with the id shape, leaking permissions across scopes.
        //
        // ⚠️ The resolved tenant comes first and the claim is the fallback. The marker alone was not
        // enough: it separates the shapes, not the scopes, and an application that resolves tenants
        // by header without issuing a tenant claim gave every workspace the SAME "g:u:{uid}" key.
        // One person then carried the first workspace's permissions into the second — reproduced in
        // PermissionCacheKeyTenantTests before this line existed. Where both are present the tenant
        // middleware has already refused the request unless they agree (EnforceTenantClaim), so
        // preferring the resolved one changes nothing there.
        var tenant = _tenantContext?.TenantId is { Length: > 0 } resolved
            ? resolved
            : _currentUser.TenantId;

        var key = tenant is { Length: > 0 } tid
            ? $"{prefix}:t:{tid}:u:{_currentUser.Id}"
            : $"{prefix}:g:u:{_currentUser.Id}";

        return key + TrustedClaimsKeySuffix() + DelegationKeySuffix();
    }

    /// <summary>
    ///     What the request carried, when the request is what the answer is read from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ With <c>TrustPermissionClaims</c> on, the permission set comes from the token —
    ///         <c>ClaimsPermissionProvider</c> reads the <c>permission</c> claims — so keying by the
    ///         user id alone made the <b>first</b> request decide what that id can do for as long as
    ///         the entry lives. Measured in an application: a caller with two permissions was refused
    ///         because an earlier request had arrived with one, and, read the other way, a caller
    ///         carrying a permission that grants nothing was admitted.
    ///     </para>
    ///     <para>
    ///         Empty when the claims are not trusted, which is the recommended posture: there the
    ///         answer is resolved server-side from roles and groups, the same for every token of that
    ///         person, and partitioning by the token would turn a hit into a store lookup per session.
    ///     </para>
    ///     <para>
    ///         SHA-256 rather than a cheap hash, and not for secrecy: two different claim sets that
    ///         collided would serve one caller another's authority. Truncated to 128 bits, which is
    ///         far past the point where that is a real event, and computed once per resolution.
    ///     </para>
    /// </remarks>
    private string TrustedClaimsKeySuffix()
    {
        if (!_trustsClaims)
            return "";

        var claims = _currentUser.Claims;
        var builder = new System.Text.StringBuilder();

        // Sorted, so the same authority written in another order is the same entry. The three the
        // providers read: the permissions themselves, and the roles and groups a resolver expands.
        foreach (var claimType in new[] { "permission", "role", "group" })
        {
            if (!claims.TryGetValue(claimType, out var values) || values.Count == 0)
                continue;

            builder.Append(claimType).Append('=');
            foreach (var value in values.OrderBy(static v => v, StringComparer.Ordinal))
                builder.Append(value).Append(',');
            builder.Append(';');
        }

        if (builder.Length == 0)
            return ":c:none";

        var digest = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(builder.ToString()));

        return ":c:" + Convert.ToHexString(digest, 0, 16).ToLowerInvariant();
    }

    /// <summary>
    ///     The segment that separates a delegated session's cached authority from the subject's own.
    ///     Empty when there is no delegation, so existing keys are unchanged byte for byte.
    /// </summary>
    /// <remarks>
    ///     🔴 Without this the cache is a privilege escalation. This resolver caches the <b>resolved
    ///     permission set</b> under the user's id; under delegation the effective authority differs
    ///     from the subject's own, so one of two things happens — the delegated session reads the
    ///     subject's full set and gains authority it was never granted, or it writes the narrowed set
    ///     and the subject silently loses permissions on their next request. Both wrong, and the first
    ///     is the dangerous one.
    ///     <para>
    ///         Same subject, actor, policy and grant means the same authority, so those four identify
    ///         it. An imprint of the permission set itself would cost more and change more often for
    ///         no additional safety.
    ///     </para>
    /// </remarks>
    private string DelegationKeySuffix()
        => _currentUser.Delegation is { } d
            ? $":d:{d.ActorId}:{d.Policy}:{d.GrantId ?? "-"}"
            : string.Empty;

    // Tags the cached permission set by everything that contributes to it, so a mutation to any
    // contributing role or group invalidates exactly the affected users (see PermissionCacheInvalidator):
    // - user:{id}  — direct per-user invalidation / account disable
    // - tenant:{id} — tenant-wide change
    // - role:{name} — invalidate when a role's permission set changes (direct role claims)
    // - group:{name} — invalidate when a group's role set changes (group → roles → permissions)
    // Roles a user inherits transitively from a group are covered by the group tag; the user's own
    // group claims are the stable membership signal at cache time.
    private ImmutableArray<string> BuildCacheTags()
    {
        var tags = ImmutableArray.CreateBuilder<string>();
        tags.Add($"user:{_currentUser.Id}");

        // The actor gets its own tag so revoking a grant can evict every session that acted under it
        // without touching the subject's own cached authority. This is the eviction half of the
        // decision on how a running delegation notices it was revoked.
        if (_currentUser.Delegation is { } delegation)
        {
            tags.Add($"actor:{delegation.ActorId}");
            if (delegation.GrantId is { Length: > 0 } grant)
                tags.Add($"grant:{grant}");
        }

        if (_currentUser.TenantId is { Length: > 0 } tid)
            tags.Add($"tenant:{tid}");

        foreach (var role in Roles)
            tags.Add($"role:{role}");

        foreach (var group in Groups)
            tags.Add($"group:{group}");

        return tags.ToImmutable();
    }

    private async ValueTask<HashSet<string>> ResolveFromProvidersAsync()
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in _providers)
        {
            var permissions = await provider.ResolvePermissionsAsync(_currentUser).ConfigureAwait(false);
            foreach (var p in permissions)
                merged.Add(p);
        }

        return merged;
    }

    private HashSet<string> ResolveFromProviders()
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in _providers)
        {
            var task = provider.ResolvePermissionsAsync(_currentUser);
            // Use Task.Run to detach from any ambient SynchronizationContext before
            // blocking — avoids deadlocks in ASP.NET Classic, WinForms, WPF contexts.
            var permissions = task.IsCompleted
                ? task.Result
                : Task.Run(() => task.AsTask()).GetAwaiter().GetResult();

            foreach (var p in permissions)
                merged.Add(p);
        }

        return merged;
    }
}

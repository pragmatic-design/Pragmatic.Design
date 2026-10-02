namespace Pragmatic.Configuration.Resolution;

using Pragmatic.Identity;
using Pragmatic.MultiTenancy;

/// <summary>
///     Cascade resolver: base → environment → tenant → user.
///     Resolves configuration values by walking the environment resolution chain and then applying,
///     in increasing order of precedence, tenant-specific and user-specific overrides.
/// </summary>
/// <remarks>
///     <para>
///         Precedence (highest wins): <c>user → tenant → environment overlay → base</c>.
///     </para>
///     <para>
///         Tenant and user overrides reuse the store's scoped-key mechanism
///         (<see cref="IConfigurationStore.GetAsync(string, string, CancellationToken)"/>). The tenant
///         scope id is the tenant id; the user scope id is namespaced as <c>user:{userId}</c> so user
///         overrides never collide with tenant overrides in the same store.
///     </para>
///     <para>
///         With <paramref name="fallbackToBase" /> <see langword="false" /> and a resolved tenant, the
///         environment overlay and the base value are not consulted: a tenant with no override of its own
///         gets nothing rather than what every tenant shares. A user override still applies.
///     </para>
/// </remarks>
/// <param name="store">Where values are read.</param>
/// <param name="environment">The environment overlay chain.</param>
/// <param name="tenantContext">The resolved tenant, when multi-tenant resolution is enabled.</param>
/// <param name="currentUser">The caller, for user overrides.</param>
/// <param name="fallbackToBase"><c>MultiTenantOptions.FallbackToBase</c>.</param>
public sealed class ConfigurationResolver(
    IConfigurationStore store,
    EnvironmentProfile environment,
    ITenantContext? tenantContext = null,
    ICurrentUser? currentUser = null,
    bool fallbackToBase = true) : IConfigurationResolver
{
    /// <summary>Scope-id prefix used to namespace user-scoped overrides in the store.</summary>
    private const string UserScopePrefix = "user:";

    /// <summary>
    ///     Resolves a single configuration value using the cascade (highest precedence first):
    ///     user → tenant → environment → base.
    /// </summary>
    public async Task<string?> ResolveAsync(string key, CancellationToken ct = default)
        => (await ResolveWithTraceAsync(key, ct).ConfigureAwait(false)).Value;

    /// <summary>
    ///     Resolves a single value and reports <b>which</b> cascade layer supplied it — the answer to
    ///     "why is this value what it is?". Same walk and precedence as <see cref="ResolveAsync" />
    ///     (user → tenant → environment → base), returning the winning layer and source key.
    /// </summary>
    public async Task<ResolvedValue> ResolveWithTraceAsync(string key, CancellationToken ct = default)
    {
        // 1. User override (highest precedence)
        if (UserScopeId() is { } userScopeId)
        {
            var userValue = await store.GetAsync(key, userScopeId, ct).ConfigureAwait(false);
            if (userValue is not null)
                return new ResolvedValue(userValue, ResolutionSource.User, userScopeId, null);
        }

        // 2. Tenant override
        if (tenantContext is { IsResolved: true, TenantId: not null })
        {
            var tenantValue = await store.GetAsync(key, tenantContext.TenantId, ct).ConfigureAwait(false);
            if (tenantValue is not null)
                return new ResolvedValue(tenantValue, ResolutionSource.Tenant, tenantContext.TenantId, null);

            if (!fallbackToBase)
                return ResolvedValue.NotFound;
        }

        // 3. Environment overlay (most specific to least; index 0 is "base", handled below)
        for (var i = environment.ResolutionChain.Count - 1; i > 0; i--)
        {
            var envTag = environment.ResolutionChain[i];
            var envKey = $"{envTag}/{key}";
            var envValue = await store.GetAsync(envKey, ct).ConfigureAwait(false);
            if (envValue is not null)
                return new ResolvedValue(envValue, ResolutionSource.Environment, envKey, envTag);
        }

        // 4. Base value
        var baseValue = await store.GetAsync(key, ct).ConfigureAwait(false);
        return baseValue is not null
            ? new ResolvedValue(baseValue, ResolutionSource.Base, key, null)
            : ResolvedValue.NotFound;
    }

    /// <summary>
    ///     Resolves all values under a given prefix, applying the same cascade logic per key.
    ///     Overlays are applied least-specific to most-specific so the highest-precedence
    ///     scope (user) wins on key collision: base → environment → tenant → user.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ResolveSectionAsync(
        string prefix, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>();
        var tenantResolved = tenantContext is { IsResolved: true, TenantId: not null };

        // Base and environment overlays, unless a resolved tenant is not to inherit them.
        if (tenantResolved && !fallbackToBase)
            return await OverlayTenantAndUserAsync(result, prefix, ct).ConfigureAwait(false);

        // Base
        var baseValues = await store.GetSectionAsync(prefix, ct).ConfigureAwait(false);
        foreach (var kv in baseValues) result[kv.Key] = kv.Value;

        // Env overlays (least specific to most specific; index 0 is "base", already applied)
        for (var i = 1; i < environment.ResolutionChain.Count; i++)
        {
            var envPrefix = $"{environment.ResolutionChain[i]}/{prefix}";
            var envValues = await store.GetSectionAsync(envPrefix, ct).ConfigureAwait(false);
            foreach (var kv in envValues)
            {
                // Guard: store may return keys that don't start with envPrefix.
                if (kv.Key.Length <= envPrefix.Length)
                    continue;
                var originalKey = kv.Key[envPrefix.Length..];
                result[$"{prefix}{originalKey}"] = kv.Value;
            }
        }

        return await OverlayTenantAndUserAsync(result, prefix, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, string>> OverlayTenantAndUserAsync(
        Dictionary<string, string> result, string prefix, CancellationToken ct)
    {
        // Tenant override
        if (tenantContext is { IsResolved: true, TenantId: not null })
        {
            var tenantValues = await store.GetSectionAsync(prefix, tenantContext.TenantId, ct).ConfigureAwait(false);
            foreach (var kv in tenantValues) result[kv.Key] = kv.Value;
        }

        // User override (highest precedence — applied last)
        if (UserScopeId() is { } userScopeId)
        {
            var userValues = await store.GetSectionAsync(prefix, userScopeId, ct).ConfigureAwait(false);
            foreach (var kv in userValues) result[kv.Key] = kv.Value;
        }

        return result;
    }

    /// <summary>
    ///     Returns the namespaced store scope id for the current authenticated user, or <c>null</c>
    ///     when there is no authenticated user to scope overrides to.
    /// </summary>
    private string? UserScopeId()
        => currentUser is { IsAuthenticated: true, Id: { Length: > 0 } id }
            ? UserScopePrefix + id
            : null;
}

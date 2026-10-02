namespace Pragmatic.Configuration.Resolution;

/// <summary>
///     Which layer of the resolution cascade supplied a value.
/// </summary>
public enum ResolutionSource
{
    /// <summary>No layer supplied a value — the key is unset everywhere.</summary>
    NotFound,

    /// <summary>The base (unscoped) value.</summary>
    Base,

    /// <summary>An environment overlay (e.g. <c>staging/…</c>).</summary>
    Environment,

    /// <summary>A tenant-scoped override.</summary>
    Tenant,

    /// <summary>A user-scoped override (highest precedence).</summary>
    User
}

/// <summary>
///     A resolved configuration value together with the provenance of <b>which</b> cascade layer supplied
///     it — the answer to "why is this value what it is?". Produced by
///     <see cref="ConfigurationResolver.ResolveWithTraceAsync" />.
/// </summary>
/// <param name="Value">The resolved value, or <c>null</c> when <see cref="Source" /> is <see cref="ResolutionSource.NotFound" />.</param>
/// <param name="Source">The winning layer of the cascade.</param>
/// <param name="SourceKey">
///     The actual store key that supplied the value (e.g. the <c>user:{id}</c> scope key, the tenant id,
///     the <c>{env}/{key}</c> overlay key, or the base key). <c>null</c> when not found.
/// </param>
/// <param name="EnvironmentTag">
///     For an <see cref="ResolutionSource.Environment" /> hit, the environment-chain entry that supplied it
///     (e.g. <c>"staging"</c>); otherwise <c>null</c>.
/// </param>
public sealed record ResolvedValue(
    string? Value,
    ResolutionSource Source,
    string? SourceKey,
    string? EnvironmentTag)
{
    /// <summary><c>true</c> when a value was found in some layer.</summary>
    public bool Found => Source != ResolutionSource.NotFound;

    /// <summary>A not-found trace.</summary>
    public static readonly ResolvedValue NotFound = new(null, ResolutionSource.NotFound, null, null);
}

using Pragmatic.Configuration.Resolution;

namespace Pragmatic.Configuration.Management.Actions;

/// <summary>
///     Result of a resolution-trace query: the effective value together with the provenance of <b>which</b>
///     cascade layer supplied it — the answer to "why is this value what it is?".
/// </summary>
/// <param name="Key">The requested configuration key.</param>
/// <param name="Value">The effective value, or <c>null</c> when unset in every layer.</param>
/// <param name="Source">The winning cascade layer (<c>User</c>, <c>Tenant</c>, <c>Environment</c>, <c>Base</c>, <c>NotFound</c>).</param>
/// <param name="SourceKey">The actual store key that supplied the value (scope key / overlay key / base key).</param>
/// <param name="EnvironmentTag">For an environment hit, the overlay tag (e.g. <c>"staging"</c>); otherwise <c>null</c>.</param>
public sealed record ConfigResolutionResult(
    string Key,
    string? Value,
    string Source,
    string? SourceKey,
    string? EnvironmentTag)
{
    /// <summary>Projects a resolver <see cref="ResolvedValue" /> onto the management result shape.</summary>
    public static ConfigResolutionResult From(string key, ResolvedValue resolved)
        => new(key, resolved.Value, resolved.Source.ToString(), resolved.SourceKey, resolved.EnvironmentTag);
}

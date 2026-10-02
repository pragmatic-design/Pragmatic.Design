namespace Pragmatic.Configuration.Resolution;

/// <summary>
///     Resolves configuration values through the precedence cascade (highest wins):
///     <c>user → tenant → environment → base</c>.
/// </summary>
public interface IConfigurationResolver
{
    /// <summary>Resolves a single value, or <c>null</c> when unset in every layer.</summary>
    Task<string?> ResolveAsync(string key, CancellationToken ct = default);

    /// <summary>
    ///     Resolves a single value and reports <b>which</b> cascade layer supplied it —
    ///     the answer to "why is this value what it is?".
    /// </summary>
    Task<ResolvedValue> ResolveWithTraceAsync(string key, CancellationToken ct = default);

    /// <summary>Resolves all values under a prefix, applying the same cascade per key.</summary>
    Task<IReadOnlyDictionary<string, string>> ResolveSectionAsync(string prefix, CancellationToken ct = default);
}

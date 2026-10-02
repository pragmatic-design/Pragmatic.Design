namespace Pragmatic.Configuration.Resolution;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.MultiTenancy;

/// <summary>
///     Decorates <see cref="IConfigurationResolver"/> so that a resolved value which is a
///     <c>secret://{key}</c> reference (see <see cref="SecretReference"/>) is replaced, <b>locally at
///     read time</b>, with the secret material fetched from the host's own <see cref="ISecretStore"/>.
/// </summary>
/// <remarks>
///     <para>
///         This is what lets an enterprise secret store (Key Vault / Vault / cloud Secrets Manager) be the
///         single source of truth for secrets: the configuration store — and therefore any distributed
///         fabric backing it (the Agent KV, gossip, on-disk snapshot) — only ever holds the
///         <c>secret://</c> pointer, never the secret. Each host resolves the pointer against its own
///         vault, to which it already has (managed-identity) access.
///     </para>
///     <para>
///         A reference that cannot be resolved (no such secret) yields <c>null</c> — the literal
///         <c>secret://</c> string is <b>never</b> surfaced to a consumer — and a warning is logged.
///     </para>
/// </remarks>
public sealed class SecretResolvingConfigurationResolver : IConfigurationResolver
{
    private readonly IConfigurationResolver _inner;
    private readonly ISecretStore _secrets;
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger _logger;

    public SecretResolvingConfigurationResolver(
        IConfigurationResolver inner,
        ISecretStore secrets,
        ITenantContext? tenantContext = null,
        ILoggerFactory? loggerFactory = null)
    {
        _inner = inner;
        _secrets = secrets;
        _tenantContext = tenantContext;
        _logger = loggerFactory?.CreateLogger<SecretResolvingConfigurationResolver>()
            ?? NullLogger<SecretResolvingConfigurationResolver>.Instance;
    }

    /// <inheritdoc />
    public async Task<string?> ResolveAsync(string key, CancellationToken ct = default)
    {
        var value = await _inner.ResolveAsync(key, ct).ConfigureAwait(false);
        return SecretReference.TryParse(value, out var secretKey)
            ? await ResolveSecretAsync(key, secretKey, ct).ConfigureAwait(false)
            : value;
    }

    /// <inheritdoc />
    public async Task<ResolvedValue> ResolveWithTraceAsync(string key, CancellationToken ct = default)
    {
        var resolved = await _inner.ResolveWithTraceAsync(key, ct).ConfigureAwait(false);
        if (!SecretReference.TryParse(resolved.Value, out var secretKey))
            return resolved;

        // Keep the provenance (which cascade layer held the reference); swap only the value for the
        // resolved secret. A miss returns null — never the literal secret:// reference.
        var secret = await ResolveSecretAsync(key, secretKey, ct).ConfigureAwait(false);
        return resolved with { Value = secret };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> ResolveSectionAsync(
        string prefix, CancellationToken ct = default)
    {
        var section = await _inner.ResolveSectionAsync(prefix, ct).ConfigureAwait(false);

        Dictionary<string, string>? mutated = null;
        foreach (var kv in section)
        {
            if (!SecretReference.TryParse(kv.Value, out var secretKey))
                continue;

            mutated ??= new Dictionary<string, string>(section, StringComparer.Ordinal);
            var secret = await ResolveSecretAsync(kv.Key, secretKey, ct).ConfigureAwait(false);
            if (secret is null)
                mutated.Remove(kv.Key); // not-found → absent, never the literal reference
            else
                mutated[kv.Key] = secret;
        }

        return mutated ?? section;
    }

    private async Task<string?> ResolveSecretAsync(string configKey, string secretKey, CancellationToken ct)
    {
        // Tenant-scoped first (mirrors the config cascade), then global fallback.
        string? secret = null;
        if (_tenantContext is { IsResolved: true, TenantId: { } tenantId })
            secret = await _secrets.GetSecretAsync(secretKey, tenantId, ct).ConfigureAwait(false);

        secret ??= await _secrets.GetSecretAsync(secretKey, ct).ConfigureAwait(false);

        if (secret is null)
            _logger.LogWarning(
                "Configuration key '{ConfigKey}' references secret '{SecretKey}' which was not found in the secret store; resolving to null.",
                configKey, secretKey);

        return secret;
    }
}

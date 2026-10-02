using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Configuration;

namespace Pragmatic.Agent.Client.Stores;

/// <summary>
///     <see cref="IConfigurationStore"/> backed by the Agent KV store.
///     Push-based: Agent notifies via socket when config changes (~100ms via gossip).
/// </summary>
/// <remarks>
///     When the Agent daemon is unreachable (<see cref="AgentConnection.IsConnected"/> is <c>false</c>)
///     the store degrades to a local fallback so reads and writes keep working in L0 mode instead of
///     silently no-opping (the old behaviour). The fallback is the store registered before
///     <c>UseAgent()</c> replaced it, if any; otherwise a self-contained in-process store. Mirrors the
///     fallback behaviour of the Agent feature-flag store.
///     <para>
///         Values are written to the Agent KV as <b>plaintext</b> (only <c>secret/</c> keys are encrypted
///         at rest, and this store writes under <c>config/</c>). A <c>[Sensitive]</c> value written here
///         would therefore travel gossip and the on-disk snapshot in the clear — so <see cref="SetAsync"/>
///         warns and steers callers to a <see cref="SecretReference"/> (<c>secret://…</c>) backed by an
///         <see cref="ISecretStore"/> instead.
///     </para>
/// </remarks>
public sealed class AgentConfigurationStore(
    AgentConnection connection,
    IConfigurationStore? fallback = null,
    ISensitiveKeyClassifier? sensitiveKeyClassifier = null,
    ILoggerFactory? loggerFactory = null)
    : IConfigurationStore
{
    // When no external fallback was captured (e.g. UseAgent ran before the default store was
    // registered), degrade to a self-contained in-process store so L0 mode stays functional.
    private readonly IConfigurationStore _fallback = fallback ?? new InProcessConfigurationStore();

    private readonly ILogger _logger =
        loggerFactory?.CreateLogger<AgentConfigurationStore>() ?? NullLogger<AgentConfigurationStore>.Instance;

    private bool UseFallback => !connection.IsConnected;

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (UseFallback)
            return await _fallback.GetAsync(key, ct).ConfigureAwait(false);

        var (value, _, found) = await connection.KvGetAsync($"config/{key}", ct).ConfigureAwait(false);
        return found ? value : null;
    }

    public async Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default)
    {
        if (UseFallback)
            return await _fallback.GetAsync(key, tenantId, ct).ConfigureAwait(false);

        // Try tenant-specific first, fall back to global
        var (tenantValue, _, tenantFound) = await connection.KvGetAsync($"config/tenant:{tenantId}/{key}", ct).ConfigureAwait(false);
        if (tenantFound) return tenantValue;

        return await GetAsync(key, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default)
    {
        if (UseFallback)
            return await _fallback.GetSectionAsync(prefix, ct).ConfigureAwait(false);

        var entries = await connection.KvPrefixAsync($"config/{prefix}", ct).ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.Value is not null)
            {
                var key = entry.Key["config/".Length..];
                result[key] = entry.Value;
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, string tenantId, CancellationToken ct = default)
    {
        if (UseFallback)
            return await _fallback.GetSectionAsync(prefix, tenantId, ct).ConfigureAwait(false);

        var entries = await connection.KvPrefixAsync($"config/tenant:{tenantId}/{prefix}", ct).ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.Value is not null)
            {
                var tenantPrefix = $"config/tenant:{tenantId}/";
                var key = entry.Key[tenantPrefix.Length..];
                result[key] = entry.Value;
            }
        }

        return result;
    }

    public async Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
    {
        // Warn (don't block) if a [Sensitive] value is being written as plaintext into the Agent KV,
        // which gossips + persists it in the clear — steer the caller to a secret:// reference instead.
        if (sensitiveKeyClassifier is not null && !string.IsNullOrEmpty(value)
            && sensitiveKeyClassifier.IsSensitive(key) && !SecretReference.IsReference(value))
        {
            _logger.LogWarning(
                "Configuration key '{Key}' is marked [Sensitive] but is being written as a plaintext value into "
                + "the Agent KV (gossip + on-disk snapshot in the clear). Store the secret in an ISecretStore "
                + "(e.g. Key Vault) and put only a reference in configuration ('{Scheme}{Key}').",
                key, SecretReference.Scheme, key);
        }

        if (UseFallback)
        {
            await _fallback.SetAsync(key, value, tenantId, ct).ConfigureAwait(false);
            return;
        }

        var kvKey = tenantId is not null ? $"config/tenant:{tenantId}/{key}" : $"config/{key}";
        await connection.KvSetAsync(kvKey, value, ct: ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        if (UseFallback)
        {
            await _fallback.DeleteAsync(key, tenantId, ct).ConfigureAwait(false);
            return;
        }

        var kvKey = tenantId is not null ? $"config/tenant:{tenantId}/{key}" : $"config/{key}";
        await connection.KvDeleteAsync(kvKey, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     The key yielded when the client (re)registers: the Agent may have been written while this store
    ///     was not listening, so everything under the pattern may have changed. It names no setting.
    /// </summary>
    public const string ReconnectedKey = "*";

    /// <summary>
    ///     Streams changes to keys under <paramref name="keyPattern" /> (a prefix; a trailing <c>*</c> is
    ///     ignored, as in the in-memory store) — from the Agent and from the fallback alike.
    /// </summary>
    /// <remarks>
    ///     Both sources are listened to whatever the connection state when the watch starts: choosing
    ///     one by that state would make a watch taken during host build, before the connection opens,
    ///     pick the fallback and never hear the Agent. Each time the client registers (when the Agent starts answering its reads),
    ///     one change keyed
    ///     <see cref="ReconnectedKey" /> tells the watcher to read everything again.
    /// </remarks>
    public async IAsyncEnumerable<ConfigurationChange> WatchAsync(
        string keyPattern, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateBounded<ConfigurationChange>(new BoundedChannelOptions(128)
        {
            // A reload re-reads everything, so a dropped change is caught by the next one.
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        var prefix = $"config/{keyPattern.TrimEnd('*')}";

        connection.OnKvChanged += OnChange;
        connection.OnRegistered += OnRegistered;
        // Complete the writer when the caller cancels so ReadAllAsync drains and exits cleanly,
        // and any in-flight TryWrite becomes a harmless no-op instead of dangling.
        using var ctReg = ct.Register(static state => ((Channel<ConfigurationChange>)state!).Writer.TryComplete(), channel);
        // Its own token: a caller that stops enumerating without cancelling must still end the pump.
        using var fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var fromFallback = PumpFallbackAsync(keyPattern, channel.Writer, fallbackCts.Token);

        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return change;
        }
        finally
        {
            connection.OnKvChanged -= OnChange;
            connection.OnRegistered -= OnRegistered;
            channel.Writer.TryComplete();
            await fallbackCts.CancelAsync().ConfigureAwait(false);
            await fromFallback.ConfigureAwait(false);
        }

        void OnChange(KvChangedPayload payload)
        {
            if (!payload.Key.StartsWith(prefix, StringComparison.Ordinal))
                return;

            channel.Writer.TryWrite(new ConfigurationChange(
                payload.Key["config/".Length..],
                OldValue: null,
                NewValue: payload.Deleted ? null : payload.Value,
                TenantId: null,
                DateTimeOffset.UtcNow));
        }

        void OnRegistered()
            => channel.Writer.TryWrite(new ConfigurationChange(
                ReconnectedKey, OldValue: null, NewValue: null, TenantId: null, DateTimeOffset.UtcNow));
    }

    private async Task PumpFallbackAsync(string keyPattern, ChannelWriter<ConfigurationChange> writer, CancellationToken ct)
    {
        try
        {
            await foreach (var change in _fallback.WatchAsync(keyPattern, ct).ConfigureAwait(false))
                writer.TryWrite(change);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The watch ended.
        }
    }
}

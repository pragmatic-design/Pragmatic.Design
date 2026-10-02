using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.FeatureFlags;

namespace Pragmatic.Agent.Client.Stores;

/// <summary>
///     <see cref="IFeatureFlagStore"/> backed by the Agent KV store.
///     Flags stored as JSON in <c>flags/{name}</c>. Evaluation is done by the caller
///     (the Agent is a dumb KV store — it doesn't understand flag semantics).
/// </summary>
public sealed class AgentFeatureFlagStore(AgentConnection connection, IFeatureFlagStore? fallback = null)
    : IFeatureFlagStore
{
    public async Task<bool> IsEnabledAsync(string flagName, CancellationToken ct = default)
    {
        if (!connection.IsConnected && fallback is not null)
            return await fallback.IsEnabledAsync(flagName, ct).ConfigureAwait(false);

        var definition = await GetDefinitionAsync(flagName, ct).ConfigureAwait(false);
        return definition?.Enabled ?? false;
    }

    public async Task<bool> IsEnabledAsync(string flagName, FeatureFlagContext context, CancellationToken ct = default)
    {
        // When the Agent is down the store becomes a thin proxy to the fallback so
        // that seeded in-memory rule evaluation (tenant/user targeting) keeps working
        // — otherwise the dumb KV-only path would drop rule semantics entirely.
        if (!connection.IsConnected && fallback is not null)
            return await fallback.IsEnabledAsync(flagName, context, ct).ConfigureAwait(false);

        var definition = await GetDefinitionAsync(flagName, ct).ConfigureAwait(false);
        return definition?.Enabled ?? false;
        // Full rule evaluation requires FeatureFlagEvaluator from Pragmatic.FeatureFlags package
        // which is not a dependency of Agent.Client. Consumers should resolve IFeatureFlagStore
        // and use the evaluator from their own package.
    }

    public async Task<FeatureFlagDefinition?> GetDefinitionAsync(string flagName, CancellationToken ct = default)
    {
        if (!connection.IsConnected && fallback is not null)
            return await fallback.GetDefinitionAsync(flagName, ct).ConfigureAwait(false);

        var (value, _, found) = await connection.KvGetAsync($"flags/{flagName}", ct).ConfigureAwait(false);
        if (!found || value is null) return null;

        try
        {
            return JsonSerializer.Deserialize(value, AgentClientJsonContext.Default.FeatureFlagDefinition);
        }
        catch
        {
            return bool.TryParse(value, out var enabled)
                ? new FeatureFlagDefinition { Name = flagName, Enabled = enabled }
                : null;
        }
    }

    public async Task<IReadOnlyList<FeatureFlagDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        if (!connection.IsConnected && fallback is not null)
            return await fallback.GetAllAsync(ct).ConfigureAwait(false);

        var entries = await connection.KvPrefixAsync("flags/", ct).ConfigureAwait(false);
        var flags = new List<FeatureFlagDefinition>();

        foreach (var entry in entries)
        {
            if (entry.Value is null) continue;
            var name = entry.Key["flags/".Length..];

            try
            {
                var def = JsonSerializer.Deserialize(entry.Value, AgentClientJsonContext.Default.FeatureFlagDefinition);
                if (def is not null) { flags.Add(def with { Name = name }); continue; }
            }
            catch { /* Not JSON — try boolean */ }

            if (bool.TryParse(entry.Value, out var enabled))
                flags.Add(new FeatureFlagDefinition { Name = name, Enabled = enabled });
        }

        return flags;
    }

    public async IAsyncEnumerable<FeatureFlagChange> WatchAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateBounded<KvChangedPayload>(128);

        // Track the last-known enabled state per flag so WasEnabled reflects the real previous
        // value instead of a fabricated `!current`. On a DELETE the flag becomes absent, so the
        // current state is false (disabled/absent) — never derived from the stale payload value.
        var lastKnown = new Dictionary<string, bool>(StringComparer.Ordinal);

        connection.OnKvChanged += OnChange;
        // Complete the writer when the caller cancels so ReadAllAsync drains and exits cleanly,
        // and any in-flight OnChange TryWrite becomes a harmless no-op instead of dangling.
        using var ctReg = ct.Register(static state => ((Channel<KvChangedPayload>)state!).Writer.TryComplete(), channel);
        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                // Guard: key must have a flag name after "flags/" prefix
                if (change.Key.Length <= "flags/".Length)
                    continue;

                var name = change.Key["flags/".Length..];

                // A deleted entry means the flag is gone → current state is absent (disabled).
                var isEnabled = !change.Deleted
                    && change.Value is not null
                    && ParseEnabled(change.Value);

                var wasEnabled = lastKnown.TryGetValue(name, out var prev) && prev;

                if (change.Deleted)
                    lastKnown.Remove(name);
                else
                    lastKnown[name] = isEnabled;

                yield return new FeatureFlagChange(name, wasEnabled, isEnabled, DateTimeOffset.UtcNow);
            }
        }
        finally
        {
            connection.OnKvChanged -= OnChange;
            channel.Writer.TryComplete();
        }

        void OnChange(KvChangedPayload payload)
        {
            if (payload.Key.StartsWith("flags/", StringComparison.Ordinal))
                channel.Writer.TryWrite(payload);
        }
    }

    /// <summary>
    ///     Determines whether a raw KV value represents an enabled flag. Supports both the JSON
    ///     <see cref="FeatureFlagDefinition"/> shape and a bare boolean string.
    /// </summary>
    private static bool ParseEnabled(string value)
    {
        try
        {
            var def = JsonSerializer.Deserialize(value, AgentClientJsonContext.Default.FeatureFlagDefinition);
            if (def is not null) return def.Enabled;
        }
        catch (JsonException)
        {
            // Not JSON — fall through to boolean parse.
        }

        return bool.TryParse(value, out var enabled) && enabled;
    }
}

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.ControlPlane;

namespace Pragmatic.Agent.Client;

/// <summary>
///     Agent-backed <see cref="IClusterLeadership"/>. A leader claims <c>leader/{scope}</c> in the Agent
///     KV via compare-and-swap, writing <c>{hostId, leaseUntil}</c>, and renews the lease on a timer
///     (heartbeat interval &lt; lease TTL). Gossip replicates the key cluster-wide, so any host can steal
///     an expired lease. This is the soft, KV-lease election for advisory singleton work — never for
///     migrations (those keep the DB advisory lock).
/// </summary>
public sealed class AgentClusterLeadership : IClusterLeadership, IAsyncDisposable
{
    // 15 s renew / 45 s lease: a leader that misses two renewals in a row
    // loses the lease, matching the Agent heartbeat cadence.
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(15);

    private readonly AgentConnection _connection;
    private readonly string _hostId;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, LeaseState> _leases = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _renewalLock = new();
    private Task? _renewalLoop;

    public AgentClusterLeadership(AgentConnection connection, AgentOptions options, ILoggerFactory? loggerFactory = null)
    {
        _connection = connection;
        _hostId = options.AppId
            ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name
            ?? "app";
        _logger = loggerFactory?.CreateLogger<AgentClusterLeadership>() ?? NullLogger<AgentClusterLeadership>.Instance;
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireAsync(string scope, CancellationToken ct = default)
    {
        if (!_connection.IsConnected)
            return false;

        var key = Key(scope);
        var (value, version, _) = await _connection.KvGetAsync(key, ct).ConfigureAwait(false);

        var existing = Parse(value);
        var now = DateTimeOffset.UtcNow;

        // Decide the CAS baseline: claim if absent; renew if it's ours; steal if expired; else bail.
        var mine = existing is not null && existing.HostId == _hostId;
        var expired = existing is null || existing.LeaseUntil <= now;
        if (existing is not null && !mine && !expired)
            return false; // Held by a live foreign lease.

        var lease = new LeaseRecord { HostId = _hostId, LeaseUntil = now + LeaseTtl };
        var (newVersion, _) = await _connection
            .KvSetAsync(key, JsonSerializer.Serialize(lease), expectedVersion: version, ct: ct)
            .ConfigureAwait(false);

        if (newVersion < 0)
            return false; // CAS conflict (raced) or not connected.

        _leases[scope] = new LeaseState(newVersion, lease.LeaseUntil);
        EnsureRenewalLoop();
        return true;
    }

    /// <inheritdoc />
    public bool IsLeader(string scope)
        => _leases.TryGetValue(scope, out var state) && state.LeaseUntil > DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public async Task ReleaseAsync(string scope, CancellationToken ct = default)
    {
        if (!_leases.TryRemove(scope, out _))
            return; // Not ours — nothing to release.

        // Best-effort delete so a waiting host can claim immediately. If the connection is down the
        // lease simply expires on its own.
        if (_connection.IsConnected)
            await _connection.KvDeleteAsync(Key(scope), ct).ConfigureAwait(false);
    }

    private void EnsureRenewalLoop()
    {
        if (_renewalLoop is not null)
            return;

        lock (_renewalLock)
        {
            _renewalLoop ??= RenewLoopAsync(_cts.Token);
        }
    }

    private async Task RenewLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(RenewInterval, ct).ConfigureAwait(false);
                foreach (var scope in _leases.Keys)
                    await RenewAsync(scope, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — expected.
        }
    }

    private async Task RenewAsync(string scope, CancellationToken ct)
    {
        if (!_leases.TryGetValue(scope, out var state))
            return;

        if (!_connection.IsConnected)
        {
            // Can't renew while disconnected — drop leadership so a connected host can take over.
            LoseLeadership(scope, "connection lost");
            return;
        }

        var lease = new LeaseRecord { HostId = _hostId, LeaseUntil = DateTimeOffset.UtcNow + LeaseTtl };
        var (newVersion, _) = await _connection
            .KvSetAsync(Key(scope), JsonSerializer.Serialize(lease), expectedVersion: state.Version, ct: ct)
            .ConfigureAwait(false);

        if (newVersion < 0)
        {
            LoseLeadership(scope, "lease renewal lost (CAS conflict)");
            return;
        }

        _leases[scope] = new LeaseState(newVersion, lease.LeaseUntil);
    }

    private void LoseLeadership(string scope, string reason)
    {
        if (_leases.TryRemove(scope, out _))
            _logger.LogWarning("Lost cluster leadership for scope {Scope}: {Reason}", scope, reason);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);

        // Best-effort release of every scope we still hold so hand-off is prompt on graceful shutdown.
        foreach (var scope in _leases.Keys)
        {
            try { await ReleaseAsync(scope).ConfigureAwait(false); }
            catch { /* shutting down — ignore */ }
        }

        _cts.Dispose();
    }

    private static string Key(string scope) => $"leader/{scope}";

    private static LeaseRecord? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<LeaseRecord>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private readonly record struct LeaseState(long Version, DateTimeOffset LeaseUntil);

    private sealed class LeaseRecord
    {
        [JsonPropertyName("hostId")]
        public required string HostId { get; init; }

        [JsonPropertyName("leaseUntil")]
        public DateTimeOffset LeaseUntil { get; init; }
    }
}

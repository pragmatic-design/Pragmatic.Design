using System.Text.Json;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Agent.Client.Stores;

/// <summary>
///     <see cref="ITenantStore"/> backed by the Agent KV store.
///     Tenants stored as JSON in <c>tenants/{tenantId}</c>. Push-based via gossip.
/// </summary>
/// <remarks>
///     When the Agent daemon is unreachable this store degrades to a local fallback so tenant reads and
///     writes keep working in L0 mode — the same graceful-degradation contract the config and feature-flag
///     stores honor (P7 / CF-6: all Agent-backed <b>data</b> stores fall back consistently; the coordination
///     <c>IControlPlane</c> degrades internally instead). The fallback is the store registered before
///     <c>UseAgent()</c> replaced it, if any; otherwise a self-contained <see cref="InProcessTenantStore"/>.
/// </remarks>
public sealed class AgentTenantStore(AgentConnection connection, ITenantStore? fallback = null) : ITenantStore
{
    private const int CasMaxAttempts = 5;

    private readonly ITenantStore _fallback = fallback ?? new InProcessTenantStore();

    private bool UseFallback => !connection.IsConnected;

    public async Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
    {
        if (UseFallback)
            return await _fallback.GetByIdAsync(tenantId, ct).ConfigureAwait(false);

        var (value, _, found) = await connection.KvGetAsync($"tenants/{tenantId}", ct).ConfigureAwait(false);
        return found && value is not null ? Deserialize(value) : null;
    }

    public async Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
    {
        if (UseFallback)
            return await _fallback.GetAllAsync(ct).ConfigureAwait(false);

        var entries = await connection.KvPrefixAsync("tenants/", ct).ConfigureAwait(false);
        var tenants = new List<TenantInfo>();
        foreach (var entry in entries)
        {
            if (entry.Value is null) continue;
            var t = Deserialize(entry.Value);
            if (t is not null) tenants.Add(t);
        }
        return tenants;
    }

    public async Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct).ConfigureAwait(false);
        return all.Where(t => t.State == TenantState.Active).ToList();
    }

    /// <remarks>
    ///     Refuses a duplicate rather than overwriting, as <see cref="ITenantStore.CreateAsync"/>
    ///     requires. The read-then-write is not atomic against a concurrent create on another host —
    ///     the Agent KV has no create-if-absent — so this closes the ordinary case and not the race.
    ///     A second host creating the same tenant id in the same instant still wins silently; making
    ///     that impossible needs a compare-and-swap primitive the KV does not expose.
    /// </remarks>
    public async Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
    {
        if (UseFallback)
            return await _fallback.CreateAsync(tenant, ct).ConfigureAwait(false);

        if (await GetByIdAsync(tenant.TenantId, ct).ConfigureAwait(false) is not null)
            throw new InvalidOperationException($"A tenant with id '{tenant.TenantId}' already exists.");

        await connection.KvSetAsync($"tenants/{tenant.TenantId}", JsonSerializer.Serialize(tenant, AgentClientJsonContext.Default.TenantInfo), ct: ct).ConfigureAwait(false);
        return tenant;
    }

    public async Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
        => UseFallback
            ? await _fallback.UpdateAsync(tenant, ct).ConfigureAwait(false)
            : await CompareAndSwapAsync(tenant.TenantId, _ => tenant, ct).ConfigureAwait(false);

    public async Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
        => UseFallback
            ? await _fallback.DeactivateAsync(tenantId, ct).ConfigureAwait(false)
            : await CompareAndSwapAsync(
                tenantId,
                existing => existing with { State = TenantState.Deactivated },
                ct).ConfigureAwait(false);

    /// <summary>
    ///     Permanently removes the tenant entry from the Agent KV store. Irreversible — prefer
    ///     <see cref="DeactivateAsync"/> for a reversible soft transition. Returns false when no
    ///     tenant with the given id exists.
    /// </summary>
    public async Task<bool> DeleteAsync(string tenantId, CancellationToken ct = default)
    {
        if (UseFallback)
            return await _fallback.DeleteAsync(tenantId, ct).ConfigureAwait(false);

        var key = $"tenants/{tenantId}";

        // Guard on existence so the bool contract means "found and removed", not "delete acked".
        var (_, _, found) = await connection.KvGetAsync(key, ct).ConfigureAwait(false);
        if (!found)
            return false;

        return await connection.KvDeleteAsync(key, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Read-modify-write with optimistic concurrency: reads the current entry (and its version),
    ///     applies <paramref name="mutate"/>, and writes back guarded by the observed version.
    ///     On a CAS conflict (a concurrent writer won) the loop re-reads and retries, so concurrent
    ///     updates compose instead of silently clobbering each other. Returns false if the tenant
    ///     does not exist or the contention budget is exhausted.
    /// </summary>
    private async Task<bool> CompareAndSwapAsync(
        string tenantId, Func<TenantInfo, TenantInfo> mutate, CancellationToken ct)
    {
        var key = $"tenants/{tenantId}";

        for (var attempt = 0; attempt < CasMaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var (value, version, found) = await connection.KvGetAsync(key, ct).ConfigureAwait(false);
            if (!found || value is null)
                return false;

            var existing = Deserialize(value);
            if (existing is null)
                return false;

            var updated = mutate(existing);
            var (_, casConflict) = await connection
                .KvSetAsync(key, JsonSerializer.Serialize(updated, AgentClientJsonContext.Default.TenantInfo), expectedVersion: version, ct: ct)
                .ConfigureAwait(false);

            if (!casConflict)
                return true;
        }

        return false;
    }

    private static TenantInfo? Deserialize(string value)
    {
        try
        {
            return JsonSerializer.Deserialize(value, AgentClientJsonContext.Default.TenantInfo);
        }
        catch (JsonException)
        {
            // Malformed entry in the KV store — skip it rather than tearing down the whole
            // read. Surfaced via trace so a poisoned key is diagnosable instead of invisible.
            System.Diagnostics.Trace.TraceWarning(
                $"AgentTenantStore: failed to deserialize tenant payload: {value}");
            return null;
        }
    }
}

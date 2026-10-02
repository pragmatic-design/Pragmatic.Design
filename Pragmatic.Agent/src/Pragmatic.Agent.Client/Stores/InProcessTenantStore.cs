using System.Collections.Concurrent;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Agent.Client.Stores;

/// <summary>
///     Self-contained in-process <see cref="ITenantStore"/> used as the L0 fallback for
///     <see cref="AgentTenantStore"/> when no external tenant store was registered before
///     <c>UseAgent()</c>. Mirrors <see cref="InProcessConfigurationStore"/>: it keeps tenant reads and
///     writes working (against local state) while the Agent daemon is unreachable, instead of silently
///     returning empty — the same graceful-degradation contract the config and feature-flag stores honor.
/// </summary>
internal sealed class InProcessTenantStore : ITenantStore
{
    private readonly ConcurrentDictionary<string, TenantInfo> _tenants = new(StringComparer.Ordinal);

    public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
        => Task.FromResult(_tenants.TryGetValue(tenantId, out var t) ? t : null);

    public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TenantInfo>>(_tenants.Values.ToList());

    public async Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
        => (await GetAllAsync(ct).ConfigureAwait(false)).Where(t => t.State == TenantState.Active).ToList();

    /// <remarks>
    ///     Refuses a duplicate rather than overwriting, as <see cref="ITenantStore.CreateAsync"/>
    ///     requires. An indexer assignment made Create silently replace an existing tenant's
    ///     metadata with another's, which is the one thing a tenant store must not do quietly.
    /// </remarks>
    public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
    {
        if (!_tenants.TryAdd(tenant.TenantId, tenant))
            throw new InvalidOperationException($"A tenant with id '{tenant.TenantId}' already exists.");

        return Task.FromResult(tenant);
    }

    /// <remarks>
    ///     Reports whether the tenant existed, as the contract says. Assigning through the indexer
    ///     alone would always answer true, so an update to an unknown id would create it and claim to
    ///     have found it.
    /// </remarks>
    public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
    {
        if (!_tenants.ContainsKey(tenant.TenantId))
            return Task.FromResult(false);

        _tenants[tenant.TenantId] = tenant;
        return Task.FromResult(true);
    }

    public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
    {
        if (!_tenants.TryGetValue(tenantId, out var existing))
            return Task.FromResult(false);
        _tenants[tenantId] = existing with { State = TenantState.Deactivated };
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string tenantId, CancellationToken ct = default)
        => Task.FromResult(_tenants.TryRemove(tenantId, out _));
}

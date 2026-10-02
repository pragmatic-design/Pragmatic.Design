using System.Collections.Concurrent;

namespace Pragmatic.MultiTenancy;

/// <summary>
///     Default in-memory tenant store for development and single-tenant deployments.
///     Can be pre-seeded programmatically. For tenants kept in a database, register an
///     <see cref="ITenantStore" /> of your own over them — no database-backed store ships.
/// </summary>
public sealed class InMemoryTenantStore : ITenantStore
{
    private readonly ConcurrentDictionary<string, TenantInfo> _tenants = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Pre-seeds tenants (e.g. from configuration during startup).
    /// </summary>
    public void Seed(IEnumerable<TenantInfo> tenants)
    {
        foreach (var tenant in tenants)
            _tenants[tenant.TenantId] = tenant;
    }

    /// <inheritdoc />
    public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
    {
        _tenants.TryGetValue(tenantId, out var tenant);
        return Task.FromResult(tenant);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TenantInfo>>(_tenants.Values.ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TenantInfo>>(
            _tenants.Values.Where(t => t.State == TenantState.Active).ToList());

    /// <inheritdoc />
    public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
    {
        if (!_tenants.TryAdd(tenant.TenantId, tenant))
            throw new InvalidOperationException($"Tenant '{tenant.TenantId}' already exists.");

        return Task.FromResult(tenant);
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
    {
        // Use TryGetValue + TryUpdate to avoid the TOCTOU race of ContainsKey + indexer.
        // TryUpdate atomically replaces only if the current value matches the comparison value.
        if (!_tenants.TryGetValue(tenant.TenantId, out var existing))
            return Task.FromResult(false);

        // If another update raced and changed the value, keep retrying until we win or the key is gone.
        while (!_tenants.TryUpdate(tenant.TenantId, tenant, existing))
        {
            if (!_tenants.TryGetValue(tenant.TenantId, out existing))
                return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
    {
        if (!_tenants.TryGetValue(tenantId, out var existing))
            return Task.FromResult(false);

        _tenants[tenantId] = existing with { State = TenantState.Deactivated };
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string tenantId, CancellationToken ct = default)
        => Task.FromResult(_tenants.TryRemove(tenantId, out _));
}

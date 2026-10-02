using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Pragmatic.MultiTenancy.Persistence;
using Xunit;

namespace Pragmatic.MultiTenancy.Tests.Persistence;

/// <summary>
///     MT-H2: <see cref="TenantConnectionResolver"/> returns a tenant's dedicated connection string
///     (null when it has none — the tenant stays on the shared database), caches results, and rejects
///     unknown / deactivated tenants.
/// </summary>
public class TenantConnectionResolverTests
{
    [Fact]
    public async Task ResolveAsync_DedicatedConnection_ReturnsIt()
    {
        var store = new FakeTenantStore();
        await store.CreateAsync(Tenant("acme", connection: "Data Source=acme.db"));
        var resolver = new TenantConnectionResolver(store, new TenantDatabaseOptions());

        (await resolver.ResolveAsync("acme")).Should().Be("Data Source=acme.db");
    }

    [Fact]
    public async Task ResolveAsync_NoDedicatedConnection_ReturnsNull()
    {
        var store = new FakeTenantStore();
        await store.CreateAsync(Tenant("shared-tenant", connection: null));
        var resolver = new TenantConnectionResolver(store, new TenantDatabaseOptions());

        (await resolver.ResolveAsync("shared-tenant")).Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_UnknownTenant_Throws()
    {
        var resolver = new TenantConnectionResolver(new FakeTenantStore(), new TenantDatabaseOptions());

        var act = () => resolver.ResolveAsync("ghost").AsTask();

        await act.Should().ThrowAsync<TenantNotFoundException>();
    }

    [Fact]
    public async Task ResolveAsync_DeactivatedTenant_Throws()
    {
        var store = new FakeTenantStore();
        await store.CreateAsync(Tenant("gone", connection: "x", state: TenantState.Deactivated));
        var resolver = new TenantConnectionResolver(store, new TenantDatabaseOptions());

        var act = () => resolver.ResolveAsync("gone").AsTask();

        await act.Should().ThrowAsync<TenantDeactivatedException>();
    }

    [Fact]
    public async Task ResolveCached_ReflectsAPriorResolve()
    {
        var store = new FakeTenantStore();
        await store.CreateAsync(Tenant("acme", connection: "Data Source=acme.db"));
        var resolver = new TenantConnectionResolver(store, new TenantDatabaseOptions());

        resolver.ResolveCached("acme").Should().BeNull(); // not resolved yet
        await resolver.ResolveAsync("acme");
        resolver.ResolveCached("acme").Should().Be("Data Source=acme.db");
    }

    private static TenantInfo Tenant(string id, string? connection, TenantState state = TenantState.Active) => new()
    {
        TenantId = id,
        TenantName = id,
        State = state,
        CreatedAt = DateTimeOffset.UnixEpoch,
        ConnectionString = connection
    };

    private sealed class FakeTenantStore : ITenantStore
    {
        private readonly Dictionary<string, TenantInfo> _t = new();
        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_t.TryGetValue(tenantId, out var v) ? v : null);
        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>([.. _t.Values]);
        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default) => GetAllAsync(ct);
        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            _t[tenant.TenantId] = tenant;
            return Task.FromResult(tenant);
        }
        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default) { _t[tenant.TenantId] = tenant; return Task.FromResult(true); }
        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(_t.Remove(tenantId));
        public Task<bool> DeleteAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(_t.Remove(tenantId));
    }
}

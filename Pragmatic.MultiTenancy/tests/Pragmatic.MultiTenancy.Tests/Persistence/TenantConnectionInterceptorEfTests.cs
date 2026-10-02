using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy.Persistence;
using Xunit;

namespace Pragmatic.MultiTenancy.Tests.Persistence;

/// <summary>
///     MT-H2 end-to-end: <c>AddDbPerTenant</c> registers <see cref="TenantConnectionInterceptor"/>, EF Core
///     auto-discovers it from the service provider, and on connection open it rewrites the DbContext's
///     connection to the current tenant's dedicated database (and leaves shared-DB tenants untouched).
/// </summary>
public class TenantConnectionInterceptorEfTests
{
    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);

    private static ServiceProvider BuildProvider(string? currentTenantConnection, string? tenantId = "t1")
    {
        var store = new StubTenantStore();
        if (tenantId is not null)
            store.Add(new TenantInfo
            {
                TenantId = tenantId, TenantName = tenantId, State = TenantState.Active,
                CreatedAt = DateTimeOffset.UnixEpoch, ConnectionString = currentTenantConnection
            });

        var services = new ServiceCollection();
        services.AddSingleton<ITenantStore>(store);
        services.AddSingleton<ITenantContext>(new MutableTenantContext { TenantId = tenantId });
        services.AddDbPerTenant(o => o.DefaultConnectionString = "Data Source=file:shared?mode=memory&cache=shared");
        // Mirrors the SG-generated host registration: 2-arg AddDbContext that threads the DI-registered
        // interceptors (scoped) so the tenant connection interceptor is applied per request.
        services.AddDbContext<TestDbContext>((sp, o) =>
            o.UseSqlite("Data Source=file:shared?mode=memory&cache=shared")
             .AddInterceptors(sp.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>()));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DedicatedTenant_ConnectionIsRewrittenToTenantDatabase()
    {
        await using var sp = BuildProvider(currentTenantConnection: "Data Source=file:tenant1?mode=memory&cache=shared");
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        await ctx.Database.OpenConnectionAsync(); // triggers ConnectionOpeningAsync on the interceptor
        try
        {
            ctx.Database.GetDbConnection().ConnectionString.Should().Contain("tenant1",
                "the interceptor must route this tenant to its dedicated database");
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task SharedTenant_ConnectionKeepsSharedDatabase()
    {
        // Tenant resolved but with no dedicated connection → stays on the shared database.
        await using var sp = BuildProvider(currentTenantConnection: null);
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        await ctx.Database.OpenConnectionAsync();
        try
        {
            ctx.Database.GetDbConnection().ConnectionString.Should().Contain("shared");
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }
    }

    private sealed class StubTenantStore : ITenantStore
    {
        private readonly Dictionary<string, TenantInfo> _t = new();
        public void Add(TenantInfo t) => _t[t.TenantId] = t;
        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_t.TryGetValue(tenantId, out var v) ? v : null);
        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>([.. _t.Values]);
        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default) => GetAllAsync(ct);
        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default) { Add(tenant); return Task.FromResult(tenant); }
        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default) { Add(tenant); return Task.FromResult(true); }
        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(_t.Remove(tenantId));
        public Task<bool> DeleteAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(_t.Remove(tenantId));
    }
}

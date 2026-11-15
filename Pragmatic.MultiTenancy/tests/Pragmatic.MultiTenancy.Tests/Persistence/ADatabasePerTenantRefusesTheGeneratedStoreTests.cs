using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.MultiTenancy.Persistence;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.MultiTenancy.Tests.Persistence;

/// <summary>
///     A host on a database per tenant whose tenant store is the one generated over <c>[TenantSource]</c> refuses to start (#645).
/// </summary>
/// <remarks>
///     That store reads through the tenant-routed context, which asks the store which database to open: without the refusal the first
///     request would recurse. The database-per-tenant choice is made at run time, so the generator cannot see it.
/// </remarks>
public class ADatabasePerTenantRefusesTheGeneratedStoreTests
{
    private static ServiceProvider Container(ITenantStore inner)
        => new ServiceCollection()
            .AddLogging()
            .AddSingleton<ITenantStore>(new ObservedTenantStore(inner, []))
            .AddDbPerTenant(o =>
            {
                o.DefaultConnectionString = "Host=shared;Database=app";
                o.ConnectionStringTemplate = "Host=shared;Database=tenant_{0}";
            })
            .BuildServiceProvider();

    private static async Task StartAsync(ServiceProvider container)
    {
        foreach (var service in container.GetServices<IHostedService>())
            await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [Fact]
    public async Task TheGeneratedStore_StopsTheHostAtStartup_NamingTheEntity()
    {
        await using var container = Container(new GeneratedStore());

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(container));

        refused.Message.Should().Contain("[TenantSource] on Company").And.Contain("AddDbPerTenant");
    }

    /// <summary>The control: a store the application wrote, as Casework's, starts.</summary>
    [Fact]
    public async Task AStoreTheApplicationWrote_Starts()
    {
        await using var container = Container(new InMemoryTenantStore());

        await StartAsync(container);
    }

    private sealed class Company;

    private sealed class GeneratedStore : IEntityTenantStore
    {
        public Type EntityType => typeof(Company);

        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default) => Task.FromResult<TenantInfo?>(null);

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TenantInfo>>([]);

        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TenantInfo>>([]);

        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.EFCore.Repository;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Repository;

/// <summary>
///     A tenant store that reads the database is scoped, and the lookups' preload runs from the root
///     provider at startup: the host has to start anyway, and preload every tenant the store knows.
/// </summary>
/// <remarks>
///     The hosted service is resolved and started the way the host does it, from the root provider of a
///     container that validates scopes, as a host does in Development.
/// </remarks>
public class AScopedTenantStoreAtStartupTests
{
    [Fact]
    public async Task TheHostStarts_AndPreloadsEachTenant()
    {
        var loader = new RecordingLoader();
        await using var services = Container(loader, s => s.AddScoped<ITenantStore>(_ => new StubTenantStore("tenant-a", "tenant-b")));

        await StartHostedServices(services).ConfigureAwait(true);

        loader.Tenants.Should().BeEquivalentTo(["tenant-a", "tenant-b"]);
    }

    /// <summary>The control: a singleton store, the shape the preload was written against, still works.</summary>
    [Fact]
    public async Task ASingletonStore_IsReadAsBefore()
    {
        var loader = new RecordingLoader();
        await using var services = Container(loader, s => s.AddSingleton<ITenantStore>(new StubTenantStore("tenant-a")));

        await StartHostedServices(services).ConfigureAwait(true);

        loader.Tenants.Should().BeEquivalentTo(["tenant-a"]);
    }

    /// <summary>The scope the store was read in does not outlive the read.</summary>
    [Fact]
    public async Task TheScopedStore_IsDisposedAfterTheRead()
    {
        var loader = new RecordingLoader();
        var store = new StubTenantStore("tenant-a");
        await using var services = Container(loader, s => s.AddScoped<ITenantStore>(_ => store));

        await StartHostedServices(services).ConfigureAwait(true);

        store.Disposed.Should().BeTrue();
    }

    private static ServiceProvider Container(RecordingLoader loader, Action<IServiceCollection> registerStore)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        registerStore(services);
        services.AddSingleton<ILookupCacheLoader>(loader);
        services.AddHostedService<LookupPreloadHostedService>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static async Task StartHostedServices(ServiceProvider root)
    {
        foreach (var hosted in root.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>What the generated loader does with the tenants, recorded instead of read.</summary>
    private sealed class RecordingLoader : ILookupCacheLoader
    {
        public List<string?> Tenants { get; } = [];

        public async Task LoadAsync(IServiceProvider serviceProvider, CancellationToken ct)
        {
            var tenants = await LookupPreload
                .TenantsToLoadAsync(serviceProvider, tenantScoped: true, "Region", ct)
                .ConfigureAwait(false);
            Tenants.AddRange(tenants);
        }

        public Task LoadForTenantAsync(IServiceProvider serviceProvider, string tenantId, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class StubTenantStore(params string[] active) : ITenantStore, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;

        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>(
                [.. active.Select(id => new TenantInfo
                {
                    TenantId = id,
                    TenantName = id,
                    State = TenantState.Active,
                    CreatedAt = DateTimeOffset.UnixEpoch
                })]);

        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<TenantInfo?>(null);

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
            => GetActiveAsync(ct);

        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
            => Task.FromResult(tenant);

        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(false);
    }
}

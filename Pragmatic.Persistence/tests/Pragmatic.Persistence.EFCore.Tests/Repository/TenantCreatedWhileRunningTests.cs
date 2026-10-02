using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.EFCore.Repository;
using Pragmatic.Persistence.Entity;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Repository;

/// <summary>
///     The whole chain, from a tenant created while the process runs to a lookup that answers for it.
/// </summary>
/// <remarks>
///     <para>
///         This is the question the issue asks, phrased as it asks it: create a tenant while the host is
///         running, and the thing derived from the set of tenants covers it without a restart; deactivate
///         one, and what was derived for it goes away. Everything between the two ends is real — an
///         ordinary tenant store behind <see cref="ObservedTenantStore" />, the observer resolved from
///         the container as the generated registration registers it, a SQLite database, and rows that
///         are only visible under their own tenant.
///     </para>
///     <para>
///         ⚠️ The one substitution is the loader. A generated <c>ILookupCacheLoader</c> exists only
///         inside a compilation the generator has run on, so this one is written by hand and does what
///         the template emits: read the set under the declared tenant, and register the cache with the
///         resolver. What that means is that this case proves the <b>signal and the reaction</b>, and
///         the generated body is covered as text (<c>LookupCacheLoaderTemplateTests</c>) and, for the
///         shared branch, by the Showcase. No application declares a tenant-scoped <c>[Lookup]</c> yet.
///     </para>
/// </remarks>
[Collection(LookupResolverCollection.Name)]
public sealed class TenantCreatedWhileRunningTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        LookupResolver.Reset();
        await _connection.OpenAsync().ConfigureAwait(true);

        var services = new ServiceCollection();
        services.AddSingleton(_connection);
        services.AddDbContext<RegionContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<ILookupCacheLoader, RegionLookupLoader>();
        services.AddSingleton<ITenantLifecycleObserver, LookupCacheTenantObserver>();
        services.AddSingleton<ITenantStore>(sp => new ObservedTenantStore(
            new MutableTenantStore(),
            sp.GetServices<ITenantLifecycleObserver>()));

        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegionContext>();
        await db.Database.EnsureCreatedAsync().ConfigureAwait(true);
        db.Regions.AddRange(
            new Region { Id = 1, Name = "North (A)", TenantId = "tenant-a" },
            new Region { Id = 1, Name = "North (B)", TenantId = "tenant-b" });
        await db.SaveChangesAsync().ConfigureAwait(true);
    }

    public async Task DisposeAsync()
    {
        LookupResolver.Reset();
        await _services.DisposeAsync().ConfigureAwait(true);
        await _connection.DisposeAsync().ConfigureAwait(true);
    }

    /// <summary>
    ///     A tenant created after the preload has run reads its own lookup rows, without a restart.
    /// </summary>
    [Fact]
    public async Task ATenantCreatedAfterStartup_ReadsItsOwnLookupRows()
    {
        var store = _services.GetRequiredService<ITenantStore>();
        await Preload(store).ConfigureAwait(true);

        await store.CreateAsync(Tenant("tenant-b")).ConfigureAwait(true);

        using (TenantScope.BeginScope("tenant-b"))
            LookupResolver.Get<Region, int>(1).Name.Should().Be("North (B)");
    }

    /// <summary>
    ///     The control: without the signal, the same tenant reads nothing — which is the defect.
    /// </summary>
    /// <remarks>
    ///     The store is used undecorated here, so the create happens exactly as it did before and the
    ///     preload's view of the tenants is the one it had at startup. Without this case, the one above
    ///     is satisfied by a resolver that answers for any tenant at all.
    /// </remarks>
    [Fact]
    public async Task WithoutTheSignal_TheSameTenantReadsNothing()
    {
        var store = new MutableTenantStore();
        await Preload(store).ConfigureAwait(true);

        await store.CreateAsync(Tenant("tenant-b")).ConfigureAwait(true);

        using (TenantScope.BeginScope("tenant-b"))
            LookupResolver.TryGet<Region, int>(1, out _).Should().BeFalse();
    }

    /// <summary>And the reverse: a tenant that is deactivated loses what was derived for it.</summary>
    [Fact]
    public async Task ATenantDeactivated_LosesItsLookupRows()
    {
        var store = _services.GetRequiredService<ITenantStore>();
        await store.CreateAsync(Tenant("tenant-a")).ConfigureAwait(true);
        await Preload(store).ConfigureAwait(true);

        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.Get<Region, int>(1).Name.Should().Be("North (A)");

        await store.DeactivateAsync("tenant-a").ConfigureAwait(true);

        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.TryGet<Region, int>(1, out _).Should().BeFalse();
    }

    /// <summary>Startup, as the hosted service performs it: every tenant the store knows.</summary>
    private async Task Preload(ITenantStore store)
    {
        var probe = new ServiceCollection();
        probe.AddSingleton(store);
        using var provider = probe.BuildServiceProvider();

        await new RegionLookupLoader(_services).LoadAsync(provider, CancellationToken.None).ConfigureAwait(true);
    }

    private static TenantInfo Tenant(string id) => new()
    {
        TenantId = id,
        TenantName = id,
        State = TenantState.Active,
        CreatedAt = DateTimeOffset.UnixEpoch
    };

    /// <summary>What the generated loader does, written out.</summary>
    private sealed class RegionLookupLoader(IServiceProvider root) : ILookupCacheLoader
    {
        public async Task LoadAsync(IServiceProvider serviceProvider, CancellationToken ct)
        {
            var tenants = await LookupPreload
                .TenantsToLoadAsync(serviceProvider, tenantScoped: true, "Region", ct)
                .ConfigureAwait(false);

            foreach (var tenantId in tenants)
                await LoadOneAsync(tenantId, ct).ConfigureAwait(false);
        }

        public Task LoadForTenantAsync(IServiceProvider serviceProvider, string tenantId, CancellationToken ct)
            => LoadOneAsync(tenantId, ct);

        private async Task LoadOneAsync(string? tenantId, CancellationToken ct)
        {
            using var tenantScope = tenantId is null ? null : TenantScope.BeginScope(tenantId);
            using var scope = root.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RegionContext>();
            var items = await db.Regions.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

            var cache = new LookupCache<Region, int>();
            cache.Load(items, r => r.Id);
            LookupResolver.Register<Region, int>(cache);
        }
    }

    private sealed class RegionContext(DbContextOptions<RegionContext> options) : DbContext(options)
    {
        // Read once, when the context is created — which happens inside the tenant scope the loader
        // declares. This is EF Core's own row-level tenancy shape: the filter closes over an instance
        // field, so it is re-parameterised per context rather than baked into the cached model.
        private readonly string? _tenantId = new TenantScope().TenantId;

        public DbSet<Region> Regions => Set<Region>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var region = modelBuilder.Entity<Region>();
            region.HasKey(r => new { r.Id, r.TenantId });
            // The row-level filter the tenant-aware persistence layer applies, and the reason a
            // tenant-scoped lookup cannot be preloaded once: read with no tenant it matches nothing.
            region.HasQueryFilter(r => r.TenantId == _tenantId);
        }
    }

    /// <summary>An ordinary tenant store: no signal of its own, which is the point.</summary>
    private sealed class MutableTenantStore : ITenantStore
    {
        private readonly List<TenantInfo> _tenants = [];

        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_tenants.Find(t => t.TenantId == tenantId));

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>([.. _tenants]);

        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>(
                [.. _tenants.Where(t => t.State == TenantState.Active)]);

        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            _tenants.Add(tenant);
            return Task.FromResult(tenant);
        }

        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            var index = _tenants.FindIndex(t => t.TenantId == tenant.TenantId);
            if (index < 0)
                return Task.FromResult(false);

            _tenants[index] = tenant;
            return Task.FromResult(true);
        }

        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
        {
            var index = _tenants.FindIndex(t => t.TenantId == tenantId);
            if (index < 0)
                return Task.FromResult(false);

            _tenants[index] = _tenants[index] with { State = TenantState.Deactivated };
            return Task.FromResult(true);
        }
    }

    private sealed class Region
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string TenantId { get; set; } = "";
    }
}

#pragma warning disable CA2007 // xUnit manages SynchronizationContext

using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Saga;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Messaging.Tests.EFCore;

/// <summary>
///     Proves row-level tenant isolation of <see cref="EfCoreSagaRepository{TSaga,TState}"/>:
///     the repository stamps the ambient tenant on create, tenant-scoped reads honour a fail-closed
///     EF query filter (mirroring the generated boundary DbContext), background scans cross tenants,
///     and the tenant-inclusive unique index lets two tenants run the same (SagaType, CorrelationId).
/// </summary>
public class SagaTenantIsolationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        // Create the schema once on a shared in-memory DB; each "request" gets its own context/tenant.
        await using var seed = NewContext("__seed__");
        await seed.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private TenantAwareSagaDbContext NewContext(string? tenantId)
    {
        var options = new DbContextOptionsBuilder<TenantAwareSagaDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new TenantAwareSagaDbContext(options, new FixedTenantContext(tenantId));
    }

    private static EfCoreSagaRepository<OrderSaga, OrderSagaState> Repo(DbContext ctx, string? tenantId) =>
        new(ctx, NullLogger<EfCoreSagaRepository<OrderSaga, OrderSagaState>>.Instance, null,
            new FixedTenantContext(tenantId));

    private static OrderSaga NewSaga(string correlationId) => new()
    {
        Id = Guid.NewGuid(),
        State = OrderSagaState.Created,
        CorrelationId = correlationId,
        StartedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task SaveAsync_StampsAmbientTenantOnCreate()
    {
        await using var ctx = NewContext("tenant-a");
        await Repo(ctx, "tenant-a").SaveAsync(NewSaga("order-1"));

        var row = await ctx.SagaInstances.IgnoreQueryFilters().SingleAsync();
        row.TenantId.Should().Be("tenant-a");
    }

    [Fact]
    public async Task FindByCorrelation_DoesNotLeakAcrossTenants()
    {
        await using (var a = NewContext("tenant-a"))
            await Repo(a, "tenant-a").SaveAsync(NewSaga("order-1"));

        // Tenant B must not see tenant A's saga (fail-closed tenant filter).
        await using var b = NewContext("tenant-b");
        var seenByB = await Repo(b, "tenant-b").FindByCorrelationAsync("order-1");
        seenByB.Should().BeNull();

        await using var a2 = NewContext("tenant-a");
        var seenByA = await Repo(a2, "tenant-a").FindByCorrelationAsync("order-1");
        seenByA.Should().NotBeNull();
    }

    [Fact]
    public async Task TwoTenants_SameCorrelation_DoNotCollideOnUniqueIndex()
    {
        // The active-saga unique index keys on TenantId, so the same (SagaType, CorrelationId) can be
        // active for two tenants — an index on (SagaType, CorrelationId) alone would reject the second.
        await using (var a = NewContext("tenant-a"))
            await Repo(a, "tenant-a").SaveAsync(NewSaga("order-shared"));

        await using var b = NewContext("tenant-b");
        var act = async () => await Repo(b, "tenant-b").SaveAsync(NewSaga("order-shared"));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetActive_IsCrossTenant_ForBackgroundOrchestration()
    {
        await using (var a = NewContext("tenant-a"))
            await Repo(a, "tenant-a").SaveAsync(NewSaga("order-a"));
        await using (var b = NewContext("tenant-b"))
            await Repo(b, "tenant-b").SaveAsync(NewSaga("order-b"));

        // The timeout orchestrator has no ambient tenant and must see every tenant's active sagas.
        await using var background = NewContext(tenantId: null);
        var active = await Repo(background, tenantId: null).GetActiveAsync();
        active.Should().HaveCount(2);
    }
}

/// <summary>Fixed <see cref="ITenantContext"/> for tests — resolved when a non-empty id is given.</summary>
internal sealed class FixedTenantContext(string? tenantId) : ITenantContext
{
    public string? TenantId => tenantId;
    public string? TenantName => null;
    public bool IsResolved => !string.IsNullOrEmpty(tenantId);
}

/// <summary>
///     Test DbContext that mirrors the generated boundary DbContext's fail-closed "Tenant" query
///     filter on <see cref="SagaInstance"/> (the SG emits it only for multi-tenant hosts).
/// </summary>
internal sealed class TenantAwareSagaDbContext(
    DbContextOptions<TenantAwareSagaDbContext> options,
    ITenantContext tenantContext) : DbContext(options)
{
    private readonly ITenantContext _tenantContext = tenantContext;

    public DbSet<SagaInstance> SagaInstances => Set<SagaInstance>();
    public DbSet<SagaStep> SagaSteps => Set<SagaStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var config = new SagaEntityTypeConfiguration();
        config.Configure(modelBuilder.Entity<SagaInstance>());
        config.Configure(modelBuilder.Entity<SagaStep>());

        modelBuilder.Entity<SagaInstance>().HasQueryFilter("Tenant",
            e => _tenantContext.TenantId != null && e.TenantId == _tenantContext.TenantId);
    }
}

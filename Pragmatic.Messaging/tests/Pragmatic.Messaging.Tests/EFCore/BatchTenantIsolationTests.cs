#pragma warning disable CA2007 // xUnit manages SynchronizationContext

using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Batch;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Messaging.Tests.EFCore;

/// <summary>
///     Proves row-level tenant isolation of <see cref="EfCoreBatchProgressStore"/>: the store
///     stamps the ambient tenant on create, per-batch reads honour a fail-closed EF query filter
///     (mirroring the generated boundary DbContext), and the monitoring scan crosses tenants.
/// </summary>
public class BatchTenantIsolationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        await using var seed = NewContext("__seed__");
        await seed.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private TenantAwareBatchDbContext NewContext(string? tenantId)
    {
        var options = new DbContextOptionsBuilder<TenantAwareBatchDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new TenantAwareBatchDbContext(options, new FixedTenantContext(tenantId));
    }

    private static EfCoreBatchProgressStore Store(DbContext ctx, string? tenantId) =>
        new(ctx, NullLogger<EfCoreBatchProgressStore>.Instance, new FixedTenantContext(tenantId));

    private static BatchProgress NewBatch() => new()
    {
        BatchId = Guid.NewGuid(),
        Total = 3,
        StartedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task CreateAsync_StampsAmbientTenant()
    {
        var batch = NewBatch();
        await using var ctx = NewContext("tenant-a");
        await Store(ctx, "tenant-a").CreateAsync(batch);

        var row = await ctx.BatchProgress.IgnoreQueryFilters().SingleAsync();
        row.TenantId.Should().Be("tenant-a");
    }

    [Fact]
    public async Task GetProgress_DoesNotLeakAcrossTenants()
    {
        var batch = NewBatch();
        await using (var a = NewContext("tenant-a"))
            await Store(a, "tenant-a").CreateAsync(batch);

        await using var b = NewContext("tenant-b");
        (await Store(b, "tenant-b").GetProgressAsync(batch.BatchId)).Should().BeNull();

        await using var a2 = NewContext("tenant-a");
        (await Store(a2, "tenant-a").GetProgressAsync(batch.BatchId)).Should().NotBeNull();
    }

    [Fact]
    public async Task GetActive_IsCrossTenant_ForMonitoring()
    {
        await using (var a = NewContext("tenant-a"))
            await Store(a, "tenant-a").CreateAsync(NewBatch());
        await using (var b = NewContext("tenant-b"))
            await Store(b, "tenant-b").CreateAsync(NewBatch());

        await using var monitor = NewContext(tenantId: null);
        var active = await Store(monitor, tenantId: null).GetActiveAsync();
        active.Should().HaveCount(2);
    }
}

/// <summary>
///     Test DbContext mirroring the generated boundary DbContext's fail-closed "Tenant" query filter on
///     <see cref="BatchProgress"/> (the SG emits it only for multi-tenant hosts).
/// </summary>
internal sealed class TenantAwareBatchDbContext(
    DbContextOptions<TenantAwareBatchDbContext> options,
    ITenantContext tenantContext) : DbContext(options)
{
    private readonly ITenantContext _tenantContext = tenantContext;

    public DbSet<BatchProgress> BatchProgress => Set<BatchProgress>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new BatchProgressEntityTypeConfiguration().Configure(modelBuilder.Entity<BatchProgress>());

        modelBuilder.Entity<BatchProgress>().HasQueryFilter("Tenant",
            e => _tenantContext.TenantId != null && e.TenantId == _tenantContext.TenantId);
    }
}

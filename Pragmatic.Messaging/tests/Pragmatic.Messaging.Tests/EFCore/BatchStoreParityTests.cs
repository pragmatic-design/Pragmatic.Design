#pragma warning disable CA2007 // xUnit manages SynchronizationContext

using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Batch;

namespace Pragmatic.Messaging.Tests.EFCore;

/// <summary>
///     The EF and in-memory batch stores must agree on "active". A zero-item batch is complete at
///     creation, and no increment ever fires on it, so both stores stamp CompletedAt in CreateAsync;
///     a store that left it null would report the batch active forever.
/// </summary>
public class BatchStoreParityTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TestBatchDbContext _ctx = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _ctx = new TestBatchDbContext(
            new DbContextOptionsBuilder<TestBatchDbContext>().UseSqlite(_connection).Options);
        await _ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ZeroItemBatch_NotActive_InBothStores()
    {
        var ef = new EfCoreBatchProgressStore(_ctx, NullLogger<EfCoreBatchProgressStore>.Instance);
        var mem = new InMemoryBatchProgressStore();

        await ef.CreateAsync(new BatchProgress { BatchId = Guid.NewGuid(), Total = 0, StartedAt = DateTimeOffset.UtcNow });
        await mem.CreateAsync(new BatchProgress { BatchId = Guid.NewGuid(), Total = 0, StartedAt = DateTimeOffset.UtcNow });

        (await ef.GetActiveAsync()).Should().BeEmpty("EF: a zero-item batch is complete at creation");
        (await mem.GetActiveAsync()).Should().BeEmpty("in-memory: same canonical semantics");
    }

    [Fact]
    public async Task NonEmptyBatch_IsActiveUntilComplete_InEfStore()
    {
        var ef = new EfCoreBatchProgressStore(_ctx, NullLogger<EfCoreBatchProgressStore>.Instance);
        var batchId = Guid.NewGuid();
        await ef.CreateAsync(new BatchProgress { BatchId = batchId, Total = 2, StartedAt = DateTimeOffset.UtcNow });

        (await ef.GetActiveAsync()).Should().ContainSingle();

        await ef.IncrementCompletedAsync(batchId);
        await ef.IncrementCompletedAsync(batchId);

        (await ef.GetActiveAsync()).Should().BeEmpty("both items done → CompletedAt stamped");
    }
}

internal sealed class TestBatchDbContext(DbContextOptions<TestBatchDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => new BatchProgressEntityTypeConfiguration().Configure(modelBuilder.Entity<BatchProgress>());
}

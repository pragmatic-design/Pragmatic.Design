#pragma warning disable CA2007 // xUnit manages SynchronizationContext

using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.EFCore;
using Pragmatic.Messaging.EFCore.Entities;
using Pragmatic.Messaging.Entities;

namespace Pragmatic.Messaging.Tests.EFCore;

/// <summary>
///     Test DbContext for messaging infrastructure tables (outbox, idempotency, audit).
///     Extends <see cref="MessagingDbContext"/> so it is accepted by the EF Core-backed stores
///     which requires an isolated messaging context (not the ambient business DbContext).
///     Uses SQLite in-memory for relational feature support (ExecuteUpdateAsync, etc.).
/// </summary>
public class TestMessagingDbContext(DbContextOptions<MessagingDbContext> options) : MessagingDbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        new OutboxEntityTypeConfiguration().Configure(modelBuilder.Entity<OutboxMessage>());
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// EfCoreIdempotencyStore tests
// ─────────────────────────────────────────────────────────────────────────────

public class EfCoreIdempotencyStoreTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TestMessagingDbContext _dbContext = null!;
    private EfCoreIdempotencyStore _store = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new TestMessagingDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _store = new EfCoreIdempotencyStore(_dbContext, NullLogger<EfCoreIdempotencyStore>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task TryMarkAsProcessedAsync_NewMessage_ReturnsTrue()
    {
        var messageId = Guid.NewGuid().ToString();

        var result = await _store.TryMarkAsProcessedAsync(messageId);

        result.Should().BeTrue();

        var record = await _dbContext.IdempotencyRecords.SingleAsync();
        record.MessageId.Should().Be(messageId);
        record.ProcessedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TryMarkAsProcessedAsync_DuplicateMessage_ReturnsFalse()
    {
        var messageId = Guid.NewGuid().ToString();

        var first = await _store.TryMarkAsProcessedAsync(messageId);
        var second = await _store.TryMarkAsProcessedAsync(messageId);

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task TryMarkAsProcessedAsync_DifferentMessages_BothReturnTrue()
    {
        var result1 = await _store.TryMarkAsProcessedAsync("msg-1");
        var result2 = await _store.TryMarkAsProcessedAsync("msg-2");

        result1.Should().BeTrue();
        result2.Should().BeTrue();

        var count = await _dbContext.IdempotencyRecords.CountAsync();
        count.Should().Be(2);
    }

    [Fact]
    public async Task PurgeOlderThanAsync_RemovesOldRecords()
    {
        // Arrange: manually insert old records
        _dbContext.IdempotencyRecords.Add(new IdempotencyRecord
        {
            MessageId = "old-msg",
            ProcessedAt = DateTimeOffset.UtcNow.AddDays(-10)
        });
        _dbContext.IdempotencyRecords.Add(new IdempotencyRecord
        {
            MessageId = "recent-msg",
            ProcessedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        await _store.PurgeOlderThanAsync(TimeSpan.FromDays(5));

        // Assert
        var remaining = await _dbContext.IdempotencyRecords.ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].MessageId.Should().Be("recent-msg");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// EfCoreOutboxSource tests.
// SQLite, not InMemory: the atomic claim is an ExecuteUpdateAsync, which InMemory cannot run.
// SQLite can, because OutboxEntityTypeConfiguration stores every timestamp as UTC ticks
// — so the claim, the mark and the release are exercised here on a relational provider.
// ─────────────────────────────────────────────────────────────────────────────

public class EfCoreOutboxSourceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TestMessagingDbContext _dbContext = null!;
    private EfCoreOutboxSource _store = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new TestMessagingDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _store = new EfCoreOutboxSource(
            _dbContext,
            "TestBoundary",
            NullLogger<EfCoreOutboxSource>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static OutboxMessage CreateOutboxMessage(
        DateTimeOffset? createdAt = null,
        DateTimeOffset? processedAt = null,
        string? error = null)
        => new()
        {
            Id = Guid.NewGuid(),
            MessageType = "TestEvent",
            Payload = """{"data":"test"}""",
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            ProcessedAt = processedAt,
            Error = error
        };

    // PeekPendingAsync is read-only, unlike GetPendingAsync, and must NOT lease the rows it
    // returns.
    [Fact]
    public async Task PeekPendingAsync_ReturnsUnprocessed_WithoutClaiming()
    {
        var pending = CreateOutboxMessage(createdAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var processed = CreateOutboxMessage(processedAt: DateTimeOffset.UtcNow);
        _dbContext.Set<OutboxMessage>().AddRange(pending, processed);
        await _dbContext.SaveChangesAsync();

        var result = await _store.PeekPendingAsync(10);

        result.Should().ContainSingle().Which.Id.Should().Be(pending.Id);

        // The pending row must remain unclaimed so the delivery pump can still grab it.
        var row = await _dbContext.Set<OutboxMessage>().AsNoTracking().FirstAsync(m => m.Id == pending.Id);
        row.ClaimedBy.Should().BeNull();
        row.ClaimedUntil.Should().BeNull();
    }

    [Fact]
    public void BoundaryName_ReturnsConfiguredName()
    {
        _store.BoundaryName.Should().Be("TestBoundary");
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsUnprocessedMessages()
    {
        var pending1 = CreateOutboxMessage(createdAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        var pending2 = CreateOutboxMessage(createdAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var processed = CreateOutboxMessage(processedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<OutboxMessage>().AddRange(pending1, pending2, processed);
        await _dbContext.SaveChangesAsync();

        var result = await _store.GetPendingAsync(10);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(m => m.ProcessedAt == null);
    }

    [Fact]
    public async Task GetPendingAsync_OrdersByCreatedAt()
    {
        var older = CreateOutboxMessage(createdAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = CreateOutboxMessage(createdAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        _dbContext.Set<OutboxMessage>().AddRange(newer, older);
        await _dbContext.SaveChangesAsync();

        var result = await _store.GetPendingAsync(10);

        result.Should().HaveCount(2);
        result[0].CreatedAt.Should().BeBefore(result[1].CreatedAt);
    }

    [Fact]
    public async Task GetPendingAsync_RespectsBatchSize()
    {
        for (var i = 0; i < 5; i++)
            _dbContext.Set<OutboxMessage>().Add(CreateOutboxMessage());
        await _dbContext.SaveChangesAsync();

        var result = await _store.GetPendingAsync(3);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetPendingAsync_EmptyTable_ReturnsEmpty()
    {
        var result = await _store.GetPendingAsync(10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkProcessedAsync_SetsProcessedAt()
    {
        var msg = CreateOutboxMessage();
        _dbContext.Set<OutboxMessage>().Add(msg);
        await _dbContext.SaveChangesAsync();

        await _store.MarkProcessedAsync(msg.Id);

        var updated = await _dbContext.Set<OutboxMessage>().AsNoTracking()
            .SingleAsync(m => m.Id == msg.Id);
        updated.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkFailedAsync_SetsErrorIncrementsRetryAndReleasesClaim()
    {
        var msg = CreateOutboxMessage();
        msg.ClaimedBy = "worker-1";
        msg.ClaimedUntil = DateTimeOffset.UtcNow.AddMinutes(5);
        _dbContext.Set<OutboxMessage>().Add(msg);
        await _dbContext.SaveChangesAsync();

        await _store.MarkFailedAsync(msg.Id, "Transport error");

        var updated = await _dbContext.Set<OutboxMessage>().AsNoTracking()
            .SingleAsync(m => m.Id == msg.Id);
        updated.Error.Should().Be("Transport error");
        updated.RetryCount.Should().Be(1);
        updated.NextAttemptAt.Should().NotBeNull(); // backoff scheduled
        updated.ClaimedBy.Should().BeNull();          // claim released for re-grab
        updated.ClaimedUntil.Should().BeNull();
    }
}

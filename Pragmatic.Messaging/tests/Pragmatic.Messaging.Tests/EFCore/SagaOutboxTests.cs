#pragma warning disable CA2007 // xUnit manages SynchronizationContext

using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Entities;
using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Tests.EFCore;

/// <summary>
///     Saga transactional outbox: a durable saga whose boundary also maps <c>__OutboxMessages</c>
///     ([EnableOutbox]) commits the step's resulting action to the outbox in the SAME transaction as the
///     state, so a transport failure after the state commit cannot lose the action — it is delivered
///     exactly-once by the pump. This closes the save-before-publish dual-write window. When no outbox is
///     mapped the repository reports false so the orchestrator publishes inline (save-before-publish).
/// </summary>
public sealed class SagaOutboxTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static EfCoreSagaRepository<OrderSaga, OrderSagaState> Repo(DbContext ctx) =>
        new(ctx, NullLogger<EfCoreSagaRepository<OrderSaga, OrderSagaState>>.Instance);

    private static OrderSaga NewSaga() => new()
    {
        Id = Guid.NewGuid(),
        State = OrderSagaState.Created,
        CorrelationId = "order-1",
        StartedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task SaveWithStepAndOutbox_WhenOutboxMapped_CommitsActionDurablyAndReportsDelivered()
    {
        await using var ctx = new SagaWithOutboxDbContext(
            new DbContextOptionsBuilder<SagaWithOutboxDbContext>().UseSqlite(_connection).Options);
        await ctx.Database.EnsureCreatedAsync();

        var delivered = await Repo(ctx).SaveWithStepAndOutboxAsync(
            NewSaga(), timeoutAt: null, executedStepName: "Start",
            pendingMessages: [new CheckInCommand(Guid.NewGuid())]);

        delivered.Should().BeTrue("with an outbox the repository owns delivery — the caller must not publish");

        var rows = await ctx.Set<OutboxMessage>().ToListAsync();
        rows.Should().ContainSingle("the action is committed to the outbox in the same transaction as the state");
        rows[0].ProcessedAt.Should().BeNull("the action is durable and pending — the pump delivers it, it cannot be lost");
        rows[0].MessageType.Should().Contain(nameof(CheckInCommand));

        (await ctx.Set<SagaInstance>().CountAsync()).Should().Be(1, "the saga advanced exactly once");
    }

    [Fact]
    public async Task SaveWithStepAndOutbox_WhenNoOutboxMapped_SavesAndReportsInline()
    {
        await using var ctx = new TestSagaDbContext(
            new DbContextOptionsBuilder<TestSagaDbContext>().UseSqlite(_connection).Options);
        await ctx.Database.EnsureCreatedAsync();

        var delivered = await Repo(ctx).SaveWithStepAndOutboxAsync(
            NewSaga(), timeoutAt: null, executedStepName: "Start",
            pendingMessages: [new CheckInCommand(Guid.NewGuid())]);

        delivered.Should().BeFalse("without an outbox the orchestrator must publish the action inline");
        (await ctx.Set<SagaInstance>().CountAsync()).Should().Be(1);
    }
}

/// <summary>A test saga action message.</summary>
public sealed record CheckInCommand(Guid ReservationId);

/// <summary>Test DbContext mapping the saga tables AND the messaging outbox (a durable [EnableOutbox] boundary).</summary>
internal sealed class SagaWithOutboxDbContext(DbContextOptions<SagaWithOutboxDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var saga = new SagaEntityTypeConfiguration();
        saga.Configure(modelBuilder.Entity<SagaInstance>());
        saga.Configure(modelBuilder.Entity<SagaStep>());
        modelBuilder.ApplyConfiguration(new OutboxEntityTypeConfiguration());
    }
}

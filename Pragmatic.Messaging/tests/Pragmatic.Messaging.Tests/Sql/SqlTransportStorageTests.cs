using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Sql;

namespace Pragmatic.Messaging.Tests.Sql;

/// <summary>
///     Unit tests for the SQL transport storage engine on in-memory Sqlite (the portable EF
///     CAS claim is Sqlite-translatable by design — same reason as EfCoreOutboxSource).
/// </summary>
public sealed class SqlTransportStorageTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContextFactory _factory;
    private readonly SqlTransportOptions _options;
    private readonly SqlTransportStorage _storage;

    public SqlTransportStorageTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<SqlTransportDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new SqlTransportDbContext(dbOptions))
        {
            db.Database.EnsureCreated();
        }

        _factory = new TestDbContextFactory(dbOptions);
        _options = new SqlTransportOptions
        {
            ConfigureDbContext = _ => { },
            BatchSize = 10,
            MaxDeliveryCount = 3,
            LockDuration = TimeSpan.FromMinutes(5),
        };
        _storage = new SqlTransportStorage(_factory, _options, NullLogger<SqlTransportStorage>.Instance);
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestDbContextFactory(DbContextOptions<SqlTransportDbContext> options)
        : IDbContextFactory<SqlTransportDbContext>
    {
        public SqlTransportDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task Publish_FansOutOneRowPerSubscription()
    {
        await _storage.UpsertSubscriptionAsync("orders.events", "sub-a");
        await _storage.UpsertSubscriptionAsync("orders.events", "sub-b");

        var queues = await _storage.PublishAsync([1, 2, 3], "orders.events", MessageContext.New());

        queues.Should().BeEquivalentTo(["sub-a", "sub-b"]);
        (await _storage.ClaimBatchAsync("sub-a")).Should().ContainSingle();
        (await _storage.ClaimBatchAsync("sub-b")).Should().ContainSingle();
    }

    [Fact]
    public async Task Publish_WithoutSubscribers_DropsAndReturnsEmpty()
    {
        var queues = await _storage.PublishAsync([1], "empty.events", MessageContext.New());
        queues.Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertSubscription_IsIdempotent()
    {
        await _storage.UpsertSubscriptionAsync("orders.events", "sub-a");
        await _storage.UpsertSubscriptionAsync("orders.events", "sub-a");

        var queues = await _storage.PublishAsync([1], "orders.events", MessageContext.New());
        queues.Should().ContainSingle();
    }

    [Fact]
    public async Task Claim_StampsTokenAndIncrementsDeliveryCount_AndSecondClaimSeesNothing()
    {
        await _storage.SendAsync([1], "q1", MessageContext.New());

        var first = await _storage.ClaimBatchAsync("q1");
        first.Should().ContainSingle();
        first[0].LockedBy.Should().NotBeNull();
        first[0].DeliveryCount.Should().Be(1);

        // Locked: a concurrent claim must win nothing.
        (await _storage.ClaimBatchAsync("q1")).Should().BeEmpty();
    }

    [Fact]
    public async Task Ack_DeletesRow_WithTokenGuard()
    {
        await _storage.SendAsync([1], "q2", MessageContext.New());
        var claimed = (await _storage.ClaimBatchAsync("q2"))[0];

        // Wrong token: nothing deleted (lease-lost scenario), row still claimable later.
        await _storage.AckAsync(claimed.Id, "someone-else");
        // Right token: gone.
        await _storage.AckAsync(claimed.Id, claimed.LockedBy!);

        (await _storage.ClaimBatchAsync("q2")).Should().BeEmpty("acked rows are deleted");
    }

    [Fact]
    public async Task Nack_ReleasesLockAndBacksOff_ThenMovesToDeadLettersAtMaxDeliveryCount()
    {
        await _storage.SendAsync([9], "q3", MessageContext.New(correlationId: "corr-dlq"));

        // MaxDeliveryCount = 3: two nacks with backoff, the third moves to dead letters.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var claimed = await ClaimIgnoringVisibilityAsync("q3");
            claimed.DeliveryCount.Should().Be(attempt);
            await _storage.NackAsync(claimed, $"boom {attempt}");
        }

        var db = _factory.CreateDbContext();
        await using (db)
        {
            (await db.Messages.CountAsync()).Should().Be(0, "the row MOVES to dead letters");
            var dead = await db.DeadLetters.SingleAsync();
            dead.QueueName.Should().Be("q3");
            dead.DeliveryCount.Should().Be(3);
            dead.Reason.Should().Be("MaxDeliveryCountExceeded");
            dead.CorrelationId.Should().Be("corr-dlq");
        }
    }

    [Fact]
    public async Task ExpiredLease_IsReclaimable()
    {
        _options.LockDuration = TimeSpan.FromMilliseconds(50);
        await _storage.SendAsync([1], "q4", MessageContext.New());

        var first = await _storage.ClaimBatchAsync("q4");
        first.Should().ContainSingle();

        await Task.Delay(200); // lease expires

        var second = await _storage.ClaimBatchAsync("q4");
        second.Should().ContainSingle("expired leases are reclaimable (crash recovery)");
        second[0].DeliveryCount.Should().Be(2);
    }

    [Fact]
    public async Task ScheduledRows_AreInvisibleUntilDue_AndCancellableByToken()
    {
        await _storage.UpsertSubscriptionAsync("orders.events", "sub-a");
        var token = Guid.NewGuid();
        await _storage.PublishAsync([1], "orders.events", MessageContext.New(),
            visibleAt: DateTimeOffset.UtcNow.AddMinutes(10), schedulingToken: token);

        (await _storage.ClaimBatchAsync("sub-a")).Should().BeEmpty("not visible yet");

        var cancelled = await _storage.CancelScheduledAsync(token);
        cancelled.Should().Be(1, "durable cancel deletes the unclaimed scheduled row");
    }

    // Headers another service wrote that do not parse (#312). The consumer read them before its try, so the
    // JsonException left the message neither acked nor nacked: the poll loop took it for a storage hiccup, dropped
    // the rest of the batch with it, and both stayed locked until the lease ran out — then the same again, without
    // end. Unreadable headers are a delivery that failed, dead-lettered like any other.
    [Fact]
    public async Task AMessageWhoseHeadersDoNotParse_IsDeadLettered_AndTheBatchGoesOn()
    {
        _options.MaxDeliveryCount = 1;
        _options.PollingInterval = TimeSpan.FromMilliseconds(20);
        _options.MaxPollingInterval = TimeSpan.FromMilliseconds(100);
        await _storage.SendAsync([1], "q5", MessageContext.New());
        await _storage.SendAsync([2], "q5", MessageContext.New());

        var db = _factory.CreateDbContext();
        await using (db)
        {
            var first = await db.Messages.OrderBy(m => m.Id).FirstAsync();
            await db.Messages.Where(m => m.Id == first.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.HeadersJson, "{\"broken\":"));
        }

        var second = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new SqlQueueConsumer(_storage, _options, "q5", (payload, _, _) =>
        {
            second.TrySetResult(payload.ToArray());
            return Task.CompletedTask;
        }, NullLogger.Instance);

        await using (consumer.ConfigureAwait(true))
        {
            (await second.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Equal([2]);
        }

        var after = _factory.CreateDbContext();
        await using (after)
        {
            var dead = await after.DeadLetters.SingleAsync();
            dead.QueueName.Should().Be("q5");
            dead.Reason.Should().Be("MaxDeliveryCountExceeded");
        }
    }

    /// <summary>Nack pushes VisibleAt forward — rewind it so the next claim can proceed in-test.</summary>
    private async Task<Pragmatic.Messaging.Sql.Entities.TransportMessage> ClaimIgnoringVisibilityAsync(string queue)
    {
        var db = _factory.CreateDbContext();
        await using (db.ConfigureAwait(false))
        {
            await db.Messages.Where(m => m.QueueName == queue)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.VisibleAt, DateTimeOffset.UtcNow.AddSeconds(-1)))
                .ConfigureAwait(false);
        }

        var batch = await _storage.ClaimBatchAsync(queue).ConfigureAwait(false);
        batch.Should().ContainSingle();
        return batch[0];
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Events;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     W1 reliability pipeline: <c>[Redelivery]</c> + <c>[ConcurrencyLimit]</c> + <c>[RateLimit]</c>.
///     When the handler exhausts its in-process <c>[Retry]</c> attempts, the generated pipeline
///     releases the handler's idempotency claim and re-schedules the message through
///     <see cref="IMessageScheduler"/> (persistent — survives a process restart) keeping the
///     ORIGINAL MessageId and bumping RetryCount as the redelivery counter. Once RetryCount
///     reaches <c>MaxAttempts</c>, the failure propagates (→ transport dead-letter).
/// </summary>
public static class ReliabilityPipelineSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- SG reliability pipeline ([Redelivery] persistent backoff) ---");

        var scheduler = new RecordingScheduler();
        var store = new InMemoryIdempotencyStore();
        var handler = new AlwaysFailingSyncHandler();

        var pipeline = new AlwaysFailingSyncHandler.Pipeline(
            handler,
            NullLogger<AlwaysFailingSyncHandler.Pipeline>.Instance,
            middlewares: [],
            idempotencyStore: store,
            callContext: null,
            scheduler: scheduler);

        // 1st delivery (RetryCount 0) → fails → redelivery scheduled, no throw
        var context = MessageContext.New(correlationId: "sync-1");
        await pipeline.ExecuteAsync(new SyncCatalog(Guid.NewGuid()), context, CancellationToken.None);
        Console.WriteLine($"  redeliveries scheduled   : {scheduler.Scheduled.Count} (delay {scheduler.Scheduled[0].Delay})");
        Console.WriteLine($"  original MessageId kept  : {scheduler.Scheduled[0].Context.MessageId == context.MessageId}");
        Console.WriteLine($"  redelivery counter       : {scheduler.Scheduled[0].Context.RetryCount}");

        // Claim was released on failure → the redelivered message is NOT a duplicate
        var claimFree = await store.TryMarkAsProcessedAsync(
            context.MessageId + ":Pragmatic.Messaging.Outbox.Samples.AlwaysFailingSyncHandler");
        Console.WriteLine($"  claim released on failure: {claimFree}");
        await store.RemoveAsync(context.MessageId + ":Pragmatic.Messaging.Outbox.Samples.AlwaysFailingSyncHandler");

        // Exhausted (RetryCount == MaxAttempts) → failure propagates to the transport
        var exhausted = context with { RetryCount = 2 };
        var threw = false;
        try
        {
            await pipeline.ExecuteAsync(new SyncCatalog(Guid.NewGuid()), exhausted, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Console.WriteLine($"  exhausted → dead-letter  : {threw}");

        // [PartitionKey] → SG-generated typed resolver (zero reflection): the publisher stamps
        // this value into the x-partition-key header, Kafka uses it as the message key.
        var catalogId = Guid.NewGuid();
        var resolver = new Generated.GeneratedPartitionKeyResolver();
        Console.WriteLine($"  partition key resolved   : {resolver.TryGetPartitionKey(new SyncCatalog(catalogId)) == catalogId.ToString()}");
        Console.WriteLine();
    }

    private sealed class RecordingScheduler : IMessageScheduler
    {
        public List<(TimeSpan Delay, MessageContext Context)> Scheduled { get; } = [];

        public Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, CancellationToken ct = default)
            where T : notnull
            => Task.FromResult(Guid.NewGuid());

        public Task<Guid> ScheduleAsync<T>(T message, DateTimeOffset scheduledAt, CancellationToken ct = default)
            where T : notnull
            => Task.FromResult(Guid.NewGuid());

        public Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, MessageContext context, CancellationToken ct = default)
            where T : notnull
        {
            Scheduled.Add((delay, context));
            return Task.FromResult(Guid.NewGuid());
        }

        public Task CancelAsync(Guid scheduleId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}

/// <summary>
///     Carrier for the reliability demo (IDomainEvent for the Events bridge). The
///     <c>[PartitionKey]</c> body property feeds the SG-generated
///     <c>GeneratedPartitionKeyResolver</c>: on Kafka, all messages for the same catalog land
///     on the same partition (per-catalog ordering). Note: the attribute must sit on a
///     DECLARED property — <c>[property:]</c> on the positional parameter is not seen by the
///     generator (Roslyn limitation).
/// </summary>
public sealed record SyncCatalog(Guid CatalogId) : IDomainEvent
{
    [PartitionKey]
    public Guid PartitionId => CatalogId;

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
///     Always fails — stands in for a downstream that stays broken longer than in-process
///     retry can absorb, which is exactly what <c>[Redelivery]</c> is for.
/// </summary>
[MessageHandler]
[Retry(MaxAttempts = 1, BaseDelayMs = 1)]
[Redelivery(MaxAttempts = 2, BaseDelaySeconds = 1)]
[ConcurrencyLimit(2)]
[RateLimit(100)]
public sealed partial class AlwaysFailingSyncHandler : IMessageHandler<SyncCatalog>
{
    public Task HandleAsync(SyncCatalog message, MessageContext context, CancellationToken ct)
        => throw new InvalidOperationException("catalog service unavailable");
}

using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Core.Tests.Unit;

/// <summary>
///     What <c>[Redelivery]</c> does when the handler it is written on fails.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>A snapshot of the generated pipeline is not this.</b>
///         <c>MessagingSnapshotTests.HandlerPipeline_RedeliveryConcurrencyRateLimit_MatchesSnapshot</c>
///         pins the shape of the code the generator writes, and would keep passing if that code
///         re-scheduled the wrong thing, never released the idempotency claim, or scheduled for ever.
///         The pipeline below is the generated one, constructed and <em>run</em>.
///     </para>
///     <para>
///         This is the unit-level proof required before the attribute is written in an example: the delivery-governance family is about failure and
///         concurrency, which an end-to-end test over a real broker has no lever on. The example
///         declares and this executes.
///     </para>
/// </remarks>
public class AHandlerThatFailsIsRescheduledNotDeadLetteredTests
{
    private static readonly AnImportWasAsked Message = new("import-1");

    [Fact]
    public async Task WhenTheHandlerFails_TheMessageIsScheduledAgainAndNothingIsThrown()
    {
        var scheduler = new RecordingScheduler();
        var store = new RecordingIdempotencyStore();

        await PipelineOf(fails: true, scheduler, store)
            .ExecuteAsync(Message, MessageContext.New());

        scheduler.Scheduled.Should().HaveCount(1,
            "the failure is re-scheduled instead of reaching the transport as a throw, which is what "
            + "dead-letters it");
        scheduler.Scheduled[0].Delay.Should().Be(TimeSpan.FromSeconds(30),
            "the first redelivery waits BaseDelaySeconds, and the declaration says 30");
        scheduler.Scheduled[0].Context.RetryCount.Should().Be(1,
            "the counter is what makes the next attempt the second and not the first for ever");
        scheduler.Scheduled[0].Message.Should().Be(Message);
    }

    /// <summary>
    ///     The redelivery keeps the original message id, and the handler's own claim is released
    ///     before it is scheduled.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the half that makes the rest work, and it is invisible from outside: the
    ///     redelivered message carries the <b>same</b> MessageId, so sibling handlers that already
    ///     succeeded skip it as a duplicate. Without the release the failing handler would skip it
    ///     too, and the redelivery would be a scheduled no-op — arriving, being recognised as already
    ///     seen, and doing nothing, with the message gone and the log saying it was delivered.
    /// </remarks>
    [Fact]
    public async Task TheFailingHandlersClaimIsReleasedSoItsOwnRedeliveryIsNotSkipped()
    {
        var scheduler = new RecordingScheduler();
        var store = new RecordingIdempotencyStore();
        var context = MessageContext.New();

        await PipelineOf(fails: true, scheduler, store).ExecuteAsync(Message, context);

        store.Removed.Should().ContainSingle(
            "the failing handler releases its own claim, and only its own");
        store.Removed[0].Should().StartWith(context.MessageId,
            "the claim released is the one for this message");
        store.Removed[0].Should().EndWith(typeof(TheImportThatWillNotGoThrough).FullName!,
            "and it is per handler, so a sibling that succeeded keeps its claim and skips the "
            + "redelivery");

        scheduler.Scheduled[0].Context.MessageId.Should().Be(context.MessageId,
            "the redelivery is the same message, not a new one");
    }

    /// <summary>
    ///     The control: a handler that succeeds schedules nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "a redelivery was scheduled" is satisfied by a pipeline that schedules on every
    ///     message, which would double every delivery in the system.
    /// </remarks>
    [Fact]
    public async Task WhenTheHandlerSucceeds_NothingIsScheduled()
    {
        var scheduler = new RecordingScheduler();

        await PipelineOf(fails: false, scheduler, new RecordingIdempotencyStore())
            .ExecuteAsync(Message, MessageContext.New());

        scheduler.Scheduled.Should().BeEmpty();
    }

    /// <summary>
    ///     The control: the attempts are finite, and the last failure throws.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without it, "it is re-scheduled rather than dead-lettered" is satisfied perfectly by a
    ///     pipeline that re-schedules for ever — a message that can never be handled would circulate
    ///     until somebody noticed the queue, which is worse than the dead letter it was avoiding.
    /// </remarks>
    [Fact]
    public async Task WhenTheRedeliveriesAreSpent_ItThrowsAndTheTransportDeadLettersIt()
    {
        var scheduler = new RecordingScheduler();

        var run = () => PipelineOf(fails: true, scheduler, new RecordingIdempotencyStore())
            .ExecuteAsync(Message, MessageContext.New() with { RetryCount = 2 });

        (await run.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Be(TheImportThatWillNotGoThrough.Excuse);
        scheduler.Scheduled.Should().BeEmpty("the second redelivery was the last one declared");
    }

    /// <summary>
    ///     The control: with no scheduler the handler falls back to the normal failure path.
    /// </summary>
    /// <remarks>
    ///     The attribute's remark promises exactly this, and it is what a host that declares
    ///     <c>[Redelivery]</c> and forgets <c>EnableScheduledMessages()</c> gets: the ordinary
    ///     dead-letter, not a silent acknowledgement.
    /// </remarks>
    [Fact]
    public async Task WithNoScheduler_ItThrowsRatherThanSwallowingTheFailure()
    {
        var run = () => PipelineOf(fails: true, scheduler: null, new RecordingIdempotencyStore())
            .ExecuteAsync(Message, MessageContext.New());

        await run.Should().ThrowAsync<InvalidOperationException>();
    }

    private static TheImportThatWillNotGoThrough.Pipeline PipelineOf(
        bool fails, IMessageScheduler? scheduler, IIdempotencyStore store)
        => new(new TheImportThatWillNotGoThrough(fails),
            NullLogger<TheImportThatWillNotGoThrough.Pipeline>.Instance,
            [],
            store,
            scheduler);

    private sealed class RecordingScheduler : IMessageScheduler
    {
        public List<(object Message, TimeSpan Delay, MessageContext Context)> Scheduled { get; } = [];

        public Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, MessageContext context,
            CancellationToken ct = default) where T : notnull
        {
            Scheduled.Add((message, delay, context));
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, CancellationToken ct = default)
            where T : notnull
            => ScheduleAsync(message, delay, MessageContext.New(), ct);

        public Task<Guid> ScheduleAsync<T>(T message, DateTimeOffset scheduledAt,
            CancellationToken ct = default) where T : notnull
            => ScheduleAsync(message, scheduledAt - DateTimeOffset.UtcNow, ct);

        public Task CancelAsync(Guid scheduleId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingIdempotencyStore : IIdempotencyStore
    {
        public List<string> Removed { get; } = [];

        public Task<bool> TryMarkAsProcessedAsync(string messageId, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<MessageClaim> TryClaimAsync(string messageId, TimeSpan lease,
            CancellationToken ct = default)
            => Task.FromResult(MessageClaim.Claimed);

        public Task MarkClaimCompletedAsync(string messageId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveAsync(string messageId, CancellationToken ct = default)
        {
            Removed.Add(messageId);
            return Task.CompletedTask;
        }

        public Task PurgeOlderThanAsync(TimeSpan age, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}

/// <summary>The message this file's handler is asked to handle.</summary>
public sealed record AnImportWasAsked(string Reference);

/// <summary>
///     A handler that fails on demand, so the pipeline generated around it can be watched failing.
/// </summary>
/// <remarks>
///     ⚠️ It lives in a test project and not in an example on purpose: a failure seam is scaffolding,
///     and a reference application carrying one would teach a shape nobody should copy. Here it is the
///     subject.
/// </remarks>
[MessageHandler]
[Redelivery(MaxAttempts = 2, BaseDelaySeconds = 30)]
public sealed partial class TheImportThatWillNotGoThrough(bool fails)
    : IMessageHandler<AnImportWasAsked>
{
    public const string Excuse = "the import could not be read";

    public Task HandleAsync(AnImportWasAsked message, MessageContext context,
        CancellationToken ct = default)
        => fails ? throw new InvalidOperationException(Excuse) : Task.CompletedTask;
}

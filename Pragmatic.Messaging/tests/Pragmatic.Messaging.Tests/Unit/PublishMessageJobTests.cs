using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs;
using Pragmatic.Messaging.Entities;
using Pragmatic.Messaging.Jobs;
using Pragmatic.Messaging.Testing;
using Pragmatic.Messaging;

namespace Pragmatic.Messaging.Tests.Unit;

public class PublishMessageJobTests
{
    public record InvoiceDueMessage(Guid InvoiceId, decimal Amount);

    [Fact]
    public async Task ExecuteAsync_WithValidRegistry_PublishesMessage()
    {
        var harness = new MessageBusTestHarness();
        var registry = new TestMessageTypeRegistry();
        var job = new PublishMessageJob(harness, [registry],
            NullLogger<PublishMessageJob>.Instance);

        var invoiceId = Guid.NewGuid();
        var parameters = new PublishMessageParams(
            MessageTypeName: "InvoiceDueMessage",
            SerializedMessage: $"{{\"InvoiceId\":\"{invoiceId}\",\"Amount\":42.50}}");

        var context = new JobContext(Guid.NewGuid(), "PublishMessageJob",
            DateTimeOffset.UtcNow, Attempt: 0, MaxAttempts: 3);

        await job.ExecuteAsync(parameters, context, CancellationToken.None);

        harness.Published.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutRegistry_ThrowsInvalidOperation()
    {
        var harness = new MessageBusTestHarness();
        var job = new PublishMessageJob(harness, [],
            NullLogger<PublishMessageJob>.Instance);

        var parameters = new PublishMessageParams(
            MessageTypeName: "Unknown.Type",
            SerializedMessage: "{}");

        var context = new JobContext(Guid.NewGuid(), "PublishMessageJob",
            DateTimeOffset.UtcNow, Attempt: 0, MaxAttempts: 3);

        var act = () => job.ExecuteAsync(parameters, context, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot deserialize*Unknown.Type*");
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownType_ThrowsInvalidOperation()
    {
        var harness = new MessageBusTestHarness();
        var registry = new TestMessageTypeRegistry(returnNull: true);
        var job = new PublishMessageJob(harness, [registry],
            NullLogger<PublishMessageJob>.Instance);

        var parameters = new PublishMessageParams(
            MessageTypeName: "NonExistent.Type",
            SerializedMessage: "{}");

        var context = new JobContext(Guid.NewGuid(), "PublishMessageJob",
            DateTimeOffset.UtcNow, Attempt: 0, MaxAttempts: 3);

        var act = () => job.ExecuteAsync(parameters, context, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesToContext()
    {
        var harness = new MessageBusTestHarness();
        var registry = new TestMessageTypeRegistry();
        var job = new PublishMessageJob(harness, [registry],
            NullLogger<PublishMessageJob>.Instance);

        var correlationId = "corr-test-123";
        var parameters = new PublishMessageParams(
            MessageTypeName: "InvoiceDueMessage",
            SerializedMessage: "{\"InvoiceId\":\"00000000-0000-0000-0000-000000000000\",\"Amount\":10}",
            CorrelationId: correlationId);

        var context = new JobContext(Guid.NewGuid(), "PublishMessageJob",
            DateTimeOffset.UtcNow, 0, 3);

        await job.ExecuteAsync(parameters, context, CancellationToken.None);

        harness.Published[0].Context!.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownType_WithDeadLetterStore_DeadLettersAndSucceeds()
    {
        // An unknown type never becomes known by retrying — route it to the dead-letter store
        // (dashboard-visible/replayable) and let the job succeed, instead of poisoning the job store.
        var harness = new MessageBusTestHarness();
        var deadLetters = new InMemoryDeadLetterStore();
        var job = new PublishMessageJob(harness, [new TestMessageTypeRegistry(returnNull: true)],
            NullLogger<PublishMessageJob>.Instance, deadLetters);

        var parameters = new PublishMessageParams("NonExistent.Type", "{}", MessageId: "mid-1");
        var context = new JobContext(Guid.NewGuid(), "PublishMessageJob", DateTimeOffset.UtcNow, 0, 3);

        await job.ExecuteAsync(parameters, context, CancellationToken.None); // must NOT throw

        harness.Published.Should().BeEmpty();
        var stored = await deadLetters.GetAllAsync();
        stored.Should().ContainSingle().Which.MessageType.Should().Be("NonExistent.Type");
    }

    /// <summary>
    ///     Republishing a message id releases the consume-side claim on it first.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The bus claims the bare message id before dispatching and marks the claim <b>completed</b>
    ///         when the dispatch returns. A handler carrying <c>[Redelivery]</c> does not throw: it
    ///         releases its own per-handler claim, schedules this job and returns. Left marked handled,
    ///         the id would make the bus drop the copy this job republishes — the same id, deliberately,
    ///         so sibling handlers still dedupe — before any handler saw it.
    ///     </para>
    ///     <para>
    ///         ⚠️ Released <em>here</em> and not where the redelivery is scheduled: there the bus
    ///         completes the claim immediately afterwards, so a release there would be undone by the
    ///         very next statement.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ReleasesTheConsumeSideClaim_BeforePublishing()
    {
        var harness = new MessageBusTestHarness();
        var idempotency = new InMemoryIdempotencyStore();
        var job = new PublishMessageJob(harness, [new TestMessageTypeRegistry()],
            NullLogger<PublishMessageJob>.Instance, deadLetterStore: null, idempotency);

        const string messageId = "the-redelivered-id";
        await idempotency.TryClaimAsync(messageId, TimeSpan.FromMinutes(5));
        await idempotency.MarkClaimCompletedAsync(messageId);

        await job.ExecuteAsync(
            new PublishMessageParams("InvoiceDueMessage", "{}", MessageId: messageId),
            new JobContext(Guid.NewGuid(), "PublishMessageJob", DateTimeOffset.UtcNow, 0, 3),
            CancellationToken.None);

        harness.Published.Should().ContainSingle();
        (await idempotency.TryClaimAsync(messageId, TimeSpan.FromMinutes(5)))
            .Should().Be(MessageClaim.Claimed,
                "the id is being delivered again, so the consumer must be able to claim it");
    }

    /// <summary>
    ///     The control: a job with no idempotency store publishes just the same.
    /// </summary>
    /// <remarks>
    ///     The store is optional — an application that never called <c>EnableIdempotency()</c> has none —
    ///     and a release that became a requirement would take scheduled messages away from every one of
    ///     them.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoIdempotencyStore_StillPublishes()
    {
        var harness = new MessageBusTestHarness();
        var job = new PublishMessageJob(harness, [new TestMessageTypeRegistry()],
            NullLogger<PublishMessageJob>.Instance);

        await job.ExecuteAsync(
            new PublishMessageParams("InvoiceDueMessage", "{}", MessageId: "no-store"),
            new JobContext(Guid.NewGuid(), "PublishMessageJob", DateTimeOffset.UtcNow, 0, 3),
            CancellationToken.None);

        harness.Published.Should().ContainSingle();
    }

    /// <summary>Minimal IMessageTypeRegistry for testing.</summary>
    private sealed class TestMessageTypeRegistry(bool returnNull = false) : IMessageTypeRegistry
    {
        public object? Deserialize(string messageType, string payload)
            => returnNull ? null : new InvoiceDueMessage(Guid.Empty, 10m);
    }
}

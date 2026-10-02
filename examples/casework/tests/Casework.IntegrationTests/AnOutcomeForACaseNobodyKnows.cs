using System.Net.Http.Json;
using Casework.IntegrationTests.Infrastructure;
using Casework.Verify.Events;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     An answer Intake cannot record stops somewhere a person can see, instead of being
///     acknowledged and lost.
/// </summary>
/// <remarks>
///     <para>
///         The operation is loaded through <c>[LoadEntity&lt;Case&gt;]</c>, so an answer naming a case
///         this service does not hold is a failure before anything runs. ⚠️ The handler must not discard
///         that failure: awaiting the operation and looking at nothing acknowledges the message, and an
///         authoritative answer — the other service performed a verification and said what it found —
///         disappears with nothing written anywhere.
///     </para>
///     <para>
///         ⚠️ <b>Why a dead letter and not a logged acknowledgement.</b> The handler cannot tell the two
///         cases apart from the error: an answer that arrived before Intake knows the case is an
///         ordering race and will succeed later, an answer for a case that never existed never will.
///         Throwing serves the first and puts the second where somebody looks; acknowledging would
///         serve neither. It is also what the sibling handler already does —
///         <c>DecideTheCaseWhenTheProcessAsks</c>, in the same folder.
///     </para>
///     <para>
///         Where a dead letter is, and why counting the queue is the assertion, is explained at length
///         in <see cref="AMessageThatCannotBeHandled"/>: the RabbitMQ consumer nacks without requeue and
///         the queue's <c>x-dead-letter-exchange</c> routes it to <c>pragmatic.dlx.dlq</c>.
///     </para>
/// </remarks>
public sealed class AnOutcomeForACaseNobodyKnows(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    private const string DeadLetterQueue = "pragmatic.dlx.dlq";

    /// <summary>Verify's exchange — what crossed the broker is read from a queue bound to it.</summary>
    private const string VerifyEvents = "verify.events";

    /// <summary>The attempt number the redelivery carries, as the transport writes it.</summary>
    private const string RetryCountHeader = "X-Pragmatic-RetryCount";

    [Fact]
    public async Task ItStopsInTheDeadLetter_InsteadOfBeingAcknowledged()
    {
        await WaitForSubscriberAsync("verification-answered");
        var alreadyThere = await MessagesInAsync(DeadLetterQueue);

        await PublishFromVerifyAsync(new VerificationAnswered(
            VerificationId: Guid.NewGuid(),
            CaseId: Guid.NewGuid(),
            Outcome: VerificationOutcome.Passed,
            OccurredAt: DateTimeOffset.UtcNow));

        await EventuallyAsync(
            async () => await MessagesInAsync(DeadLetterQueue) > alreadyThere,
            "the answer stopped in the broker's dead-letter queue instead of being acknowledged");
    }

    /// <summary>
    ///     The answer is retried once before it stops, and it is the <em>retry</em> that
    ///     stops.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Without this, "it ends in the dead letter" is satisfied by a handler with no
    ///         <c>[Redelivery]</c> at all: the first delivery throws, the transport nacks, the queue
    ///         grows, and the assertion above is green while the declaration does nothing. So what is
    ///         asserted here is the redelivery itself — a second copy of <b>this</b> message crossing
    ///         the broker, carrying the attempt number the pipeline put on it.
    ///     </para>
    ///     <para>
    ///         ⚠️ And the dead letter grows by <b>one</b>, not two. The first delivery is acknowledged —
    ///         a handler that schedules a redelivery returns rather than throwing — so the copy that
    ///         ends up in the dead letter can only be the retried one. That is what says the redelivered
    ///         copy reached the handler, which a count of dead letters alone cannot.
    ///     </para>
    ///     <para>
    ///         ⚠️ That is the trap this count guards: the bus claims the message id before dispatching and
    ///         completes the claim when the dispatch returns, and a handler carrying <c>[Redelivery]</c>
    ///         returns. If that completed claim covered the bare id, the republished copy — the same id,
    ///         by design, so sibling handlers still dedupe — would be dropped at the bus before any
    ///         handler saw it, and the answer would be neither retried nor dead-lettered.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheAnswerIsRetriedOnce_AndItIsTheRetryThatStops()
    {
        await WaitForSubscriberAsync("verification-answered");
        var alreadyThere = await MessagesInAsync(DeadLetterQueue);
        var observer = await ObserveAsync(VerifyEvents);

        try
        {
            var answered = new VerificationAnswered(
                VerificationId: Guid.NewGuid(),
                CaseId: Guid.NewGuid(),
                Outcome: VerificationOutcome.Passed,
                OccurredAt: DateTimeOffset.UtcNow);

            await PublishFromVerifyAsync(answered);

            await EventuallyAsync(
                async () => await MessagesInAsync(DeadLetterQueue) > alreadyThere,
                "the answer stopped in the dead-letter queue, after its one retry");

            var ours = (await MessagesOnAsync(observer))
                .Where(m => m.Body.Contains(answered.VerificationId.ToString("D"), StringComparison.OrdinalIgnoreCase))
                .ToList();

            ours.Should().HaveCount(2, "the original crossed the broker and so did the one retry");
            ours.Should().Contain(m => m.Headers.ContainsKey(RetryCountHeader) && m.Headers[RetryCountHeader] == "1",
                "the retried copy carries the attempt the pipeline scheduled it as");

            (await MessagesInAsync(DeadLetterQueue)).Should().Be(alreadyThere + 1,
                "the first delivery is acknowledged — a handler that schedules a retry returns instead "
                + "of throwing — so only the retried copy can reach the dead letter");
        }
        finally
        {
            await StopObservingAsync(observer);
        }
    }

    /// <summary>
    ///     The control: an answer for a case this service does hold is recorded, and nothing is
    ///     dead-lettered.
    /// </summary>
    /// <remarks>
    ///     Without it, "it ends in the dead letter" is satisfied by a handler that throws on every
    ///     answer — which would lose every outcome instead of the ones it cannot place, and look
    ///     correct here.
    /// </remarks>
    [Fact]
    public async Task ForACaseItHolds_TheOutcomeIsRecordedAndNothingIsDeadLettered()
    {
        await WaitForSubscriberAsync("verification-answered");
        var alreadyThere = await MessagesInAsync(DeadLetterQueue);

        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);

        await PublishFromVerifyAsync(new VerificationAnswered(
            VerificationId: Guid.NewGuid(),
            CaseId: id,
            Outcome: VerificationOutcome.Passed,
            OccurredAt: DateTimeOffset.UtcNow));

        // ⚠️ TryGetProperty and not GetProperty: the host serialises with WhenWritingNull, so until the
        // outcome is recorded the property is ABSENT — and GetProperty on an absent one throws a bare
        // KeyNotFoundException out of the polling predicate, which reads as a defect in the
        // application and is a defect in the test. It stays latent for as long as the first read happens
        // to land after the outcome is recorded.
        await EventuallyAsync(
            async () => (await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}")))
                .TryGetProperty("verificationOutcome", out var outcome)
                && outcome.GetString() == "Passed",
            "Intake recorded the outcome");

        (await MessagesInAsync(DeadLetterQueue)).Should().Be(alreadyThere,
            "an answer that was recorded is not dead-lettered, and a pipeline that collected both would "
            + "make the assertion in the other test meaningless");
    }

    private async Task PublishFromVerifyAsync(VerificationAnswered answered)
    {
        using var scope = VerifyServices.CreateScope();
        await AsTenantAsync(async () =>
        {
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(answered);
            return true;
        });
    }

    private static async Task<Guid> ACaseAsync(HttpClient caller)
    {
        var created = await ReadJsonAsync(await caller.PostAsJsonAsync("api/cases", new
        {
            subject = "A case whose answer can be placed",
            applicant = "A. Applicant"
        }));

        return created.GetProperty("id").GetGuid();
    }
}

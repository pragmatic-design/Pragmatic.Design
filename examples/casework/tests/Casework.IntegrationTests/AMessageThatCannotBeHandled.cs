using System.Diagnostics;
using System.Net.Http.Json;
using Casework.Intake.Events;
using Casework.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     A message that cannot be handled is retried as declared and then stops somewhere
///     visible.
/// </summary>
/// <remarks>
///     <para>
///         The failure is a real one and not a thrown stub: <c>DecideTheCase</c> for a case that is not
///         in verification is refused by the state machine, the operation returns a failure and the
///         handler throws. So what is asserted is the path a genuine refusal takes.
///     </para>
///     <para>
///         ⚠️ <b>Where a dead letter is.</b> Not in <c>IDeadLetterStore</c>: that is the in-memory store
///         <c>UseMessaging</c> registers, and on this path nothing writes to it — it is written by the
///         outbox pump for a type it cannot resolve, and by the channel transport. The RabbitMQ consumer
///         nacks without requeue, and the queue's <c>x-dead-letter-exchange</c> routes the message to
///         <c>pragmatic.dlx</c>, a fanout the transport declares with <c>pragmatic.dlx.dlq</c> bound to
///         it. This test looked in the store first and found nothing for twenty seconds, which is how
///         the difference was learnt.
///     </para>
///     <para>
///         ⚠️ <b>The attempt count is not observable from outside</b>, and the story asked for it to be
///         read rather than assumed — so what is read is its <b>signature</b>: three attempts with
///         <c>BaseDelayMs = 500</c> and an exponential backoff cannot complete faster than the sum of
///         the waits between them, so the time from publish to dead letter is measured and asserted to
///         be at least that. A pipeline that gave up on the first failure would arrive sooner. Counting
///         the executions themselves would mean a counter inside the handler, which is the module's code
///         bent to a test's shape.
///     </para>
/// </remarks>
public sealed class AMessageThatCannotBeHandled(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    /// <summary>The queue the transport binds to its dead-letter exchange.</summary>
    private const string DeadLetterQueue = "pragmatic.dlx.dlq";

    /// <summary>
    ///     What three attempts at 500 ms with an exponential backoff cannot be faster than.
    /// </summary>
    /// <remarks>
    ///     The waits are between the attempts, so three attempts wait twice: 500 ms and 1,000 ms. Jitter
    ///     only ever <b>adds</b> in this engine's implementation, and the bound is a minimum, so a
    ///     conservative floor is safe — this is the assertion that tells "retried" from "gave up".
    /// </remarks>
    private static readonly TimeSpan TheBackoffOfThreeAttempts = TimeSpan.FromMilliseconds(1500);

    [Fact]
    public async Task AfterTheDeclaredAttempts_ItIsInTheDeadLetter()
    {
        await WaitForSubscriberAsync("decide-the-case");
        var alreadyThere = await MessagesInAsync(DeadLetterQueue);

        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);

        // A decision for a case nobody asked a verification for: the state machine has no move from
        // Open to Approved, so this can never succeed however many times it is delivered.
        var clock = Stopwatch.StartNew();
        await PublishAsync(new DecideTheCase(id, Casework.Verify.Events.VerificationOutcome.Passed));

        await EventuallyAsync(
            async () => await MessagesInAsync(DeadLetterQueue) > alreadyThere,
            "the message stopped in the broker's dead-letter queue instead of disappearing");

        clock.Elapsed.Should().BeGreaterThan(TheBackoffOfThreeAttempts,
            "three attempts wait twice — 500 ms then 1,000 ms — so arriving sooner than that would mean "
            + "the pipeline gave up on the first failure and [Retry(MaxAttempts = 3)] did nothing");

        // And the case is untouched: a refused decision decides nothing, however often it is retried.
        (await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}")))
            .GetProperty("status").GetString().Should().Be("Open");
    }

    /// <summary>
    ///     The control: the same message, for a case that <b>can</b> be decided, is handled and nothing
    ///     is dead-lettered.
    /// </summary>
    /// <remarks>
    ///     Without it, "it ends in the dead letter" would be satisfied by a pipeline that dead-letters
    ///     everything — including what it handled.
    /// </remarks>
    [Fact]
    public async Task ForACaseThatCanBeDecided_NothingIsDeadLettered()
    {
        await WaitForSubscriberAsync("decide-the-case");
        var alreadyThere = await MessagesInAsync(DeadLetterQueue);

        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);
        await operator1.PostAsJsonAsync($"api/cases/{id}/verifications", new { kind = "identity" });

        await PublishAsync(new DecideTheCase(id, Casework.Verify.Events.VerificationOutcome.Failed));

        await EventuallyAsync(
            async () => (await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}")))
                .GetProperty("status").GetString() == "Rejected",
            "the decision was carried out");

        (await MessagesInAsync(DeadLetterQueue)).Should().Be(alreadyThere,
            "a message that was handled is not dead-lettered, and a pipeline that collected both would "
            + "make the assertion in the other test meaningless");
    }

    /// <summary>Publishes on Intake's own bus, as the saga's orchestrator would.</summary>
    private async Task PublishAsync(DecideTheCase message)
    {
        using var scope = IntakeServices.CreateScope();

        await AsTenantAsync(async () =>
        {
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(message);
            return true;
        });
    }

    private static async Task<Guid> ACaseAsync(HttpClient caller)
    {
        var created = await ReadJsonAsync(await caller.PostAsJsonAsync("api/cases", new
        {
            subject = "A licence for a food stall",
            applicant = "A. Applicant"
        }));

        return created.GetProperty("id").GetGuid();
    }
}

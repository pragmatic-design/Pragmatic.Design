using System.Net.Http.Json;
using Casework.Verify.Events;
using Casework.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     The outcome comes back, and it comes back twice: the second delivery changes nothing.
/// </summary>
/// <remarks>
///     <para>
///         <b>Measured by publishing the same message twice</b>, with the same <c>EventId</c>, which is
///         what at-least-once delivery does on a bad day and what no amount of care in the transport can
///         rule out. The first delivery has to <b>write</b> the answer and the second has to leave it
///         alone: a handler that dropped everything would satisfy "one answer" just as well, which is
///         why both halves are asserted.
///     </para>
///     <para>
///         ⚠️ The host's <c>EnableIdempotency()</c> is <b>not</b> what makes this pass, and the test is
///         built so that it cannot be: its store is in memory and its key is the <em>message</em> id,
///         which for two publishes is two different ids. What holds is <c>Case.AnsweredByEvent</c> — the
///         event's own id, in a column, compared before anything is written. Remove that check and this
///         test goes red while the framework's deduplication is still on.
///     </para>
/// </remarks>
public sealed class AnOutcomeThatComesBackTwice(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    [Fact]
    public async Task TheSecondDeliveryChangesNothing()
    {
        await WaitForSubscriberAsync("verification-answered");

        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);

        // One answer, published twice — the same event, id included, as a redelivery would carry it.
        var answered = new VerificationAnswered(
            VerificationId: Guid.NewGuid(),
            CaseId: id,
            Outcome: VerificationOutcome.Passed,
            OccurredAt: DateTimeOffset.UtcNow);

        await PublishFromVerifyAsync(answered);

        await EventuallyAsync(
            async () => await OutcomeAsync(operator1, id) == "Passed",
            "Intake recorded the outcome of the first delivery");

        var afterOne = await CaseAsync(operator1, id);
        var recordedOn = afterOne.GetProperty("verificationAnsweredOn").GetString();
        var writesAfterOne = await AuditEntriesAsync(id);
        writesAfterOne.Should().BeGreaterThan(0,
            "the case is [Audited], so the first delivery left a trail — without this the count below "
            + "would be satisfied by an audit that records nothing");

        await PublishFromVerifyAsync(answered);
        await Task.Delay(TimeSpan.FromSeconds(2));

        var afterTwo = await CaseAsync(operator1, id);
        afterTwo.GetProperty("verificationOutcome").GetString().Should().Be("Passed",
            "the same answer, and not a second one");
        afterTwo.GetProperty("verificationAnsweredOn").GetString().Should().Be(recordedOn,
            "the second delivery wrote nothing at all — not even a new timestamp");
        // Still Open: in this test nobody asked for the verification — the answer was published straight
        // at the consumer — and recording an answer moves nothing by itself. Deciding is the saga's.
        // ⚠️ A handler that also transitioned would make this read "Approved" and would be a
        // second place where a case's life is written.
        afterTwo.GetProperty("status").GetString().Should().Be("Open",
            "recording an answer is writing down a fact, not deciding the case");

        // And the audit trail agrees: the second delivery added no entry. Counted rather than matched on
        // content, because what is being asserted is "nothing was written" and a count says that without
        // depending on how an entry spells a change.
        (await AuditEntriesAsync(id)).Should().Be(writesAfterOne,
            "one answer recorded means one change in the trail, however many messages arrived");
    }

    /// <summary>
    ///     The control of the control: a <b>different</b> answer for the same case is a second write.
    /// </summary>
    /// <remarks>
    ///     Without this, "changes nothing the second time" would also be satisfied by a handler that
    ///     ignores every answer after the first — which is not idempotency, it is deafness. Two events
    ///     with two ids are two facts, and the case takes the newer one.
    /// </remarks>
    [Fact]
    public async Task ADifferentAnswerIsRecorded()
    {
        await WaitForSubscriberAsync("verification-answered");

        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);

        await PublishFromVerifyAsync(new VerificationAnswered(
            Guid.NewGuid(), id, VerificationOutcome.Passed, DateTimeOffset.UtcNow));

        await EventuallyAsync(
            async () => await OutcomeAsync(operator1, id) == "Passed",
            "the first answer was recorded");

        await PublishFromVerifyAsync(new VerificationAnswered(
            Guid.NewGuid(), id, VerificationOutcome.Failed, DateTimeOffset.UtcNow));

        await EventuallyAsync(
            async () => await OutcomeAsync(operator1, id) == "Failed",
            "a second answer with its own id is a second fact, and it is recorded");
    }

    /// <summary>
    ///     Publishes as Verify would: from Verify's own bus, so the message is the one that service sends.
    /// </summary>
    /// <remarks>
    ///     Through the bus and not through the outbox, because what this test is about is the
    ///     <b>consumer</b>: a second publish is the cheapest way to produce the second delivery that a
    ///     broker produces on its own schedule. That the outbox delivers at all is
    ///     <see cref="TheOutboxDeliversWhatItHeld" />; that an answer recorded in Verify travels is
    ///     <see cref="AnAnswerTravelsBack" />.
    /// </remarks>
    private async Task PublishFromVerifyAsync(VerificationAnswered answered)
    {
        using var scope = VerifyServices.CreateScope();
        await AsTenantAsync(async () =>
        {
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(answered);
            return true;
        });
    }

    /// <summary>How many audit entries this case has — the table is <c>__AuditEntries</c>, keyed by target.</summary>
    private async Task<long> AuditEntriesAsync(Guid id)
        => (long)(await ScalarAsync(
            IntakeConnectionString,
            $"""select count(*) from "__AuditEntries" where "TargetId" = '{id}'""") ?? 0L);

    private static async Task<System.Text.Json.JsonElement> CaseAsync(HttpClient caller, Guid id)
        => await ReadJsonAsync(await caller.GetAsync($"api/cases/{id}"));

    /// <summary>
    ///     The outcome on the case, or <see langword="null" /> while there is none.
    /// </summary>
    /// <remarks>
    ///     <c>TryGetProperty</c>, because the host serialises with <c>WhenWritingNull</c>: before the
    ///     first answer the property is not on the wire at all, and asking for it by name throws.
    /// </remarks>
    private static async Task<string?> OutcomeAsync(HttpClient caller, Guid id)
        => (await CaseAsync(caller, id)).TryGetProperty("verificationOutcome", out var outcome)
            ? outcome.GetString()
            : null;

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

using System.Net.Http.Json;
using Casework.IntegrationTests.Infrastructure;
using Casework.Verify.Enums;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     A verification that cannot be carried out makes the process give up, and giving up
///     undoes what it had asked for.
/// </summary>
/// <remarks>
///     <para>
///         The three outcomes of a saga step are told apart here and they are not interchangeable:
///     </para>
///     <para>
///         <b>Rejected</b> — the step throws <c>SagaRejectedException</c>: the orchestrator walks the
///         compensation chain over the steps that <em>ran</em>, marks the instance <c>Compensated</c>,
///         and does <b>not</b> rethrow, so the delivery is not retried. A business refusal does not
///         become allowed by asking three times.
///         <b>Faulted</b> — any other exception: the instance is marked <c>Faulted</c> and the exception
///         is rethrown, so the pipeline retries and dead-letters. ⚠️ It does <b>not</b> compensate, on
///         purpose: a transient fault is about to be retried, and compensating
///         each time would re-publish the whole chain per redelivery. That outcome is
///         <see cref="AMessageThatCannotBeHandled" />'s subject.
///         <b>Neither</b> — the step returns normally: nothing compensates. That is the control below,
///         and without it "the compensation runs" would be satisfied by one that runs always.
///     </para>
///     <para>
///         ⚠️ The compensator is <b>seeded from the saga by JSON</b>: the orchestrator serializes the
///         instance and deserializes it into the compensating type, so that type's members have to be
///         named like the saga's. A name that does not match is a <c>JsonException</c> the generated
///         code catches and logs as "compensation skipped" — a compensation that silently does nothing,
///         which is why this test asserts on the <b>data</b> and not on the saga's status alone.
///     </para>
/// </remarks>
public sealed class TheProcessGivesUpAndUndoesWhatItAsked(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";
    private const string VerificationService = "verification-service";

    /// <summary>
    ///     An answer that verifies nothing: the process gives up, and the case is no longer waiting.
    /// </summary>
    [Fact]
    public async Task AnInconclusiveAnswer_CompensatesAndReleasesTheCase()
    {
        await WaitForSubscriberAsync("verify.verification-requested");
        await WaitForSubscriberAsync("intake.verification-requested");
        await WaitForSubscriberAsync("intake.verification-answered");
        // The compensator's own queue. Named in full because "each service has its own copy" is the
        // claim, and a fragment both services' queues match would not assert it.
        await WaitForSubscriberAsync("intake.the-verification-is-no-longer-wanted");

        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);

        await operator1.PostAsJsonAsync($"api/cases/{id}/verifications", new { kind = "identity" });

        await EventuallyAsync(
            async () => await SagaStatusAsync(id) is not null,
            "the process started");

        var verification = await VerificationForAsync(id);
        await VerifyAs(VerificationService).PostAsJsonAsync(
            $"api/verifications/{verification}/answer",
            new { outcome = "Inconclusive", note = "The document was unreadable." });

        // The saga refused: compensated, not completed, and not retried into a dead letter.
        await EventuallyAsync(
            async () => await SagaStatusAsync(id) == SagaCompensated,
            "the saga was compensated — a rejection is not a fault");

        // ⚠️ And the effect is gone, which is the half a status cannot show — measured by removal:
        // take [CompensateWith] off the start step and the saga is still marked Compensated, both
        // assertions above still pass, and the case stays in verification for ever. "A compensation
        // nobody runs" looks like success from the status alone, and only an assertion on the data
        // catches it.
        await EventuallyAsync(
            async () => (await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}")))
                .GetProperty("status").GetString() == "Open",
            "the case came back out of verification, so an operator can ask again");

        // ⚠️ And the other service is NOT told, deliberately. The process gives up because an answer arrived that it could not act on,
        // so Verify has already answered — and an answer is a fact it recorded, not an effect to undo.
        // A withdrawal crossing the broker would ask that service to unsay what it found.
        (await VerificationStatusAsync(verification)).Should().Be(VerificationStatus.Answered,
            "the compensation undoes this service's request, not the other service's answer");
    }

    /// <summary>
    ///     ⚠️ The control: an answer the process can act on compensates <b>nothing</b>.
    /// </summary>
    /// <remarks>
    ///     "The compensation runs" is satisfied by a compensation that runs on every answer, which would
    ///     withdraw a verification that had just decided a case. Returning normally is the third outcome
    ///     and it has to stay distinguishable from the other two.
    /// </remarks>
    [Fact]
    public async Task AnAnswerTheProcessCanActOn_CompensatesNothing()
    {
        await WaitForSubscriberAsync("verify.verification-requested");
        await WaitForSubscriberAsync("intake.verification-answered");

        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);

        await operator1.PostAsJsonAsync($"api/cases/{id}/verifications", new { kind = "identity" });

        await EventuallyAsync(async () => await SagaStatusAsync(id) is not null, "the process started");

        var verification = await VerificationForAsync(id);
        await VerifyAs(VerificationService).PostAsJsonAsync(
            $"api/verifications/{verification}/answer",
            new { outcome = "Passed", note = "The identity card matches." });

        await EventuallyAsync(
            async () => (await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}")))
                .GetProperty("status").GetString() == "Approved",
            "the case was decided");

        (await SagaStatusAsync(id)).Should().NotBe(SagaCompensated,
            "a step that returns normally compensates nothing");

        (await VerificationStatusAsync(verification)).Should().Be(VerificationStatus.Answered,
            "and the verification that did its job was not withdrawn under it");
    }

    /// <summary><see cref="Pragmatic.Messaging.Saga.SagaStatus" />.Compensated, as the column holds it.</summary>
    private const int SagaCompensated = 2;

    /// <summary>The saga instance's status for a case, or <see langword="null" /> when there is none.</summary>
    /// <remarks>
    ///     ⚠️ <c>Status</c> and not <c>State</c>: the state is where the process got to, the status is
    ///     whether it is still running. Both are <c>int</c> columns — see
    ///     <see cref="TheProcessThatCarriesACase" />, where reading one of them <c>as string</c> hid a
    ///     working saga for a day.
    /// </remarks>
    private async Task<int?> SagaStatusAsync(Guid caseId)
    {
        var value = await ScalarAsync(
            IntakeConnectionString,
            $"""select "Status" from "__SagaInstances" where "CorrelationId" = '{caseId}'""");

        return value is null ? null : (int)value;
    }

    /// <summary>The other service's view of the verification.</summary>
    /// <remarks>
    ///     ⚠️ Read as the <c>int</c> the column holds and cast, not <c>as string</c> — which on an
    ///     <c>int</c> is <see langword="null" /> and reads as "no row".
    /// </remarks>
    private async Task<VerificationStatus?> VerificationStatusAsync(Guid verificationId)
    {
        var value = await ScalarAsync(
            VerifyConnectionString,
            $"""select "Status" from "Verifications" where "PersistenceId" = '{verificationId}'""");

        return value is null ? null : (VerificationStatus)(int)value;
    }

    private async Task<Guid> VerificationForAsync(Guid caseId)
    {
        Guid verification = default;
        await EventuallyAsync(
            async () =>
            {
                verification = await ScalarAsync(
                    VerifyConnectionString,
                    $"""select "PersistenceId" from "Verifications" where "CaseId" = '{caseId}'""") is Guid found
                    ? found
                    : default;
                return verification != default;
            },
            "the other service wrote the verification down");

        return verification;
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

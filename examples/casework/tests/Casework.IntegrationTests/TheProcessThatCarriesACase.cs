using System.Net.Http.Json;
using Casework.Intake.Enums;
using Casework.IntegrationTests.Infrastructure;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     The process that carries a case from open to decided, readable as rows.
/// </summary>
/// <remarks>
///     <para>
///         The saga is the answer to "what happens next", and the point of asserting it <b>with SQL</b>
///         is that the answer survives the process: one instance per case in <c>__SagaInstances</c>, one
///         row per step in <c>__SagaSteps</c>, in order. A saga that only existed in memory would satisfy
///         every behavioural assertion in this suite and lose everything on a restart.
///     </para>
///     <para>
///         ⚠️ Verify does not know the saga exists, and nothing in this test tells it: the other service
///         answers a request and publishes an outcome, exactly as it would without one. What the saga
///         adds is that Intake has one thing that <b>follows</b> the conversation.
///     </para>
///     <para>
///         ⚠️⚠️ <b>Three things have to hold for these two to pass, and each fails quietly.</b>
///         The subscription name carries the subscriber — <c>intake.verification-requested</c> and
///         <c>verify.verification-requested</c> — so each service has a queue of its own. A saga step
///         binds a transport subscription, because a local <c>IMessageHandler</c> only answers a
///         message the process already has, and without the binding the start step never runs. And
///         the state is read with a cast, not with <c>as</c>: <c>as</c> on the wrong type is
///         <see langword="null" />, and null read as "no saga" is indistinguishable from a saga that
///         never started (see <see cref="SagaStateAsync" />).
///     </para>
///     <para>
///         ⚠️ The waits below name the <b>real</b> queues, one per service: a wait on a fragment both
///         services' queues match is satisfied by either one of them binding, so naming them is what
///         makes "each service has its own copy" an assertion rather than a hope.
///     </para>
/// </remarks>
public sealed class TheProcessThatCarriesACase(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";
    private const string VerificationService = "verification-service";

    [Fact]
    public async Task FromOpenToApproved_TheSagaAndItsStepsAreRows()
    {
        // Both services' own queues for the same event: Verify answers the request, Intake's saga
        // follows it. One queue for the two would be the defect, so both are waited for by name.
        await WaitForSubscriberAsync("verify.verification-requested");
        await WaitForSubscriberAsync("intake.verification-requested");
        await WaitForSubscriberAsync("intake.verification-answered");

        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);

        await operator1.PostAsJsonAsync($"api/cases/{id}/verifications", new { kind = "identity" });

        // The saga starts on the request, and it starts because the request was published — nobody told
        // it to.
        await EventuallyAsync(
            async () => await SagaStateAsync(id) is not null,
            "the saga instance exists — the start step never ran");

        (await SagaStateAsync(id)).Should().Be(CaseProcess.AwaitingVerification,
            "a case whose answer has not arrived is a process that is waiting");

        // The answer, from Verify's own API: the outcome crosses back and the saga decides.
        var verification = await VerificationForAsync(id);
        await VerifyAs(VerificationService).PostAsJsonAsync(
            $"api/verifications/{verification}/answer",
            new { outcome = "Passed", note = "The identity card matches." });

        await EventuallyAsync(
            async () => await SagaStateAsync(id) == CaseProcess.Approved,
            "the saga advanced on the answer");

        // And the case itself was decided — by the process, through an operation, and not by a route.
        await EventuallyAsync(
            async () => (await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}")))
                .GetProperty("status").GetString() == "Approved",
            "the case reached its end: the saga asked, the operation moved it, the state machine allowed it");

        // The steps, in order, one row each.
        var steps = await StepsAsync(id);
        steps.Should().BeEquivalentTo(
            ["WhenAVerificationIsAsked", "WhenTheAnswerArrives"],
            "one row per step, in the order they ran — which is what makes the process readable after "
            + "the fact instead of only assertable while it runs");
    }

    /// <summary>
    ///     The control: an answer for a case whose process is not waiting advances nothing.
    /// </summary>
    /// <remarks>
    ///     Two things are asserted at once, and both matter. No saga instance exists for a case nobody
    ///     asked a verification for — so the orchestrator has nothing to route the answer to and creates
    ///     nothing, because <c>VerificationAnswered</c> is not the start step. And the case stays
    ///     <c>Open</c>: the outcome is recorded as a fact and the decision is refused, because
    ///     <c>Case.Decide</c> asks a state machine with no move from <c>Open</c> to <c>Approved</c>.
    /// </remarks>
    [Fact]
    public async Task AnAnswerForACaseThatIsNotWaiting_AdvancesNothing()
    {
        await WaitForSubscriberAsync("intake.verification-answered");

        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);

        await PublishFromVerifyAsync(new Casework.Verify.Events.VerificationAnswered(
            Guid.NewGuid(), id, Casework.Verify.Events.VerificationOutcome.Passed, DateTimeOffset.UtcNow));

        // Wait for the outcome to be recorded, which is what proves the message was delivered at all —
        // otherwise "nothing advanced" would be satisfied by a message that never arrived.
        await EventuallyAsync(
            async () => (await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}")))
                .TryGetProperty("verificationOutcome", out _),
            "the answer was delivered and recorded as a fact");

        (await SagaStateAsync(id)).Should().BeNull(
            "no process was following this case, and an answer does not start one");

        (await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}")))
            .GetProperty("status").GetString().Should().Be("Open",
                "and the case was not decided: the state machine has no move from Open to Approved");
    }

    /// <summary>The saga instance's state for a case, or <see langword="null" /> when there is none.</summary>
    /// <remarks>
    ///     <para>
    ///         Read by correlation, which is the case's id — <c>[CorrelationKey]</c> on the messages is
    ///         what puts it there.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>The column holds the enum's underlying <c>int</c>, not its name.</b>
    ///         <c>SagaInstance.State</c> is an <c>int</c> because a saga's state type is a type argument
    ///         and no column can be typed for it — <see cref="Casework.Intake.Enums.CaseProcess" />'s own
    ///         remark says so ("persisted by the saga repository as its underlying value"). Read
    ///         <c>as string</c>, an <c>int</c> is <see langword="null" /> rather than an error, and the
    ///         saga looks absent while it is running.
    ///     </para>
    ///     <para>
    ///         Cast and not <c>as</c>, deliberately: a column whose type changes should fail here and
    ///         name itself, not turn into "no saga".
    ///     </para>
    /// </remarks>
    private async Task<CaseProcess?> SagaStateAsync(Guid caseId)
    {
        var value = await ScalarAsync(
            IntakeConnectionString,
            $"""select "State" from "__SagaInstances" where "CorrelationId" = '{caseId}'""");

        return value is null ? null : (CaseProcess)(int)value;
    }

    /// <summary>The steps this case's saga ran, in the order they ran.</summary>
    /// <remarks>
    ///     ⚠️ The join is on <c>SagaStep.SagaInstanceId</c>, and the order is <c>StartedAt</c>, when the
    ///     step ran. <c>s."Id"</c> is a <b>random Guid</b>: ordering two steps by it is a coin toss on an
    ///     assertion about order, which is the one thing this method exists to establish. The id only
    ///     breaks a tie.
    /// </remarks>
    private async Task<List<string>> StepsAsync(Guid caseId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(IntakeConnectionString);
        await connection.OpenAsync();

        await using var command = new Npgsql.NpgsqlCommand(
            """
            select s."StepName"
            from "__SagaSteps" s
            join "__SagaInstances" i on i."Id" = s."SagaInstanceId"
            where i."CorrelationId" = @correlation
            order by s."StartedAt", s."Id"
            """,
            connection);
        command.Parameters.AddWithValue("correlation", caseId.ToString());

        await using var reader = await command.ExecuteReaderAsync();

        var steps = new List<string>();
        while (await reader.ReadAsync())
            steps.Add(reader.GetString(0));

        return steps;
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
            "Verify recorded the request");

        return verification;
    }

    private async Task PublishFromVerifyAsync(Casework.Verify.Events.VerificationAnswered answered)
    {
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .CreateScope(VerifyServices);

        await AsTenantAsync(async () =>
        {
            await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<Pragmatic.Messaging.IMessageBus>(scope.ServiceProvider)
                .PublishAsync(answered);

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

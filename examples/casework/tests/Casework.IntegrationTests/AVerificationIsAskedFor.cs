using System.Net.Http.Json;
using Casework.IntegrationTests.Infrastructure;
using Casework.Verify.Enums;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     A case asks for a verification: Intake moves and publishes, Verify writes the request
///     down in its own database, and nobody answers on the request path.
/// </summary>
/// <remarks>
///     <para>
///         The two halves are asserted on <b>two databases</b>: Intake's case in <c>InVerification</c>,
///         read over HTTP, and Verify's row read with SQL from the other connection. A test that only
///         checked Intake would pass with the message going nowhere.
///     </para>
///     <para>
///         And the tenant is asserted on Verify's row, because it is the one fact of this exchange that
///         nothing in the contract carries: the transport puts it in a header
///         (<c>X-Pragmatic-TenantId</c>) and the consumer restores it into the consume scope, so the row
///         another service writes belongs to the organisation the request was made in. Had it not, the
///         row would be written with an empty tenant and Verify's own API — whose filter is fail-closed —
///         would never see it again.
///     </para>
/// </remarks>
public sealed class AVerificationIsAskedFor(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";
    private const string VerificationService = "verification-service";

    [Fact]
    public async Task TheCaseMovesAndTheOtherServiceWritesTheRequestDown()
    {
        await WaitForSubscriberAsync("verification-requested");

        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);

        var asked = await operator1.PostAsJsonAsync($"api/cases/{id}/verifications", new { kind = "identity" });
        asked.IsSuccessStatusCode.Should().BeTrue(
            $"the request is accepted: {await asked.Content.ReadAsStringAsync()}");

        // Intake's half: the case is in verification, and readable in that state.
        var moved = await ReadJsonAsync(asked);
        moved.GetProperty("status").GetString().Should().Be("InVerification",
            "the state machine moved the case, and the state is the whole answer");
        moved.GetProperty("verificationAskedOn").ValueKind.Should().NotBe(
            System.Text.Json.JsonValueKind.Undefined, "and when it was asked is on the case");

        // The tenant on the publisher's side first: the outbox row carries it, because the interceptor
        // reads the ambient tenant while the transaction is being saved. Asserted here so a failure on
        // Verify's side below cannot be blamed on the wrong half of the chain.
        (await ScalarAsync(
                IntakeConnectionString,
                $"""select "TenantId" from "__OutboxMessages" where "Payload" like '%{id}%'"""))
            .Should().Be(Tenant, "the outbox row belongs to the organisation whose request it announces");

        // Verify's half: its own row, in its own database, with its own state.
        await EventuallyAsync(
            async () => await ScalarAsync(
                VerifyConnectionString,
                $"""select count(*) from "Verifications" where "CaseId" = '{id}'""") is 1L,
            "Verify wrote the request down — the message never arrived, or the handler never ran");

        // The enum is an int in the column — the name is what the API shows, not what is stored — so the
        // assertion names it through the enum rather than hard-coding 0, which would keep passing if
        // somebody reordered the members.
        (await ScalarAsync(
                VerifyConnectionString,
                $"""select "Status" from "Verifications" where "CaseId" = '{id}'"""))
            .Should().Be((int)VerificationStatus.Pending,
                "the verification has its own life, and it starts waiting");

        (await ScalarAsync(
                VerifyConnectionString,
                $"""select "TenantId" from "Verifications" where "CaseId" = '{id}'"""))
            .Should().Be(Tenant, "the tenant crossed the broker in a header, not in the contract");

        (await ScalarAsync(
                VerifyConnectionString,
                $"""select "Deadline" from "Verifications" where "CaseId" = '{id}'"""))
            .Should().NotBeNull("the request says by when, which ExpireVerificationsJob enforces");
    }

    /// <summary>
    ///     The answer is recorded by Verify's own API, reached with a service token.
    /// </summary>
    /// <remarks>
    ///     A service and not a user: Verify holds no accounts, and the caller of this endpoint is another
    ///     system. Recording the answer is all this test looks at. The outcome travelling back to Intake
    ///     is <c>TheProcessThatCarriesACase</c>'s, and this test does not look at Intake afterwards: the
    ///     outcome goes back by message after the answer's commit, so what Intake shows at that moment
    ///     depends on whether the outbox has polled yet — a race, not a fact.
    /// </remarks>
    [Fact]
    public async Task VerifyRecordsAnAnswerThroughItsOwnApi()
    {
        await WaitForSubscriberAsync("verification-requested");

        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);
        await operator1.PostAsJsonAsync($"api/cases/{id}/verifications", new { kind = "identity" });

        Guid verification = default;
        await EventuallyAsync(
            async () =>
            {
                verification = (await ScalarAsync(
                    VerifyConnectionString,
                    $"""select "PersistenceId" from "Verifications" where "CaseId" = '{id}'""")) is Guid found
                    ? found
                    : default;

                return verification != default;
            },
            "Verify wrote the request down");

        var service = VerifyAs(VerificationService);
        var answered = await service.PostAsJsonAsync($"api/verifications/{verification}/answer", new
        {
            outcome = "Passed",
            note = "The identity card matches the applicant."
        });

        var body = await ReadJsonAsync(answered);
        body.GetProperty("status").GetString().Should().Be("Answered",
            "the verification moved, through its own state machine");
        body.GetProperty("outcome").GetString().Should().Be("Passed");

        // Verify's own read agrees with what the answer returned: the state is stored, not only reported.
        (await ScalarAsync(
                VerifyConnectionString,
                $"""select "Status" from "Verifications" where "CaseId" = '{id}'"""))
            .Should().Be((int)VerificationStatus.Answered, "the answer is written down in Verify's database");
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

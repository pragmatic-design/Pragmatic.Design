using System.Net.Http.Json;
using Casework.IntegrationTests.Infrastructure;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     The whole round trip, with nobody publishing anything by hand.
/// </summary>
/// <remarks>
///     <para>
///         An operator asks for a verification over Intake's API; the request crosses the broker from
///         Intake's outbox; Verify writes it down; a service records the answer through Verify's own API;
///         the answer crosses back from <b>Verify's</b> outbox; Intake writes the outcome on the case.
///         Six steps, two processes, two databases, and not one line of the application names the bus.
///     </para>
///     <para>
///         It is the test the other ones stand on: <see cref="AnOutcomeThatComesBackTwice" /> publishes
///         the answer itself to produce a duplicate, which is the cheap way to get a second delivery —
///         and would pass just as well if nothing ever published the answer for real. This one is what
///         says the exchange exists.
///     </para>
/// </remarks>
public sealed class AnAnswerTravelsBack(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";
    private const string VerificationService = "verification-service";

    [Fact]
    public async Task FromAnAnswerInVerify_TheOutcomeReachesTheCase()
    {
        await WaitForSubscriberAsync("verification-requested");
        await WaitForSubscriberAsync("verification-answered");

        var operator1 = IntakeAs(Caseworker);
        var created = await ReadJsonAsync(await operator1.PostAsJsonAsync("api/cases", new
        {
            subject = "A licence for a food stall",
            applicant = "A. Applicant"
        }));
        var id = created.GetProperty("id").GetGuid();

        await operator1.PostAsJsonAsync($"api/cases/{id}/verifications", new { kind = "identity" });

        // Verify hears the request through the broker and writes it down.
        Guid verification = default;
        await EventuallyAsync(
            async () =>
            {
                verification = await ScalarAsync(
                    VerifyConnectionString,
                    $"""select "PersistenceId" from "Verifications" where "CaseId" = '{id}'""") is Guid found
                    ? found
                    : default;

                return verification != default;
            },
            "Verify recorded the request");

        // The service answers, into Verify's own API. Nothing publishes: the entity raises the event and
        // Verify's outbox carries it.
        var answered = await VerifyAs(VerificationService).PostAsJsonAsync(
            $"api/verifications/{verification}/answer",
            new { outcome = "Failed", note = "The address could not be confirmed." });

        answered.IsSuccessStatusCode.Should().BeTrue(
            $"the answer is recorded: {await answered.Content.ReadAsStringAsync()}");

        // And it comes back across the broker, on its own.
        await EventuallyAsync(
            async () => (await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}")))
                .TryGetProperty("verificationOutcome", out var outcome) && outcome.GetString() == "Failed",
            "the outcome reached the case — Verify's outbox never delivered it, or Intake's handler never ran");

        var @case = await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}"));
        @case.GetProperty("status").GetString().Should().Be("InVerification",
            "the answer is a fact on the case and not a decision about it: deciding is the saga's job");
        @case.GetProperty("verificationAnsweredOn").ValueKind.Should().NotBe(
            System.Text.Json.JsonValueKind.Undefined,
            "and when this service recorded it, from this service's clock");
    }
}

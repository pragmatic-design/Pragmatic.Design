using System.Net.Http.Json;
using Casework.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Messaging.RabbitMQ;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     The control — with Verify not consuming, Intake asks, moves and answers anyway.
/// </summary>
/// <remarks>
///     <para>
///         This is the half of the story that the happy path cannot show. "Nothing in Intake waits for
///         the answer" is satisfied by a request that works while the other service is deaf: the case
///         moves to <c>InVerification</c>, the caller gets an answer, and the case stays readable.
///     </para>
///     <para>
///         ⚠️ The consumer is removed from <b>Verify</b>, by type, rather than the whole host being taken
///         down: the point is a service that does not answer, not a broker that is missing. Intake's
///         publish still succeeds, its outbox still drains, and the message waits in the queue nobody is
///         reading — which is what a service being restarted looks like from the other side.
///     </para>
/// </remarks>
public sealed class NothingInIntakeWaitsForTheAnswer(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    /// <summary>Takes the consuming half out of Verify's host — by type, not by a name match.</summary>
    protected override void ConfigureVerify(IServiceCollection services)
    {
        foreach (var consumer in services
                     .Where(service => service.ImplementationType == typeof(RabbitMqConsumerService))
                     .ToList())
        {
            services.Remove(consumer);
        }
    }

    [Fact]
    public async Task WithVerifyNotConsuming_TheCaseStillMovesAndStaysReadable()
    {
        var operator1 = IntakeAs(Caseworker);

        var created = await ReadJsonAsync(await operator1.PostAsJsonAsync("api/cases", new
        {
            subject = "A licence for a food stall",
            applicant = "A. Applicant"
        }));
        var id = created.GetProperty("id").GetGuid();

        var asked = await operator1.PostAsJsonAsync($"api/cases/{id}/verifications", new { kind = "identity" });

        asked.IsSuccessStatusCode.Should().BeTrue(
            $"asking does not depend on anybody answering: {await asked.Content.ReadAsStringAsync()}");

        var @case = await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}"));
        @case.GetProperty("status").GetString().Should().Be("InVerification",
            "a case waiting for an answer is a state, not a blocked request");

        // And the other side really did not hear it: nothing was written there.
        await Task.Delay(TimeSpan.FromSeconds(2));

        (await ScalarAsync(
                VerifyConnectionString,
                $"""select count(*) from "Verifications" where "CaseId" = '{id}'"""))
            .Should().Be(0L, "with nobody consuming, the message waits in the queue and no row exists");
    }
}

using System.Net.Http.Json;
using Casework.IntegrationTests.Infrastructure;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     Every request about one case carries that case's id in <c>x-partition-key</c>.
/// </summary>
/// <remarks>
///     <para>
///         <c>[PartitionKey]</c> is declared on <c>VerificationRequested.CaseId</c>, a positional record
///         parameter of a contracts assembly. ⚠️ That form works for two reasons: the attribute is read
///         from the property symbol, because syntax does not see <c>[property: …]</c> on a parameter; and
///         the scan runs over the assembly that declares the message, because the bus resolves the
///         resolver in the <b>publisher</b>. A scan from a <c>[MessageHandler]</c> would generate it in
///         the consumer, where nothing asks for it.
///     </para>
///     <para>
///         <b>Half of the promise, and the half is stated.</b> The header travels on every transport; what
///         it is <em>for</em> — messages with one key landing on one partition, in order — needs a
///         partitioned transport, and this example runs RabbitMQ. So this asserts that the value is
///         stamped and carried, and says plainly that the ordering it enables is not demonstrated here.
///     </para>
///     <para>
///         Read from a queue of the test's own, bound to the same exchange: a header is restored into the
///         consume scope and no handler looks at it, so the only honest place to see one is the wire. The
///         alternative would have been a line in a reference application that exists for a test.
///     </para>
/// </remarks>
public sealed class TheKeyAMessageIsPartitionedBy(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    /// <summary>The role that registers organisations, as <c>OnboardingCrossesBothServices</c> has it.</summary>
    private const string Operator = "service-operator";

    /// <summary>The exchange Intake publishes its events to — the boundary's, not the project's.</summary>
    private const string IntakeEvents = "intake.events";

    private const string PartitionKeyHeader = "x-partition-key";

    [Fact]
    public async Task TheRequestCarriesTheCaseIdItDeclared()
    {
        await WaitForSubscriberAsync("verification-requested");
        var observer = await ObserveAsync(IntakeEvents);

        try
        {
            var operator1 = IntakeAs(Caseworker);
            var id = await ACaseAsync(operator1);

            (await operator1.PostAsJsonAsync($"api/cases/{id}/verifications", new { kind = "identity" }))
                .IsSuccessStatusCode.Should().BeTrue("the request is accepted");

            var request = await EventuallyOnTheWireAsync(observer, id.ToString());

            request.Headers.Should().ContainKey(PartitionKeyHeader,
                "the publisher stamps the declared property into the header");
            request.Headers[PartitionKeyHeader].Should().Be(id.ToString(),
                "and the value is the case's id, which is what every request about this case shares");
        }
        finally
        {
            await StopObservingAsync(observer);
        }
    }

    /// <summary>
    ///     The control: a message on the same exchange that declares no key carries no header.
    /// </summary>
    /// <remarks>
    ///     Without it, "the header is there" is satisfied by a bus that stamps every message with
    ///     something — and the assertion above would hold while the declaration meant nothing.
    ///     <c>TenantOnboarded</c> is Intake's other published event and has no <c>[PartitionKey]</c>.
    /// </remarks>
    [Fact]
    public async Task AMessageWithNoDeclaredKey_CarriesNoHeader()
    {
        await WaitForSubscriberAsync("verification-requested");
        var observer = await ObserveAsync(IntakeEvents);

        try
        {
            var newcomer = $"westford{Guid.NewGuid():N}"[..16];

            (await IntakeAs(Operator).PostAsJsonAsync("api/organisations", new
            {
                tenantKey = newcomer,
                name = "Westford Borough",
                dedicatedDatabase = false,
            })).IsSuccessStatusCode.Should().BeTrue("an organisation is registered");

            var onboarded = await EventuallyOnTheWireAsync(observer, newcomer);

            onboarded.Headers.Should().NotContainKey(PartitionKeyHeader,
                "no [PartitionKey] is declared on TenantOnboarded, so nothing is stamped");
        }
        finally
        {
            await StopObservingAsync(observer);
        }
    }

    /// <summary>The message whose payload names <paramref name="needle" />, once it has crossed.</summary>
    private async Task<(string Body, IReadOnlyDictionary<string, string> Headers)>
        EventuallyOnTheWireAsync(string observer, string needle)
    {
        (string Body, IReadOnlyDictionary<string, string> Headers)? found = null;

        await EventuallyAsync(
            async () =>
            {
                found = (await MessagesOnAsync(observer))
                    .Where(message => message.Body.Contains(needle, StringComparison.Ordinal))
                    .Select(message => ((string, IReadOnlyDictionary<string, string>)?)message)
                    .FirstOrDefault();

                return found is not null;
            },
            $"a message naming '{needle}' crossed the exchange");

        return found!.Value;
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

using System.Net;
using System.Net.Http.Json;
using Casework.IntegrationTests.Infrastructure;
using Casework.Verify.Organisations;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     The control — when Verify cannot make its database, the onboarding does not report
///     success and the organisation cannot work.
/// </summary>
/// <remarks>
///     <para>
///         <b>This is the test that makes the other one a measurement.</b> "Both databases exist" is
///         satisfied by an onboarding that never checks anything, as long as nothing goes wrong. What
///         has to be true is the other side of it: when the second half fails, nobody says ready.
///     </para>
///     <para>
///         The failure is injected where a real one would happen — Verify's own provisioning — and not
///         by stopping the broker: a broker that is down is a message that has not arrived yet, which is
///         the same state as one in flight. What this asserts is a half that ran and could not finish.
///     </para>
/// </remarks>
public sealed class OnboardingWhenTheSecondHalfFails(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    private const string Operator = "service-operator";

    private const string Ashford = "ashford";

    /// <summary>Verify's provisioning, broken on purpose — the way a full disk or a refused grant breaks it.</summary>
    private sealed class ADatabaseThatCannotBeMade : IProvisionTenantDatabases
    {
        // It still answers where the rows would go — otherwise there would be nothing to fail at, and
        // the test would pass by taking the shared-schema path. What fails is the making, which is the
        // failure a full disk or a refused grant produces, and the one the register is written before.
        public string? ConnectionStringFor(string tenantId, bool wantsItsOwnDatabase)
            => wantsItsOwnDatabase ? $"Host=nowhere;Database=casework_verify_{tenantId}" : null;

        public Task ProvisionAsync(string tenantId, string connectionString, CancellationToken ct = default)
            => throw new InvalidOperationException($"no room for '{tenantId}' on this server");
    }

    protected override void ConfigureVerify(IServiceCollection services)
        => services.AddSingleton<IProvisionTenantDatabases, ADatabaseThatCannotBeMade>();

    [Fact]
    public async Task TheOrganisationStaysProvisioningAndIsRefused()
    {
        await WaitForSubscriberAsync("tenant-onboarded");

        var registered = await ReadJsonAsync(
            await IntakeAs(Operator).PostAsJsonAsync("api/organisations", new
            {
                tenantKey = Ashford,
                name = "Ashford Town Council",
                dedicatedDatabase = true
            }));

        // Intake's half worked, and Intake's half is not the onboarding: what it reports is where the
        // process is, which is exactly what it is still able to say truthfully.
        registered.GetProperty("state").GetString().Should().Be("Provisioning");

        // Long enough for three attempts with backoff and the dead letter that follows them.
        await Task.Delay(TimeSpan.FromSeconds(8));

        (await StateOfAsync(IntakeConnectionString, Ashford)).Should()
            .Be((int)Casework.Intake.Enums.OrganisationState.Provisioning,
                "nothing moved it to Active, because nothing reported the second half done");

        (await StateOfAsync(VerifyConnectionString, Ashford)).Should()
            .Be((int)Casework.Verify.Enums.OrganisationState.Provisioning,
                "Verify wrote its row before trying, and never got to change it");

        // And the consequence that matters: the organisation cannot open a case it could never have
        // verified. This is the defect the story exists to prevent, asserted rather than argued.
        (await IntakeAs(Caseworker, Ashford).PostAsJsonAsync("api/cases", new
            {
                subject = "A licence nobody could ever verify",
                applicant = "A. Applicant"
            }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The failure is somewhere a person looks, rather than nowhere.
        (await MessagesInAsync("dlq")).Should().BeGreaterThan(0,
            "a message that could not be handled is dead-lettered, which is how an operator finds it");
    }
}

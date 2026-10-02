using System.Net;
using System.Net.Http.Json;
using Casework.IntegrationTests.Infrastructure;
using Npgsql;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     An organisation created while the services are running can work immediately, and
///     "working" means both of them.
/// </summary>
/// <remarks>
///     <para>
///         <b>Onboarding is the first operation that is itself distributed.</b> A database per tenant in
///         a two-service system means two databases, and neither service provisions for the other: Intake
///         makes its own and says so, Verify hears it and makes its own. The half-way state — Intake
///         ready, Verify not — is a real state of the world, and this test is about what the system says
///         while it lasts.
///     </para>
///     <para>
///         ⚠️ <b>Not a saga, deliberately</b>, and the reason is in
///         <c>Casework.Intake/Organisations/Actions/OnboardAnOrganisationAction.cs</c>: the state of this
///         process is the tenant's own state, which the framework already has a vocabulary for
///         (<c>TenantState.Provisioning</c> → <c>TenantState.Active</c>) and which the request pipeline
///         already reads. A saga would put the same fact in a second place, and the one that decides
///         whether a request is served would still be the register.
///     </para>
///     <para>
///         The assertion is the databases themselves, listed out of <c>pg_database</c> — not the absence
///         of an error from the provisioner, which is satisfied by a provisioner that does nothing.
///     </para>
/// </remarks>
public sealed class OnboardingCrossesBothServices(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    /// <summary>
    ///     The role that runs the service rather than the cases.
    /// </summary>
    /// <remarks>
    ///     Not an extra grant on the caseworker: creating an organisation is not a step of handling a
    ///     case, and the two are held by different people.
    /// </remarks>
    private const string Operator = "service-operator";

    /// <summary>The organisation that does not exist when the hosts start.</summary>
    private const string Wayland = "wayland";

    [Fact]
    public async Task ANewOrganisationIsProvisionedInBothServicesAndCanWorkImmediately()
    {
        await WaitForSubscriberAsync("verification-requested");

        var registered = await ReadJsonAsync(
            await IntakeAs(Operator).PostAsJsonAsync("api/organisations", new
            {
                tenantKey = Wayland,
                name = "Wayland Borough Council",
                dedicatedDatabase = true
            }));

        // ⚠️ What comes back is not "created": the second half happens in another process, and the
        // answer says where the process is. (A 202 would say it better; an endpoint cannot declare its
        // own success status in this framework, so the state is in the body rather than in the line.)
        registered.GetProperty("state").GetString().Should().Be("Provisioning",
            "Intake's half is done and the organisation is not ready until the other service says so");

        // Both databases exist, and they exist because a provisioner made them: the server is asked
        // what it holds, rather than the call being asked whether it threw.
        await EventuallyAsync(
            async () => (await DatabasesAsync()).Contains(DatabaseIn(ConnectionStringFor(Wayland))),
            "Intake provisioned its own database for the new organisation");

        await EventuallyAsync(
            async () => (await DatabasesAsync()).Contains(DatabaseIn(VerifyConnectionStringFor(Wayland))),
            "Verify provisioned its own — neither service provisions for the other");

        // And the process reports its end where the request pipeline reads it: the register.
        await EventuallyAsync(
            async () => await StateOfAsync(IntakeConnectionString, Wayland)
                == (int)Casework.Intake.Enums.OrganisationState.Active,
            "Intake's register moves the organisation out of Provisioning when Verify confirms");

        (await StateOfAsync(VerifyConnectionString, Wayland)).Should()
            .Be((int)Casework.Verify.Enums.OrganisationState.Active,
            "Verify registered it too, or its own API would refuse an organisation it does not know");

        // The point of all of it: the new organisation works end to end, with no deployment in between.
        var caseworker = IntakeAs(Caseworker, Wayland);

        var opened = await ReadJsonAsync(await caseworker.PostAsJsonAsync("api/cases", new
        {
            subject = "A licence for a food stall on the green",
            applicant = "W. Applicant"
        }));
        var caseId = opened.GetProperty("id").GetGuid();

        (await caseworker.PostAsJsonAsync($"api/cases/{caseId}/verifications", new { kind = "identity" }))
            .IsSuccessStatusCode.Should().BeTrue("the new organisation can ask for a verification");

        await EventuallyAsync(
            async () => await VerificationsForAsync(VerifyConnectionStringFor(Wayland), caseId) == 1,
            "and the verification is in the new organisation's own Verify database");
    }

    /// <summary>How many verifications one database holds for a case.</summary>
    private static async Task<int> VerificationsForAsync(string connectionString, Guid caseId)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        try
        {
            await connection.OpenAsync();
        }
        catch (PostgresException failed) when (failed.SqlState == "3D000")
        {
            return 0;
        }

        await using var command = new NpgsqlCommand(
            "select count(*) from \"Verifications\" where \"CaseId\" = @case", connection);
        command.Parameters.AddWithValue("case", caseId);

        return (int)(long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    /// <summary>
    ///     The control: while the second half has not happened, the organisation cannot work.
    /// </summary>
    /// <remarks>
    ///     A tenant that can open a case and can never have it verified is the defect this story exists
    ///     to prevent, and "Intake said yes" is exactly how a system arrives there. The request pipeline
    ///     refuses a tenant that is still <c>Provisioning</c> — <c>EnforceTenantState</c>, which the host
    ///     turns on for this reason — so the window is closed by the state and not by a race being short.
    /// </remarks>
    [Fact]
    public async Task WhileItIsStillProvisioning_TheOrganisationCannotOpenACase()
    {
        const string halfway = "halfway";

        await OnboardAsync(IntakeServices, halfway, null, TenantState.Provisioning);

        var refused = await IntakeAs(Caseworker, halfway).PostAsJsonAsync("api/cases", new
        {
            subject = "A licence asked for too early",
            applicant = "H. Applicant"
        });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the tenant middleware refuses a tenant that is not Active, which is what Provisioning is for");
    }
}

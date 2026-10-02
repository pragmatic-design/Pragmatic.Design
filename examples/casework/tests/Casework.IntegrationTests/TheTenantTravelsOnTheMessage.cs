using System.Net.Http.Json;
using Casework.Intake.Events;
using Casework.IntegrationTests.Infrastructure;
using Casework.Verify.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pragmatic.Messaging;
using Pragmatic.MultiTenancy;
using Pragmatic.MultiTenancy.Persistence;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     Two organisations ask at the same time and each answer lands in its own database.
/// </summary>
/// <remarks>
///     <para>
///         <b>The question only these two features together ask.</b> A consumer has no request, so it
///         has no claim, no header and no route to take a tenant from — and yet it has to write into one
///         organisation's database out of several. The answer is that the tenant travels <b>with the
///         message</b>: the outbox row carries it, the transport puts it in <c>X-Pragmatic-TenantId</c>,
///         <c>TransportSubscriptionBinder</c> restores it into the consume scope, and the connection
///         interceptor routes the write from there. Nothing in the handler, the operation or the entity
///         mentions any of it.
///     </para>
///     <para>
///         ⚠️ The fragile link is the first: EF's internal service provider has no tenant, so an outbox
///         interceptor that asked it would get null, every message would cross without one and every
///         consumer would write rows belonging to nobody. <c>AVerificationIsAskedFor</c> asserts that
///         link on its own, on the outbox row; this class is the proof that the whole chain holds.
///     </para>
/// </remarks>
public sealed class TheTenantTravelsOnTheMessage(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";


    /// <summary>Two organisations with a database of their own in <b>both</b> services…</summary>
    private const string Northwind = "northwind";

    private const string Southgate = "southgate";

    /// <summary>…and one that shares the schema, because the answer has to land right for it too.</summary>
    private const string Eastco = "eastco";

    [Fact]
    public async Task EachOrganisationsRequestLandsInItsOwnDatabase()
    {
        await WaitForSubscriberAsync("verification-requested");
        await OnboardEverywhereAsync();

        // Three organisations ask at the same time — the interleaving is the point: one ambient tenant
        // per consume scope, and a mistake shows up as two rows in one database.
        var asked = await Task.WhenAll(
            AskAsync(Northwind),
            AskAsync(Southgate),
            AskAsync(Eastco));

        var (northwind, southgate, eastco) = (asked[0], asked[1], asked[2]);

        // What the case number was taken from is part of the same question: a value generated for
        // Northwind comes from **Northwind's** sequence, in Northwind's database. Taken from the shared
        // one, three concurrent requests would create one sequence in one database, and the failure is a
        // 23505 on pg_class instead of a wrong row.
        (await SequencesInAsync(ConnectionStringFor(Northwind))).Should().Contain("Case_Number_seq",
            "the number of Northwind's case came from Northwind's own database");
        (await SequencesInAsync(ConnectionStringFor(Southgate))).Should().Contain("Case_Number_seq");

        // Each answer where its organisation's row says, read against **that** connection string.
        await EventuallyAsync(
            async () => await VerificationsForAsync(VerifyConnectionStringFor(Northwind), northwind) == 1,
            "Northwind's verification is in Northwind's database");

        await EventuallyAsync(
            async () => await VerificationsForAsync(VerifyConnectionStringFor(Southgate), southgate) == 1,
            "Southgate's verification is in Southgate's database");

        await EventuallyAsync(
            async () => await VerificationsForAsync(VerifyConnectionString, eastco) == 1,
            "and the shared-schema organisation's is in the shared database");

        // And not in each other's — which is the assertion that tells routing from a lucky write.
        (await VerificationsForAsync(VerifyConnectionStringFor(Northwind), southgate)).Should().Be(0,
            "Southgate's verification is not in Northwind's database");
        (await VerificationsForAsync(VerifyConnectionStringFor(Southgate), northwind)).Should().Be(0);
        (await VerificationsForAsync(VerifyConnectionString, northwind)).Should().Be(0,
            "nor in the shared one, which is the plausible wrong answer");

        // The tenant column is set too: on the shared schema it is the only thing separating the rows.
        (await ScalarAsync(
                VerifyConnectionString,
                $"""select "TenantId" from "Verifications" where "CaseId" = '{eastco}'"""))
            .Should().Be(Eastco);
    }

    /// <summary>
    ///     The control: a message with no tenant writes nothing, anywhere.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>"Written to the shared database" is the plausible wrong answer</b>, and it is what
    ///         would happen if nothing refused: the consume scope has no tenant, the connection
    ///         interceptor leaves the shared connection alone, and the tenant interceptor stamps an empty
    ///         string. A row belonging to nobody, in everybody's database — which is how a multi-tenant
    ///         system starts leaking.
    ///     </para>
    ///     <para>
    ///         What refuses is <b>the framework</b>: <c>TenantInterceptor</c> throws when
    ///         an <c>ITenantEntity</c> would be written with no tenant resolved and
    ///         <c>MultiTenancyOptions.RequireTenant</c> is on — the stance the read filter always took,
    ///         arriving on the write path. The message is retried and dead-lettered instead of landing in
    ///         the wrong place. Until then the only thing refusing was three lines in
    ///         <c>RecordTheVerificationRequest</c>, written because nothing else did; they are gone, and
    ///         this test passing with them gone is what says the refusal moved rather than disappeared.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AMessageWithNoTenant_WritesNothingAnywhere()
    {
        await WaitForSubscriberAsync("verification-requested");
        await OnboardEverywhereAsync();

        var orphan = Guid.CreateVersion7();

        // Published with no ambient tenant: no scope, so nothing stamps the outbox row or the header.
        using (var scope = IntakeServices.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(
                new VerificationRequested(orphan, "identity", DateTimeOffset.UtcNow.AddDays(10),
                    DateTimeOffset.UtcNow));
        }

        await Task.Delay(TimeSpan.FromSeconds(3));

        (await VerificationsForAsync(VerifyConnectionString, orphan)).Should().Be(0,
            "not in the shared database, which is where a row belonging to nobody would go");
        (await VerificationsForAsync(VerifyConnectionStringFor(Northwind), orphan)).Should().Be(0);
        (await VerificationsForAsync(VerifyConnectionStringFor(Southgate), orphan)).Should().Be(0);
    }

    /// <summary>Asks for a verification as one organisation, and answers with the case's id.</summary>
    /// <remarks>
    ///     The failure names the organisation: three requests run together, and "one of them threw" is
    ///     not a diagnosis when which one is the question.
    /// </remarks>
    private async Task<Guid> AskAsync(string tenantId)
    {
        try
        {
            return await AskCoreAsync(tenantId);
        }
        catch (Exception failed)
        {
            throw new InvalidOperationException($"{tenantId} could not ask: {failed.Message}", failed);
        }
    }

    private async Task<Guid> AskCoreAsync(string tenantId)
    {
        var caller = IntakeAs(Caseworker, tenantId);

        var created = await ReadJsonAsync(await caller.PostAsJsonAsync("api/cases", new
        {
            subject = $"A licence for a food stall in {tenantId}",
            applicant = "A. Applicant"
        }));
        var id = created.GetProperty("id").GetGuid();

        (await caller.PostAsJsonAsync($"api/cases/{id}/verifications", new { kind = "identity" }))
            .IsSuccessStatusCode.Should().BeTrue($"{tenantId} asks for a verification");

        return id;
    }

    /// <summary>
    ///     Registers the three organisations in both services, with the databases the dedicated ones need.
    /// </summary>
    /// <remarks>
    ///     Two registers and up to four databases for two organisations, which is the arithmetic of this
    ///     example: <b>2 services × N tenants</b>. In the application, onboarding does this as a process
    ///     (<c>OnboardingCrossesBothServices</c>); here it is a helper, so the test is about the message.
    /// </remarks>
    private async Task OnboardEverywhereAsync()
    {
        foreach (var tenantId in new[] { Northwind, Southgate })
        {
            await ProvisionAsync(ConnectionStringFor(tenantId), intake: true);
            await ProvisionAsync(VerifyConnectionStringFor(tenantId), intake: false);

            await OnboardAsync(IntakeServices, tenantId, ConnectionStringFor(tenantId));
            await OnboardAsync(VerifyServices, tenantId, VerifyConnectionStringFor(tenantId));
        }

        await OnboardAsync(IntakeServices, Eastco, null);
        await OnboardAsync(VerifyServices, Eastco, null);
    }

    /// <summary>Creates one database and puts one service's schema in it.</summary>
    private async Task ProvisionAsync(string connectionString, bool intake)
    {
        await (intake ? IntakeServices : VerifyServices)
            .GetRequiredService<ITenantDatabaseProvisioner>()
            .ProvisionAsync("tenant", connectionString);

        if (intake)
        {
            var options = new DbContextOptionsBuilder<Casework.Intake.Entities.IntakeDbContext>()
                .UseNpgsql(connectionString).Options;
            await using var context = new Casework.Intake.Entities.IntakeDbContext(options);
            await context.Database.EnsureCreatedAsync();
        }
        else
        {
            var options = new DbContextOptionsBuilder<VerifyDbContext>()
                .UseNpgsql(connectionString).Options;
            await using var context = new VerifyDbContext(options);
            await context.Database.EnsureCreatedAsync();
        }
    }

    /// <summary>The sequences one database holds, by name — where a generated value came from.</summary>
    private static async Task<string> SequencesInAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select string_agg(relname, ',') from pg_class where relkind = 'S'", connection);

        return (await command.ExecuteScalarAsync())?.ToString() ?? "none";
    }

    /// <summary>How many verifications one database holds for a case. A missing database holds none.</summary>
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
            """select count(*) from "Verifications" where "CaseId" = @case""", connection);
        command.Parameters.AddWithValue("case", caseId);

        return (int)(long)(await command.ExecuteScalarAsync() ?? 0L);
    }
}

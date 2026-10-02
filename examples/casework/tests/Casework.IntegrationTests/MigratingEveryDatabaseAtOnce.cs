using System.Net.Http.Json;
using Casework.Intake.Host;
using Casework.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     One run brings every database to the current schema, and says which ones it moved.
/// </summary>
/// <remarks>
///     <para>
///         <b>A deployment cannot depend on remembering how many customers there are.</b> The register is
///         the list, and one run walks it: the shared database plus every organisation that has one of
///         its own. What makes this worth a test is not that a migration works — the framework's own
///         suites cover that — but that <b>none of the databases is missed</b>.
///     </para>
///     <para>
///         ⚠️ The schema change is real: <c>Case.ApplicantEmail</c>, which the decision mail is sent
///         to. A column called <c>Migrated</c> would prove the mechanics and hide
///         the question a schema change actually raises — this one is nullable, which is what makes it
///         appliable to a table with rows in it at all (the diff engine blocks a breaking change unless
///         the application asks for <c>Force()</c>).
///     </para>
///     <para>
///         ⚠️ The databases are put <b>behind</b> with SQL rather than built from an old model: the test
///         process holds one version of the code, so "a database that is behind" is something that has to
///         be made, and dropping the column is the honest way to make it. What the run then does to them
///         is the real path.
///     </para>
/// </remarks>
public sealed class MigratingEveryDatabaseAtOnce(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Operator = "service-operator";

    /// <summary>Two organisations with a database of their own…</summary>
    private const string Kirkwall = "kirkwall";

    private const string Lerwick = "lerwick";

    /// <summary>…and the shared one, which belongs to no tenant and which the sweep never visits.</summary>
    private const string Stromness = "stromness";

    private static readonly int Active = (int)Casework.Intake.Enums.OrganisationState.Active;

    [Fact]
    public async Task OneRunLeavesEveryDatabaseAtTheNewSchema()
    {
        await OnboardThroughTheApiAsync(Kirkwall, dedicated: true);
        await OnboardThroughTheApiAsync(Lerwick, dedicated: true);
        await OnboardThroughTheApiAsync(Stromness, dedicated: false);

        // ⚠️ Waited for, and not a detail of the test: the sweep visits **active** organisations, and an
        // onboarding is only over when the other service answers. Without this the run is correct and
        // the report is short, with an organisation still Provisioning and its database untouched.
        await EventuallyAsync(
            async () => await StateOfAsync(IntakeConnectionString, Kirkwall) == Active
                && await StateOfAsync(IntakeConnectionString, Lerwick) == Active,
            "both organisations finished onboarding, so the sweep will visit them");

        // Three databases, put one schema version behind.
        await PutBehindAsync(IntakeConnectionString);
        await PutBehindAsync(ConnectionStringFor(Kirkwall));
        await PutBehindAsync(ConnectionStringFor(Lerwick));

        (await HasTheColumnAsync(ConnectionStringFor(Kirkwall))).Should().BeFalse(
            "the test starts from a database that is genuinely behind, or it measures nothing");

        var report = await IntakeServices.GetRequiredService<TheMigrationOfEveryDatabase>().RunAsync();

        // Every one of them, asserted by asking the column — not by the run's own word for it. The
        // failure carries the report, because "this database is behind" without "and here is what the
        // run said about it" is a second run away from a diagnosis.
        var said = Describe(report);

        (await HasTheColumnAsync(IntakeConnectionString)).Should().BeTrue(
            "the shared database belongs to no tenant, so the per-tenant sweep never visits it. {0}", said);
        (await HasTheColumnAsync(ConnectionStringFor(Kirkwall))).Should().BeTrue("{0}", said);
        (await HasTheColumnAsync(ConnectionStringFor(Lerwick))).Should().BeTrue("{0}", said);

        // And the report names the databases it moved, because a run that skips one silently is the
        // failure this story exists to catch.
        report.Should().HaveCountGreaterOrEqualTo(3);
        report.Should().OnlyContain(line => line.Succeeded);
        report.Select(line => line.Tenant).Should().Contain([null, Kirkwall, Lerwick]);
        report.Select(line => line.Database).Should().Contain(
        [
            DatabaseIn(IntakeConnectionString),
            DatabaseIn(ConnectionStringFor(Kirkwall)),
            DatabaseIn(ConnectionStringFor(Lerwick))
        ]);

        // The shared-schema organisation is not a line of its own and must not be: its rows are in the
        // database the first line names, and counting it twice would report four databases for three.
        report.Select(line => line.Tenant).Should().NotContain(Stromness);

        // The column is not merely there: the application can write it, which is what the migration was
        // for. Stromness is on the shared schema, so this also reads back through the database the
        // first line of the report named.
        var opened = await ReadJsonAsync(await IntakeAs("caseworker", Stromness).PostAsJsonAsync(
            "api/cases",
            new
            {
                subject = "A licence for a stall by the harbour",
                applicant = "S. Applicant",
                applicantEmail = "s.applicant@example.org"
            }));

        (await ScalarAsync(
                IntakeConnectionString,
                $"""select "ApplicantEmail" from "Cases" where "PersistenceId" = '{opened.GetProperty("id").GetGuid()}'"""))
            .Should().Be("s.applicant@example.org");
    }

    /// <summary>
    ///     The other service moves the same way, to a schema of its own.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Two services, two schemas, two runs — and that is the point rather than a repetition:
    ///     <c>VerifyDatabaseSchema.Current</c> is a different object from Intake's, each host migrates
    ///     only what it owns, and neither can apply the other's. An example that migrated one service
    ///     would leave the harder half of "N databases" unsaid, because N here is
    ///     <b>services × tenants</b>.
    /// </remarks>
    [Fact]
    public async Task TheOtherServiceMigratesItsOwnDatabasesToItsOwnSchema()
    {
        const string sanday = "sanday";

        await OnboardThroughTheApiAsync(sanday, dedicated: true);

        await EventuallyAsync(
            async () => await StateOfAsync(VerifyConnectionString, sanday)
                == (int)Casework.Verify.Enums.OrganisationState.Active,
            "Verify finished its half of the onboarding, so its sweep will visit the organisation");

        await PutVerifyBehindAsync(VerifyConnectionString);
        await PutVerifyBehindAsync(VerifyConnectionStringFor(sanday));

        (await HasVerifysColumnAsync(VerifyConnectionStringFor(sanday))).Should().BeFalse(
            "the database starts behind, or this measures nothing");

        var report = await VerifyServices
            .GetRequiredService<Casework.Verify.Host.TheMigrationOfEveryDatabase>()
            .RunAsync();

        (await HasVerifysColumnAsync(VerifyConnectionString)).Should().BeTrue();
        (await HasVerifysColumnAsync(VerifyConnectionStringFor(sanday))).Should().BeTrue();

        report.Select(line => line.Tenant).Should().Contain(sanday);
    }

    /// <summary>
    ///     An organisation onboarded after the migration gets the new schema from the start.
    /// </summary>
    /// <remarks>
    ///     Onboarding provisions and migrates in one go, so a database made today is at today's
    ///     schema — there is no window in which a new customer is behind. Asserted separately because it
    ///     is a different mechanism: the sweep visits databases that exist, this one makes them.
    /// </remarks>
    [Fact]
    public async Task AnOrganisationOnboardedAfterwardsStartsAtTheNewSchema()
    {
        const string westray = "westray";

        await OnboardThroughTheApiAsync(westray, dedicated: true);

        (await HasTheColumnAsync(ConnectionStringFor(westray))).Should().BeTrue(
            "the database was created and migrated by the onboarding, not by a later sweep");
    }

    /// <summary>The run's report on one line, for a failure message.</summary>
    private static string Describe(IEnumerable<Pragmatic.Migrations.Tenant.MigratedDatabase> report)
        => "the run reported: " + string.Join(
            "; ",
            report.Select(line =>
                $"{line.Database}{(line.Tenant is null ? " (shared)" : $" [{line.Tenant}]")} "
                + $"{(line.Succeeded ? "ok" : "FAILED")} {line.Changes} change(s)"
                + (line.Error is null ? "" : $" — {line.Error}")));

    private async Task OnboardThroughTheApiAsync(string tenantKey, bool dedicated)
        => (await IntakeAs(Operator).PostAsJsonAsync("api/organisations", new
            {
                tenantKey,
                name = tenantKey,
                dedicatedDatabase = dedicated
            }))
            .IsSuccessStatusCode.Should().BeTrue($"{tenantKey} is onboarded");

    /// <summary>Takes one database back a schema version, by dropping the column this story added.</summary>
    private static async Task PutBehindAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """alter table "Cases" drop column if exists "ApplicantEmail" """, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>The same, for Verify's own schema change.</summary>
    private static async Task PutVerifyBehindAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """alter table "Verifications" drop column if exists "AnsweredBy" """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> HasVerifysColumnAsync(string connectionString)
        => await ScalarAsync(
            connectionString,
            """
            select 1 from information_schema.columns
            where table_name = 'Verifications' and column_name = 'AnsweredBy'
            """) is not null;

    private static async Task<bool> HasTheColumnAsync(string connectionString)
        => await ScalarAsync(
            connectionString,
            """
            select 1 from information_schema.columns
            where table_name = 'Cases' and column_name = 'ApplicantEmail'
            """) is not null;
}

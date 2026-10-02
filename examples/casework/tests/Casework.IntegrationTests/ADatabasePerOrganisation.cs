using System.Net;
using System.Net.Http.Json;
using Casework.Intake;
using Casework.Intake.Entities;
using Casework.Intake.Host;
using Casework.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.MultiTenancy.Persistence;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     One organisation per database, and a small one left on the shared schema.
/// </summary>
/// <remarks>
///     <para>
///         <b>The model is hybrid and that is the subject.</b> Two organisations here have a database of
///         their own; two stay on the shared one with row-level isolation. An example that showed one
///         mode would tell half the truth, and the framework's own words are "tenants without a
///         connection string continue using row-level isolation on the shared database".
///     </para>
///     <para>
///         ⚠️ The routing has <b>no wiring to find</b>: <c>UseDbPerTenant</c> registers a
///         <c>TenantConnectionInterceptor</c>, EF Core discovers it from DI, and it rewrites the
///         connection string on each connection open. Nothing in the DbContext registration changes and
///         nothing in the module knows. A reader looking for the seam will not find one, which is why
///         the host says so where the call is.
///     </para>
///     <para>
///         The organisations are registered through the application's own <c>ITenantStore</c> — the
///         register on the shared database — because that is what a deployment does and what
///         <c>OnboardAnOrganisationAction</c> does. Seeding rows behind its back would test a store nobody uses.
///     </para>
/// </remarks>
public sealed class ADatabasePerOrganisation(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    /// <summary>Two with a database of their own…</summary>
    private const string Acme = "acme";

    private const string Globex = "globex";

    /// <summary>…and two on the shared schema, which is what makes the isolation test worth running.</summary>
    private const string Tinyco = "tinyco";

    private const string Minorco = "minorco";

    [Fact]
    public async Task EachOrganisationsCasesAreWhereItsRowSays()
    {
        await OnboardAsync();

        var acme = await ACaseAsync(Acme, "A licence for a food stall in Acme");
        var globex = await ACaseAsync(Globex, "A licence for a food stall in Globex");
        var tinyco = await ACaseAsync(Tinyco, "A licence for a food stall in Tinyco");

        // A dedicated organisation's case is in its own database — asserted against **that** connection
        // string, which is the only assertion that can tell routing from a tenant column.
        (await CasesInAsync(ConnectionStringFor(Acme))).Should().Be(1,
            "Acme's case is in Acme's database, because Acme's row names one");
        (await CasesInAsync(ConnectionStringFor(Globex))).Should().Be(1);

        // And it is not in the shared one.
        (await ScalarAsync(
                IntakeConnectionString,
                $"""select count(*) from "Cases" where "PersistenceId" = '{acme}'"""))
            .Should().Be(0L, "a dedicated organisation's rows are not in the shared database at all — "
                             + "which is the difference between this and a tenant column");

        // The shared-schema organisation's case is in the shared database, with its tenant column set:
        // row-level isolation, in the same deployment.
        (await ScalarAsync(
                IntakeConnectionString,
                $"""select "TenantId" from "Cases" where "PersistenceId" = '{tinyco}'"""))
            .Should().Be(Tinyco, "an organisation without a connection string stays on the shared schema");

        (await CasesInAsync(ConnectionStringFor(Tinyco))).Should().Be(0,
            "and no database of its own was provisioned for it");

        // Every organisation reads its own case back and nothing else.
        (await ReadJsonAsync(await IntakeAs(Caseworker, Acme).GetAsync($"api/cases/{acme}")))
            .GetProperty("subject").GetString().Should().Contain("Acme");
        (await ReadJsonAsync(await IntakeAs(Caseworker, Globex).GetAsync($"api/cases/{globex}")))
            .GetProperty("subject").GetString().Should().Contain("Globex");
    }

    /// <summary>
    ///     No organisation ever reads another's case — including across the two on the shared schema.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two halves, because the two isolation models fail differently. Across <b>databases</b> a
    ///         mistake needs a connection string, and the request simply looks in the wrong place. On the
    ///         <b>shared schema</b> the rows are next to each other and only a <c>where</c> keeps them
    ///         apart — so the nasty case is two organisations there, and the read that tries hardest to
    ///         see everything.
    ///     </para>
    ///     <para>
    ///         ⚠️ <c>FilterMode.Admin</c> is that read: it lifts ownership and every permission-based
    ///         filter — "all data within the tenant" — and <b>keeps</b> the tenant
    ///         (<c>FilterContext.SkipTenant</c> is <c>Mode &gt;= Background</c>, and <c>Admin</c> is
    ///         below it). An administrator of one organisation is still an administrator of one
    ///         organisation, and if that were not true the whole shared-schema half of this deployment
    ///         would be a single database with a convention on top.
    ///     </para>
    ///     <para>
    ///         ⚠️ The assertion is "sees mine, does not see theirs" and not "sees exactly mine": the tests
    ///         of this class share one database, and the other test may already have left a case of this
    ///         same organisation in it. An exact-set assertion over shared state measures
    ///         the order the tests ran in.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task NoOrganisationReadsAnothersCase()
    {
        await OnboardAsync();

        var mine = await ACaseAsync(Tinyco, "A licence for a food stall in Tinyco");
        var theirs = await ACaseAsync(Minorco, "A licence for a food stall in Minorco");
        var dedicated = await ACaseAsync(Acme, "A licence for a food stall in Acme");

        // Across the shared schema, over HTTP.
        (await IntakeAs(Caseworker, Tinyco).GetAsync($"api/cases/{theirs}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound,
                "two organisations on one schema are kept apart by the filter, not by luck");

        // Across databases, over HTTP.
        (await IntakeAs(Caseworker, Tinyco).GetAsync($"api/cases/{dedicated}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "and a case in another database is not there to find");

        // And the nasty one: the most permissive read this application has, inside one organisation.
        using var scope = IntakeServices.CreateScope();
        var cases = scope.ServiceProvider.GetRequiredService<IReadRepository<Casework.Intake.Entities.Case>>();
        var filters = scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>();

        using (TenantScope.BeginScope(Tinyco))
        using (filters.UseMode(FilterMode.Admin))
        {
            var everything = (await cases.FindAsync(Spec<Casework.Intake.Entities.Case>.Where(_ => true)))
                .Select(@case => @case.Id)
                .ToList();

            everything.Should().Contain(mine, "the read did happen and saw this organisation's case");
            everything.Should().NotContain(theirs,
                "an administrator of one organisation sees that organisation — FilterMode.Admin lifts "
                + "ownership and permissions and keeps the tenant, and the whole shared-schema half of "
                + "this deployment rests on it");
            everything.Should().NotContain(dedicated, "and another organisation's database is not read");
        }
    }

    /// <summary>
    ///     A document of one organisation is not reachable from another, by key or otherwise.
    /// </summary>
    /// <remarks>
    ///     The tenant is in the storage key's container (<c>cases/{tenant}/{case}</c>), so the bytes of
    ///     two organisations are never in one place — this is the assertion that says so. The route
    ///     refuses first: the case is read through the tenant-filtered repository, so a caller of
    ///     another organisation gets 404 before any key is composed.
    /// </remarks>
    [Fact]
    public async Task ADocumentOfOneOrganisationIsNotReachableFromAnother()
    {
        await OnboardAsync();

        var acme = await ACaseAsync(Acme, "A licence for a food stall in Acme");

        var uploaded = await IntakeAs(Caseworker, Acme).PostAsync($"api/cases/{acme}/documents", AScan());
        uploaded.IsSuccessStatusCode.Should().BeTrue(
            $"Acme attaches a document: {await uploaded.Content.ReadAsStringAsync()}");
        var document = (await ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();

        // Acme reads it back.
        (await IntakeAs(Caseworker, Acme).GetAsync($"api/cases/{acme}/documents/{document}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        // Globex knows both ids and gets nothing.
        (await IntakeAs(Caseworker, Globex).GetAsync($"api/cases/{acme}/documents/{document}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound,
                "the case is not in Globex's database, so there is nothing to serve — and the key the "
                + "bytes are under carries Acme's name anyway");

        // And the same for an organisation on the shared schema, where the rows are side by side.
        (await IntakeAs(Caseworker, Tinyco).GetAsync($"api/cases/{acme}/documents/{document}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>The upload, as the document tests send it.</summary>
    private static MultipartFormDataContent AScan()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([0x25, 0x50, 0x44, 0x46, .. "a scan"u8]);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(file, "File", "identity-card.pdf");

        return form;
    }

    /// <summary>
    ///     Registers the four organisations: two with a database of their own, two without.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Through the host's own store, which writes the register on the shared database — because
    ///         that is what a deployment does and what <c>OnboardAnOrganisationAction</c> does.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>The dedicated databases are created and schema'd here, on purpose.</b> There is no
    ///         "provision on first access" in the framework. <c>PostgresTenantProvisioner</c> — which an application opts
    ///         into with <c>UseAutoProvision&lt;T&gt;()</c> — runs <c>CREATE DATABASE</c> and no
    ///         migration, because the schema to migrate to is generated into the host and no framework
    ///         assembly can name it. So this helper does both explicitly, which is what onboarding does
    ///         for one organisation and <c>TheMigrationOfEveryDatabase</c> does for the schema of all of
    ///         them at once.
    ///     </para>
    /// </remarks>
    private async Task OnboardAsync()
    {
        var store = IntakeServices.GetRequiredService<ITenantStore>();

        foreach (var (tenantId, connectionString) in new (string, string?)[]
                 {
                     (Acme, ConnectionStringFor(Acme)),
                     (Globex, ConnectionStringFor(Globex)),
                     (Tinyco, null),
                     (Minorco, null)
                 })
        {
            if (await store.GetByIdAsync(tenantId) is not null)
                continue;

            if (connectionString is not null)
                await ProvisionAsync(connectionString);

            await store.CreateAsync(new TenantInfo
            {
                TenantId = tenantId,
                TenantName = tenantId,
                ConnectionString = connectionString,
                State = TenantState.Active,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    /// <summary>Creates one organisation's database and puts this boundary's schema in it.</summary>
    /// <remarks>
    ///     The provisioner is the framework's own — it is what <c>UseAutoProvision&lt;T&gt;()</c>
    ///     registers — and the schema comes from the generated <c>DbContext</c> pointed at that database.
    ///     ⚠️ <c>EnsureCreated</c> and not the migration runner: the runner migrates the database the
    ///     host was configured with, and migrating N of them is <c>TheMigrationOfEveryDatabase</c>. What this test needs is a
    ///     schema, and what it is asserting is where the rows land.
    /// </remarks>
    private async Task ProvisionAsync(string connectionString)
    {
        await IntakeServices.GetRequiredService<ITenantDatabaseProvisioner>()
            .ProvisionAsync("tenant", connectionString);

        var options = new DbContextOptionsBuilder<IntakeDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var context = new IntakeDbContext(options);
        await context.Database.EnsureCreatedAsync();
    }

    private async Task<Guid> ACaseAsync(string tenantId, string subject)
    {
        var created = await ReadJsonAsync(await IntakeAs(Caseworker, tenantId).PostAsJsonAsync("api/cases", new
        {
            subject,
            applicant = "A. Applicant"
        }));

        return created.GetProperty("id").GetGuid();
    }

    private static async Task<int> CasesInAsync(string connectionString)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);

        try
        {
            await connection.OpenAsync();
        }
        catch (Npgsql.PostgresException failed) when (failed.SqlState == "3D000")
        {
            // 3D000 is "database does not exist", which for an organisation on the shared schema is the
            // right answer and not a failure: nothing was provisioned for it.
            return 0;
        }

        await using var command = new Npgsql.NpgsqlCommand("""select count(*) from "Cases" """, connection);

        return (int)(long)(await command.ExecuteScalarAsync() ?? 0L);
    }
}

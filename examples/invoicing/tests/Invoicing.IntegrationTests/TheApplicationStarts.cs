using Invoicing.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pragmatic.Jobs;
using Pragmatic.Jobs.EFCore;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     The application starts on an empty database, publishes its contract, and carries both
///     modules on the one database.
/// </summary>
/// <remarks>
///     The skeleton has no operation yet, so the document lists no routes: what is asserted is that the
///     document is served and that both modules were composed — the schema of the one database holds a
///     table of each. A host that included only one module starts and serves a document just the same,
///     which is why the second assertion is the one that says "two modules, one host".
/// </remarks>
public sealed class TheApplicationStarts(PostgresFixture database) : InvoicingTestBase(database)
{
    [Fact]
    public async Task OnAnEmptyDatabase_ItStarts_AndServesItsOpenApiDocument()
    {
        var document = await ReadJsonAsync(await Client.GetAsync("/openapi/v1.json"));

        document.GetProperty("openapi").GetString().Should().StartWith("3.");
    }

    [Fact]
    public async Task BothModules_AreMigratedOntoTheOneDatabase()
    {
        // The host migrates at startup; asking the client for the document is what waits for it.
        await ReadJsonAsync(await Client.GetAsync("/openapi/v1.json"));

        var tables = await TablesAsync();

        tables.Should().Contain("Organizations", "Registry's entity is on the one database");
        tables.Should().Contain("Invoices", "Billing's entity is on the same one");
    }

    /// <summary>
    ///     The durable job store's tables are part of this application's schema, and a
    ///     recurring definition registered through the framework's own registrar is a row an operator can
    ///     read, with the next occurrence in it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The chain asserted end to end: <c>[EnableJobPersistence]</c> on the boundary → the two
    ///         configurations in the generated DbContext → the migration that creates
    ///         <c>__Jobs</c>/<c>__RecurringJobs</c> → the unkeyed <c>DbContext</c> the EF store resolves →
    ///         the generated <c>IRecurringJobProvider</c> → the row.
    ///     </para>
    ///     <para>
    ///         ⚠️ Through the registrar and not through the scheduler, deliberately: the scheduler waits
    ///         two seconds before its first pass by design, and a test that waited for it would be
    ///         asserting that delay as much as this wiring. The registrar is the
    ///         very thing the scheduler calls.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheDurableJobStore_HasItsTables_AndTheScheduleIsARowWithItsNextOccurrence()
    {
        await ReadJsonAsync(await Client.GetAsync("/openapi/v1.json"));

        var tables = await TablesAsync();
        tables.Should().Contain("__Jobs", "a job enqueued and not yet run has to survive a restart");
        tables.Should().Contain("__RecurringJobs", "and so does the schedule an operator reads");

        using var scope = Services.CreateScope();
        Services.GetRequiredService<IRecurringJobStore>().Should().BeOfType<EfCoreRecurringJobStore>(
            "UseEfCore() + UseEfCorePersistence() asked for the durable store, and a silent fall back to "
            + "memory is what shipped an application without the persistence it asked for");

        var registrar = scope.ServiceProvider.GetRequiredService<IRecurringJobRegistrar>();
        foreach (var definition in scope.ServiceProvider.GetServices<IRecurringJobProvider>()
                     .SelectMany(provider => provider.GetDefinitions()))
        {
            (await registrar.RegisterAsync(definition)).Should().BeTrue();
        }

        var (cron, next) = await ScheduleAsync("chase-overdue-invoices");

        cron.Should().Be("0 7 * * *", "the cron the [RecurringJob] declares, persisted as declared");
        next.Should().NotBeNull("the first occurrence is seeded on registration — a schedule with no next "
                                + "execution is a definition the poll never returns");
    }

    /// <summary>The persisted schedule, read with SQL: what an operator sees, not what the store remembers.</summary>
    private async Task<(string Cron, DateTimeOffset? Next)> ScheduleAsync(string id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """select "CronExpression", "NextExecutionAt" from "__RecurringJobs" where "Id" = @id""",
            connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue($"'{id}' should have a row of its own");

        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
    }

    private async Task<List<string>> TablesAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "select table_name from information_schema.tables where table_schema = 'public'", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var tables = new List<string>();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));

        return tables;
    }
}

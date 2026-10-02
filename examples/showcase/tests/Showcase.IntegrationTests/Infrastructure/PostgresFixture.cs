using Npgsql;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Sql;
using Testcontainers.PostgreSql;

namespace Showcase.IntegrationTests.Infrastructure;

/// <summary>
///     Shared PostgreSQL container for all integration tests.
///     Started once per test run via xUnit CollectionFixture, reused across all test classes.
///     Creates both databases and applies Pragmatic Migrations (no EF Core migrations).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("showcase_app")
        .WithUsername("pragmatic")
        .WithPassword("Pragmatic@Test!")
        .Build();

    /// <summary>
    ///     Connection string for the App database (Catalog + Booking + Accounts boundaries).
    /// </summary>
    public string AppConnectionString { get; private set; } = null!;

    /// <summary>
    ///     Connection string for the Financial database (Billing boundary).
    /// </summary>
    public string FinancialConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);

        AppConnectionString = _container.GetConnectionString();

        // Create the financial database in the same container
        var conn = new NpgsqlConnection(AppConnectionString);
        await using (conn.ConfigureAwait(false))
        {
            await conn.OpenAsync().ConfigureAwait(false);
            var cmd = conn.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = "CREATE DATABASE showcase_financial;";
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        // Build financial connection string (same host/port, different database)
        var builder = new NpgsqlConnectionStringBuilder(AppConnectionString)
        {
            Database = "showcase_financial"
        };
        FinancialConnectionString = builder.ConnectionString;

        // Apply schema via Pragmatic Migrations (no EF Core migrations needed)
        await ApplyPragmaticMigrationsAsync().ConfigureAwait(false);

        // Reference data, before any host boots — see SeedLookupCategoriesAsync.
        await SeedLookupCategoriesAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Writes the <c>[Lookup]</c> rows the Catalog declares, once, into the App database.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A lookup table is reference data: present when the application starts, read once by the
    ///         preload, never written by the app. Seeding it here reproduces that order without a second
    ///         hosted service racing the preload — the container is up, the schema is applied, and only
    ///         then does a host boot.
    ///     </para>
    ///     <para>
    ///         ⚠️ Without these rows the one declared lookup would be empty, so the loader would run
    ///         against an empty table and the mechanism would stay green with its <c>Load</c> call deleted.
    ///     </para>
    /// </remarks>
    private async Task SeedLookupCategoriesAsync()
    {
        var connection = new NpgsqlConnection(AppConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync().ConfigureAwait(false);

            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = """
                    INSERT INTO "Categories" ("PersistenceId", "Name", "Description", "Level", "Path", "IsDeleted", "ParentId")
                    VALUES (@rootId, @rootName, 'Seeded lookup reference data', 0, '/resort', false, NULL),
                           (@childId, @childName, 'Seeded lookup reference data', 1, '/resort/beach-resort', false, @rootId)
                    ON CONFLICT ("PersistenceId") DO NOTHING;
                    """;
                cmd.Parameters.AddWithValue("rootId", SeededCategories.ResortId);
                cmd.Parameters.AddWithValue("rootName", SeededCategories.ResortName);
                cmd.Parameters.AddWithValue("childId", SeededCategories.BeachResortId);
                cmd.Parameters.AddWithValue("childName", SeededCategories.BeachResortName);

                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     The host every test shares, built once for the collection.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Built here and not in <c>IntegrationTestBase.InitializeAsync</c>: xUnit constructs a new
    ///         instance of a test class for every test method, so a <c>ShowcaseWebFactory</c> built there
    ///         would build and dispose a whole ASP.NET host <b>585 times</b>. Measured that way, the
    ///         suite ran 240.7s of which 69.2s was test execution, and one test alone cost 7.12s
    ///         against 14.57s for fifteen of the same class — about <b>446 ms of host per test</b>.
    ///     </para>
    ///     <para>
    ///         ⚠️ What this shares is the DI <b>root</b>, so singletons live for the run: caches,
    ///         in-memory stores, background job state. Scoped services are per request, and the
    ///         database is shared by every test through this same fixture anyway. A class that
    ///         genuinely needs cold singletons says so with
    ///         <c>IntegrationTestBase.NeedsItsOwnHost</c> rather than being quietly special.
    ///     </para>
    /// </remarks>
    public ShowcaseWebFactory SharedHost => _sharedHost ??= new ShowcaseWebFactory(this);

    private ShowcaseWebFactory? _sharedHost;

    public async Task DisposeAsync()
    {
        if (_sharedHost is not null)
            await _sharedHost.DisposeAsync().ConfigureAwait(false);

        await _container.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ApplyPragmaticMigrationsAsync()
    {
        var introspector = new PostgreSqlSchemaIntrospector();
        var diffEngine = new SchemaDiffEngine();
        var sqlGenerator = new PostgreSqlMigrationGenerator();
        var options = new MigrationOptions { Force = true };

        // App database (Catalog + Booking + Accounts)
        await MigrateDatabaseAsync(
            AppConnectionString,
            Showcase.ShowcaseAppDatabaseSchema.Current,
            introspector, diffEngine, sqlGenerator, options).ConfigureAwait(false);

        // Financial database (Billing)
        await MigrateDatabaseAsync(
            FinancialConnectionString,
            Showcase.ShowcaseFinancialDatabaseSchema.Current,
            introspector, diffEngine, sqlGenerator, options).ConfigureAwait(false);

    }

    private static async Task MigrateDatabaseAsync(
        string connectionString,
        Pragmatic.Migrations.Schema.SchemaVersion desiredSchema,
        ISchemaIntrospector introspector,
        ISchemaDiffEngine diffEngine,
        ISqlMigrationGenerator sqlGenerator,
        MigrationOptions options)
    {
        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync().ConfigureAwait(false);

            var current = await introspector.IntrospectAsync(connection).ConfigureAwait(false);
            var diff = diffEngine.ComputeDiff(desiredSchema, current);

            if (!diff.HasChanges)
                return;

            var sql = sqlGenerator.GenerateScript(diff);

            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            // Record in audit table
            await SchemaAuditStore.EnsureTableAsync(connection, sqlGenerator).ConfigureAwait(false);
            await SchemaAuditStore.RecordMigrationAsync(
                connection, desiredSchema, sql, diff.Changes.Length, 0).ConfigureAwait(false);
        }
    }
}

using Pragmatic.Testing.Assertions;
using Npgsql;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Sql;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Migrations;

/// <summary>
///     E2E tests for the Pragmatic Migrations pipeline on a real PostgreSQL database.
///     Uses the shared Testcontainers fixture with actual EF Core-migrated schema.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class MigrationPipelineTests(PostgresFixture fixture)
{
    private readonly PostgreSqlSchemaIntrospector _introspector = new();
    private readonly SchemaDiffEngine _diffEngine = new();
    private readonly PostgreSqlMigrationGenerator _sqlGenerator = new();

    [Fact]
    public async Task Introspect_RealDatabase_ReturnsTablesAndColumns()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();

        var schema = await _introspector.IntrospectAsync(connection);

        schema.Tables.Should().NotBeEmpty("database has EF Core-migrated tables");
        schema.Hash.Should().NotBeNullOrEmpty();

        // Verify known tables exist
        var tableNames = schema.Tables.Select(t => t.Name).ToList();
        tableNames.Should().Contain("Reservations", "Booking boundary has Reservations");
        tableNames.Should().Contain("Guests", "Booking boundary has Guests");
        tableNames.Should().Contain("Properties", "Catalog boundary has Properties");
    }

    [Fact]
    public async Task Introspect_ReturnsColumnsWithCorrectTypes()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();

        var schema = await _introspector.IntrospectAsync(connection);

        var guests = schema.Tables.FirstOrDefault(t => t.Name == "Guests");
        guests.Should().NotBeNull();

        var columns = guests!.Columns;
        columns.Should().Contain(c => c.Name == "PersistenceId" && c.IsPrimaryKey, "PersistenceId is PK");
        columns.Should().HaveCountGreaterThan(1, "Guests should have multiple columns");
    }

    [Fact]
    public async Task Introspect_ReturnsIndexes()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();

        var schema = await _introspector.IntrospectAsync(connection);

        var guests = schema.Tables.FirstOrDefault(t => t.Name == "Guests");
        guests.Should().NotBeNull();

        guests!.Indexes.Should().NotBeEmpty("Guests should have indexes (at least logic key + IsDeleted)");
    }

    [Fact]
    public async Task Introspect_ReturnsForeignKeys()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();

        var schema = await _introspector.IntrospectAsync(connection);

        var reservations = schema.Tables.FirstOrDefault(t => t.Name == "Reservations");
        reservations.Should().NotBeNull();

        reservations!.ForeignKeys.Should().NotBeEmpty("Reservations references Guests and Properties");
    }

    [Fact]
    public async Task Introspect_SameDatabase_ProducesSameHash()
    {
        await using var conn1 = new NpgsqlConnection(fixture.AppConnectionString);
        await conn1.OpenAsync();
        var schema1 = await _introspector.IntrospectAsync(conn1);

        await using var conn2 = new NpgsqlConnection(fixture.AppConnectionString);
        await conn2.OpenAsync();
        var schema2 = await _introspector.IntrospectAsync(conn2);

        schema1.Hash.Should().Be(schema2.Hash, "same database state should produce same hash");
    }

    [Fact]
    public async Task Diff_IdenticalSchema_ProducesNoChanges()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();

        var introspected = await _introspector.IntrospectAsync(connection);

        // Re-created from the same tables rather than reusing the instance, so the engine has to
        // reach its conclusion by comparing structures rather than by reference.
        var desired = new Pragmatic.Migrations.Schema.SchemaVersion(introspected.Tables);
        var diff = _diffEngine.ComputeDiff(desired, introspected);

        diff.HasChanges.Should().BeFalse("structurally identical schemas produce zero changes");
        desired.Hash.Should().Be(introspected.Hash, "and their canonical identities agree");
    }

    [Fact]
    public async Task Diff_WithNewTable_ProducesCreateTable()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();

        var current = await _introspector.IntrospectAsync(connection);

        // Add a new table to the desired schema
        var newTable = new Pragmatic.Migrations.Schema.TableSchema(
            "__MigrationTest",
            "public",
            [
                new Pragmatic.Migrations.Schema.ColumnSchema("Id", "uuid", false, true),
                new Pragmatic.Migrations.Schema.ColumnSchema("Name", "text", false, false)
            ],
            [],
            []);

        var desired = current with { Tables = current.Tables.Add(newTable) };

        var diff = _diffEngine.ComputeDiff(desired, current);

        diff.HasChanges.Should().BeTrue();
        diff.Changes.OfType<Pragmatic.Migrations.Diff.Changes.CreateTable>()
            .Should().ContainSingle(ct => ct.Table.Name == "__MigrationTest");
        diff.HasBreakingChanges.Should().BeFalse("adding a table is not breaking");
    }

    [Fact]
    public async Task FullPipeline_CreateTable_ApplyAndVerify()
    {
        // Create a separate test database to avoid polluting the shared fixture
        await using var setupConn = new NpgsqlConnection(fixture.AppConnectionString);
        await setupConn.OpenAsync();

        var cmd = setupConn.CreateCommand();
        cmd.CommandText = "CREATE DATABASE migration_test_db;";
        try { await cmd.ExecuteNonQueryAsync(); } catch { /* may already exist */ }

        var testConnStr = new NpgsqlConnectionStringBuilder(fixture.AppConnectionString)
        {
            Database = "migration_test_db"
        }.ConnectionString;

        // Phase 1: Introspect empty database
        await using var conn1 = new NpgsqlConnection(testConnStr);
        await conn1.OpenAsync();
        var emptySchema = await _introspector.IntrospectAsync(conn1);

        // Phase 2: Define desired schema
        var desired = new Pragmatic.Migrations.Schema.SchemaVersion(
            [
                new Pragmatic.Migrations.Schema.TableSchema("TestUsers", "public",
                    [
                        new Pragmatic.Migrations.Schema.ColumnSchema("Id", "uuid", false, true),
                        new Pragmatic.Migrations.Schema.ColumnSchema("Email", "varchar(256)", false, false),
                        new Pragmatic.Migrations.Schema.ColumnSchema("IsActive", "boolean", false, false, "true")
                    ],
                    [
                        new Pragmatic.Migrations.Schema.IndexSchema("IX_TestUsers_Email",
                            System.Collections.Immutable.ImmutableArray.Create("Email"), true)
                    ],
                    [])
            ],
            DatabaseName: "migration_test_db",
            ProviderName: "PostgreSql");

        // Phase 3: Diff
        var diff = _diffEngine.ComputeDiff(desired, emptySchema);
        diff.HasChanges.Should().BeTrue();
        diff.Changes.OfType<Pragmatic.Migrations.Diff.Changes.CreateTable>().Should().ContainSingle();

        // Phase 4: Generate SQL
        var sql = _sqlGenerator.GenerateScript(diff);
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS");
        sql.Should().Contain("\"TestUsers\"");
        sql.Should().Contain("\"Email\" varchar(256) NOT NULL");

        // Phase 5: Execute SQL
        var execCmd = conn1.CreateCommand();
        execCmd.CommandText = sql;
        await execCmd.ExecuteNonQueryAsync();

        // Phase 6: Re-introspect — verify table exists
        await using var conn2 = new NpgsqlConnection(testConnStr);
        await conn2.OpenAsync();
        var afterSchema = await _introspector.IntrospectAsync(conn2);

        afterSchema.Tables.Should().Contain(t => t.Name == "TestUsers");
        var testUsers = afterSchema.Tables.First(t => t.Name == "TestUsers");
        testUsers.Columns.Should().HaveCount(3);

        // Verify column details
        testUsers.Columns.Should().Contain(c => c.Name == "Id" && c.IsPrimaryKey);
        testUsers.Columns.Should().Contain(c => c.Name == "Email");
        testUsers.Columns.Should().Contain(c => c.Name == "IsActive");

        // Verify index was created
        testUsers.Indexes.Should().ContainSingle(i => i.Name == "IX_TestUsers_Email" && i.IsUnique);
    }

    [Fact]
    public async Task FullPipeline_SqlIsIdempotent_RerunProducesNoErrors()
    {
        await using var setupConn = new NpgsqlConnection(fixture.AppConnectionString);
        await setupConn.OpenAsync();

        var cmd = setupConn.CreateCommand();
        cmd.CommandText = "CREATE DATABASE idempotent_test_db;";
        try { await cmd.ExecuteNonQueryAsync(); } catch { /* may already exist */ }

        var testConnStr = new NpgsqlConnectionStringBuilder(fixture.AppConnectionString)
        {
            Database = "idempotent_test_db"
        }.ConnectionString;

        var desired = new Pragmatic.Migrations.Schema.SchemaVersion(
            [
                new Pragmatic.Migrations.Schema.TableSchema("IdempotentTable", "public",
                    [
                        new Pragmatic.Migrations.Schema.ColumnSchema("Id", "integer", false, true),
                        new Pragmatic.Migrations.Schema.ColumnSchema("Value", "text", true, false)
                    ], [], [])
            ]);

        await using var conn = new NpgsqlConnection(testConnStr);
        await conn.OpenAsync();

        var emptySchema = await _introspector.IntrospectAsync(conn);
        var diff = _diffEngine.ComputeDiff(desired, emptySchema);
        var sql = _sqlGenerator.GenerateScript(diff);

        // Execute the same SQL twice — should not throw
        var cmd1 = conn.CreateCommand();
        cmd1.CommandText = sql;
        await cmd1.ExecuteNonQueryAsync();

        var cmd2 = conn.CreateCommand();
        cmd2.CommandText = sql;
        var act = () => cmd2.ExecuteNonQueryAsync();
        await act.Should().NotThrowAsync("idempotent SQL should be safe to re-run");
    }
}

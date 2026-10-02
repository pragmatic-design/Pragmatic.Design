#pragma warning disable CA2007

using System.Data.Common;
using Npgsql;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Sql;
using Xunit;

namespace Pragmatic.Migrations.Tests.Provider;

/// <summary>
///     PostgreSQL provider tests via Testcontainers.
/// </summary>
/// <remarks>
///     One container for the class (<see cref="PostgreSqlContainerFixture" />) and one database per
///     test: the scenarios diff a desired schema against the real database, so they cannot share one.
/// </remarks>
public class PostgreSqlScenarioTests(PostgreSqlContainerFixture fixture)
    : SchemaScenarioTestBase, IClassFixture<PostgreSqlContainerFixture>
{
    /// <summary>This test's own database. A class instance is a test, so a name here is a name per test.</summary>
    private readonly string _database = "scenario_" + Guid.NewGuid().ToString("N");

    private bool _created;

    protected override ISqlMigrationGenerator CreateGenerator() => new PostgreSqlMigrationGenerator();
    protected override ISchemaIntrospector CreateIntrospector() => new PostgreSqlSchemaIntrospector();

    protected override async Task<DbConnection> CreateConnectionAsync()
    {
        if (!_created)
        {
            var admin = new NpgsqlConnection(fixture.ConnectionString);
            await using (admin)
            {
                await admin.OpenAsync();
                var create = admin.CreateCommand();
                await using (create)
                {
                    // The name is a GUID this class made, never user input.
                    create.CommandText = $"CREATE DATABASE \"{_database}\";";
                    await create.ExecuteNonQueryAsync();
                }
            }

            _created = true;
        }

        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = _database
        };

        var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    protected override string PkType() => "uuid";
    protected override string TextType() => "text";
    protected override string IntType() => "integer";
    protected override string BoolType() => "boolean";
    protected override string DecimalType() => "numeric(18,2)";
    protected override string TimestampType() => "timestamptz";
    protected override string DefaultFalse() => "false";
    protected override string DefaultTrue() => "true";
}

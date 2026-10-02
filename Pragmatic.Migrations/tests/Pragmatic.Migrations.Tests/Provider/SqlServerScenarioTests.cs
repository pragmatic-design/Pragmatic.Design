#pragma warning disable CA2007

using System.Data.Common;
using Microsoft.Data.SqlClient;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Sql;
using Xunit;

namespace Pragmatic.Migrations.Tests.Provider;

/// <summary>
///     SQL Server provider tests via Testcontainers.
/// </summary>
/// <remarks>
///     One container for the class (<see cref="SqlServerContainerFixture" />) and one database per
///     test: the scenarios diff a desired schema against the real database, so they cannot share one.
/// </remarks>
public class SqlServerScenarioTests(SqlServerContainerFixture fixture)
    : SchemaScenarioTestBase, IClassFixture<SqlServerContainerFixture>
{
    /// <summary>This test's own database. A class instance is a test, so a name here is a name per test.</summary>
    private readonly string _database = "scenario_" + Guid.NewGuid().ToString("N");

    private bool _created;

    protected override ISqlMigrationGenerator CreateGenerator() => new SqlServerMigrationGenerator();
    protected override ISchemaIntrospector CreateIntrospector() => new SqlServerSchemaIntrospector();

    protected override async Task<DbConnection> CreateConnectionAsync()
    {
        if (!_created)
        {
            var master = new SqlConnection(fixture.ConnectionString);
            await using (master)
            {
                await master.OpenAsync();
                var create = master.CreateCommand();
                await using (create)
                {
                    // The name is a GUID this class made, never user input.
                    create.CommandText = $"CREATE DATABASE [{_database}];";
                    await create.ExecuteNonQueryAsync();
                }
            }

            _created = true;
        }

        var builder = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            InitialCatalog = _database
        };

        var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    protected override string PkType() => "uniqueidentifier";
    protected override string TextType() => "nvarchar(max)";
    protected override string IndexableTextType() => "nvarchar(450)";
    protected override string IntType() => "int";
    protected override string BoolType() => "bit";
    protected override string DecimalType() => "decimal(18,2)";
    protected override string TimestampType() => "datetimeoffset";
    protected override string DefaultFalse() => "0";
    protected override string DefaultTrue() => "1";
}

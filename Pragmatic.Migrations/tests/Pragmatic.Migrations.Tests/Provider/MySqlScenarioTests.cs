#pragma warning disable CA2007

using System.Collections.Immutable;
using System.Data.Common;
using MySqlConnector;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Migrations.Tests.Provider;

/// <summary>
///     MySQL provider tests via Testcontainers: one container for the class (<see cref="MySqlContainerFixture" />) and one
///     database per test. The test's connection runs the session setup the runner's do (ANSI_QUOTES), since the scenarios'
///     own statements quote identifiers with double quotes.
/// </summary>
public class MySqlScenarioTests(MySqlContainerFixture fixture)
    : SchemaScenarioTestBase, IClassFixture<MySqlContainerFixture>
{
    private readonly string _database = "scenario_" + Guid.NewGuid().ToString("N");

    private bool _created;

    protected override ISqlMigrationGenerator CreateGenerator() => new MySqlMigrationGenerator();
    protected override ISchemaIntrospector CreateIntrospector() => new MySqlSchemaIntrospector();

    protected override async Task<DbConnection> CreateConnectionAsync()
    {
        if (!_created)
        {
            var server = new MySqlConnection(fixture.ConnectionString);
            await using (server)
            {
                await server.OpenAsync();
                var create = server.CreateCommand();
                await using (create)
                {
                    // The name is a GUID this class made, never user input.
                    create.CommandText = $"CREATE DATABASE `{_database}`";
                    await create.ExecuteNonQueryAsync();
                }
            }

            _created = true;
        }

        var conn = new MySqlConnection(new MySqlConnectionStringBuilder(fixture.ConnectionString) { Database = _database }.ConnectionString);
        await conn.OpenAsync();
        var setup = conn.CreateCommand();
        await using (setup)
        {
            setup.CommandText = MySqlMigrationGenerator.SessionSetup;
            await setup.ExecuteNonQueryAsync();
        }

        return conn;
    }

    /// <summary>MySQL has no partial index: the filtered index is refused with its name, not created without its filter.</summary>
    [Fact]
    public override Task Scenario21_FilteredIndex_ConvergesAfterOneApply()
    {
        var index = new IndexSchema("IX_Sagas_CorrelationId", ImmutableArray.Create("CorrelationId"), true, "\"Status\" = 0");
        var script = () => CreateGenerator().GenerateScript(new SchemaDiff(ImmutableArray.Create<SchemaChange>(new AddIndex("Sagas", index)), false));

        script.Should().Throw<NotSupportedException>();
        return Task.CompletedTask;
    }

    protected override string PkType() => "char(36)";
    protected override string TextType() => "longtext";
    protected override string IndexableTextType() => "varchar(255)";
    protected override string IntType() => "int";

    protected override int? IdentifierLimit => 64;
    protected override string BoolType() => "tinyint(1)";
    protected override string DecimalType() => "decimal(18,2)";
    protected override string TimestampType() => "datetime(6)";
    protected override string DefaultFalse() => "0";
    protected override string DefaultTrue() => "1";
}

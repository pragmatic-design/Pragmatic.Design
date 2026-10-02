using System.Data.Common;
using Microsoft.Data.Sqlite;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Tests.Provider;

/// <summary>
///     SQLite provider tests — in-process, no Docker required.
///     Table rebuild handles ALTER COLUMN, FK constraints inline at CREATE TABLE.
/// </summary>
public class SqliteScenarioTests : SchemaScenarioTestBase, IDisposable
{
    private readonly SqliteConnection _sharedConnection;

    public SqliteScenarioTests()
    {
        _sharedConnection = new SqliteConnection("Data Source=:memory:");
        _sharedConnection.Open();
    }

    public void Dispose() => _sharedConnection.Dispose();

    protected override ISqlMigrationGenerator CreateGenerator() => new SqliteMigrationGenerator();
    protected override ISchemaIntrospector CreateIntrospector() => new SqliteSchemaIntrospector();

    protected override Task<DbConnection> CreateConnectionAsync()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        return Task.FromResult<DbConnection>(conn);
    }

    // Table rebuild handles nullability changes
    [Fact]
    public override Task Scenario09_ChangeNullability() => base.Scenario09_ChangeNullability();

    // FK constraints inline at CREATE TABLE + canonical FK names in introspection
    [Fact]
    public override Task Scenario04_ForeignKey() => base.Scenario04_ForeignKey();

    [Fact]
    public override Task Scenario13_SelfReferencingFk() => base.Scenario13_SelfReferencingFk();

    protected override string PkType() => "TEXT";
    protected override string TextType() => "TEXT";
    protected override string IntType() => "INTEGER";
    protected override string BoolType() => "INTEGER";
    protected override string DecimalType() => "REAL";
    protected override string TimestampType() => "TEXT";
    protected override string DefaultFalse() => "0";
    protected override string DefaultTrue() => "1";
}

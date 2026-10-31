using Pragmatic.Migrations.Configuration;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The generator registers a connection factory with Pragmatic.Migrations for every driver the compilation references, MySQL's and
///     Oracle's as PostgreSQL's, SQL Server's and SQLite's: this suite references the five, so its own generated module initializer is the
///     host's. Without it a host on MySQL or Oracle had to call <c>UseProvider</c> by hand for a driver it already references.
/// </summary>
public class MigrationDriversTests
{
    [Theory]
    [InlineData("PostgreSql", typeof(Npgsql.NpgsqlConnection))]
    [InlineData("SqlServer", typeof(Microsoft.Data.SqlClient.SqlConnection))]
    [InlineData("Sqlite", typeof(Microsoft.Data.Sqlite.SqliteConnection))]
    [InlineData("MySql", typeof(MySqlConnector.MySqlConnection))]
    [InlineData("Oracle", typeof(Oracle.ManagedDataAccess.Client.OracleConnection))]
    public void Each_referenced_driver_has_its_connection_factory(string provider, Type connection)
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(MigrationDriversTests).Module.ModuleHandle);

        var factory = MigrationDriverRegistry.Find(provider);

        Assert.NotNull(factory);
        using var opened = factory(""); // constructed, not opened
        Assert.IsType(connection, opened);
    }
}

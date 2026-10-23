#pragma warning disable CA2007

using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Migrations.Tests.Provider;

/// <summary>
///     What the MySQL provider does to the connections the runner opens, outside the migration scenarios: the scenarios'
///     own connection already runs the session setup, so they cannot tell whether the runner does.
/// </summary>
public class MySqlConnectionAndLockTests(MySqlContainerFixture fixture) : IClassFixture<MySqlContainerFixture>
{
    /// <summary>
    ///     The statements this library writes outside the generator quote identifiers with double quotes, which MySQL reads
    ///     as identifiers only under ANSI_QUOTES: the connection factory the builder registers must turn it on.
    /// </summary>
    [Fact]
    public async Task TheRunnersConnection_QuotesIdentifiersWithDoubleQuotes()
    {
        var database = await CreateDatabaseAsync();
        var services = new ServiceCollection();
        var builder = new MigrationsBuilder(services);
        builder.UseProvider(MigrationConstants.ProviderMySql, cs => new MySqlConnection(cs));
        builder.Build();

        var provider = services.BuildServiceProvider();
        await using (provider)
        {
            var factory = provider.GetRequiredKeyedService<IConnectionFactory>(MigrationConstants.ProviderMySql);
            var connection = await factory.CreateOpenConnectionAsync(ConnectionString(database));
            await using (connection)
            {
                var cmd = connection.CreateCommand();
                await using (cmd)
                {
                    cmd.CommandText = "SELECT @@SESSION.sql_mode";
                    ((string)(await cmd.ExecuteScalarAsync())!).Should().Contain("ANSI_QUOTES");
                }
            }
        }
    }

    /// <summary>
    ///     ⚠️ GET_LOCK's names are global to the server. Under one fixed name, a migration of one database waited for the
    ///     migration of any other database on the same server and failed after the lock timeout: tenants migrated in
    ///     parallel, or two applications sharing a server, could not migrate at once. PostgreSQL's and SQL Server's locks
    ///     are the database's.
    /// </summary>
    [Fact]
    public async Task TwoDatabasesOnOneServer_TakeTheMigrationLockAtOnce()
    {
        await using var first = await OpenAsync(await CreateDatabaseAsync());
        await using var second = await OpenAsync(await CreateDatabaseAsync());

        await using var held = await MigrationLock.AcquireAsync(first, MigrationConstants.ProviderMySql, lockTimeout: TimeSpan.FromSeconds(1));
        var other = async () =>
        {
            var lockOnOther = await MigrationLock.AcquireAsync(second, MigrationConstants.ProviderMySql, lockTimeout: TimeSpan.FromSeconds(1));
            await lockOnOther.DisposeAsync();
        };

        await other.Should().NotThrowAsync();
    }

    /// <summary>The control case: two sessions on the same database still exclude each other.</summary>
    [Fact]
    public async Task TwoSessionsOnOneDatabase_ExcludeEachOther()
    {
        var database = await CreateDatabaseAsync();
        await using var first = await OpenAsync(database);
        await using var second = await OpenAsync(database);

        await using var held = await MigrationLock.AcquireAsync(first, MigrationConstants.ProviderMySql, lockTimeout: TimeSpan.FromSeconds(1));
        var same = () => MigrationLock.AcquireAsync(second, MigrationConstants.ProviderMySql, lockTimeout: TimeSpan.FromSeconds(1));

        await same.Should().ThrowAsync<InvalidOperationException>();
    }

    private string ConnectionString(string database) =>
        new MySqlConnectionStringBuilder(fixture.ConnectionString) { Database = database }.ConnectionString;

    private async Task<MySqlConnection> OpenAsync(string database)
    {
        var connection = new MySqlConnection(ConnectionString(database));
        await connection.OpenAsync();
        return connection;
    }

    private async Task<string> CreateDatabaseAsync()
    {
        var database = "lock_" + Guid.NewGuid().ToString("N");
        var server = new MySqlConnection(fixture.ConnectionString);
        await using (server)
        {
            await server.OpenAsync();
            var create = server.CreateCommand();
            await using (create)
            {
                // The name is a GUID this class made, never user input.
                create.CommandText = $"CREATE DATABASE `{database}`";
                await create.ExecuteNonQueryAsync();
            }
        }

        return database;
    }
}

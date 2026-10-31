using System.Collections.Immutable;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Pragmatic.Migrations;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;
using Pragmatic.Persistence.Sql;

namespace PragmaticEntities.Tests;

/// <summary>
///     The schema the Sql engine describes for the entities (<c>DefaultSchema</c>) applied by Pragmatic.Migrations: the runner as a host composes
///     it, the diff it computes, and, for SQLite's in-memory database per session, the script its generator writes from nothing (the runner opens
///     connections of its own, which would not see that database; the runner on SQLite is <c>PragmaticMigrationTests</c>').
/// </summary>
internal static class PragmaticMigrations
{
    private static readonly List<string> Excluded = [MigrationConstants.AuditTableName, MigrationConstants.DataMigrationTableName, MigrationConstants.LockTableName, MigrationConstants.EfMigrationsTableName];

    private static readonly Lazy<string> SqliteCreate = new(() => new SqliteMigrationGenerator().GenerateScript(new SchemaDiffEngine().ComputeDiff(DialectSchemas.For(SqlDialect.Sqlite), null)));

    /// <summary>The statements that create the schema on an empty SQLite database, as Pragmatic.Migrations writes them.</summary>
    public static string SqliteScript => SqliteCreate.Value;

    /// <summary>The runner as a host composes it (DI, a connection factory per provider), on <paramref name="schema" />.</summary>
    public static async Task<MigrationResult> MigrateAsync(string connectionString, SchemaVersion schema)
    {
        var services = new ServiceCollection();
        new MigrationsBuilder(services)
            .UseProvider(MigrationConstants.ProviderSqlite, cs => new SqliteConnection(cs))
            .UseProvider(MigrationConstants.ProviderPostgreSql, cs => new NpgsqlConnection(cs))
            .UseProvider(MigrationConstants.ProviderSqlServer, cs => new SqlConnection(cs))
            .UseProvider(MigrationConstants.ProviderMySql, cs => new MySqlConnection(cs))
            .UseProvider(MigrationConstants.ProviderOracle, cs => new OracleConnection(cs))
            .Build();
        await using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<IMigrationRunner>();
        return await runner.MigrateAsync(new MigrationContext(connectionString, schema, provider.GetRequiredService<MigrationOptions>()));
    }

    /// <summary>The changes between <paramref name="schema" /> and the database's, as the runner computes them.</summary>
    public static async Task<ImmutableArray<Pragmatic.Migrations.Diff.Changes.SchemaChange>> DiffAsync(SqlDialect dialect, string connectionString, SchemaVersion schema)
        => new SchemaDiffEngine().ComputeDiff(schema, await IntrospectAsync(dialect, connectionString)).Changes;

    /// <summary>The database's schema as the runner reads it.</summary>
    public static async Task<SchemaVersion> IntrospectAsync(SqlDialect dialect, string connectionString)
    {
        ISchemaIntrospector introspector = dialect switch
        {
            SqlDialect.Sqlite => new SqliteSchemaIntrospector { ExcludedTables = Excluded },
            SqlDialect.PostgreSql => new PostgreSqlSchemaIntrospector { ExcludedTables = Excluded },
            SqlDialect.MySql => new MySqlSchemaIntrospector { ExcludedTables = Excluded },
            SqlDialect.Oracle => new OracleSchemaIntrospector { ExcludedTables = Excluded },
            _ => new SqlServerSchemaIntrospector { ExcludedTables = Excluded },
        };
        await using var connection = await OpenAsync(dialect, connectionString);
        return await introspector.IntrospectAsync(connection);
    }

    /// <summary>A connection opened and prepared as the engine's statements expect (MySQL's ANSI_QUOTES and UTC, Oracle's binary NLS and UTC).</summary>
    public static async Task<DbConnection> OpenAsync(SqlDialect dialect, string connectionString)
    {
        var connection = Connect(dialect, connectionString);
        await connection.OpenAsync();
        if (dialect == SqlDialect.MySql)
            SqlMySql.Prepare(connection);
        if (dialect == SqlDialect.Oracle)
            SqlOracle.Prepare(connection);
        return connection;
    }

    public static DbConnection Connect(SqlDialect dialect, string connectionString) => dialect switch
    {
        SqlDialect.Sqlite => new SqliteConnection(connectionString),
        SqlDialect.PostgreSql => new NpgsqlConnection(connectionString),
        SqlDialect.SqlServer => new SqlConnection(connectionString),
        SqlDialect.MySql => new MySqlConnection(connectionString),
        _ => new OracleConnection(connectionString),
    };
}

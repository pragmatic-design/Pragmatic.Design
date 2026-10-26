using System.Data.Common;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Pragmatic.Persistence.Sql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The five databases, with the tables of Pragmatic's test entities: SQLite in memory per session, PostgreSQL, SQL Server, MySQL and
///     Oracle in Docker (the images the SQL suite uses, already on the machine). Containers that do not start fail the tests: these are the
///     signal that the filter families hold on every database, so a run without them proves nothing.
/// </summary>
/// <remarks>
///     Each server gets the schema the Sql engine describes for the entities (<c>DefaultSchema</c>), applied by Pragmatic.Migrations' runner; no
///     table is written by hand.
/// </remarks>
public sealed class PragmaticDatabases : IAsyncLifetime
{
    private const string Password = "spike";
    private PostgreSqlContainer? _postgres;
    private MsSqlContainer? _sqlServer;
    private IContainer? _mySql;
    private IContainer? _oracle;

    static PragmaticDatabases()
    {
        OracleConfiguration.BindByName = true; // the statements name their parameters (:pN)
    }

    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder().WithImage("pgvector/pgvector:pg17").Build();
        // SQL Server 2025 with Full-Text, from the SQL suite's Dockerfile: built here too, so this suite does not need that one to have run first.
        var sqlServerImage = new ImageFromDockerfileBuilder().WithDockerfileDirectory(CommonDirectoryPath.GetProjectDirectory(), "../Pragmatic.Persistence.Sql.Core.Tests/docker/mssql-fts")
            .WithName("pragmatic-spike/mssql-fts:2025").WithCleanUp(false).Build();
        await sqlServerImage.CreateAsync();
        _sqlServer = new MsSqlBuilder().WithImage(sqlServerImage).Build();
        _mySql = new ContainerBuilder().WithImage("mysql:8.4").WithEnvironment("MYSQL_ROOT_PASSWORD", Password).WithEnvironment("MYSQL_DATABASE", "spike").WithPortBinding(3306, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("ready for connections.*port: 3306")).Build();
        _oracle = new ContainerBuilder().WithImage("gvenzl/oracle-free:23-faststart").WithEnvironment("ORACLE_PASSWORD", Password)
            .WithEnvironment("APP_USER", "spike").WithEnvironment("APP_USER_PASSWORD", Password).WithPortBinding(1521, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("DATABASE IS READY TO USE!")).Build();
        await Task.WhenAll(_postgres.StartAsync(), _sqlServer.StartAsync(), _mySql.StartAsync(), _oracle.StartAsync());
        foreach (var dialect in new[] { SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle })
        {
            var migrated = await PragmaticMigrations.MigrateAsync(ConnectionString(dialect), DialectSchemas.For(dialect));
            if (!migrated.Success)
                throw new InvalidOperationException($"The generated schema did not migrate on {dialect}: {migrated.Error}\n{migrated.GeneratedSql}");
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var container in new IContainer?[] { _postgres, _sqlServer, _mySql, _oracle })
        {
            if (container is not null)
                await container.DisposeAsync();
        }
    }

    /// <summary>A session on the dialect's database, emptied, for the caller <paramref name="filters" /> describes.</summary>
    public async Task<SqlSession> SessionAsync(SqlDialect dialect, ISqlFilters? filters)
    {
        if (dialect == SqlDialect.Sqlite)
            return ShapeRoundTripTests.Open(filters);
        var connection = Open(dialect);
        // The tree's own key first: MySQL checks a foreign key row by row, so one DELETE of a parent and its children fails on
        // whichever comes first (the others check at the end of the statement).
        using (var detach = connection.CreateCommand())
        {
            detach.CommandText = $"UPDATE {PragmaticSchema.Quote(dialect, "Categories")} SET {PragmaticSchema.Quote(dialect, "ParentId")} = NULL";
            await detach.ExecuteNonQueryAsync();
        }

        foreach (var table in PragmaticSchema.Tables)
        {
            using var delete = connection.CreateCommand();
            delete.CommandText = $"DELETE FROM {PragmaticSchema.Quote(dialect, table)}";
            await delete.ExecuteNonQueryAsync();
        }

        return new SqlSession(connection, dialect) { Filters = filters };
    }

    private string ConnectionString(SqlDialect dialect, string? database = null) => dialect switch
    {
        SqlDialect.PostgreSql => _postgres!.GetConnectionString(),
        SqlDialect.SqlServer => _sqlServer!.GetConnectionString(),
        SqlDialect.MySql => $"Server={_mySql!.Hostname};Port={_mySql.GetMappedPublicPort(3306)};User ID=root;Password={Password};Database={database ?? "spike"}",
        _ => $"User Id={database ?? "spike"};Password={Password};Data Source={_oracle!.Hostname}:{_oracle.GetMappedPublicPort(1521)}/FREEPDB1",
    };

    private int _created;

    /// <summary>
    ///     An empty database of its own on the dialect's server, for a test that migrates from nothing: a file for SQLite, a database on
    ///     PostgreSQL, SQL Server and MySQL, a user on Oracle (created by SYSTEM, with what an application user holds to migrate).
    /// </summary>
    public async Task<string> CreateAsync(SqlDialect dialect, string purpose)
    {
        var name = $"{purpose}_{Interlocked.Increment(ref _created)}".ToLowerInvariant();
        if (dialect == SqlDialect.Sqlite)
            return $"Data Source={Path.Combine(Path.GetTempPath(), $"pragmatic-entities-{Guid.NewGuid():N}.db")}";
        var admin = dialect == SqlDialect.Oracle ? ConnectionString(dialect, "system") : ConnectionString(dialect);
        await using (var connection = PragmaticMigrations.Connect(dialect, admin))
        {
            await connection.OpenAsync();
            string[] statements = dialect switch
            {
                SqlDialect.PostgreSql => [$"CREATE DATABASE \"{name}\""],
                SqlDialect.SqlServer => [$"CREATE DATABASE [{name}]"],
                SqlDialect.MySql => [$"CREATE DATABASE `{name}`"],
                _ => [$"CREATE USER {name} IDENTIFIED BY {Password} QUOTA UNLIMITED ON USERS", $"GRANT CREATE SESSION, CREATE TABLE TO {name}"],
            };
            foreach (var statement in statements)
            {
                await using var create = connection.CreateCommand();
                create.CommandText = statement;
                await create.ExecuteNonQueryAsync();
            }
        }

        return dialect switch
        {
            SqlDialect.PostgreSql => new NpgsqlConnectionStringBuilder(ConnectionString(dialect)) { Database = name }.ConnectionString,
            SqlDialect.SqlServer => new SqlConnectionStringBuilder(ConnectionString(dialect)) { InitialCatalog = name }.ConnectionString,
            _ => ConnectionString(dialect, name),
        };
    }

    private DbConnection Open(SqlDialect dialect)
    {
        DbConnection connection = dialect == SqlDialect.Sqlite ? new SqliteConnection("Data Source=:memory:") : PragmaticMigrations.Connect(dialect, ConnectionString(dialect));
        connection.Open();
        if (dialect == SqlDialect.MySql)
            SqlMySql.Prepare(connection); // ANSI_QUOTES and UTC on the session, as the library's statements expect
        if (dialect == SqlDialect.Oracle)
            SqlOracle.Prepare(connection); // binary sorting and comparison, UTC
        return connection;
    }
}

[CollectionDefinition("pragmatic databases")]
public sealed class PragmaticDatabasesCollection : ICollectionFixture<PragmaticDatabases>;

/// <summary>The tables of the test entities (DefaultSchema's), children before parents: the order a session empties them in.</summary>
internal static class PragmaticSchema
{
    public static readonly string[] Tables = ["Categories", "ReviewComments","PropertyNotes", "PropertyTagLinks", "PropertyTags", "PropertyAttachments", "StaffAssignments","PropertyAmenities", "Reviews", "Reservations", "RoomTypes", "Amenities", "Properties", "Vouchers"];

    public static string Quote(SqlDialect dialect, string name) => dialect == SqlDialect.SqlServer ? "[" + name + "]" : "\"" + name + "\"";
}

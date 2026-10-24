using Microsoft.Data.Sqlite;
using Npgsql;
using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Samples;
using Pragmatic.Persistence.Workload;
using Pragmatic.Persistence.Sql.Dialects;

// Pragmatic.Persistence.Sql by example: every feature of the spike, one area per file (Reads, Includes, Writes,
// Declared, Runtime, Model, Sessions), each sample saying what it shows and checking what it got against the seed.
// Runs on a seeded SQLite file (always) and on PostgreSQL / SQL Server / MySQL / Oracle when PRAG_SPIKE_PG / PRAG_SPIKE_MSSQL / PRAG_SPIKE_MYSQL / PRAG_SPIKE_ORACLE name a
// server (SQL Server needs -p:WithSqlClient=true, MySQL -p:WithMySql=true, Oracle -p:WithOracle=true). Exit code 0 when every check held. The SQL of every call site is
// in obj/Release/net10.0/generated (EmitCompilerGeneratedFiles), or in the build log with -p:PragmaticCensus=true.

var seed = new Seed();
var sqlitePath = Path.Combine(AppContext.BaseDirectory, "samples.sqlite");
if (File.Exists(sqlitePath))
    File.Delete(sqlitePath);
var failures = 0;

using (var sqlite = new SqliteConnection($"Data Source={sqlitePath}"))
{
    sqlite.Open();
    await Seeder.SeedAsync(sqlite, SqlDialect.Sqlite, seed);
    failures += await Samples.RunAllAsync(new SqlSession(sqlite, SqlDialect.Sqlite), seed, () => new SqliteConnection($"Data Source={sqlitePath}"));
}

if (Environment.GetEnvironmentVariable("PRAG_SPIKE_PG") is { } postgres)
{
    using var connection = new NpgsqlConnection(postgres);
    connection.Open();
    await Seeder.SeedAsync(connection, SqlDialect.PostgreSql, seed);
    failures += await Samples.RunAllAsync(new SqlSession(connection, SqlDialect.PostgreSql) { BulkCopier = new Pragmatic.Persistence.Sql.Npgsql.NpgsqlBulkCopier() }, seed, () => new NpgsqlConnection(postgres));
}
else
{
    Console.WriteLine("PostgreSQL skipped: PRAG_SPIKE_PG is not set.");
}

#if WITH_SQLCLIENT
if (Environment.GetEnvironmentVariable("PRAG_SPIKE_MSSQL") is { } sqlServer)
{
    using var connection = new Microsoft.Data.SqlClient.SqlConnection(sqlServer);
    connection.Open();
    await Seeder.SeedAsync(connection, SqlDialect.SqlServer, seed);
    failures += await Samples.RunAllAsync(new SqlSession(connection, SqlDialect.SqlServer) { BulkCopier = new Pragmatic.Persistence.Sql.SqlClient.SqlClientBulkCopier() }, seed, () => new Microsoft.Data.SqlClient.SqlConnection(sqlServer));
}
else
{
    Console.WriteLine("SQL Server skipped: PRAG_SPIKE_MSSQL is not set.");
}
#else
Console.WriteLine("SQL Server skipped: build with -p:WithSqlClient=true.");
#endif

#if WITH_MYSQL
if (Environment.GetEnvironmentVariable("PRAG_SPIKE_MYSQL") is { } mySql)
{
    // Every connection speaks ANSI_QUOTES and UTC (SqlMySql.Prepare); the ones the samples' pool opens itself rely on the server's sql_mode being set the same way.
    using var connection = new MySqlConnector.MySqlConnection(mySql);
    connection.Open();
    SqlMySql.Prepare(connection);
    await Seeder.SeedAsync(connection, SqlDialect.MySql, seed);
    failures += await Samples.RunAllAsync(new SqlSession(connection, SqlDialect.MySql), seed, () => new MySqlConnector.MySqlConnection(mySql));
}
else
{
    Console.WriteLine("MySQL skipped: PRAG_SPIKE_MYSQL is not set.");
}
#else
Console.WriteLine("MySQL skipped: build with -p:WithMySql=true.");
#endif

#if WITH_ORACLE
if (Environment.GetEnvironmentVariable("PRAG_SPIKE_ORACLE") is { } oracle)
{
    // Parameters bound by name (the statements name them); every connection gets the binary sort, comparison and UTC (SqlOracle.Prepare):
    // the ones opened here, and the ones the samples open themselves through the factory (on open). A SqlSessionPool does it on its own.
    Oracle.ManagedDataAccess.Client.OracleConfiguration.BindByName = true;
    using var connection = new Oracle.ManagedDataAccess.Client.OracleConnection(oracle);
    connection.Open();
    SqlOracle.Prepare(connection);
    await Seeder.SeedAsync(connection, SqlDialect.Oracle, seed);
    failures += await Samples.RunAllAsync(new SqlSession(connection, SqlDialect.Oracle), seed, () =>
    {
        var opened = new Oracle.ManagedDataAccess.Client.OracleConnection(oracle);
        opened.StateChange += (_, change) =>
        {
            if (change.CurrentState == System.Data.ConnectionState.Open)
                SqlOracle.Prepare(opened);
        };
        return opened;
    });
}
else
{
    Console.WriteLine("Oracle skipped: PRAG_SPIKE_ORACLE is not set.");
}
#else
Console.WriteLine("Oracle skipped: build with -p:WithOracle=true.");
#endif

Console.WriteLine(failures == 0 ? "SAMPLES-OK" : $"SAMPLES-FAILED: {failures} checks");
return failures == 0 ? 0 : 1;

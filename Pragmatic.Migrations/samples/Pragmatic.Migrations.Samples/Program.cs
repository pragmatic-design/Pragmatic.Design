using System.Data.Common;
using Microsoft.Data.Sqlite;
using Pragmatic.Migrations.Samples.Samples;

Console.WriteLine("=== Pragmatic.Migrations Samples ===\n");

// Everything runs against a single fresh sqlite file under %TEMP% so the
// three scenarios naturally chain: fresh creation -> schema evolution ->
// idempotent re-run on an unchanged schema. No Docker, no host, no EF.
//
// For Postgres / SQL Server scenarios (with Testcontainers), a separate
// sample project is planned — this one sticks to the zero-setup path.

var dbPath = Path.Combine(Path.GetTempPath(), "pragmatic-migrations-sample.sqlite");
if (File.Exists(dbPath))
    File.Delete(dbPath);
var connectionString = $"Data Source={dbPath}";

Console.WriteLine($"Database file: {dbPath}\n");

// One shared connection across scenarios so Sqlite "file already locked"
// quirks don't bite. Real apps open per-operation connections.
await using var connection = new SqliteConnection(connectionString);
await connection.OpenAsync();

await FreshDatabaseSample.Run(connection);
await EvolveSchemaSample.Run(connection);
await IdempotentRerunSample.Run(connection);
await DataMigrationSample.Run(connectionString);
await ExcludeTableSample.Run(connection);
MultiProviderSample.Run();

// Scenarios 6, 8 use the MigrationRunner against their own fresh databases (the shared
// connection above is already migrated to V2, so a clean file shows real changes).
await MigrationHookSample.Run(FreshDbPath("hook"));
ConcurrentIndexSample.Run();
await MigrationOptionsSample.Run(FreshDbPath("options"));
await LeaderElectionSample.Run();
await TenantMigrationSample.Run();
ClientGenerationSample.Run();
await CliCommandsSample.Run();

Console.WriteLine("\n=== All samples completed. Inspect the sqlite file with `sqlite3` if you want. ===");

static string FreshDbPath(string tag)
{
    var path = Path.Combine(Path.GetTempPath(), $"pragmatic-migrations-{tag}-{Guid.NewGuid():N}.sqlite");
    if (File.Exists(path)) File.Delete(path);
    return $"Data Source={path}";
}

// Expose the helper to the sample classes.
internal static class SampleRunner
{
    public static async Task ExecuteScriptAsync(DbConnection conn, string sql, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

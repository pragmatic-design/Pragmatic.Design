using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Migrations.Runner;

namespace Pragmatic.Migrations.Samples.Samples;

/// <summary>
///     Scenario 10 — distributed leader election. In a multi-instance deployment only ONE
///     instance should run migrations while the others wait. <see cref="DatabaseLeaderElection" />
///     is provider-agnostic (a <c>__PragmaticLock</c> lease table) and works on SQLite, so it is
///     shown live here against a shared file DB. <see cref="PgLeaderElection" /> (PostgreSQL
///     advisory locks) and <see cref="AlwaysLeaderElection" /> (single-instance default) are
///     described in comments — the former needs a live PostgreSQL server.
/// </summary>
public static class LeaderElectionSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Scenario 10: Distributed leader election (lease table) ---");

        // Shared file DB so the lock table is visible to both "instances".
        var dbPath = Path.Combine(Path.GetTempPath(), $"prag-leader-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        async Task<DbConnection> OpenAsync()
        {
            var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync();
            return conn;
        }

        try
        {
            // Two competing instances over the same database (distinct host ids).
            var podA = new DatabaseLeaderElection(OpenAsync, "Sqlite",
                NullLogger<DatabaseLeaderElection>.Instance, hostId: "pod-A");
            var podB = new DatabaseLeaderElection(OpenAsync, "Sqlite",
                NullLogger<DatabaseLeaderElection>.Instance, hostId: "pod-B");

            var aWon = await podA.TryBecomeLeaderAsync();
            var bWon = await podB.TryBecomeLeaderAsync();
            Console.WriteLine($"  pod-A became leader : {aWon}");
            Console.WriteLine($"  pod-B became leader : {bWon} (blocked while pod-A holds the lease)");

            await podA.ReleaseLeadershipAsync();
            Console.WriteLine("  pod-A released the lease.");

            var bWonAfter = await podB.TryBecomeLeaderAsync();
            Console.WriteLine($"  pod-B became leader after release : {bWonAfter}");
            await podB.ReleaseLeadershipAsync();

            DescribeOtherStrategies();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }

        Console.WriteLine();
    }

    private static void DescribeOtherStrategies()
    {
        // AlwaysLeaderElection — single-instance deployments: leadership is always granted.
        IMigrationLeaderElection single = new AlwaysLeaderElection();
        _ = single;

        // PgLeaderElection — PostgreSQL advisory locks (pg_try_advisory_lock). Needs a live
        // PostgreSQL connection, so it is only described here:
        //
        //   var pg = new PgLeaderElection(ct => OpenNpgsqlConnectionAsync(ct));
        //   if (await pg.TryBecomeLeaderAsync())
        //   {
        //       try { /* run migrations */ }
        //       finally { await pg.ReleaseLeadershipAsync(); }
        //   }
        //   else
        //   {
        //       await pg.WaitForLeaderCompletionAsync(); // block startup until the leader is done
        //   }
        Console.WriteLine("  also available      : AlwaysLeaderElection (single instance), " +
                          "PgLeaderElection (PostgreSQL advisory locks — needs a live server).");
    }
}

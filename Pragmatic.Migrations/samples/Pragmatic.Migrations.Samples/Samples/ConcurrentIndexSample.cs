using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Samples.Samples;

/// <summary>
///     Scenario 8 — <c>UseConcurrentIndexes</c> end-to-end (SQL generation). PostgreSQL can
///     build an index without locking writes via <c>CREATE INDEX CONCURRENTLY</c>. At runtime
///     <c>MigrationsBuilder.UseConcurrentIndexes()</c> sets <see cref="MigrationOptions.ConcurrentIndexes" />;
///     the runner then defers each <see cref="AddIndex" /> to a post-commit phase and marks it
///     <c>IsConcurrent = true</c> before generating its SQL. This sample shows the two generated
///     forms side by side (SQLite always builds inline — the flag is a no-op there).
/// </summary>
public static class ConcurrentIndexSample
{
    public static void Run()
    {
        Console.WriteLine("--- Scenario 8: UseConcurrentIndexes — CREATE INDEX CONCURRENTLY (PostgreSQL) ---");

        // A single AddIndex change, as the diff engine would emit when an index is added.
        var index = new IndexSchema("IX_Users_Email", ["Email"], IsUnique: true);
        var addIndex = new AddIndex("Users", index);

        var pg = new PostgreSqlMigrationGenerator();

        // Default: inline (locking) build — runs inside the migration transaction.
        Console.WriteLine("  standard (locking) build:");
        Console.WriteLine("    | " + pg.GenerateChangeScript(addIndex).Trim());

        // UseConcurrentIndexes(): the runner re-emits the change with IsConcurrent = true.
        var concurrent = addIndex with { IsConcurrent = true };
        Console.WriteLine("  concurrent (non-locking) build:");
        Console.WriteLine("    | " + pg.GenerateChangeScript(concurrent).Trim());

        Console.WriteLine("  verdict            : CONCURRENTLY runs post-commit (cannot be in a transaction);");
        Console.WriteLine("                       a failed concurrent build leaves the index INVALID on the server.");
        Console.WriteLine();
    }
}

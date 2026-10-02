using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Samples.Samples;

/// <summary>
///     Scenario 4 — multi-provider SQL generation. The SAME
///     <see cref="DesiredSchemas.V2"/> (and empty current schema) is passed to
///     all three <see cref="ISqlMigrationGenerator"/> implementations. The
///     diff is provider-agnostic; only the SQL dialect differs
///     (quoting, types, batch terminators, idempotency idioms).
///
///     This sample doesn't execute against a live database — Postgres and
///     SQL Server would require Docker. For the zero-setup console experience
///     we print the three scripts side-by-side so the reader can inspect the
///     dialect differences.
/// </summary>
public static class MultiProviderSample
{
    public static void Run()
    {
        Console.WriteLine("--- Scenario 6: multi-provider SQL (Sqlite / PostgreSQL / SQL Server) ---");

        var diffEngine = new SchemaDiffEngine();
        var diff = diffEngine.ComputeDiff(DesiredSchemas.V2, current: null);
        Console.WriteLine($"  same diff for all 3 providers : {diff.Changes.Length} change(s)");
        foreach (var change in diff.Changes)
            Console.WriteLine($"    - {change.GetType().Name,-16}  {change.Description}");
        Console.WriteLine();

        ISqlMigrationGenerator[] generators =
        [
            new SqliteMigrationGenerator(),
            new PostgreSqlMigrationGenerator(),
            new SqlServerMigrationGenerator(),
        ];

        foreach (var generator in generators)
        {
            var sql = generator.GenerateScript(diff);
            Console.WriteLine($"  === {generator.ProviderName} ===");
            PrintSql(sql);
            Console.WriteLine();
        }
    }

    private static void PrintSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            Console.WriteLine("    (empty)");
            return;
        }

        foreach (var line in sql.Split('\n', StringSplitOptions.None))
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Length == 0) continue;
            Console.WriteLine($"    | {trimmed}");
        }
    }
}

using System.Data.Common;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Samples.Samples;

/// <summary>
///     Scenario 1 — fresh database. The sqlite file is brand new: the
///     introspector returns an empty SchemaVersion, the diff engine sees that
///     every desired table needs to be created, the generator emits idempotent
///     DDL, and a single command executes it. After the migration, the same
///     introspector reports a schema that matches the desired state.
/// </summary>
public static class FreshDatabaseSample
{
    public static async Task Run(DbConnection connection)
    {
        Console.WriteLine("--- Scenario 1: fresh database -> create from empty ---");

        var introspector = new SqliteSchemaIntrospector();
        var diffEngine = new SchemaDiffEngine();
        var sqlGenerator = new SqliteMigrationGenerator();

        var current = await introspector.IntrospectAsync(connection);
        Console.WriteLine($"  current schema         : {current.Tables.Length} table(s)");

        var desired = DesiredSchemas.V1;
        var diff = diffEngine.ComputeDiff(desired, current);
        Console.WriteLine($"  diff                   : {diff.Changes.Length} change(s), "
                        + $"{(diff.HasBreakingChanges ? "includes breaking" : "none breaking")}");
        foreach (var change in diff.Changes)
            Console.WriteLine($"    - {change.GetType().Name,-16}  {change.Description}");

        var sql = sqlGenerator.GenerateScript(diff);
        PrintSqlExcerpt(sql);

        await SampleRunner.ExecuteScriptAsync(connection, sql);

        var afterMigration = await introspector.IntrospectAsync(connection);
        Console.WriteLine($"  schema after migrate   : {afterMigration.Tables.Length} table(s)");
        foreach (var t in afterMigration.Tables)
            Console.WriteLine($"    - {t.Name,-12} columns: {string.Join(", ", t.Columns.Select(c => c.Name))}");
        Console.WriteLine();
    }

    internal static void PrintSqlExcerpt(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            Console.WriteLine("  generated SQL          : (empty — no changes needed)");
            return;
        }
        var lines = sql.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine($"  generated SQL          : {lines.Length} statement line(s)");
        foreach (var line in lines.Take(6))
            Console.WriteLine($"    | {line}");
        if (lines.Length > 6)
            Console.WriteLine($"    | ... ({lines.Length - 6} more)");
    }
}

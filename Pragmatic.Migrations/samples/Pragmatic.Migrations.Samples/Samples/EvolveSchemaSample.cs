using System.Data.Common;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Samples.Samples;

/// <summary>
///     Scenario 2 — evolve the schema. V1 is already applied; V2 adds one
///     nullable column (Email) and one unique index on it. The diff should
///     surface exactly those two changes, the generator emits only the
///     delta SQL, and the runner applies it without touching the existing
///     rows.
/// </summary>
public static class EvolveSchemaSample
{
    public static async Task Run(DbConnection connection)
    {
        Console.WriteLine("--- Scenario 2: evolve schema -> V1 to V2 (add column + index) ---");

        var introspector = new SqliteSchemaIntrospector();
        var diffEngine = new SchemaDiffEngine();
        var sqlGenerator = new SqliteMigrationGenerator();

        var current = await introspector.IntrospectAsync(connection);
        Console.WriteLine($"  current schema         : {current.Tables.Length} table(s), "
                        + $"{current.Tables.Sum(t => t.Columns.Length)} column(s)");

        var desired = DesiredSchemas.V2;
        var diff = diffEngine.ComputeDiff(desired, current);
        Console.WriteLine($"  diff changes           : {diff.Changes.Length}");
        foreach (var change in diff.Changes)
            Console.WriteLine($"    - {change.GetType().Name,-16}  {change.Description}");

        var sql = sqlGenerator.GenerateScript(diff);
        FreshDatabaseSample.PrintSqlExcerpt(sql);

        await SampleRunner.ExecuteScriptAsync(connection, sql);

        var afterMigration = await introspector.IntrospectAsync(connection);
        var customers = afterMigration.Tables.First(t => t.Name == "Customers");
        Console.WriteLine($"  after migrate          : Customers has {customers.Columns.Length} column(s)");
        foreach (var col in customers.Columns)
            Console.WriteLine($"    - {col.Name,-12}  {col.SqlType,-8}  nullable={col.IsNullable}  pk={col.IsPrimaryKey}");
        Console.WriteLine();
    }
}

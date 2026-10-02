using System.Data.Common;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Samples.Samples;

/// <summary>
///     Scenario 3 — idempotent re-run. The database already matches V2 after
///     scenario 2; asking for the same desired schema again should produce an
///     empty diff and an empty SQL script. This is the property that makes
///     "run on every boot" safe for Pragmatic.Migrations.
/// </summary>
public static class IdempotentRerunSample
{
    public static async Task Run(DbConnection connection)
    {
        Console.WriteLine("--- Scenario 3: idempotent re-run -> no-op when schemas match ---");

        var introspector = new SqliteSchemaIntrospector();
        var diffEngine = new SchemaDiffEngine();
        var sqlGenerator = new SqliteMigrationGenerator();

        var current = await introspector.IntrospectAsync(connection);
        var desired = DesiredSchemas.V2;
        var diff = diffEngine.ComputeDiff(desired, current);

        Console.WriteLine($"  current schema         : {current.Tables.Length} table(s)");
        Console.WriteLine($"  desired schema         : {desired.Tables.Length} table(s)");
        Console.WriteLine($"  diff changes           : {diff.Changes.Length} (expected: 0)");

        var sql = sqlGenerator.GenerateScript(diff);
        FreshDatabaseSample.PrintSqlExcerpt(sql);

        if (diff.Changes.Length == 0)
            Console.WriteLine("  verdict                : re-running the migration is a no-op — safe to boot on every startup.");
        else
            Console.WriteLine("  verdict                : re-run would produce changes — investigate the diff above.");
        Console.WriteLine();
    }
}

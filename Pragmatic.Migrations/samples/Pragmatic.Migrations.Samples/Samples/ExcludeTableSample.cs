using System.Data.Common;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Introspection;

namespace Pragmatic.Migrations.Samples.Samples;

/// <summary>
///     Scenario 5 — ExcludeTable. An externally-owned table on the database (created
///     by a DBA, another tool, or a database extension) is kept out of migration
///     management: with the exclusion, the diff engine does not propose to drop it.
/// </summary>
public static class ExcludeTableSample
{
    public static async Task Run(DbConnection connection)
    {
        Console.WriteLine("--- Scenario 5: ExcludeTable — keep externally-owned tables out of migration ---");

        // Pretend another system created this table on the database.
        await SampleRunner.ExecuteScriptAsync(connection,
            """CREATE TABLE IF NOT EXISTS "AuditExternal" ("Id" TEXT PRIMARY KEY, "Event" TEXT);""");

        var desired = DesiredSchemas.V2;        // does not declare "AuditExternal"
        var diffEngine = new SchemaDiffEngine();

        // Without ExcludeTable: the diff engine sees AuditExternal and proposes to drop it.
        var introspectorOpen = new SqliteSchemaIntrospector();
        var currentOpen = await introspectorOpen.IntrospectAsync(connection);
        var diffOpen = diffEngine.ComputeDiff(desired, currentOpen);
        var dropsOpen = diffOpen.Changes.OfType<DropTable>().Select(d => d.TableName).ToArray();
        Console.WriteLine($"  without ExcludeTable : would drop {dropsOpen.Length} table(s) — {Join(dropsOpen)}");

        // With ExcludeTable("AuditExternal"): same effect as
        //   builder.UsePragmaticMigrations(m => m.ExcludeTable("AuditExternal"))
        var introspectorScoped = new SqliteSchemaIntrospector
        {
            ExcludedTables = [.. introspectorOpen.ExcludedTables, "AuditExternal"]
        };
        var currentScoped = await introspectorScoped.IntrospectAsync(connection);
        var diffScoped = diffEngine.ComputeDiff(desired, currentScoped);
        var dropsScoped = diffScoped.Changes.OfType<DropTable>().Select(d => d.TableName).ToArray();
        Console.WriteLine($"  with ExcludeTable    : would drop {dropsScoped.Length} table(s) — {Join(dropsScoped)}");
        Console.WriteLine("  verdict              : AuditExternal stays put; the diff engine ignores it entirely.");
        Console.WriteLine();
    }

    private static string Join(string[] names) => names.Length == 0 ? "(none)" : string.Join(", ", names);
}

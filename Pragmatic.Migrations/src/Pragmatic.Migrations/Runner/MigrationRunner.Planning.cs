using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     <see cref="MigrationRunner"/> planning helpers: dry-run rendering and error-suggestion
///     synthesis. Pure functions over the computed diff and failure context.
/// </summary>
public sealed partial class MigrationRunner
{
    /// <summary>
    ///     Splits the diff into the tables that must be rebuilt to be changed at all, and the
    ///     tables this migration creates from scratch.
    /// </summary>
    /// <returns>
    ///     <c>Rebuild</c> — tables needing a rebuild, already excluding freshly created ones;
    ///     <c>Created</c> — tables the diff creates, whose CREATE statement carries the target shape.
    /// </returns>
    internal static (HashSet<string> Rebuild, HashSet<string> Created) PlanTableRebuilds(
        SchemaDiff diff, ISqlMigrationGenerator generator)
    {
        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in diff.Changes)
        {
            if (change is CreateTable createTable)
                created.Add(createTable.Table.Name);
        }

        var rebuild = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in diff.Changes)
        {
            if (generator.GetRebuildTableName(change) is { } table && !created.Contains(table))
                rebuild.Add(table);
        }

        return (rebuild, created);
    }

    /// <summary>
    ///     Builds the rebuild statements for <paramref name="tableName" />: the desired shape comes
    ///     from the compile-time schema, the columns to preserve from what the database actually has.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     The desired schema has no such table. That would mean rebuilding a table into a shape
    ///     nobody declared, so the migration fails instead of guessing.
    /// </exception>
    private static string BuildTableRebuildSql(
        ISqlMigrationGenerator generator,
        SchemaVersion desiredSchema,
        SchemaVersion currentSchema,
        string tableName)
    {
        var desiredTable = desiredSchema.Tables
            .FirstOrDefault(t => string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase));

        if (desiredTable is null)
            throw new InvalidOperationException(
                $"Cannot rebuild table '{tableName}': it is not present in the desired schema. " +
                $"{generator.ProviderName} can only apply this change by recreating the table with its " +
                "target shape, which the compile-time schema must describe.");

        var currentColumns = currentSchema.Tables
            .FirstOrDefault(t => string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase))
            ?.Columns.Select(c => c.Name).ToArray() ?? [];

        return generator.GenerateTableRebuild(desiredTable, currentColumns);
    }

    /// <summary>
    ///     Renders a change's SQL for error reporting, swallowing a generator that refuses to render
    ///     it. Used only on the failure path, where the original exception is the message that counts.
    /// </summary>
    private static string? TryRenderChangeSql(ISqlMigrationGenerator generator, SchemaChange? change)
    {
        if (change is null) return null;
        try
        {
            return generator.GenerateChangeScript(change);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string BuildDryRunSummary(SchemaDiff diff, string dbName)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"[{dbName}] Dry run: {diff.Changes.Length} changes"));
        foreach (var change in diff.Changes)
        {
            var indicator = change.IsBreaking ? "BREAKING" : "safe";
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  [{indicator}] {change.Description}"));
        }

        return sb.ToString();
    }

    private static ImmutableArray<string> BuildErrorSuggestions(SchemaChange? failedChange, Exception ex)
    {
        var suggestions = new List<string>();

        suggestions.Add("Run with DryRun=true to inspect the generated SQL before applying");

        if (failedChange is AlterColumnType)
        {
            suggestions.Add("Column type change failed — the column may contain data incompatible with the new type");
            suggestions.Add("Consider adding a migration step to convert existing data first");
        }
        else if (failedChange is AlterColumnNullability { NewIsNullable: false })
        {
            suggestions.Add("SET NOT NULL failed — the column may contain NULL values");
            suggestions.Add("Update existing NULL values before applying this change");
        }
        else if (failedChange is AddColumn { Column: { IsNullable: false, DefaultValue: null } })
        {
            suggestions.Add("ADD COLUMN failed — a NOT NULL column with no default cannot be added to a table with existing rows");
            suggestions.Add("Add a default value or make the column nullable, then backfill the data");
        }
        else if (failedChange is AddForeignKey)
        {
            suggestions.Add("FK creation failed — orphan rows may exist that violate the constraint");
            suggestions.Add("Clean up orphan data or create the FK with NOT VALID first");
        }
        else if (failedChange is CreateTable)
        {
            suggestions.Add("Table creation failed — the table or a dependency may already exist in an unexpected state");
        }

        if (ex.Message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add("Check database user permissions — the migration user needs DDL privileges");
        }

        suggestions.Add("All changes have been rolled back — the database is in its original state");

        return suggestions.ToImmutableArray();
    }
}

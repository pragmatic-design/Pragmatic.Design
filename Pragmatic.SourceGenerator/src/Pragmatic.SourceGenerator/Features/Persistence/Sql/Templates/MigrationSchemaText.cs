using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;
using System.Linq;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>A table as Pragmatic.Migrations' <c>TableSchema</c> constructs it: one expression, for the templates that list a database's tables.</summary>
internal static class MigrationSchemaText
{
    private const string Migrations = "global::Pragmatic.Migrations.Schema.";

    public static string Table(TableSchemaModel table)
    {
        var columns = table.Columns.Select(c => $"new {Migrations}ColumnSchema({Literal(c.Name)}, {Literal(c.Type)}, {Bool(c.Nullable)}, {Bool(c.Key)}{(c.DefaultValue is null ? "" : ", " + Literal(c.DefaultValue))})");
        var indexes = table.Indexes.Select(i => $"new {Migrations}IndexSchema({Literal(i.Name)}, [{string.Join(", ", i.Columns.Select(Literal))}]{(i.Unique ? ", IsUnique: true" : "")}{(i.Filter is null ? "" : $", Filter: {Literal(i.Filter)}")})");
        var foreignKeys = table.ForeignKeys.Select(f => $"new {Migrations}ForeignKeySchema({Literal(f.Name)}, {Literal(f.Column)}, {Literal(f.Table)}, {Literal(f.ReferencedColumn)}, {Migrations}ReferentialAction.{f.OnDelete})");
        var checks = table.Checks.Count == 0 ? "" : $", [{string.Join(", ", table.Checks.Select(c => $"new {Migrations}CheckConstraintSchema({Literal(c.Name)}, {Literal(c.Expression)})"))}]";
        return $"new {Migrations}TableSchema({Literal(table.Name)}, {(table.Schema is null ? "null" : Literal(table.Schema))}, [{string.Join(", ", columns)}], [{string.Join(", ", indexes)}], [{string.Join(", ", foreignKeys)}]{checks})";
    }

    private static string Literal(string text) => SqlInterceptorGenerator.Literal(text);

    private static string Bool(bool value) => value ? "true" : "false";
}

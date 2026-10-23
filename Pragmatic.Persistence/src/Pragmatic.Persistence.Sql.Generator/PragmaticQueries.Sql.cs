using System.Linq;
using System.Text;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>A declared query's Sql half: its sorts as an ordering of compiled keys (<c>ToSqlOrdering()</c>), the text of each key per dialect.</summary>
internal sealed partial class PragmaticQuery
{
    /// <summary>The query's shape on the Sql engine.</summary>
    public (string Hint, string Source) Shape() => ShapeText("Pragmatic.Persistence.Sql", "Sql engine", AppendSqlOrdering);

    private void AppendSqlOrdering(StringBuilder text)
    {
        text.AppendLine($"    internal global::Pragmatic.Persistence.Sql.SqlOrdering<{EntityName}> ToSqlOrdering()");
        text.AppendLine("    {");
        text.AppendLine($"        var ordering = global::Pragmatic.Persistence.Sql.SqlOrdering<{EntityName}>.By(__OrderBy_PersistenceId);");
        foreach (var sort in Sorts.OrderBy(s => s.Priority))
        {
            var fallback = sort.Direction < 0 ? "null" : $"(global::Pragmatic.Persistence.Query.SortDirection)({sort.Direction})";
            text.AppendLine($"        if ((this.{sort.Name} ?? {fallback}) is {{ }} {sort.Name}Direction)");
            text.AppendLine($"            ordering = ordering.Then(__OrderBy_{sort.Column.Replace('.', '_')}, {sort.Name}Direction == global::Pragmatic.Persistence.Query.SortDirection.Descending);");
        }

        text.AppendLine("        return ordering;");
        text.AppendLine("    }");
        foreach (var column in Sorts.Select(s => s.Column).Append("PersistenceId").Distinct())
            text.AppendLine($"    private static readonly global::Pragmatic.Persistence.Sql.SqlOrderKey __OrderBy_{column.Replace('.', '_')} = new({OrderKeyTexts(column)});");
    }

    /// <summary>The five dialect texts of an order key on the column <paramref name="column" /> of alias t.</summary>
    internal static string OrderKeyTexts(string column)
        => string.Join(", ", new[] { SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle }
            .Select(d => SqlInterceptorGenerator.Literal(PlanBuilder.ForDialectText($"t.\"{column}\"", d))));
}

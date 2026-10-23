using System.Collections.Generic;
using System.Linq;
using Pragmatic.Persistence.Sql.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>A declared query's Sql half: its sorts as an ordering of compiled keys (<c>ToSqlOrdering()</c>), the text of each key per dialect.</summary>
internal sealed partial class PragmaticQuery
{
    /// <summary>The query's shape on the Sql engine.</summary>
    public (string Hint, string Source) Shape() => ShapeText("Pragmatic.Persistence.Sql", "Sql engine", SqlOrdering());

    private SqlOrderingModel SqlOrdering()
        => new(
            Sorts.OrderBy(s => s.Priority).Select(s => new QuerySortModel(s.Name, s.Column.Replace('.', '_'), s.Direction)).ToEquatableArray(),
            Sorts.Select(s => s.Column).Append("PersistenceId").Distinct().Select(c => new OrderKeyModel(c.Replace('.', '_'), OrderKeyDialects(c).ToEquatableArray())).ToEquatableArray());

    /// <summary>The texts of an order key on the column <paramref name="column" /> of alias t, one per dialect.</summary>
    internal static IEnumerable<string> OrderKeyDialects(string column)
        => new[] { SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle }.Select(d => PlanBuilder.ForDialectText($"t.\"{column}\"", d));
}

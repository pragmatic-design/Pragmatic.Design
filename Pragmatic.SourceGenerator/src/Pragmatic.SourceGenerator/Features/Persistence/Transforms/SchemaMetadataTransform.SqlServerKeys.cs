using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

internal static partial class SchemaMetadataTransform
{
    private const string UnboundedString = "nvarchar(max)";

    /// <summary>EF Core's type for a SQL Server string a key or an index holds, when no length is declared.</summary>
    private const string IndexableString = "nvarchar(450)";

    /// <summary>
    ///     <paramref name="table" /> with every unbounded string a key or an index holds as <c>nvarchar(450)</c>: SQL Server indexes no
    ///     <c>nvarchar(max)</c> (a key is at most 900 bytes), so the index would fail to create and the migration with it. EF Core's model
    ///     gives those columns the same type by convention, so the schema describes what the context maps.
    /// </summary>
    private static TableSchemaModel WithIndexableStrings(TableSchemaModel table)
    {
        var keyed = new HashSet<string>(table.Indexes.SelectMany(i => i.Columns), StringComparer.Ordinal);
        foreach (var column in table.Columns.Where(c => c.IsPrimaryKey))
            keyed.Add(column.Name);
        if (!table.Columns.Any(c => c.SqlType == UnboundedString && keyed.Contains(c.Name)))
            return table;

        var columns = table.Columns.Select(c => c.SqlType == UnboundedString && keyed.Contains(c.Name) ? c with { SqlType = IndexableString } : c);
        return table with { Columns = columns.ToImmutableArray() };
    }
}

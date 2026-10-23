using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     A declared DTO's collection members (<c>List&lt;TChildDto&gt;</c>): one companion statement per collection, selecting the
///     child DTO's columns and the child's foreign key for the children of the parent statement's rows; the loader reads
///     the result sets in the same order, groups the children on the key and assigns them to the parent DTO through the
///     property that reads the parent's key. Nested collections repeat the pattern under the children.
/// </summary>
internal sealed partial class PlanBuilder
{
    /// <summary>The companion of every collection of <paramref name="projection" />, depth first, registered on the root in reading order.</summary>
    private void BuildProjectedChildren(SqlInterceptorGenerator.Projection projection, PlanBuilder parentStatement, EntityModel keyOwner)
    {
        foreach (var collection in projection.Collections)
        {
            var composite = collection.Collection.IsComposite;
            var parentKey = composite ? string.Join(", ", keyOwner.Keys.Select((k, i) => $"t.\"{k.Column}\" AS k{i}")) : $"t.\"{keyOwner.KeyColumn}\"";
            var builder = new PlanBuilder(collection.Child, parentStatement, parentKey, collection.ForeignKeyColumn)
            {
                _projection = collection.ChildProjection,
                _elementType = collection.ChildDtoName,
                _foreignKeys = composite ? collection.ForeignKeyColumns : null,
            };
            Merge(builder._joins, collection.ChildJoins);
            builder.MarkDeclaredJoins(collection.Child); // the child DTO's flattened members: their targets' rules in the joins
            builder.AddRules(Location.None, []); // validated where the child entity is read
            builder._selectList = $"{collection.ChildProjection.ColumnList}, {string.Join(", ", collection.ForeignKeyColumns.Select(fk => $"t.\"{fk}\""))}";
            Root._children.Add(builder);
            builder.BuildProjectedChildren(collection.ChildProjection, builder, collection.Child);
        }
    }

    /// <summary>The loader of a projected plan: each companion's rows built with the child DTO's constructor, grouped on the parent key read after the DTO's columns.</summary>
    private string ProjectedLoader()
    {
        var text = new StringBuilder();
        text.Append($"static async (global::System.Data.Common.DbDataReader r, SqlDialect d, global::System.Collections.Generic.List<{_elementType}> parents, global::System.Threading.CancellationToken ct) =>").AppendLine();
        text.AppendLine("            {");
        var statement = 0;
        ProjectedLoads(text, _projection!, _entity, "parents", "", ref statement);
        text.Append("            }");
        return text.ToString();
    }

    private void ProjectedLoads(StringBuilder text, SqlInterceptorGenerator.Projection projection, EntityModel parent, string parents, string prefix, ref int statement)
    {
        var key = parent.Keys[0];
        var composite = projection.KeyProperties is not null; // a composite parent key: the groups on the tuple of its columns
        var keyType = composite ? "(" + string.Join(", ", parent.Keys.Select(k => k.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ")" : key.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var parentKey = composite ? "(" + string.Join(", ", projection.KeyProperties!.Select(p => "parent." + p)) + ")" : "parent." + projection.KeyProperty;
        foreach (var collection in projection.Collections)
        {
            var name = prefix + collection.Property;
            var keyRead = composite ? "(" + string.Join(", ", parent.Keys.Select((k, i) => k.Read("r", (collection.ChildProjection.ColumnCount + i).ToString(), "d"))) + ")" : key.Read("r", collection.ChildProjection.ColumnCount.ToString(), "d");
            statement++; // this collection's companion, in BuildProjectedChildren's order; the next result set is its
            var child = collection.ChildProjection;
            var nested = child.Collections.Count > 0;
            text.AppendLine("                await r.NextResultAsync(ct).ConfigureAwait(false);");
            text.AppendLine($"                var {name}_groups = new global::System.Collections.Generic.Dictionary<{keyType}, global::System.Collections.Generic.List<{collection.ChildDtoName}>>();");
            if (nested)
                text.AppendLine($"                var {name}_rows = new global::System.Collections.Generic.List<{collection.ChildDtoName}>();");
            text.AppendLine($"                while (await r.ReadAsync(ct).ConfigureAwait(false)) {{ var row = {child.Construct}; var key = {keyRead}; if (!{name}_groups.TryGetValue(key, out var group)) {name}_groups[key] = group = new(); group.Add(row);{(nested ? $" {name}_rows.Add(row);" : "")} }}");
            text.AppendLine($"                foreach (var parent in {parents}) parent.{collection.Property} = {name}_groups.TryGetValue({parentKey}, out var {name}_mine) ? {name}_mine : new();");
            if (nested)
                ProjectedLoads(text, child, collection.Child, $"{name}_rows", name + "_", ref statement);
        }
    }
}

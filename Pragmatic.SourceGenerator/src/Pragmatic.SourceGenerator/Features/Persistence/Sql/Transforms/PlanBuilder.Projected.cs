using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

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
    private LoaderModel ProjectedLoader()
    {
        var statement = 0;
        return new LoaderModel(_elementType, ProjectedLoads(_projection!, _entity, "parents", "", ref statement).ToEquatableArray());
    }

    private static List<LoadStepModel> ProjectedLoads(SqlInterceptorGenerator.Projection projection, EntityModel parent, string parents, string prefix, ref int statement)
    {
        var steps = new List<LoadStepModel>();
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
            var children = child.Collections.Count > 0 ? ProjectedLoads(child, collection.Child, $"{name}_rows", name + "_", ref statement) : [];
            steps.Add(new ProjectedLoadModel(name, parents, collection.Property, children.ToEquatableArray(), keyType, collection.ChildDtoName, child.Construct, keyRead, parentKey));
        }

        return steps;
    }
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A declared query's documents half (its model and <c>ToSpecification()</c> are <see cref="PragmaticQuery" />'s, engine-neutral): its sorts as an
///     ordering chosen at runtime (<c>ToDocOrdering()</c>, one <c>DocOrderKey</c> per field), and its result as a projection the documents engine runs
///     in .NET: every settable property of the DTO from the entity's field of the same name. What it cannot map refuses the query (PRAG3108), as the
///     Sql engine's PRAG3033 does.
/// </summary>
internal static class DocQueries
{
    /// <summary>
    ///     The documents engine's checks on top of the model's (the sorts name stored fields, the result maps from the entity), and the result's
    ///     projection: <c>.Select(e =&gt; new R { P = e.P, … })</c>, empty when the result is the entity, null when the query is refused.
    /// </summary>
    public static string? Prepare(PragmaticQuery query, DocModel model)
    {
        foreach (var sort in query.Sorts.Where(s => DocOrderKeys.Texts(model, s.Column) is null))
            query.Refuse($"the sort {sort.Name} orders by {sort.Column}, which is not a field of the document");
        if (SymbolEqualityComparer.Default.Equals(query.Result, query.Entity))
            return "";

        var assignments = new List<string>();
        foreach (var property in query.Result.GetMembers().OfType<IPropertySymbol>().Where(p => p is { IsStatic: false, DeclaredAccessibility: Accessibility.Public, SetMethod: not null }))
        {
            if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "NotMappedAttribute"))
                continue;
            if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "MapPropertyAttribute"))
            {
                query.Refuse($"{query.Result.Name}.{property.Name} is mapped by [MapProperty], which the documents engine does not read yet");
                return null;
            }

            // A Pragmatic entity's Id is its shape's alias of the key (Id => PersistenceId), which a document stores as its key: read that.
            var field = property.Name == "Id" && model.Field("Id") is null && model.IsPragmaticEntity && model.Key?.Property.Name == "PersistenceId" ? model.Key : model.Field(property.Name);
            // The field as the shaped entity declares it (the model is read on the compilation with the shape): the same type, or its nullable.
            var source = field?.Property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
            var target = property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
            if (source is null || (source != target && source + "?" != target))
            {
                query.Refuse($"{query.Result.Name}.{property.Name} has no field of the same name and type on {query.Entity.Name} (a navigation, a computed member)");
                return null;
            }

            assignments.Add($"{property.Name} = e.{field!.Property.Name}");
        }

        return $".Select(e => new {query.ResultName} {{ {string.Join(", ", assignments)} }})";
    }

    /// <summary>The query's shape on the documents engine (hint name, text).</summary>
    public static (string Hint, string Source) Shape(PragmaticQuery query, DocModel model)
        => query.ShapeText("Pragmatic.Persistence.Documents", "documents engine", "DocQueryShape", new QueryOrderingModel(
            "ToDocOrdering", $"global::Pragmatic.Persistence.Documents.Planning.DocOrdering<{query.EntityName}>", "global::Pragmatic.Persistence.Documents.Planning.DocOrderKey",
            query.Sorts.OrderBy(s => s.Priority).Select(s => new QuerySortModel(s.Name, s.Column.Replace('.', '_'), s.Direction)).ToEquatableArray(),
            query.Sorts.Select(s => s.Column).Append("PersistenceId").Distinct()
                .Select(c => new OrderKeyModel(c.Replace('.', '_'), (DocOrderKeys.Texts(model, c) ?? DocOrderKeys.Named(c)).ToEquatableArray())).ToEquatableArray()));
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A declared query's documents half (its model and <c>ToSpecification()</c> are <see cref="PragmaticQuery" />'s, engine-neutral): its sorts as an
///     ordering chosen at runtime (<c>ToDocOrdering()</c>, one <c>DocOrderKey</c> per field), and its result as a projection the documents engine runs
///     in .NET: every settable property of the DTO from the entity's field of the same name, or of the path its <c>[MapProperty]</c> names (a field
///     of the document's own, of an embedded or owned one, or of the document a declared reference reaches). What it cannot map refuses the query (PRAG3108), as the
///     Sql engine's PRAG3033 does.
/// </summary>
internal static partial class DocQueries
{
    /// <summary>
    ///     The documents engine's checks on top of the model's (the sorts name stored fields, the result maps from the entity), and the result's
    ///     projection's selector: <c>e =&gt; new R { P = e.P, … }</c>, empty when the result is the entity, null when the query is refused. A
    ///     <c>[MapProperty("Nav.Field")]</c> through a reference navigation adds the related documents to read to <paramref name="lookups" />.
    /// </summary>
    public static string? Prepare(PragmaticQuery query, DocModel model, Dictionary<string, DocModel> models, List<DocLookupModel> lookups)
    {
        // The shape would compose it (PragmaticQuery reads it for the Sql engine); no documents suite runs a filter object's OR yet.
        if (query.HasComplexFilter)
            query.Refuse("[ComplexFilter] is not compiled on the documents engine yet");
        foreach (var sort in query.Sorts.Where(s => DocOrderKeys.Texts(model, s.Column) is null))
            query.Refuse($"the sort {sort.Name} orders by {sort.Column}, which is not a field of the document");
        if (SymbolEqualityComparer.Default.Equals(query.Result, query.Entity))
            return "";

        var assignments = new List<string>();
        foreach (var property in query.Result.GetMembers().OfType<IPropertySymbol>().Where(p => p is { IsStatic: false, DeclaredAccessibility: Accessibility.Public, SetMethod: not null }))
        {
            if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "NotMappedAttribute"))
                continue;
            DocField? field;
            var read = "e.";
            if (property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "MapPropertyAttribute") is { } map)
            {
                var path = map.ConstructorArguments.FirstOrDefault().Value as string ?? "";
                if (Related(query, model, models, path, property, lookups) is { } related)
                {
                    if (related.Length == 0)
                        return null; // refused, with the reason
                    assignments.Add(related);
                    continue;
                }

                field = Follow(query.Entity, model, path, out read, out var problem);
                if (field is null)
                {
                    query.Refuse($"{query.Result.Name}.{property.Name} is mapped by [MapProperty(\"{path}\")]: {problem}");
                    return null;
                }
            }
            else
            {
                // A Pragmatic entity's Id is its shape's alias of the key (Id => PersistenceId), which a document stores as its key: read that.
                field = property.Name == "Id" && model.Field("Id") is null && model.IsPragmaticEntity && model.Key?.Property.Name == "PersistenceId" ? model.Key : model.Field(property.Name);
            }

            // The field as the shaped entity declares it (the model is read on the compilation with the shape): the same type, or its nullable.
            var source = field?.Property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
            var target = property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
            if (source is null || (source != target && source + "?" != target))
            {
                query.Refuse($"{query.Result.Name}.{property.Name} has no field of the same name and type on {query.Entity.Name} (a navigation, a computed member)");
                return null;
            }

            assignments.Add($"{property.Name} = {read}{field!.Property.Name}");
        }

        return $"e => new {query.ResultName} {{ {string.Join(", ", assignments)} }}";
    }

    /// <summary>
    ///     The field a <c>[MapProperty]</c> path names on the document: a field of its own, or a member of an embedded or owned field that is never
    ///     null (<c>Placement.Floor</c>); <paramref name="read" /> is the code before the field's name (<c>e.Placement.</c>). Null with the reason
    ///     otherwise; a path through a navigation names it, since a document keeps a relation as its key and the related one is not in it.
    /// </summary>
    private static DocField? Follow(INamedTypeSymbol entity, DocModel model, string path, out string read, out string problem)
    {
        read = "e.";
        problem = "";
        var segments = path.Split('.');
        var current = model;
        INamedTypeSymbol owner = entity;
        for (var i = 0; i < segments.Length; i++)
        {
            var field = current.Field(segments[i]);
            if (field is null)
            {
                problem = i == 0 && IsNavigation(owner, segments[i])
                    ? $"the path runs through the navigation {segments[i]}, which a document keeps as its key, not as the related document"
                    : $"{owner.Name} has no field {segments[i]} in the document";
                return null;
            }

            if (i == segments.Length - 1)
                return field;
            if (field.Kind != DocKind.Embedded || field.Embedded is null || DocModel.Read(field.Embedded, []) is not { } inner)
            {
                problem = $"{segments[i]} is not an embedded or owned field, so {segments[i + 1]} is not a member of it";
                return null;
            }

            if (field.Nullable)
            {
                problem = $"{segments[i]} may be null, and the projection reads {segments[i + 1]} through it";
                return null;
            }

            read += field.Property.Name + ".";
            current = inner;
            owner = field.Embedded;
        }

        return null;
    }

    /// <summary>A property of <paramref name="type" /> or a base type named <paramref name="name" /> that is a navigation (an entity, or a collection of them).</summary>
    private static bool IsNavigation(INamedTypeSymbol type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault() is { } property)
                return DocModel.IsNavigation(property.Type);
        }

        return false;
    }

    /// <summary>The query's shape on the documents engine (hint name, text).</summary>
    public static (string Hint, string Source) Shape(PragmaticQuery query, DocModel model)
        => query.ShapeText("Pragmatic.Persistence.Documents", "documents engine", "DocQueryShape", new QueryOrderingModel(
            "ToDocOrdering", $"global::Pragmatic.Persistence.Documents.Planning.DocOrdering<{query.EntityName}>", "global::Pragmatic.Persistence.Documents.Planning.DocOrderKey",
            query.Sorts.OrderBy(s => s.Priority).Select(s => new QuerySortModel(s.Name, s.Column.Replace('.', '_'), s.Direction)).ToEquatableArray(),
            query.Sorts.Select(s => s.Column).Append("PersistenceId").Distinct()
                .Select(c => new OrderKeyModel(c.Replace('.', '_'), (DocOrderKeys.Texts(model, c) ?? DocOrderKeys.Named(c)).ToEquatableArray())).ToEquatableArray()));
}

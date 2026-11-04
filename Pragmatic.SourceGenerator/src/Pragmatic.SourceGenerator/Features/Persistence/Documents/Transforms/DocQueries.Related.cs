using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A declared query's <c>[MapProperty("Nav.Field")]</c> through a reference navigation on the documents engine: a document keeps the relation as
///     its key, so the related documents are read by the keys of the page's documents (<see cref="DocLookupModel" />) and the field is read from
///     the one each key finds, as the Sql engine reads it through its join (<c>SqlInterceptorGenerator.Declared</c>, <c>Flattened</c>). A document
///     whose related one is not found (missing, or outside the caller's filters) reads the property's default: null for a reference or a nullable,
///     as EF Core's left join gives it.
/// </summary>
internal static partial class DocQueries
{
    /// <summary>
    ///     The assignment of <paramref name="property" /> when <paramref name="path" /> runs through a reference navigation of the entity, with its
    ///     lookup added to <paramref name="lookups" />; empty when it does but cannot be read (the query refused, with the reason); null when the
    ///     path starts with no reference navigation, which the document's own fields then answer.
    /// </summary>
    private static string? Related(PragmaticQuery query, DocModel model, Dictionary<string, DocModel> models, string path, IPropertySymbol property,
        List<DocLookupModel> lookups)
    {
        var segments = path.Split('.');
        if (segments.Length < 2 || model.Field(segments[0]) is not null)
            return null;
        if (PragmaticRelations.References(query.Entity, "ManyToOne").FirstOrDefault(r => r.Navigation == segments[0]) is not { Navigation: not null } reference)
            return null;

        string Refuse(string why)
        {
            query.Refuse($"{query.Result.Name}.{property.Name} is mapped by [MapProperty(\"{path}\")]: {why}");
            return "";
        }

        if (segments.Length > 2)
            return Refuse($"the path runs through the navigation {segments[0]} and on through {segments[1]}; one navigation's own field is read");
        if (model.Field(reference.ForeignKey) is not { } foreignKey)
            return Refuse($"{query.Entity.Name} has no field {reference.ForeignKey} holding the key of {segments[0]}");
        if (Served(reference.Target, models) is not { Key: { } key } related)
            return Refuse($"{reference.Target.Name}, which {segments[0]} reaches, is not a document the engine serves");
        if (related.Field(segments[1]) is not { } field)
            return Refuse($"{reference.Target.Name} has no field {segments[1]} in the document");

        var keyType = key.Property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var foreignKeyType = foreignKey.Property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (foreignKeyType != keyType && foreignKeyType != $"global::System.Nullable<{keyType}>" && foreignKeyType != keyType + "?")
            return Refuse($"{query.Entity.Name}.{reference.ForeignKey} is not of the type of {reference.Target.Name}'s key");

        var source = field.Property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
        var target = property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
        if (source != target && source + "?" != target)
            return Refuse($"{reference.Target.Name}.{segments[1]} is not of the type of {query.Result.Name}.{property.Name}");

        var lookup = lookups.FirstOrDefault(l => l.ForeignKey == reference.ForeignKey);
        if (lookup is null)
        {
            lookup = new DocLookupModel(lookups.Count, "global::" + reference.Target.ToDisplayString(), reference.ForeignKey,
                foreignKey.Nullable || foreignKeyType.EndsWith("?", System.StringComparison.Ordinal) || foreignKeyType.StartsWith("global::System.Nullable<", System.StringComparison.Ordinal),
                key.Property.Name, keyType);
            lookups.Add(lookup);
        }

        // The property's default when the related document is not found: null for a reference or a nullable, as EF Core's left join reads it.
        // A reference the property declares not nullable gets that null too, said with !: it is where EF Core would put it (#497).
        var fallback = property.Type.IsReferenceType && property.Type.NullableAnnotation != NullableAnnotation.Annotated
            ? "default!"
            : $"default({property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier))})";
        var found = $"__{property.Name}";
        var key0 = lookup.ForeignKeyNullable ? $"e.{lookup.ForeignKey} is {{ }} __key{property.Name} && " : "";
        var keyRead = lookup.ForeignKeyNullable ? $"__key{property.Name}" : $"e.{lookup.ForeignKey}";
        return $"{property.Name} = {key0}__related{lookup.Index}.TryGetValue({keyRead}, out var {found}) ? {found}.{field.Property.Name} : {fallback}";
    }

    /// <summary>
    ///     The document <paramref name="target" /> is, when the engine serves it: one of this compilation's, or a Pragmatic entity of another module
    ///     whose own assembly made it a document (<c>IDocumentEntity&lt;T&gt;</c>, its reader there), as a boundary reads it through
    ///     <c>[ReadAccess&lt;T&gt;]</c> (#502). That one's model is read from its metadata once, and kept with the others.
    /// </summary>
    private static DocModel? Served(INamedTypeSymbol target, Dictionary<string, DocModel> models)
    {
        var name = target.ToDisplayString();
        if (models.TryGetValue(name, out var own))
            return own;
        if (target.DeclaringSyntaxReferences.Length > 0 || !IsDocumentOfItsAssembly(target) || DocModel.Read(target, []) is not { } referenced)
            return null;
        models[name] = referenced;
        return referenced;
    }

    private static bool IsDocumentOfItsAssembly(INamedTypeSymbol type)
        => type.AllInterfaces.Any(i => i is { Name: "IDocumentEntity", TypeArguments.Length: 1 } && i.ContainingNamespace.ToDisplayString() == "Pragmatic.Persistence.Documents"
                                        && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type));
}

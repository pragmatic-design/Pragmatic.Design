using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A declared query's <c>[Join&lt;T&gt;]</c> by key on the documents engine (#508): the joined documents are read by the keys of the matched ones,
///     as a <c>[MapProperty("Nav.Field")]</c>'s are (<see cref="DocLookupModel" />), and each result property the join answers is read from the one its
///     key finds. A document whose joined one is not found reads the property's default on a left join, and is dropped on an inner one.
/// </summary>
internal static partial class DocQueries
{
    /// <summary>
    ///     The assignment of <paramref name="property" /> when the query's join answers it, with the join's lookup added to <paramref name="lookups" />
    ///     once; empty when it does but cannot be read (the query refused, with the reason); null when the document's own fields answer it.
    /// </summary>
    private static string? Joined(PragmaticQuery query, DocModel model, Dictionary<string, DocModel> models, IPropertySymbol property, List<DocLookupModel> lookups)
    {
        if (query.JoinTarget is not { } target || !query.Joined.TryGetValue(property.Name, out var name))
            return null;

        string Refuse(string why)
        {
            query.Refuse($"[Join<{target.Name}>] {why}");
            return "";
        }

        if (JoinLookup(query, model, models, lookups, out var related, out var why) is not { } lookup)
            return Refuse(why);
        if ((related!.Field(name) ?? KeyAlias(related, name)) is not { } field)
            return Refuse($"reads {name} for {query.Result.Name}.{property.Name}, which is not a field of the {target.Name} document");

        var source = field.Property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
        var type = property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
        if (source != type && source + "?" != type)
            return Refuse($"reads {target.Name}.{name}, which is not of the type of {query.Result.Name}.{property.Name}");

        // On a left join, the property's default when the joined document is not found, as Related reads a navigation's field (#486).
        var fallback = property.Type.IsReferenceType && property.Type.NullableAnnotation != NullableAnnotation.Annotated
            ? "default!"
            : $"default({property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier))})";
        var found = $"__{property.Name}";
        var key0 = lookup.ForeignKeyNullable ? $"e.{lookup.ForeignKey} is {{ }} __key{property.Name} && " : "";
        var keyRead = lookup.ForeignKeyNullable ? $"__key{property.Name}" : $"e.{lookup.ForeignKey}";
        return $"{property.Name} = {key0}__related{lookup.Index}.TryGetValue({keyRead}, out var {found}) ? {found}.{field.Property.Name} : {fallback}";
    }

    /// <summary>
    ///     The lookup of the joined documents, added to <paramref name="lookups" /> once; null with the reason (<paramref name="why" />) when the
    ///     documents engine cannot read them: the joined entity is not a document it serves, or the target key is not the document's key.
    /// </summary>
    private static DocLookupModel? JoinLookup(PragmaticQuery query, DocModel model, Dictionary<string, DocModel> models, List<DocLookupModel> lookups,
        out DocModel? related, out string why)
    {
        related = null;
        why = "";
        var target = query.JoinTarget!;
        if ((model.Field(query.JoinForeignKey) ?? KeyAlias(model, query.JoinForeignKey)) is not { } foreignKey)
        {
            why = $"names the foreign key {query.JoinForeignKey}, which is not a field of the {query.Entity.Name} document";
            return null;
        }

        if (Served(target, models) is not { Key: { } key } joined)
        {
            why = $"joins {target.Name}, which is not a document the engine serves";
            return null;
        }

        if (!ReferenceEquals(joined.Field(query.JoinTargetKey) ?? KeyAlias(joined, query.JoinTargetKey), key))
        {
            why = $"matches {target.Name}.{query.JoinTargetKey}, which is not the document's key: the joined documents are read by their key";
            return null;
        }

        related = joined;
        var keyType = key.Property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var foreignKeyType = foreignKey.Property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var nullable = foreignKey.Nullable || foreignKeyType == $"global::System.Nullable<{keyType}>" || foreignKeyType == keyType + "?";
        if (foreignKeyType != keyType && !nullable)
        {
            why = $"joins {query.Entity.Name}.{query.JoinForeignKey}, which is not of the type of {target.Name}'s key";
            return null;
        }

        var existing = lookups.FirstOrDefault(l => l.Declared && l.ForeignKey == foreignKey.Property.Name && l.RelatedType == "global::" + target.ToDisplayString() && l.Drops == query.JoinDrops);
        if (existing is not null)
            return existing;
        var lookup = new DocLookupModel(lookups.Count, "global::" + target.ToDisplayString(), foreignKey.Property.Name, nullable, key.Property.Name, keyType) { Drops = query.JoinDrops, Declared = true };
        lookups.Add(lookup);
        return lookup;
    }

    /// <summary>A Pragmatic entity's <c>Id</c>, its shape's alias of the key (<c>Id =&gt; PersistenceId</c>), which a document stores as its key.</summary>
    private static DocField? KeyAlias(DocModel model, string name)
        => name == "Id" && model.Field("Id") is null && model.IsPragmaticEntity && model.Key?.Property.Name == "PersistenceId" ? model.Key : null;
}

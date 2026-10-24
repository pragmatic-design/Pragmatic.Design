using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     The schema of the Pragmatic entities the documents engine serves, per boundary (<see cref="Templates.DocSchemaTemplate" />): each collection's
///     indexes as the Sql engine's schema declares them for the same entity (<c>[Unique]</c>, the tenant's scope first and the live documents only
///     under a soft delete; then IsDeleted, OwnerId, TenantId and each reference's key, named <c>IX_{collection}_{fields}</c>), its partition key path
///     on Cosmos DB, and the composite index each declared query's default ordering needs there (two keys or more). Pragmatic has no
///     <c>[Indexed]</c>: the indexes are the ones its EF schema derives.
/// </summary>
internal static class PragmaticDocSchema
{
    /// <summary>One model per boundary; none without the runtime's <c>DocSchema</c>.</summary>
    public static List<DocSchemaModel> For(Compilation compilation, List<INamedTypeSymbol> entities, Dictionary<string, DocModel> models, List<PragmaticQuery> queries)
    {
        var schemas = new List<DocSchemaModel>();
        if (compilation.GetTypeByMetadataName("Pragmatic.Persistence.Documents.DocSchema") is null)
            return schemas;
        foreach (var group in entities.Where(e => models.ContainsKey(e.ToDisplayString()))
                     .GroupBy(e => PragmaticPermissions.BoundaryOf(e), SymbolEqualityComparer.Default)
                     .OrderBy(g => g.Key?.ToDisplayString() ?? "", StringComparer.Ordinal))
        {
            var boundary = group.Key as INamedTypeSymbol;
            if (boundary is not null && !SymbolEqualityComparer.Default.Equals(boundary.ContainingAssembly, compilation.Assembly))
                continue; // a boundary of a referenced assembly describes its schema there
            var name = boundary is null ? "DefaultDocSchema" : boundary.Name + "DocSchema";
            var first = group.OrderBy(e => e.Name, StringComparer.Ordinal).First();
            var ns = boundary is not null
                ? boundary.ContainingNamespace.IsGlobalNamespace ? "" : boundary.ContainingNamespace.ToDisplayString()
                : RootNamespace(first.ContainingNamespace.IsGlobalNamespace ? compilation.AssemblyName ?? "" : first.ContainingNamespace.ToDisplayString());
            var collections = group.OrderBy(e => models[e.ToDisplayString()].Collection, StringComparer.Ordinal)
                .Select(e => CollectionText(e, models[e.ToDisplayString()], queries.Where(q => SymbolEqualityComparer.Default.Equals(q.Entity, e))))
                .ToEquatableArray();
            schemas.Add(new DocSchemaModel(VirtualFolderHints.ForType(name, "DocSchema", ns), ns.Length == 0 ? null : ns, name,
                boundary?.Name ?? "the entities of no boundary", collections));
        }

        return schemas;
    }

    private static string CollectionText(INamedTypeSymbol entity, DocModel model, IEnumerable<PragmaticQuery> queries)
    {
        var softDelete = PragmaticShape.Has(entity, "SoftDeleteAttribute") && model.Field("IsDeleted") is not null;
        var tenant = entity.AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.MultiTenancy.ITenantEntity") && model.Field("TenantId") is not null;
        var indexes = new List<(string Name, string[] Fields, bool Unique, string? Live)>();
        void Index(string[] fields, bool unique = false, string? live = null)
        {
            var name = $"IX_{model.Collection}_{string.Join("_", fields)}";
            if (fields.Length > 0 && !indexes.Exists(i => i.Name == name))
                indexes.Add((name, fields, unique, live));
        }

        foreach (var unique in entity.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.Persistence.Entity.UniqueAttribute"))
        {
            var fields = unique.ConstructorArguments.SelectMany(a => a.Kind == TypedConstantKind.Array ? a.Values.Select(v => v.Value as string) : new[] { a.Value as string })
                .Where(n => n is not null).Select(n => model.Field(n!)?.Name ?? n!).ToList();
            var global = unique.NamedArguments.Any(n => n.Key == "Scope" && n.Value.Value is { } scope && n.Value.Type is INamedTypeSymbol kind
                                                        && kind.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => Equals(f.ConstantValue, scope))?.Name == "Global");
            Index(tenant && !global ? fields.Prepend("TenantId").ToArray() : fields.ToArray(), true, softDelete ? "IsDeleted" : null);
        }

        if (softDelete)
            Index(["IsDeleted"]);
        if (PragmaticShape.Has(entity, "HasOwnerAttribute") && model.Field("OwnerId") is not null)
            Index(["OwnerId"]);
        if (tenant)
            Index(["TenantId"]);
        // The references are the shape's members: read on the model's type, the entity as the shaped compilation has it.
        foreach (var reference in model.Type.GetMembers().OfType<IPropertySymbol>().Where(p => DocModel.IsPragmatic(p.Type)).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (model.Field(reference.Name + "Id") is { } key)
                Index([key.Name]); // a reference's key, named after it (PragmaticRelations): the relation a document keeps
        }

        // Cosmos DB: an ORDER BY on several keys needs its composite index. A declared query's default ordering is its sorts with a default
        // direction, by priority; another combination chosen at run time needs its own, which the declaration does not say.
        var composites = new List<string>();
        foreach (var query in queries)
        {
            var keys = query.Sorts.Where(s => s.Direction >= 0).OrderBy(s => s.Priority).Select(s => (Field: model.Field(s.Column), Descending: s.Direction == 1)).ToList();
            if (keys.Count < 2 || keys.Any(k => k.Field is null))
                continue;
            var composite = "new (string, bool)[] { " + string.Join(", ", keys.Select(k => $"(\"/{DocModel.JsonName(k.Field!)}\", {(k.Descending ? "true" : "false")})")) + " }";
            if (!composites.Contains(composite))
                composites.Add(composite);
        }

        var partition = "/" + (model.PartitionKey is { } p ? DocModel.JsonName(p) : "id");
        var indexText = indexes.Select(i => $"new global::Pragmatic.Persistence.Documents.DocIndex({DocTypeEmitter.Literal(i.Name)}, [{string.Join(", ", i.Fields.Select(DocTypeEmitter.Literal))}]{(i.Unique ? ", Unique: true" : "")}{(i.Live is null ? "" : ", LiveField: " + DocTypeEmitter.Literal(i.Live))})");
        return $"new global::Pragmatic.Persistence.Documents.DocCollectionSchema({DocTypeEmitter.Literal(model.Collection!)}, {DocTypeEmitter.Literal(partition)}, [{string.Join(", ", indexText)}], [{string.Join(", ", composites)}])";
    }

    /// <summary>Pragmatic's <c>DeriveRootNamespace</c>, as the Sql engine's <c>DefaultSchema</c> uses it.</summary>
    private static string RootNamespace(string ns)
    {
        if (ns.EndsWith(".Entities", StringComparison.Ordinal))
            ns = ns.Substring(0, ns.Length - ".Entities".Length);
        var parts = ns.Split('.');
        return parts.Length >= 2 ? string.Join(".", parts.Take(parts.Length - 1)) : ns;
    }
}

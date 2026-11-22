using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     The model of Pragmatic's read repository and repository for a Pragmatic entity on the documents engine
///     (<see cref="Templates.DocReadRepositoryTemplate" />): <c>Query()</c> is not served (an <c>IQueryable</c> is EF Core's surface, decision of
///     2026-10-21); a declared query (<c>RunAsync</c>) is a case of a generated switch, as on the Sql engine.
/// </summary>
internal static class DocReadRepository
{
    /// <summary>True when the compilation has what the repositories are written against: Pragmatic's contracts and the documents engine's bridge.</summary>
    public static bool Applies(Compilation compilation)
        => compilation.GetTypeByMetadataName("Pragmatic.Persistence.Repository.IReadRepository`1") is not null
           && compilation.GetTypeByMetadataName("Pragmatic.Persistence.Documents.DocCompiledSpecification`1") is not null
           && compilation.GetTypeByMetadataName("Pragmatic.Persistence.Documents.DocUnitOfWork") is not null;

    /// <summary>The repositories nested in <paramref name="entity" />'s partial class; null for an entity that is not an <c>IEntity</c>.</summary>
    public static DocReadRepositoryModel? For(INamedTypeSymbol entity, IReadOnlyList<(PragmaticQuery Query, string? Projection, List<DocLookupModel> Lookups)> queries,
        DocModel? document = null, IEnumerable<string>? sortMembers = null)
    {
        if (!entity.AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.Persistence.Entity.IEntity"))
            return null;
        return new DocReadRepositoryModel(
            VirtualFolderHints.ForType(entity.Name, "DocReadRepository", entity.ContainingNamespace.ToDisplayString()),
            entity.ContainingNamespace.IsGlobalNamespace ? null : entity.ContainingNamespace.ToDisplayString(),
            entity.Name,
            "global::" + entity.ToDisplayString(),
            queries.Where(q => !q.Query.Paged).Select(RunCaseOf).ToEquatableArray(),
            queries.Where(q => q.Query.Paged).Select(RunCaseOf).ToEquatableArray(),
            PragmaticShape.Has(entity, "SoftDeleteAttribute"),
            AutocompleteReads.Of(entity),
            PragmaticReadRepository.DerivesFromAnEntity(entity),
            Identity.Transforms.UserEntityTransform.MatchPropertyOf(entity),
            PageKeysOf(document, sortMembers ?? []));
    }

    /// <summary>
    ///     <c>PageAsync</c>'s keys: the document's key first (the fallback), then each member a grid filter sorts by that is a scalar field of the
    ///     document. Another member has no key, and an ordering naming it is refused (<see cref="Unkeyed" />).
    /// </summary>
    private static EquatableArray<Sql.Models.PageOrderKeyModel> PageKeysOf(DocModel? document, IEnumerable<string> sortMembers)
    {
        // The key's field as the grid adapters read it (DocGrids): the document's, or the name.
        var key = (document is null ? null : DocOrderKeys.Texts(document, "PersistenceId")) ?? DocOrderKeys.Named("PersistenceId");
        var keys = new List<Sql.Models.PageOrderKeyModel> { new("PersistenceId", "PersistenceId", key.ToEquatableArray()) };
        if (document is null)
            return keys.ToEquatableArray();
        foreach (var member in sortMembers.Distinct())
        {
            if (member != "PersistenceId" && DocOrderKeys.Texts(document, member) is { } texts)
                keys.Add(new(member, member.Replace('.', '_'), texts.ToEquatableArray()));
        }

        return keys.ToEquatableArray();
    }

    /// <summary>Why <paramref name="member" /> has no key in <paramref name="document" />, or null when it has one.</summary>
    internal static string? Unkeyed(DocModel document, string member)
        => DocOrderKeys.Texts(document, member) is null
            ? $"the sort on {member}, which is not a scalar field of the document (an ordering that names it is refused)"
            : null;

    private static DocRunCaseModel RunCaseOf((PragmaticQuery Query, string? Projection, List<DocLookupModel> Lookups) declared)
        => new("global::" + declared.Query.Type.ToDisplayString(), declared.Query.Type.Name, declared.Query.Unsupported, declared.Query.ResultName, declared.Projection ?? "",
            declared.Lookups.ToEquatableArray(), declared.Query.MapsInMemory && declared.Query.Unsupported is null,
            declared.Query.View is not null && declared.Query.Unsupported is null ? declared.Query.GroupedInMemory("documents") : null,
            declared.Query.Unsupported is null ? declared.Query.EmptyGroupsStatement("rows") : null);
}

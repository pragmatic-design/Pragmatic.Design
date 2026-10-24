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
    public static DocReadRepositoryModel? For(INamedTypeSymbol entity, IReadOnlyList<(PragmaticQuery Query, string? Projection)> queries)
    {
        if (!entity.AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.Persistence.Entity.IEntity"))
            return null;
        return new DocReadRepositoryModel(
            VirtualFolderHints.ForType(entity.Name, "DocReadRepository", entity.ContainingNamespace.ToDisplayString()),
            entity.ContainingNamespace.IsGlobalNamespace ? null : entity.ContainingNamespace.ToDisplayString(),
            entity.Name,
            "global::" + entity.ToDisplayString(),
            queries.Where(q => !q.Query.Paged).Select(RunCaseOf).ToEquatableArray(),
            queries.Where(q => q.Query.Paged).Select(RunCaseOf).ToEquatableArray());
    }

    private static DocRunCaseModel RunCaseOf((PragmaticQuery Query, string? Projection) declared)
        => new("global::" + declared.Query.Type.ToDisplayString(), declared.Query.Type.Name, declared.Query.Unsupported, declared.Query.ResultName, declared.Projection ?? "");
}

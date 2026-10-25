using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     Pragmatic's <c>IReadRepository&lt;T&gt;</c> for a Pragmatic entity on the Sql engine, as source with chains on <c>SqlQuery&lt;T&gt;</c>: the
///     generator binds and intercepts it like the application's own chains, so every method runs a compiled plan, the entity's filter
///     families included. A specification is read through <c>Matching</c> (its compiled leaves, composed at runtime). <c>Query()</c> is not
///     served (an <c>IQueryable</c> is EF Core's surface, decision of 2026-10-21); a declared query runs through <c>RunAsync</c>, one case per
///     <c>[Query]</c> of the entity (<see cref="PragmaticQuery" />): its filters composed, its sorts ordered, its page taken, its result projected.
/// </summary>
internal static class PragmaticReadRepository
{
    private const string ReadRepository = "Pragmatic.Persistence.Repository.IReadRepository`1";

    /// <summary>True when the compilation has what the repositories are written against: Pragmatic's contracts and the Sql engine's bridge to them.</summary>
    public static bool Applies(Compilation compilation)
        => compilation.GetTypeByMetadataName(ReadRepository) is not null && compilation.GetTypeByMetadataName("Pragmatic.Persistence.Sql.CompiledSpecification`1") is not null
           && compilation.GetTypeByMetadataName("Pragmatic.Persistence.Sql.SqlUnitOfWork") is not null;

    /// <summary>
    ///     The repository nested in <paramref name="entity" />'s partial class (hint name, text); null for an entity that is not an <c>IEntity</c>.
    ///     <paramref name="loads" /> are the compiled loads with navigations of the entity (<see cref="LoadIncludes" />): one read per key.
    /// </summary>
    public static (string Hint, string Source)? For(INamedTypeSymbol entity, IReadOnlyList<PragmaticQuery> queries, IReadOnlyList<LoadIncludes.Declaration> loads)
    {
        if (!entity.AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.Persistence.Entity.IEntity"))
            return null;
        var model = new ReadRepositoryModel(
            VirtualFolderHints.ForType(entity.Name, "SqlReadRepository", entity.ContainingNamespace.ToDisplayString()),
            entity.ContainingNamespace.IsGlobalNamespace ? null : entity.ContainingNamespace.ToDisplayString(),
            entity.Name,
            "global::" + entity.ToDisplayString(),
            queries.Where(q => !q.Paged).Select(RunCaseOf).ToEquatableArray(),
            queries.Where(q => q.Paged).Select(RunCaseOf).ToEquatableArray(),
            loads.GroupBy(l => l.Key).OrderBy(g => g.Key, System.StringComparer.Ordinal)
                .Select(g => new IncludeReadModel(g.Key, g.First().Includes.ToEquatableArray())).ToEquatableArray());
        return (model.Hint, new ReadRepositoryTemplate(model).ToSourceText().ToString());
    }

    private static RunCaseModel RunCaseOf(PragmaticQuery query)
        => new("global::" + query.Type.ToDisplayString(), query.Type.Name, query.Unsupported, "global::" + query.Result.ToDisplayString(), !SymbolEqualityComparer.Default.Equals(query.Result, query.Entity));
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The persistence of each boundary an engine serves, registered as Pragmatic registers a boundary's (<c>RepositoryRegistrationTemplate</c>,
///     <c>BoundaryKeyedServices</c>): the unit of work keyed by <c>typeof(TBoundary)</c>, its session with it, and each entity's
///     <c>IRepository&lt;T&gt;</c> and <c>IReadRepository&lt;T&gt;</c> unkeyed (an entity has one boundary). The session is the host's to open (its
///     connection, its database); the filters are built here from the services Pragmatic registers, and handed to it. One method per boundary,
///     <c>Add{Boundary}Persistence</c>: two boundaries on two engines in one host are two calls. Ported, this is the engine's branch of the template.
/// </summary>
internal sealed class PragmaticRegistration(EngineRegistrationModel engine)
{
    public static readonly PragmaticRegistration Sql = new(new(
        "global::Pragmatic.Persistence.Sql.SqlSession", "global::Pragmatic.Persistence.Sql.Filters.ISqlFilters", "global::Pragmatic.Persistence.Sql.PragmaticSqlFilters",
        "global::Pragmatic.Persistence.Sql.SqlUnitOfWork", "global::Pragmatic.Persistence.Sql.ISqlSavePhase", "SqlRepository",
        "SqlPersistence", "Pragmatic.Persistence.Sql"));

    public static readonly PragmaticRegistration Documents = new(new(
        "global::Pragmatic.Persistence.Documents.DocSession", "global::Pragmatic.Persistence.Documents.Filters.IDocFilters", "global::Pragmatic.Persistence.Documents.PragmaticDocFilters",
        "global::Pragmatic.Persistence.Documents.DocUnitOfWork", "global::Pragmatic.Persistence.Documents.IDocSavePhase", "DocRepository",
        "DocPersistence", "Pragmatic.Persistence.Documents"));

    /// <summary>
    ///     One source per boundary of <paramref name="entities" /> (the ones the engine serves); none without the dependency injection abstractions
    ///     or the engine's unit of work in the compilation. An entity without a boundary is not registered: it has no key.
    /// </summary>
    public List<(string Hint, string Source)> For(Compilation compilation, IEnumerable<INamedTypeSymbol> entities)
    {
        var sources = new List<(string, string)>();
        if (compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions") is null
            || compilation.GetTypeByMetadataName(engine.UnitOfWork.Substring("global::".Length)) is null)
            return sources;
        // By name, not by symbol: a trait's child the engine persists comes from the compilation with the shape, its boundary with it
        // (SqlInterceptorGenerator.ShapeDeclaredEntities), and is registered with the boundary's other entities all the same.
        foreach (var group in entities.Select(e => (Entity: e, Boundary: PragmaticPermissions.BoundaryOf(e) as INamedTypeSymbol)).Where(p => p.Boundary is not null)
                     .GroupBy(p => p.Boundary!.ToDisplayString(), System.StringComparer.Ordinal).OrderBy(g => g.Key, System.StringComparer.Ordinal))
        {
            var boundary = group.First().Boundary!;
            if (boundary.ContainingAssembly?.Name != compilation.Assembly.Name)
                continue; // a boundary of a referenced assembly registers there
            var model = new BoundaryPersistenceModel(
                VirtualFolderHints.ForType(boundary.Name, engine.Artifact, boundary.ContainingNamespace.ToDisplayString()),
                boundary.ContainingNamespace.IsGlobalNamespace ? null : boundary.ContainingNamespace.ToDisplayString(),
                boundary.Name,
                boundary.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                engine,
                group.Select(p => p.Entity).OrderBy(e => e.ToDisplayString(), System.StringComparer.Ordinal).Select(e => e.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToEquatableArray());
            sources.Add((model.Hint, new BoundaryPersistenceTemplate(model).ToSourceText().ToString()));
        }

        return sources;
    }
}

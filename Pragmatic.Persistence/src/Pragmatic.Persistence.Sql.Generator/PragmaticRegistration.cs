using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Sql.Generator.Models;
using Pragmatic.Persistence.Sql.Generator.Templates;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator;

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
        "global::Pragmatic.Persistence.Sql.SqlSession", "global::Pragmatic.Persistence.Sql.ISqlFilters", "global::Pragmatic.Persistence.Sql.Pragmatic.PragmaticSqlFilters",
        "global::Pragmatic.Persistence.Sql.Pragmatic.SqlUnitOfWork", "global::Pragmatic.Persistence.Sql.Pragmatic.ISqlSavePhase", "SqlRepository"));

    public static readonly PragmaticRegistration Documents = new(new(
        "global::Pragmatic.Persistence.Documents.DocSession", "global::Pragmatic.Persistence.Documents.IDocFilters", "global::Pragmatic.Persistence.Documents.Pragmatic.PragmaticDocFilters",
        "global::Pragmatic.Persistence.Documents.Pragmatic.DocUnitOfWork", "global::Pragmatic.Persistence.Documents.Pragmatic.IDocSavePhase", "DocRepository"));

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
        foreach (var group in entities.Select(e => (Entity: e, Boundary: PragmaticPermissions.BoundaryOf(e))).Where(p => p.Boundary is INamedTypeSymbol)
                     .GroupBy(p => p.Boundary!, SymbolEqualityComparer.Default).OrderBy(g => g.Key!.ToDisplayString(), System.StringComparer.Ordinal))
        {
            var boundary = (INamedTypeSymbol)group.Key!;
            if (!SymbolEqualityComparer.Default.Equals(boundary.ContainingAssembly, compilation.Assembly))
                continue; // a boundary of a referenced assembly registers there
            var model = new BoundaryPersistenceModel(
                $"{boundary.ToDisplayString().Replace('.', '_')}.Persistence.g.cs",
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

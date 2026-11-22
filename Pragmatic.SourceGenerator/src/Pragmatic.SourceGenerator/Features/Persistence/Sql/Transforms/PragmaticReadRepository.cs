using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Transforms;

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
    ///     <paramref name="includeSets" /> are the sets of paths the invokers read the entity with (a <c>[LoadEntity]</c>'s, <see cref="LoadIncludes" />;
    ///     a mutation's load): one read per key.
    /// </summary>
    public static (string Hint, string Source)? For(INamedTypeSymbol entity, INamedTypeSymbol shapedEntity, IReadOnlyList<PragmaticQuery> queries, IEnumerable<IReadOnlyList<string>> includeSets,
        IEnumerable<string>? sortMembers = null)
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
            includeSets.GroupBy(s => IncludePaths.Key(s)).OrderBy(g => g.Key, System.StringComparer.Ordinal)
                .Select(g => new IncludeReadModel(g.Key, g.First().ToEquatableArray())).ToEquatableArray(),
            // The collections of entities the shape declares (a [Relation]'s navigation is generated: a member of the shaped symbol only), each
            // a case of INavigationLinker.LinkAsync.
            shapedEntity.GetMembers().OfType<IPropertySymbol>()
                .Where(p => !p.IsStatic && p.GetMethod is not null && p.Type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } collection
                            && collection.AllInterfaces.Concat([collection]).Any(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_ICollection_T)
                            && collection.TypeArguments[0].AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.Persistence.Entity.IEntity"))
                .Select(p => new LinkNavigationModel(p.Name, ((INamedTypeSymbol)p.Type).TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                .ToEquatableArray(),
            AutocompleteReads.Of(entity),
            DerivesFromAnEntity(entity),
            Identity.Transforms.UserEntityTransform.MatchPropertyOf(entity),
            PageKeysOf(entity, sortMembers ?? []));
        return (model.Hint, new ReadRepositoryTemplate(model).ToSourceText().ToString());
    }

    /// <summary>
    ///     <c>PageAsync</c>'s keys: the entity's key first (the fallback), then each member a grid filter sorts by whose column the entity's
    ///     table holds. A member through a navigation has no key, and an ordering naming it is refused (<see cref="Unkeyed" />).
    /// </summary>
    private static EquatableArray<PageOrderKeyModel> PageKeysOf(INamedTypeSymbol entity, IEnumerable<string> sortMembers)
        => new[] { "PersistenceId" }.Concat(sortMembers.Where(m => Unkeyed(entity, m) is null)).Distinct()
            .Select(member => new PageOrderKeyModel(member, member.Replace('.', '_'), PragmaticQuery.OrderKeyDialects(PragmaticQuery.ColumnOf(entity, member)).ToEquatableArray()))
            .ToEquatableArray();

    /// <summary>Why <paramref name="member" /> has no key of <paramref name="entity" />'s table, or null when it has one.</summary>
    internal static string? Unkeyed(INamedTypeSymbol entity, string member)
        => PragmaticQuery.ColumnOf(entity, member).Contains('.')
            ? $"the sort on {member}, through a navigation the Sql engine does not order by (an ordering that names it is refused)"
            : null;

    /// <summary>
    ///     True when <paramref name="entity" /> derives from another Pragmatic entity (a hierarchy's derived type): that entity has the members the
    ///     engines nest in an entity too, so the derived type's hide them.
    /// </summary>
    internal static bool DerivesFromAnEntity(INamedTypeSymbol entity)
        => entity.BaseType is { } baseType && baseType.AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.Persistence.Entity.IEntity");

    private static RunCaseModel RunCaseOf(PragmaticQuery query)
        => new("global::" + query.Type.ToDisplayString(), query.Type.Name, query.Unsupported, "global::" + query.Result.ToDisplayString(),
            !query.MapsInMemory && query.View is null && !SymbolEqualityComparer.Default.Equals(query.Result, query.Entity), query.MapsInMemory, query.InMemoryNavigations.ToEquatableArray(),
            query.View is not null && query.Unsupported is null ? query.GroupedChain() : null,
            query.JoinTarget is not null && query.Unsupported is null ? query.JoinedChain("session") : null,
            query.Unsupported is null ? query.EmptyGroupsStatement("rows") : null);
}

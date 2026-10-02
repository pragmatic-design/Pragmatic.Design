using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates DI registration extension method for all auto-generated query filters
///     (SoftDelete, Tenant) discovered in the compilation.
/// </summary>
internal sealed class QueryFilterRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<EntityMetadataModel> _entities;
    private readonly string _namespacePrefix;

    public QueryFilterRegistrationTemplate(ImmutableArray<EntityMetadataModel> entities)
    {
        _entities = entities;
        _namespacePrefix = DeriveNamespacePrefix(entities);
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/QueryPipeline";
    protected override string? SourceInfo => $"QueryFilter registration for {_entities.Length} entities";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForAssembly("Persistence", "QueryFilters"),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return HasFilteredEntity(_entities);
    }

    /// <summary>
    ///     The fully-qualified method this template emits, or <c>null</c> when it emits nothing.
    /// </summary>
    /// <remarks>
    ///     Declared on the Persistence metadata so the generated host can call it. Nothing called it
    ///     before: the method was generated into every boundary and invoked by no one, so an
    ///     application with <c>[HasAccessScopes]</c> or <c>[HasOwner]</c> entities started, answered
    ///     200 to everything, and applied no row-level filter at all — the SQL carried only the
    ///     soft-delete predicate. It stayed invisible here because the Showcase calls it by hand.
    /// </remarks>
    public static string? RegistrationMethodFor(ImmutableArray<EntityMetadataModel> entities)
    {
        if (!HasFilteredEntity(entities))
            return null;

        var prefix = DeriveNamespacePrefix(entities);
        var container = string.IsNullOrEmpty(prefix)
            ? "QueryFilterRegistrationExtensions"
            : $"{prefix}.QueryFilterRegistrationExtensions";

        return $"{container}.{MethodNameFor(prefix)}";
    }

    /// <summary>
    ///     Whether this assembly has anything to filter. <see cref="FilterMapRegistryTemplate" /> asks
    ///     the same question and must get the same answer.
    /// </summary>
    internal static bool HasFilteredEntity(ImmutableArray<EntityMetadataModel> entities)
        => entities.Any(e =>
            e.IsSoftDelete || e.IsTenantEntity || e.IsTemporalRelation || e.IsOwnedEntity || e.IsScopedEntity
            || e.HasParentVisibilityFilter || e.HasInternalVisibilityFilter);

    private static string MethodNameFor(string prefix)
        => string.IsNullOrEmpty(prefix)
            ? "AddGeneratedQueryFilters"
            : $"Add{NamespacePrefixHelper.ToIdentifier(prefix)}QueryFilters";

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Microsoft.Extensions.DependencyInjection.Extensions");
        AddUsing("Pragmatic.Persistence.Query.Filters");

        if (!string.IsNullOrEmpty(_namespacePrefix))
            AppendNamespace(_namespacePrefix);
        AppendLine();

        XmlSummary("Auto-generated query filter DI registration.");

        var methodName = MethodNameFor(_namespacePrefix);

        Class("QueryFilterRegistrationExtensions", () =>
        {
            XmlSummary("Registers all auto-generated query filters and infrastructure.");
            XmlParam("services", "The service collection.");
            XmlReturns("The service collection for chaining.");

            var parameters = new List<MethodParameter>
            {
                new("this global::Microsoft.Extensions.DependencyInjection.IServiceCollection", "services")
            };

            Method(methodName, RenderMethodBody, "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
                parameters, modifiers: new MethodModifiers { IsStatic = true });
        },
        modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderMethodBody()
    {
        Comment("Infrastructure — DefaultQueryFilterProvider, QueryFilterToggle, and type registry");
        AppendLine("services.TryAddSingleton<global::Pragmatic.Persistence.Query.Filters.IQueryFilterTypeRegistry, global::Pragmatic.Persistence.Query.Filters.PassthroughQueryFilterTypeRegistry>();");
        AppendLine("services.TryAddScoped<global::Pragmatic.Persistence.Query.Filters.IQueryFilterProvider, global::Pragmatic.Persistence.Query.Filters.DefaultQueryFilterProvider>();");
        AppendLine("services.TryAddScoped<global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle, global::Pragmatic.Persistence.Query.Filters.QueryFilterToggle>();");
        // The adapter FilterMapComposer always documented and nobody wrote: without it the map
        // the navigation visitor reads holds only what the generator put there, so a filter
        // registered in DI guarded a root query and not an Include pointing at the same rows.
        AppendLine("services.TryAddScoped<global::Pragmatic.Persistence.Query.Filters.IVisibilityFilterProvider, global::Pragmatic.Persistence.Query.Filters.QueryFilterProviderAdapter>();");
        AppendLine();

        // SoftDelete filters (singleton — stateless)
        var softDeleteEntities = _entities.Where(e => e.IsSoftDelete).ToList();
        if (softDeleteEntities.Count > 0)
        {
            Comment("Auto-generated SoftDelete filters (stateless — singleton)");
            foreach (var entity in softDeleteEntities)
            {
                var filterClass = $"global::{entity.Namespace}.{entity.TypeName}.SoftDeleteFilter";
                AppendLine($"services.AddSingleton<global::Pragmatic.Persistence.Query.Filters.IQueryFilter, {filterClass}>();");
            }
            AppendLine();
        }

        // Tenant filters (scoped — depends on ITenantContext)
        var tenantEntities = _entities.Where(e => e.IsTenantEntity).ToList();
        if (tenantEntities.Count > 0)
        {
            Comment("Auto-generated Tenant filters (scoped — depends on ITenantContext)");
            foreach (var entity in tenantEntities)
            {
                var filterClass = $"global::{entity.Namespace}.{entity.TypeName}.TenantFilter";
                AppendLine($"services.AddScoped<global::Pragmatic.Persistence.Query.Filters.IQueryFilter, {filterClass}>();");
            }
            AppendLine();
        }

        // Temporal filters (singleton — stateless, uses UtcNow at evaluation time)
        var temporalEntities = _entities.Where(e => e.IsTemporalRelation).ToList();
        if (temporalEntities.Count > 0)
        {
            Comment("Auto-generated Temporal filters (stateless — singleton, evaluates UtcNow at query time)");
            foreach (var entity in temporalEntities)
            {
                var filterClass = $"global::{entity.Namespace}.{entity.TypeName}.TemporalFilter";
                AppendLine($"services.AddSingleton<global::Pragmatic.Persistence.Query.Filters.IQueryFilter, {filterClass}>();");
            }
            AppendLine();
        }

        // Data access filters — combined (L1+L2), ownership-only, or scoped-only
        var combinedEntities = _entities.Where(e => e.IsOwnedEntity && e.IsScopedEntity).ToList();
        if (combinedEntities.Count > 0)
        {
            Comment("Auto-generated DataAccess filters (scoped — combined ownership + scopes, OR logic)");
            foreach (var entity in combinedEntities)
            {
                var filterClass = $"global::{entity.Namespace}.{entity.TypeName}.DataAccessFilter";
                AppendLine($"services.AddScoped<global::Pragmatic.Persistence.Query.Filters.IQueryFilter, {filterClass}>();");
            }
            AppendLine();
        }

        var ownershipOnlyEntities = _entities.Where(e => e.IsOwnedEntity && !e.IsScopedEntity).ToList();
        if (ownershipOnlyEntities.Count > 0)
        {
            Comment("Auto-generated Ownership filters (scoped — depends on ICurrentUser)");
            foreach (var entity in ownershipOnlyEntities)
            {
                var filterClass = $"global::{entity.Namespace}.{entity.TypeName}.OwnershipFilter";
                AppendLine($"services.AddScoped<global::Pragmatic.Persistence.Query.Filters.IQueryFilter, {filterClass}>();");
            }
            AppendLine();
        }

        var scopedOnlyEntities = _entities.Where(e => e.IsScopedEntity && !e.IsOwnedEntity).ToList();
        if (scopedOnlyEntities.Count > 0)
        {
            Comment("Auto-generated ScopedData filters (scoped — depends on IUserScopeResolver)");
            foreach (var entity in scopedOnlyEntities)
            {
                var filterClass = $"global::{entity.Namespace}.{entity.TypeName}.ScopedDataFilter";
                AppendLine($"services.AddScoped<global::Pragmatic.Persistence.Query.Filters.IQueryFilter, {filterClass}>();");
            }
            AppendLine();
        }

        // ComputedScopeFilter — auto-registered for all scoped entities (resolves DataScopeRule<T> at runtime)
        var allScopedEntities = _entities.Where(e => e.IsScopedEntity).ToList();
        if (allScopedEntities.Count > 0)
        {
            Comment("Auto-registered ComputedScopeFilter (scoped — ORs DataScopeRule<T> expressions at query time)");
            foreach (var entity in allScopedEntities)
            {
                var entityFqn = $"global::{entity.Namespace}.{entity.TypeName}";
                // TryAddEnumerable, not TryAddScoped: the three lines above register single services,
                // where TryAdd is right; IQueryFilter is a collection, where TryAdd asks whether any
                // filter at all exists and therefore always skips. Four near-identical lines, one of
                // them wrong, and the difference is the cardinality of the service — not visible in
                // the line itself.
                AppendLine($"services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped<global::Pragmatic.Persistence.Query.Filters.IQueryFilter, global::Pragmatic.Persistence.Scopes.ComputedScopeFilter<{entityFqn}>>());");
            }
            AppendLine();

            // The write half of the same rules. ComputedScopeFilter evaluates a Computed rule at query
            // time; a Materialized one has to be written onto the row, and IScopeMaterializer — which
            // does exactly that — was registered by an application and called by nothing, so a rule
            // could be declared, accepted, and never evaluated against anything.
            //
            // One typed step per scoped entity rather than one reflective step for all of them: the
            // generator knows which entities are scoped, so the type is decided here instead of by a
            // MakeGenericType on entry.Entity.GetType() at save time.
            Comment("Auto-registered scope materialization (writes scope:{name} onto the row at SaveChanges)");
            AppendLine("services.TryAddSingleton<global::Pragmatic.Persistence.Scopes.IScopeMaterializer, global::Pragmatic.Persistence.Scopes.ScopeMaterializer>();");
            foreach (var entity in allScopedEntities)
            {
                var entityFqn = $"global::{entity.Namespace}.{entity.TypeName}";
                AppendLine($"services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped<global::Pragmatic.Persistence.EFCore.Scopes.IScopeMaterializationStep, global::Pragmatic.Persistence.EFCore.Scopes.ScopeMaterializationStep<{entityFqn}>>());");
            }
            AppendLine();
        }

        // Trait children whose parent restricts rows: the child query filters by parent id only, so
        // without this the caller could list the children of a parent it cannot see.
        var parentVisibilityEntities = _entities.Where(e => e.HasParentVisibilityFilter).ToList();
        if (parentVisibilityEntities.Count > 0)
        {
            Comment("Auto-generated ParentVisibility filters (scoped — the parent's own predicate, one level up)");
            foreach (var entity in parentVisibilityEntities)
            {
                var filterClass = $"global::{entity.Namespace}.{entity.TypeName}.ParentVisibilityFilter";
                AppendLine($"services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped<global::Pragmatic.Persistence.Query.Filters.IQueryFilter, {filterClass}>());");
            }
            AppendLine();
        }

        // Comments marked Internal: read only with the trait's view-internal permission.
        var internalVisibilityEntities = _entities.Where(e => e.HasInternalVisibilityFilter).ToList();
        if (internalVisibilityEntities.Count > 0)
        {
            Comment("Auto-generated InternalVisibility filters (comments marked Internal, bypassed by view-internal)");
            foreach (var entity in internalVisibilityEntities)
            {
                var filterClass = $"global::{entity.Namespace}.{entity.TypeName}.InternalVisibilityFilter";
                AppendLine($"services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped<global::Pragmatic.Persistence.Query.Filters.IQueryFilter, {filterClass}>());");
            }
            AppendLine();
        }

        Comment("FilterMapComposer — composes static + dynamic filters for navigation visitor");
        // Add, not TryAdd: every module has its own FilterMapRegistry, and TryAdd kept the
        // first one to register. In a multi-module application that meant the static filter
        // map belonged to whichever module won the race, and the other modules' entities had
        // no navigation filters at all. FilterMapComposer merges them now.
        AppendLine("services.AddSingleton<global::System.Func<global::Pragmatic.Persistence.Query.Filters.FilterContext, global::Pragmatic.Persistence.Query.Filters.FilterMap>>(FilterMapRegistry.CreateForContext);");
        AppendLine("services.TryAddScoped<global::Pragmatic.Persistence.EFCore.Query.FilterMapComposer>();");
        AppendLine();

        AppendLine("return services;");
    }

    private static string DeriveNamespacePrefix(ImmutableArray<EntityMetadataModel> entities)
    {
        var namespaces = entities
            .Where(e => e.IsSoftDelete || e.IsTenantEntity || e.IsTemporalRelation || e.IsOwnedEntity || e.IsScopedEntity
                        || e.HasParentVisibilityFilter || e.HasInternalVisibilityFilter)
            .Select(e => e.Namespace)
            .Where(ns => !string.IsNullOrEmpty(ns) && ns != "<global namespace>")
            .ToList();

        if (namespaces.Count == 0)
            return string.Empty;
        return NamespacePrefixHelper.DerivePrefix(namespaces);
    }
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Sql.Generator.Models;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     The filter classes Pragmatic's generator nests in an entity (<c>SoftDeleteFilter</c>, <c>TenantFilter</c>, <c>OwnershipFilter</c>,
///     <c>ScopedDataFilter</c>, <c>DataAccessFilter</c>), declared with the shape when that generator does not (no EF Core). The Sql engine
///     does not evaluate them: it translates each family itself. They are declared so the names a caller lifts a filter by
///     (<c>Disable&lt;Reservation.OwnershipFilter&gt;()</c>, <c>[WithoutFilter&lt;T&gt;]</c>) are the same types on both engines. The text
///     mirrors <c>SoftDeleteFilterTemplate</c>, <c>TenantFilterTemplate</c>, <c>OwnershipFilterTemplate</c>, <c>ScopedDataFilterTemplate</c>
///     and <c>DataAccessFilterTemplate</c>; <c>ShapeParityTests</c> compares them with what those emit.
/// </summary>
internal static class PragmaticFilters
{
    private const string Filters = "global::Pragmatic.Persistence.Query.Filters.";

    /// <summary>The nested filter classes for <paramref name="entity" />, appended to its shape's.</summary>
    public static void Append(INamedTypeSymbol entity, List<ShapeFilterModel> filters)
    {
        var type = "global::" + entity.ToDisplayString();
        var owned = PragmaticShape.Has(entity, "HasOwnerAttribute");
        var scoped = PragmaticShape.Has(entity, "HasAccessScopesAttribute");
        var bypass = PragmaticPermissions.ViewAll(entity);

        if (PragmaticShape.Has(entity, "SoftDeleteAttribute"))
            filters.Add(new("SoftDeleteFilter", "", $"{Filters}IQueryFilter<{type}>", 100, "Default", null, "=> entity => !entity.IsDeleted;"));

        if (entity.AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.MultiTenancy.ITenantEntity"))
            filters.Add(new("TenantFilter", "global::Pragmatic.MultiTenancy.ITenantContext tenantContext", $"{Filters}IQueryFilter<{type}>, {Filters}ITenantFilter", 200, "All", null,
                "=> entity => tenantContext.TenantId != null && entity.TenantId == tenantContext.TenantId;"));

        // One of three, as Pragmatic decides: owner and scopes together are the combined filter (owner OR scope).
        const string resolver = "global::Pragmatic.Authorization.IUserScopeResolver scopeResolver, global::Pragmatic.Identity.ICurrentUser currentUser";
        const string anyScope = "global::System.Linq.Enumerable.Any(entity.AccessScopes, s => userScopes.Contains(s))";
        if (owned && scoped)
            filters.Add(new("DataAccessFilter", resolver, $"{Filters}IPermissionBasedFilter<{type}>, {Filters}IScopeVisibilityFilter", 200, "Default", bypass,
                $"{{ var userScopes = scopeResolver.ResolveAccessScopes(currentUser); var userId = currentUser.Id; return entity => entity.OwnerId == userId || {anyScope}; }}"));
        else if (owned)
            filters.Add(new("OwnershipFilter", "global::Pragmatic.Identity.ICurrentUser currentUser", $"{Filters}IPermissionBasedFilter<{type}>", 200, "Default", bypass,
                "=> entity => entity.OwnerId == currentUser.Id;"));
        else if (scoped)
            filters.Add(new("ScopedDataFilter", resolver, $"{Filters}IPermissionBasedFilter<{type}>, {Filters}IScopeVisibilityFilter", 250, "Default", bypass,
                $"{{ var userScopes = scopeResolver.ResolveAccessScopes(currentUser); return entity => {anyScope}; }}"));

        // [TemporalRelation]: the stretches active now (TemporalFilterTemplate), and the scope that lifts it (TemporalHistoryScopeTemplate).
        if (IsTemporal(entity))
            filters.Add(new("TemporalFilter", "global::System.TimeProvider? timeProvider = null", $"{Filters}IQueryFilter<{type}>", 200, "Default", null,
                "{ var now = (timeProvider ?? global::System.TimeProvider.System).GetUtcNow(); return entity => entity.ValidFrom <= now && (entity.ValidTo == null || entity.ValidTo > now); }",
                IncludeHistory: true));

        AppendParentVisibility(entity, filters);
    }

    /// <summary>
    ///     A trait's child (<c>[TraitOf&lt;TParent&gt;]</c>, HACK of the spike) of an owned or scoped parent: <c>ParentVisibilityFilter</c>, the
    ///     parent's own predicate read through the navigation named after it, its bypass the parent's (TraitParentVisibilityFilterTemplate).
    /// </summary>
    public static void AppendParentVisibility(INamedTypeSymbol entity, List<ShapeFilterModel> filters)
    {
        if (TraitParent(entity) is not { } parent)
            return;
        var owned = PragmaticShape.Has(parent, "HasOwnerAttribute");
        var scoped = PragmaticShape.Has(parent, "HasAccessScopesAttribute");
        if (!owned && !scoped)
            return;
        var type = "global::" + entity.ToDisplayString();
        var nav = "entity." + parent.Name;
        var parameters = (scoped ? "global::Pragmatic.Authorization.IUserScopeResolver scopeResolver, " : "") + "global::Pragmatic.Identity.ICurrentUser currentUser";
        var ownedPredicate = $"{nav} != null && {nav}.OwnerId == userId";
        var scopedPredicate = $"{nav} != null && global::System.Linq.Enumerable.Any({nav}.AccessScopes, s => userScopes.Contains(s))";
        var predicate = owned && scoped ? $"{ownedPredicate} || {scopedPredicate}" : owned ? ownedPredicate : scopedPredicate;
        filters.Add(new("ParentVisibilityFilter", parameters, $"{Filters}IPermissionBasedFilter<{type}>", 200, "Default", PragmaticPermissions.ViewAll(parent),
            $"{{ {(scoped ? "var userScopes = scopeResolver.ResolveAccessScopes(currentUser); " : "")}var userId = currentUser.Id; return entity => {predicate}; }}"));
    }

    /// <summary>The parent a trait's child names (<c>[TraitOf&lt;TParent&gt;]</c>, read by name); null for any other entity.</summary>
    public static INamedTypeSymbol? TraitParent(INamedTypeSymbol entity)
        => entity.GetAttributes().Select(a => a.AttributeClass).FirstOrDefault(c => c is { Name: "TraitOfAttribute", TypeArguments.Length: 1 })?.TypeArguments[0] as INamedTypeSymbol;

    /// <summary>A <c>[TemporalRelation]</c> entity (its validity, <c>ITemporalRelation</c>'s ValidFrom and ValidTo, its own).</summary>
    public static bool IsTemporal(INamedTypeSymbol entity) => PragmaticShape.Has(entity, "TemporalRelationAttribute");
}

using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

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

    /// <summary>The nested filter classes for <paramref name="entity" />, appended to its shape.</summary>
    public static void Append(INamedTypeSymbol entity, StringBuilder body)
    {
        var type = "global::" + entity.ToDisplayString();
        var owned = PragmaticShape.Has(entity, "HasOwnerAttribute");
        var scoped = PragmaticShape.Has(entity, "HasAccessScopesAttribute");
        var bypass = PragmaticPermissions.ViewAll(entity);

        if (PragmaticShape.Has(entity, "SoftDeleteAttribute"))
            Filter(body, "SoftDeleteFilter", "", $"{Filters}IQueryFilter<{type}>", 100, "Default", null,
                $"public global::System.Linq.Expressions.Expression<global::System.Func<{type}, bool>> GetFilter() => entity => !entity.IsDeleted;");

        if (entity.AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.MultiTenancy.ITenantEntity"))
            Filter(body, "TenantFilter", "global::Pragmatic.MultiTenancy.ITenantContext tenantContext", $"{Filters}IQueryFilter<{type}>, {Filters}ITenantFilter", 200, "All", null,
                $"public global::System.Linq.Expressions.Expression<global::System.Func<{type}, bool>> GetFilter() => entity => tenantContext.TenantId != null && entity.TenantId == tenantContext.TenantId;");

        // One of three, as Pragmatic decides: owner and scopes together are the combined filter (owner OR scope).
        const string resolver = "global::Pragmatic.Authorization.IUserScopeResolver scopeResolver, global::Pragmatic.Identity.ICurrentUser currentUser";
        const string anyScope = "global::System.Linq.Enumerable.Any(entity.AccessScopes, s => userScopes.Contains(s))";
        if (owned && scoped)
            Filter(body, "DataAccessFilter", resolver, $"{Filters}IPermissionBasedFilter<{type}>, {Filters}IScopeVisibilityFilter", 200, "Default", bypass,
                $"public global::System.Linq.Expressions.Expression<global::System.Func<{type}, bool>> GetFilter() {{ var userScopes = scopeResolver.ResolveAccessScopes(currentUser); var userId = currentUser.Id; return entity => entity.OwnerId == userId || {anyScope}; }}");
        else if (owned)
            Filter(body, "OwnershipFilter", "global::Pragmatic.Identity.ICurrentUser currentUser", $"{Filters}IPermissionBasedFilter<{type}>", 200, "Default", bypass,
                $"public global::System.Linq.Expressions.Expression<global::System.Func<{type}, bool>> GetFilter() => entity => entity.OwnerId == currentUser.Id;");
        else if (scoped)
            Filter(body, "ScopedDataFilter", resolver, $"{Filters}IPermissionBasedFilter<{type}>, {Filters}IScopeVisibilityFilter", 250, "Default", bypass,
                $"public global::System.Linq.Expressions.Expression<global::System.Func<{type}, bool>> GetFilter() {{ var userScopes = scopeResolver.ResolveAccessScopes(currentUser); return entity => {anyScope}; }}");

        // [TemporalRelation]: the stretches active now (TemporalFilterTemplate), and the scope that lifts it (TemporalHistoryScopeTemplate).
        if (IsTemporal(entity))
        {
            Filter(body, "TemporalFilter", "global::System.TimeProvider? timeProvider = null", $"{Filters}IQueryFilter<{type}>", 200, "Default", null,
                $"public global::System.Linq.Expressions.Expression<global::System.Func<{type}, bool>> GetFilter() {{ var now = (timeProvider ?? global::System.TimeProvider.System).GetUtcNow(); return entity => entity.ValidFrom <= now && (entity.ValidTo == null || entity.ValidTo > now); }}");
            body.AppendLine($"    public static global::System.IDisposable IncludeHistory({Filters}IQueryFilterToggle filters) => filters.Disable<{type}.TemporalFilter>();");
        }

        AppendParentVisibility(entity, body);
    }

    /// <summary>
    ///     A trait's child (<c>[TraitOf&lt;TParent&gt;]</c>, HACK of the spike) of an owned or scoped parent: <c>ParentVisibilityFilter</c>, the
    ///     parent's own predicate read through the navigation named after it, its bypass the parent's (TraitParentVisibilityFilterTemplate).
    /// </summary>
    public static void AppendParentVisibility(INamedTypeSymbol entity, StringBuilder body)
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
        Filter(body, "ParentVisibilityFilter", parameters, $"{Filters}IPermissionBasedFilter<{type}>", 200, "Default", PragmaticPermissions.ViewAll(parent),
            $"public global::System.Linq.Expressions.Expression<global::System.Func<{type}, bool>> GetFilter() {{ {(scoped ? "var userScopes = scopeResolver.ResolveAccessScopes(currentUser); " : "")}var userId = currentUser.Id; return entity => {predicate}; }}");
    }

    /// <summary>The parent a trait's child names (<c>[TraitOf&lt;TParent&gt;]</c>, read by name); null for any other entity.</summary>
    public static INamedTypeSymbol? TraitParent(INamedTypeSymbol entity)
        => entity.GetAttributes().Select(a => a.AttributeClass).FirstOrDefault(c => c is { Name: "TraitOfAttribute", TypeArguments.Length: 1 })?.TypeArguments[0] as INamedTypeSymbol;

    /// <summary>A <c>[TemporalRelation]</c> entity (its validity, <c>ITemporalRelation</c>'s ValidFrom and ValidTo, its own).</summary>
    public static bool IsTemporal(INamedTypeSymbol entity) => PragmaticShape.Has(entity, "TemporalRelationAttribute");

    private static void Filter(StringBuilder body, string name, string parameters, string bases, int priority, string scope, string? bypass, string getFilter)
    {
        body.AppendLine($"    public sealed class {name}{(parameters.Length == 0 ? "" : $"({parameters})")} : {bases}");
        body.AppendLine("    {");
        if (bypass is not null)
            body.AppendLine($"        public string BypassPermission => \"{bypass}\";");
        body.AppendLine($"        public int Priority => {priority};");
        body.AppendLine($"        public {Filters}FilterScope Scope => {Filters}FilterScope.{scope};");
        body.AppendLine($"        {getFilter}");
        body.AppendLine("    }");
    }
}

using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates a static FilterMapRegistry class that builds a FilterMap
///     from all SG-discovered filters (SoftDelete, Tenant).
///     Unlike the individual IQueryFilter classes, this produces a single
///     dictionary-based FilterMap for use by PragmaticQueryFilterVisitor.
/// </summary>
internal sealed class FilterMapRegistryTemplate : CSharpTemplate
{
    private readonly ImmutableArray<EntityMetadataModel> _entities;
    private readonly string _namespacePrefix;

    public FilterMapRegistryTemplate(ImmutableArray<EntityMetadataModel> entities)
    {
        _entities = entities;
        _namespacePrefix = DeriveNamespacePrefix(entities);
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/QueryPipeline";
    protected override string? SourceInfo => $"FilterMapRegistry for {_entities.Length} entities";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForAssembly("Persistence", "FilterMap"),
            ToSourceText());
    }

    /// <summary>
    ///     The same question <see cref="QueryFilterRegistrationTemplate" /> asks, and deliberately
    ///     the same code.
    /// </summary>
    /// <remarks>
    ///     If the two conditions differed — say the registration counted owned and scoped entities and
    ///     this one did not — a boundary whose only filtered entities were <c>[HasAccessScopes]</c>
    ///     would get a registration referring to a registry nobody emitted: CS0103 inside generated
    ///     code, and the boundary would not compile at all.
    /// </remarks>
    protected override bool Validate() => QueryFilterRegistrationTemplate.HasFilteredEntity(_entities);

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Collections.Generic");
        AddUsing("System.Linq.Expressions");
        AddUsing("Pragmatic.Persistence.Query.Filters");

        if (!string.IsNullOrEmpty(_namespacePrefix))
            AppendNamespace(_namespacePrefix);
        AppendLine();

        XmlSummary(
            "Auto-generated compile-time filter registry. " +
            "Builds a <see cref=\"FilterMap\"/> from all SG-discovered filters (SoftDelete, Tenant).");

        Class("FilterMapRegistry", RenderClassBody,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderClassBody()
    {
        var softDeleteEntities = _entities
            .Where(e => e.IsValid && e.IsSoftDelete)
            .ToList();

        var tenantEntities = _entities
            .Where(e => e.IsValid && e.IsTenantEntity)
            .ToList();

        var temporalEntities = _entities
            .Where(e => e.IsValid && e.IsTemporalRelation)
            .ToList();

        // All entity types with any filter — these need pre-resolved Where<T> MethodInfo
        var allFilteredEntities = _entities
            .Where(e => e.IsValid && (e.IsSoftDelete || e.IsTenantEntity || e.IsTemporalRelation))
            .GroupBy(e => e.FullTypeName)
            .Select(g => g.First())
            .OrderBy(e => e.FullTypeName)
            .ToList();

        // Pre-resolved Where<T> MethodInfo per entity — AOT-full, zero runtime reflection
        if (allFilteredEntities.Count > 0)
        {
            RenderWhereMethodsMap(allFilteredEntities);
            AppendLine();
        }

        // Static SoftDelete FilterMap — cached, immutable
        if (softDeleteEntities.Count > 0)
        {
            RenderStaticSoftDeleteMap(softDeleteEntities, allFilteredEntities.Count > 0);
            AppendLine();
        }

        // CreateForContext method
        RenderCreateForContext(softDeleteEntities, tenantEntities, temporalEntities, allFilteredEntities.Count > 0);
    }

    private void RenderWhereMethodsMap(List<EntityMetadataModel> allFilteredEntities)
    {
        Comment("Enumerable.Where<T> pre-resolved per entity via expression tree — zero GetMethods reflection");
        foreach (var entity in allFilteredEntities)
        {
            var globalType = $"global::{entity.FullTypeName}";
            var fieldName = $"_where_{entity.TypeName}";
            AppendLine($"private static readonly global::System.Reflection.MethodInfo {fieldName} = ((global::System.Linq.Expressions.MethodCallExpression)((global::System.Linq.Expressions.Expression<global::System.Func<global::System.Collections.Generic.IEnumerable<{globalType}>, global::System.Collections.Generic.IEnumerable<{globalType}>>>)(x => x.Where(_ => true))).Body).Method;");
        }
        AppendLine();

        Comment("Pre-resolved Where<T> MethodInfo dictionary — consumed by PragmaticQueryFilterVisitor");
        AppendLine("private static readonly global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Reflection.MethodInfo> WhereMethods = new()");
        AppendLine("{");
        IncreaseIndent();
        foreach (var entity in allFilteredEntities)
        {
            var globalType = $"global::{entity.FullTypeName}";
            var fieldName = $"_where_{entity.TypeName}";
            AppendLine($"[typeof({globalType})] = {fieldName},");
        }
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderStaticSoftDeleteMap(List<EntityMetadataModel> softDeleteEntities, bool hasWhereMethods)
    {
        XmlSummary("Cached SoftDelete filters — stateless, shared across all contexts.");
        AppendLine("private static readonly global::Pragmatic.Persistence.Query.Filters.FilterMap SoftDeleteFilters = new(new global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Linq.Expressions.LambdaExpression>");
        AppendLine("{");
        IncreaseIndent();

        foreach (var entity in softDeleteEntities)
        {
            var globalType = $"global::{entity.FullTypeName}";
            AppendLine($"[typeof({globalType})] = (global::System.Linq.Expressions.Expression<global::System.Func<{globalType}, bool>>)(entity => !entity.IsDeleted),");
        }

        DecreaseIndent();
        if (hasWhereMethods)
            AppendLine("}, WhereMethods);");
        else
            AppendLine("});");
    }

    private void RenderCreateForContext(
        List<EntityMetadataModel> softDeleteEntities,
        List<EntityMetadataModel> tenantEntities,
        List<EntityMetadataModel> temporalEntities,
        bool hasWhereMethods)
    {
        XmlSummary("Creates a <see cref=\"FilterMap\"/> for the given context, respecting filter mode and disabled filters.");
        XmlParam("context", "The current filter context (mode, tenant, disabled filters).");
        XmlReturns("A FilterMap with all applicable filters for the current context.");

        var parameters = new List<MethodParameter>
        {
            new("global::Pragmatic.Persistence.Query.Filters.FilterContext", "context")
        };

        Method("CreateForContext", () => RenderCreateForContextBody(softDeleteEntities, tenantEntities, temporalEntities, hasWhereMethods),
            "global::Pragmatic.Persistence.Query.Filters.FilterMap",
            parameters,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderCreateForContextBody(
        List<EntityMetadataModel> softDeleteEntities,
        List<EntityMetadataModel> tenantEntities,
        List<EntityMetadataModel> temporalEntities,
        bool hasWhereMethods)
    {
        // Raw mode — no filters at all
        AppendLine("if (context.IsRaw)");
        AppendLine("    return global::Pragmatic.Persistence.Query.Filters.FilterMap.Empty;");
        AppendLine();

        // Start with SoftDelete if available (already carries WhereMethods)
        if (softDeleteEntities.Count > 0)
        {
            AppendLine("var map = SoftDeleteFilters;");
        }
        else if (hasWhereMethods)
        {
            // No soft-delete but other filters exist — start with empty filters + WhereMethods
            AppendLine("var map = new global::Pragmatic.Persistence.Query.Filters.FilterMap(new global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Linq.Expressions.LambdaExpression>(), WhereMethods);");
        }
        else
        {
            AppendLine("var map = global::Pragmatic.Persistence.Query.Filters.FilterMap.Empty;");
        }

        // Add tenant filters if not skipped
        if (tenantEntities.Count > 0)
        {
            AppendLine();
            // Fail-closed: guard on !SkipTenant only. An unresolved tenant (TenantId == null) yields a
            // constant-false predicate → zero rows, matching the SG TenantFilter (IQueryFilter) and the
            // EF named "Tenant" filter (MT-M2). Guarding out the whole block on a null tenant was
            // fail-OPEN — it left the nav/Include layer unfiltered when the root guard was bypassed.
            AppendLine("if (!context.SkipTenant)");
            Block(() =>
            {
                AppendLine("var tenantId = context.TenantId;");
                AppendLine($"var tenantFilters = new global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Linq.Expressions.LambdaExpression>({tenantEntities.Count})");
                AppendLine("{");
                IncreaseIndent();

                foreach (var entity in tenantEntities)
                {
                    var globalType = $"global::{entity.FullTypeName}";
                    AppendLine($"[typeof({globalType})] = (global::System.Linq.Expressions.Expression<global::System.Func<{globalType}, bool>>)(entity => tenantId != null && entity.TenantId == tenantId),");
                }

                DecreaseIndent();
                AppendLine("};");
                AppendLine("map = map.Merge(tenantFilters);");
            });
        }

        // Add temporal filters (active records only by default)
        if (temporalEntities.Count > 0)
        {
            AppendLine();
            AppendLine("var now = context.Now;");
            AppendLine($"var temporalFilters = new global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Linq.Expressions.LambdaExpression>({temporalEntities.Count})");

            AppendLine("{");
            IncreaseIndent();

            foreach (var entity in temporalEntities)
            {
                var globalType = $"global::{entity.FullTypeName}";
                AppendLine($"[typeof({globalType})] = (global::System.Linq.Expressions.Expression<global::System.Func<{globalType}, bool>>)(entity => entity.ValidFrom <= now && (entity.ValidTo == null || entity.ValidTo > now)),");
            }

            DecreaseIndent();
            AppendLine("};");
            AppendLine("map = map.Merge(temporalFilters);");
        }

        AppendLine();
        AppendLine("return map;");
    }

    private static string DeriveNamespacePrefix(ImmutableArray<EntityMetadataModel> entities)
    {
        var namespaces = entities
            .Where(e => e.IsSoftDelete || e.IsTenantEntity || e.IsTemporalRelation)
            .Select(e => e.Namespace)
            .Where(ns => !string.IsNullOrEmpty(ns) && ns != "<global namespace>")
            .ToList();

        if (namespaces.Count == 0)
            return string.Empty;
        return NamespacePrefixHelper.DerivePrefix(namespaces);
    }
}

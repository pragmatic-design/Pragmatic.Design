using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Validation;

/// <summary>
///     Reports PRAG0705: a required reference navigation pointing at a <c>[SoftDelete]</c> entity.
///     <para>
///     The hazard: <c>EntityConfigurationTemplate</c> emits <c>HasQueryFilter("SoftDelete", e =&gt; !e.IsDeleted)</c>
///     on the target and <c>.IsRequired()</c> on the relationship. EF Core translates a required navigation
///     into an INNER JOIN, so the target's filter silently removes the DEPENDENT rows too from every query
///     that joins the navigation. The rows are still in the table; they are simply invisible, and nothing
///     connects the two facts.
///     </para>
/// </summary>
/// <remarks>
///     Calibration — the rule stays silent on three shapes where reporting would be noise, not signal:
///     <list type="number">
///         <item><description>
///             <b>Cross-boundary navigations the source cannot read.</b> <c>RelationGraphBuilder</c>
///             emits no CLR navigation property for them — only the raw FK column. With no relationship
///             in the EF model there is no join and no filter propagation, so the hazard cannot occur.
///             A crossing the boundary declares <c>[ReadAccess&lt;T&gt;]</c> to is a real navigation,
///             mapped by convention, and is checked like any other.
///         </description></item>
///         <item><description>
///             <b>The target owns the dependent.</b> When the target declares the inverse collection
///             (<c>[Relation.OneToMany&lt;TDependent&gt;]</c>), the dependent is a child of the target's
///             aggregate; hiding the children along with the soft-deleted root is the declared intent, not a
///             bug. Only a bare reference — the dependent points at the target, the target knows nothing about
///             it — is reported.
///         </description></item>
///         <item><description>
///             <b>A soft-deletable dependent.</b> It has its own <c>IsDeleted</c> lifecycle (and is reachable
///             by <c>[SoftDelete(Cascade = true)]</c>), so its rows can be marked and restored. The reported
///             case is the one with no way out: a permanent row that no query can reach.
///         </description></item>
///     </list>
/// </remarks>
internal static class SoftDeleteNavigationValidator
{
    /// <summary>
    ///     Validates every entity declared in the current compilation and reports PRAG0705 for each
    ///     offending navigation.
    /// </summary>
    public static void Validate(
        SourceProductionContext context,
        ImmutableArray<EntityMetadataModel> entities)
    {
        if (entities.IsDefaultOrEmpty)
            return;

        var byFullName = new Dictionary<string, EntityMetadataModel>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            if (entity.IsValid)
                byFullName[Normalize(entity.FullTypeName)] = entity;
        }

        foreach (var entity in entities)
        {
            // Diagnostics belong to the project that owns the declaration: an entity read from a
            // referenced assembly has no syntax and is already reported in its own compilation.
            if (!entity.IsValid || entity.IsFromReference || entity.IsSoftDelete)
                continue;

            foreach (var nav in entity.Navigations)
            {
                if (!IsRequiredReference(nav))
                    continue;
                if (nav.IsUnreadableCrossing(entity))
                    continue;

                var targetKey = Normalize(nav.TargetFullTypeName ?? nav.TargetTypeName);
                if (!byFullName.TryGetValue(targetKey, out var target) || !target.IsSoftDelete)
                    continue;

                if (OwnsAsChild(target, entity))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPipelineDiagnostics.SoftDeleteRequiredNavigation,
                    entity.DeclarationLocation?.ToLocation() ?? Location.None,
                    entity.TypeName,
                    nav.Name,
                    target.TypeName));
            }
        }
    }

    /// <summary>
    ///     A navigation that becomes an INNER JOIN: a reference (not a collection), owned by the dependent
    ///     side (the principal end of a one-to-one holds no FK and is configured by the other side), not an
    ///     owned type (mapped flat into the parent table by <c>OwnsOne</c> — no join, no separate filter).
    /// </summary>
    private static bool IsRequiredReference(NavigationMetadataModel nav)
        => nav is { IsRequired: true, IsCollection: false, IsOwned: false, IsPrincipal: false }
           && nav.NavigationType is "ManyToOne" or "OneToOne";

    /// <summary>
    ///     Whether <paramref name="target"/> declares the inverse collection to <paramref name="dependent"/>,
    ///     i.e. the dependent is a child of the target's aggregate.
    /// </summary>
    private static bool OwnsAsChild(EntityMetadataModel target, EntityMetadataModel dependent)
    {
        var dependentKey = Normalize(dependent.FullTypeName);
        foreach (var nav in target.Navigations)
        {
            if (!nav.IsCollection)
                continue;
            if (Normalize(nav.TargetFullTypeName ?? nav.TargetTypeName) == dependentKey)
                return true;
        }

        return false;
    }

    private static string Normalize(string typeName)
        => typeName.StartsWith("global::", StringComparison.Ordinal) ? typeName.Substring(8) : typeName;
}

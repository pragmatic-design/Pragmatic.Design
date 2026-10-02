using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Parses [WithoutFilter&lt;TEntity&gt;] and [FilterMode] attributes from a symbol.
///     Used by MutationTransform, ActionTransform, and EndpointTransform.
/// </summary>
internal static class FilterOverrideParser
{
    /// <summary>The name <c>EntityConfigurationTemplate</c> installs the soft-delete filter under.</summary>
    private const string SoftDeleteFilterName = "SoftDelete";

    /// <summary>
    ///     Parses filter override attributes from the given symbol.
    ///     Returns null if no filter overrides are configured.
    /// </summary>
    public static FilterOverrideModel? Parse(INamedTypeSymbol symbol)
    {
        var disabledEntityTypes = ImmutableArray.CreateBuilder<string>();
        var liftedQueryFilterNames = ImmutableArray.CreateBuilder<string>();
        int? filterModeOverride = null;

        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            // [WithoutFilter<TEntity>] — generic attribute, TEntity is the entity type directly
            if (attrClass.Name == "WithoutFilterAttribute" &&
                attrClass.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Query.Filters" &&
                attrClass.TypeArguments.Length == 1)
            {
                var argument = attrClass.TypeArguments[0]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                // The same attribute names two different things, and they are lifted by different
                // machinery. An entity type disables that entity's filters inside the Pragmatic
                // provider; a visibility rule is installed on the EF model instead, where the only
                // way past it is IgnoreQueryFilters with the name it was registered under.
                if (IsVisibilityRule(attrClass.TypeArguments[0]))
                    liftedQueryFilterNames.Add(VisibilityFilterNaming.ForRule(argument));
                else
                {
                    disabledEntityTypes.Add(argument);

                    // ⚠️ A [SoftDelete] entity also carries the EF named filter "SoftDelete", and the
                    // provider cannot lift that: only its name can. Without it the attribute disabled
                    // every filter but the one that hides a soft-deleted row, and an operation declared
                    // to reach one found nothing. The tenant safety net is not lifted here.
                    if (IsSoftDeletable(attrClass.TypeArguments[0]) && !liftedQueryFilterNames.Contains(SoftDeleteFilterName))
                        liftedQueryFilterNames.Add(SoftDeleteFilterName);
                }
            }

            // [FilterMode(FilterMode.Admin)] — non-generic attribute
            if (attrClass.Name == "FilterModeAttribute" &&
                attrClass.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Query.Filters" &&
                attr.ConstructorArguments.Length == 1 &&
                attr.ConstructorArguments[0].Value is int modeValue)
            {
                filterModeOverride = modeValue;
            }
        }

        if (disabledEntityTypes.Count == 0
            && liftedQueryFilterNames.Count == 0
            && filterModeOverride is null)
        {
            return null;
        }

        return new FilterOverrideModel
        {
            DisabledEntityTypes = disabledEntityTypes.ToImmutable(),
            LiftedQueryFilterNames = liftedQueryFilterNames.ToImmutable(),
            FilterModeOverride = filterModeOverride
        };
    }

    /// <summary>
    ///     Whether the type derives from <c>VisibilityRule&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     Walks the base chain rather than testing the immediate base, so a rule that factors shared
    ///     logic into an intermediate abstract class is still recognised as one. Matched on name and
    ///     namespace, not on <c>ToDisplayString()</c>, because the base is generic and its rendering
    ///     carries the entity type with it.
    /// </remarks>
    private static bool IsSoftDeletable(ITypeSymbol type)
        => type.GetAttributes().Any(a =>
            a.AttributeClass is { Name: "SoftDeleteAttribute" } attribute
            && attribute.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Entity");

    private static bool IsVisibilityRule(ITypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "VisibilityRule"
                && current.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Query.Filters")
            {
                return true;
            }
        }

        return false;
    }
}

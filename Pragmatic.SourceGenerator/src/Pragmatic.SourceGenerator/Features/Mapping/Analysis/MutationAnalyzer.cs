using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     What a <c>Mutation&lt;TEntity&gt;</c> is, and what it writes.
/// </summary>
/// <remarks>
///     <para>
///         A mutation is a mapping under another classifier — <c>[Mutation]</c> where a DTO carries
///         <c>[MapTo]</c> — so three places in this feature need the same answer: the transform that
///         builds the model, the nested analyzer deciding whether a child is a child, and the write
///         analysis choosing how that child is constructed.
///     </para>
///     <para>
///         ⚠️ The base type is matched by name, not by symbol: <c>Pragmatic.Actions</c> is not
///         referenced by the mapping feature, and it must not become referenced for this. The name is
///         the whole contract, which is why it lives here once rather than being retyped at each site.
///     </para>
/// </remarks>
internal static class MutationAnalyzer
{
    private const string MutationBase = "Pragmatic.Actions.Mutation.Mutation";

    /// <summary>The entity a mutation writes, or null when the type is not one.</summary>
    public static INamedTypeSymbol? EntityOf(INamedTypeSymbol? symbol)
    {
        for (var baseType = symbol?.BaseType; baseType is not null; baseType = baseType.BaseType)
            if (baseType.OriginalDefinition.ToDisplayString().StartsWith(MutationBase, System.StringComparison.Ordinal)
                && baseType.TypeArguments.Length > 0)
                return baseType.TypeArguments[0] as INamedTypeSymbol;

        return null;
    }

    /// <summary>
    ///     Whether <paramref name="child"/> declared that <paramref name="parent"/> may write it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>[PartOf&lt;TParent&gt;]</c> is the child's own statement, and it is what keeps a
    ///         parent from becoming a way around the rules the child would have applied — its
    ///         validator, its <c>[RequirePermission]</c>. Actions refuses the pair with PRAG0436;
    ///         emitting the write anyway would leave the generated body disagreeing with the
    ///         diagnostic beside it.
    ///     </para>
    ///     <para>
    ///         ⚠️ Asked only of a child that is a <b>mutation</b>. A <c>[MapTo]</c> DTO writing nested
    ///         children has never required the declaration, and demanding it here would break every
    ///         one that exists.
    ///     </para>
    /// </remarks>
    public static bool IsPartOf(INamedTypeSymbol child, ISymbol parent)
    {
        foreach (var attribute in child.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "PartOfAttribute" } attributeClass)
                continue;

            if (attributeClass.TypeArguments.Length == 1
                && SymbolEqualityComparer.Default.Equals(attributeClass.TypeArguments[0], parent))
                return true;
        }

        return false;
    }

    /// <summary>Whether the type writes like a <c>[MapTo]</c>, on the write direction only.</summary>
    /// <remarks>
    ///     A mutation has no read shape — it is write-only — so on the read side it is not a nested
    ///     DTO and never was.
    /// </remarks>
    public static bool IsMutationOver(INamedTypeSymbol type, MappingDirection direction)
        => direction == MappingDirection.ToEntity && EntityOf(type) is not null;
}

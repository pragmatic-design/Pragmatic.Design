using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     Helper for determining if a property mapping is SQL-translatable.
/// </summary>
internal static class SqlTranslatableAnalyzer
{
    /// <summary>
    ///     Checks if a property mapping can be translated to SQL (for EF Core projections).
    /// </summary>
    public static bool IsSqlTranslatable(
        IPropertySymbol prop,
        string? converterType,
        MapPropertyInfo? mapPropertyAttr)
    {
        // Converters are not SQL-translatable
        if (!string.IsNullOrEmpty(converterType))
            return false;

        // Format strings are not SQL-translatable
        if (mapPropertyAttr?.Format is not null)
            return false;

        // Simple types are translatable
        if (TypeAnalyzer.IsSimpleType(prop.Type))
            return true;

        // Enums are translatable
        if (prop.Type.TypeKind == TypeKind.Enum)
            return true;

        // Nullable<T> value types (e.g., int?, Status?)
        if (prop.Type is INamedTypeSymbol { IsGenericType: true, OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: > 0 } namedType)
        {
            var underlyingType = namedType.TypeArguments[0];
            return TypeAnalyzer.IsSimpleType(underlyingType) || underlyingType.TypeKind == TypeKind.Enum;
        }

        // Collections of scalars (List<string>, string[], IReadOnlyList<int>, …) are projectable by
        // EF Core 8+ as primitive collections (JSON column) — include them in the projection so a
        // query-returned DTO carries the collection. Collections of DTOs/entities are NOT handled here:
        // they go through the element-DTO projection path (IsElementDto) instead.
        // Immutable collections are excluded: EF materializes List, which can't be assigned to them.
        var collection = CollectionAnalyzer.AnalyzeCollectionType(prop.Type);
        if (collection is { Kind: not (CollectionKind.None or CollectionKind.ImmutableArray or CollectionKind.ImmutableList), IsDictionary: false, ElementTypeSymbol: not null }
            && (collection.IsElementSimple || collection.ElementTypeSymbol.TypeKind == TypeKind.Enum))
            return true;

        // A [ValueObject] is mapped as an EF Core complex type, so EF projects it whole: the query
        // for `new Dto { Position = e.Position }` selects Position_Start and Position_Length. Left out
        // of this list it would be dropped from the projection without a word, and every projected read
        // would answer with the DTO's own initialiser while the columns hold the right values.
        if (IsProjectedWhole(prop.Type))
            return true;

        return false;
    }

    /// <summary>
    ///     Whether EF Core projects this type whole, so naming it in the projection is enough.
    /// </summary>
    /// <remarks>
    ///     <c>Money</c> is here beside <c>[ValueObject]</c> because the persistence generator maps it as a
    ///     complex type too — <c>RenderMoneyComplexProperty</c>, two columns — while carrying none of the
    ///     attribute. It is the same hole the attribute had, found the same way: a list of payments read
    ///     through a projection answered <c>0</c> for every amount, with the rows holding the right ones.
    /// </remarks>
    public static bool IsProjectedWhole(ITypeSymbol type) => IsValueObject(type) || IsMoney(type);

    /// <summary>The type the persistence generator configures as a complex type, nullable or not.</summary>
    private static bool IsMoney(ITypeSymbol type)
        => Unwrap(type).ToDisplayString() == "Pragmatic.Internationalization.Types.Money";

    /// <summary>Whether the type carries <c>[ValueObject]</c>, unwrapping <c>Nullable&lt;T&gt;</c>.</summary>
    public static bool IsValueObject(ITypeSymbol type)
        => Unwrap(type) is INamedTypeSymbol named
           && named.GetAttributes().Any(a =>
               a.AttributeClass?.ToDisplayString() == "Pragmatic.Persistence.Entity.ValueObjectAttribute");

    /// <summary><c>Nullable&lt;T&gt;</c> answers for its <c>T</c>: the column layout is the same either way.</summary>
    private static ITypeSymbol Unwrap(ITypeSymbol type)
        => type is INamedTypeSymbol
        {
            IsGenericType: true,
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            TypeArguments.Length: 1
        } nullable
            ? nullable.TypeArguments[0]
            : type;
}

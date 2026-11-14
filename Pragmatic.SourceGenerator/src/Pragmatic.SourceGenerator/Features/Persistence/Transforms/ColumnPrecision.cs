using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     A decimal column's declared precision and scale: <c>[Precision(p, s)]</c> on a <see langword="decimal" /> property (#731). Every branch
///     reads it here: EF Core's configuration and generated schema, the cross-assembly metadata, the Sql engine's schema.
/// </summary>
/// <remarks>
///     Matched by full name: EF Core's own <c>[Precision]</c> shares the simple name, and an entity declares one spelling for one fact. On any
///     type but a decimal (or a nullable one) there is no column precision to declare, so it is ignored.
/// </remarks>
internal static class ColumnPrecision
{
    public const string AttributeFullName = "Pragmatic.Persistence.Entity.PrecisionAttribute";

    /// <summary>The precision and scale <paramref name="property" /> declares, or null without them.</summary>
    public static (int Precision, int Scale)? Of(IPropertySymbol? property)
    {
        if (property is null || !IsDecimal(property.Type))
            return null;
        foreach (var attribute in property.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == AttributeFullName
                && attribute.ConstructorArguments.Length == 2
                && attribute.ConstructorArguments[0].Value is int precision
                && attribute.ConstructorArguments[1].Value is int scale)
                return (precision, scale);
        return null;
    }

    private static bool IsDecimal(ITypeSymbol type)
        => type.SpecialType == SpecialType.System_Decimal
           || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
           && nullable.TypeArguments[0].SpecialType == SpecialType.System_Decimal;
}

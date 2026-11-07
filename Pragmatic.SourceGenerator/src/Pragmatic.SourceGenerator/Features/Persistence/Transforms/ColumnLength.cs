using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     A column's declared length: Pragmatic.Validation's <c>[MaxLength(n)]</c> on a string or binary property, the rule and the column in one
///     attribute. Every branch reads it here: EF Core's configuration and generated schema, the cross-assembly metadata, the Sql engine's schema.
/// </summary>
/// <remarks>
///     Matched by full name. DataAnnotations' <c>[MaxLength]</c> and <c>[StringLength]</c> share the simple name and declare nothing: one spelling
///     for one fact. On a collection the attribute counts elements, which is no column length, so only a string or a <c>byte[]</c> has one.
/// </remarks>
internal static class ColumnLength
{
    public const string AttributeFullName = "Pragmatic.Validation.Attributes.MaxLengthAttribute";

    /// <summary>The length <paramref name="property" /> declares, or null without one.</summary>
    public static int? Of(IPropertySymbol? property)
    {
        if (property is null || !HoldsLength(property.Type))
            return null;
        foreach (var attribute in property.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == AttributeFullName
                && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int length)
                return length;
        return null;
    }

    private static bool HoldsLength(ITypeSymbol type)
        => type.SpecialType == SpecialType.System_String
           || type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte };
}

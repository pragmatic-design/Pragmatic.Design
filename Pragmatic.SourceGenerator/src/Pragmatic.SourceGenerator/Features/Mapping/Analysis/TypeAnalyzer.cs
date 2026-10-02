using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     Helper methods for type analysis.
/// </summary>
internal static class TypeAnalyzer
{
    /// <summary>
    ///     Checks if a type is a simple/primitive type that doesn't need mapping.
    /// </summary>
    public static bool IsSimpleType(ITypeSymbol type)
    {
        // Strip nullable annotation for reference types (e.g., string? -> string)
        var typeName = type.ToDisplayString().TrimEnd('?');

        if (KnownTypes.SimpleTypes.Contains(typeName))
            return true;

        // Enums are simple types (mapped directly, not recursively)
        return type.TypeKind == TypeKind.Enum;
    }

    /// <summary>
    ///     Checks if a type name represents a built-in C# type.
    /// </summary>
    public static bool IsBuiltInType(string? type)
    {
        if (string.IsNullOrEmpty(type))
            return false;

        var cleanType = type!.TrimEnd('?');
        return KnownTypes.BuiltInTypes.Contains(cleanType);
    }

    /// <summary>
    ///     Unwraps nullable types to get the underlying type.
    ///     For reference types with nullable annotation, returns the type without annotation.
    ///     For Nullable&lt;T&gt; value types, returns T.
    /// </summary>
    public static ITypeSymbol? UnwrapNullable(ITypeSymbol? type)
    {
        if (type is null)
            return null;

        // Handle Nullable<T> value types (e.g., int?)
        if (type is INamedTypeSymbol { IsGenericType: true, OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } namedType)
            return namedType.TypeArguments[0];

        // Handle nullable reference types - NullableAnnotation doesn't change the underlying type
        // The type symbol itself is the unwrapped type; the annotation is metadata
        return type;
    }
}

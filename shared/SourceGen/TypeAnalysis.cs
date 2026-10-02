using Microsoft.CodeAnalysis;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     Shared type analysis utilities for source generators.
///     Used by multiple SGs (Endpoints, Validation) to ensure consistent type classification.
/// </summary>
internal static class TypeAnalysis
{
    /// <summary>
    ///     Determines whether a type is scalar (primitive, string, Guid, DateTime, enum, decimal).
    ///     Scalar types cannot be passed directly as [FromBody] in ASP.NET Core — they need a DTO wrapper.
    /// </summary>
    public static bool IsScalarType(ITypeSymbol type)
    {
        // Unwrap nullable
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        // Primitives + string
        if (type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_String)
            return true;

        // Enum
        if (type.TypeKind == TypeKind.Enum)
            return true;

        // Well-known scalar value types
        var name = type.ToDisplayString();
        return name is "System.Guid" or "System.DateTime" or "System.DateTimeOffset"
            or "System.TimeSpan" or "System.DateOnly" or "System.TimeOnly"
            or "System.Decimal";
    }
}

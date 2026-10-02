using Pragmatic.SourceGenerator.Features.Mapping.Analysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Templates;

/// <summary>
///     Default value generation for nullable-to-non-nullable property mappings.
/// </summary>
internal sealed partial class MappingTemplate
{
    private static bool IsStringDefault(PropertyMappingModel prop)
    {
        var propType = prop.PropertyType.TrimEnd('?');
        return propType == "string" || propType == "System.String";
    }

    /// <summary>
    ///     Gets the auto-default expression pattern for nullable to non-nullable conversion.
    ///     Returns null if no auto-default is available (requires explicit [MapProperty(Default=)]).
    ///     When isProjection is true, uses null-coalescing (??) for EF Core Expression Tree compatibility.
    ///     When isProjection is false, uses GetValueOrDefault() for in-memory runtime mapping.
    /// </summary>
    private static string? GetAutoDefault(string? sourceType, string targetType, bool isEnum, bool isProjection)
    {
        if (string.IsNullOrEmpty(sourceType))
            return null;

        var underlyingType = sourceType!.TrimEnd('?');

        // Enums
        if (isEnum)
            return isProjection
                ? $"{{expr}} ?? default({targetType})"
                : "({expr}).GetValueOrDefault()";

        // String: use ?? "" (same for both modes)
        if (KnownTypes.StringTypes.Contains(underlyingType))
            return "{expr} ?? \"\"";

        // Numeric types
        if (KnownTypes.NumericTypes.Contains(underlyingType))
        {
            if (!isProjection)
                return "({expr}).GetValueOrDefault()";

            // Projection mode needs type-specific literal suffixes
            return underlyingType switch
            {
                "sbyte" or "System.SByte" => "{expr} ?? (sbyte)0",
                "float" or "System.Single" => "{expr} ?? 0f",
                "double" or "System.Double" => "{expr} ?? 0d",
                "decimal" or "System.Decimal" => "{expr} ?? 0m",
                _ => "{expr} ?? 0"
            };
        }

        // Boolean
        if (KnownTypes.BoolTypes.Contains(underlyingType))
            return isProjection ? "{expr} ?? false" : "({expr}).GetValueOrDefault()";

        // Char
        if (KnownTypes.CharTypes.Contains(underlyingType))
            return isProjection ? "{expr} ?? default(char)" : "({expr}).GetValueOrDefault()";

        // Date/Time types
        if (KnownTypes.DateTimeTypes.Contains(underlyingType))
            return isProjection ? "{expr} ?? default(DateTime)" : "({expr}).GetValueOrDefault()";

        if (underlyingType is "DateTimeOffset" or "System.DateTimeOffset")
            return isProjection ? "{expr} ?? default(DateTimeOffset)" : "({expr}).GetValueOrDefault()";

        if (underlyingType is "TimeSpan" or "System.TimeSpan")
            return isProjection ? "{expr} ?? default(TimeSpan)" : "({expr}).GetValueOrDefault()";

        if (KnownTypes.DateOnlyTypes.Contains(underlyingType))
            return isProjection ? "{expr} ?? default(DateOnly)" : "({expr}).GetValueOrDefault()";

        if (KnownTypes.TimeOnlyTypes.Contains(underlyingType))
            return isProjection ? "{expr} ?? default(TimeOnly)" : "({expr}).GetValueOrDefault()";

        // Guid
        if (KnownTypes.GuidTypes.Contains(underlyingType))
            return isProjection ? "{expr} ?? Guid.Empty" : "({expr}).GetValueOrDefault()";

        // No auto-default available — will fall through to simple mapping
        return null;
    }
}

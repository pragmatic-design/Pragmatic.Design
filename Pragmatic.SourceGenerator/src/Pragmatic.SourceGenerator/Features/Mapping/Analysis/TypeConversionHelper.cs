using System;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     Helper for detecting and generating type conversions.
/// </summary>
/// <remarks>
///     Split across this file and <c>TypeConversionHelper.Validation.cs</c>, which answers the other
///     half of every conversion written here: whether the value can be converted at all. The two
///     belong together because they must stay in step — a conversion added here without its check
///     turns a bad request into a 500.
/// </remarks>
internal static partial class TypeConversionHelper
{
    /// <summary>
    ///     Detects the required conversion between source and target types.
    /// </summary>
    public static ConversionKind DetectConversion(
        string? sourceType,
        string targetType,
        bool sourceIsEnum,
        bool targetIsEnum)
    {
        if (string.IsNullOrEmpty(sourceType))
            return ConversionKind.None;

        // Strip nullable annotation for comparison
        var cleanSource = StripNullable(sourceType!);
        var cleanTarget = StripNullable(targetType);

        // Same type or C# handles implicit conversion
        if (cleanSource == cleanTarget)
            return ConversionKind.None;

        // String → X conversions
        if (IsStringType(cleanSource))
        {
            if (IsNumericType(cleanTarget))
                return ConversionKind.StringToNumeric;

            if (IsDateTimeType(cleanTarget))
                return ConversionKind.StringToDateTime;

            if (IsDateTimeOffsetType(cleanTarget))
                return ConversionKind.StringToDateTimeOffset;

            if (IsDateOnlyType(cleanTarget))
                return ConversionKind.StringToDateOnly;

            if (IsTimeOnlyType(cleanTarget))
                return ConversionKind.StringToTimeOnly;

            if (IsGuidType(cleanTarget))
                return ConversionKind.StringToGuid;

            if (IsBoolType(cleanTarget))
                return ConversionKind.StringToBool;

            if (targetIsEnum)
                return ConversionKind.StringToEnum;
        }

        // X → String conversions
        if (IsStringType(cleanTarget))
        {
            if (IsNumericType(cleanSource))
                return ConversionKind.NumericToString;

            if (IsDateTimeType(cleanSource) || IsDateOnlyType(cleanSource) || IsTimeOnlyType(cleanSource)
                || IsDateTimeOffsetType(cleanSource))
                return ConversionKind.DateTimeToString;

            if (IsGuidType(cleanSource))
                return ConversionKind.GuidToString;

            if (IsBoolType(cleanSource))
                return ConversionKind.BoolToString;

            if (sourceIsEnum)
                return ConversionKind.EnumToString;
        }

        // Enum ↔ number: a cast, with no converter class.
        if (sourceIsEnum && IsNumericType(cleanTarget))
            return ConversionKind.EnumToNumeric;

        if (targetIsEnum && IsNumericType(cleanSource))
            return ConversionKind.NumericToEnum;

        // DateTime → DateOnly/TimeOnly
        if (IsDateTimeType(cleanSource))
        {
            if (IsDateOnlyType(cleanTarget))
                return ConversionKind.DateTimeToDateOnly;

            if (IsTimeOnlyType(cleanTarget))
                return ConversionKind.DateTimeToTimeOnly;
        }

        // DateOnly → DateTime
        if (IsDateOnlyType(cleanSource) && IsDateTimeType(cleanTarget))
            return ConversionKind.DateOnlyToDateTime;

        // Numeric widening is handled by C# compiler
        if (IsNumericType(cleanSource) && IsNumericType(cleanTarget))
            return ConversionKind.None;

        return ConversionKind.None;
    }

    /// <summary>
    ///     Generates the conversion expression for the given conversion kind.
    /// </summary>
    public static string GenerateConversionExpression(
        string sourceExpression,
        string targetType,
        ConversionKind conversion,
        bool sourceIsNullable,
        string? format = null,
        string onUnknown = "Throw",
        System.Collections.Generic.IReadOnlyList<string>? enumAliases = null)
    {
        var cleanTarget = StripNullable(targetType);

        return conversion switch
        {
            ConversionKind.StringToNumeric => GenerateStringToNumeric(sourceExpression, cleanTarget, sourceIsNullable),
            ConversionKind.StringToDateTime => GenerateStringToDateTime(sourceExpression, sourceIsNullable),
            ConversionKind.StringToDateOnly => GenerateStringToDateOnly(sourceExpression, sourceIsNullable),
            ConversionKind.StringToTimeOnly => GenerateStringToTimeOnly(sourceExpression, sourceIsNullable),
            ConversionKind.StringToGuid => GenerateStringToGuid(sourceExpression, sourceIsNullable),
            ConversionKind.StringToBool => GenerateStringToBool(sourceExpression, sourceIsNullable),
            ConversionKind.StringToEnum => GenerateStringToEnum(
                sourceExpression, cleanTarget, sourceIsNullable, onUnknown, enumAliases),
            ConversionKind.EnumToNumeric => GenerateEnumCast(sourceExpression, cleanTarget, sourceIsNullable),
            ConversionKind.NumericToEnum => GenerateEnumCast(sourceExpression, cleanTarget, sourceIsNullable),
            ConversionKind.EnumToEnumByValue => GenerateEnumCast(sourceExpression, cleanTarget, sourceIsNullable),
            ConversionKind.StringToDateTimeOffset => GenerateStringToDateTimeOffset(sourceExpression, sourceIsNullable),

            ConversionKind.NumericToString => GenerateNumericToString(sourceExpression, sourceIsNullable, format),
            ConversionKind.DateTimeToString => GenerateDateTimeToString(sourceExpression, sourceIsNullable, format),
            ConversionKind.GuidToString => GenerateToString(sourceExpression, sourceIsNullable, format),
            ConversionKind.BoolToString => GenerateToString(sourceExpression, sourceIsNullable, format),
            ConversionKind.EnumToString => GenerateEnumToString(
                sourceExpression, sourceIsNullable, format, enumAliases),

            // ⚠️ These three honour the nullable flag like every other row of the table. Passing the
            // expression through unguarded, a `DateTime?` source would produce
            // `DateOnly.FromDateTime(DateTime?)` — CS1503 inside a generated file. No date conversion
            // has a reason to behave differently from a numeric one.
            ConversionKind.DateTimeToDateOnly => Guarded(
                sourceExpression, sourceIsNullable, v => $"global::System.DateOnly.FromDateTime({v})"),
            ConversionKind.DateTimeToTimeOnly => Guarded(
                sourceExpression, sourceIsNullable, v => $"global::System.TimeOnly.FromDateTime({v})"),
            ConversionKind.DateOnlyToDateTime => Guarded(
                sourceExpression, sourceIsNullable, v => $"{v}.ToDateTime(global::System.TimeOnly.MinValue)"),

            _ => sourceExpression
        };
    }

    /// <summary>
    ///     Applies <paramref name="convert" /> to the value, guarding a nullable source.
    /// </summary>
    /// <remarks>
    ///     The same shape every other conversion in this file writes by hand: a nullable source is
    ///     unwrapped through <c>is { }</c> and falls back to <c>default</c>, a non-nullable one is
    ///     converted directly. Here as a helper because three conversions needed it at once.
    /// </remarks>
    private static string Guarded(string sourceExpr, bool sourceIsNullable, System.Func<string, string> convert)
        => sourceIsNullable
            ? $"{sourceExpr} is {{ }} __v ? {convert("__v")} : default"
            : convert(sourceExpr);

    private static string GenerateStringToNumeric(string sourceExpr, string targetType, bool sourceIsNullable)
    {
        var parseMethod = GetNumericParseMethod(targetType);
        if (sourceIsNullable)
            return
                $"{sourceExpr} is {{ }} __str ? {parseMethod}(__str, global::System.Globalization.CultureInfo.InvariantCulture) : default";
        return $"{parseMethod}({sourceExpr}, global::System.Globalization.CultureInfo.InvariantCulture)";
    }

    private static string GetNumericParseMethod(string targetType)
    {
        return targetType switch
        {
            "int" or "System.Int32" => "int.Parse",
            "long" or "System.Int64" => "long.Parse",
            "short" or "System.Int16" => "short.Parse",
            "byte" or "System.Byte" => "byte.Parse",
            "sbyte" or "System.SByte" => "sbyte.Parse",
            "uint" or "System.UInt32" => "uint.Parse",
            "ulong" or "System.UInt64" => "ulong.Parse",
            "ushort" or "System.UInt16" => "ushort.Parse",
            "float" or "System.Single" => "float.Parse",
            "double" or "System.Double" => "double.Parse",
            "decimal" or "System.Decimal" => "decimal.Parse",
            _ => "int.Parse" // Fallback
        };
    }

    private static string GenerateStringToDateTime(string sourceExpr, bool sourceIsNullable)
    {
        if (sourceIsNullable)
            return
                $"{sourceExpr} is {{ }} __str ? global::System.DateTime.Parse(__str, global::System.Globalization.CultureInfo.InvariantCulture) : default";
        return
            $"global::System.DateTime.Parse({sourceExpr}, global::System.Globalization.CultureInfo.InvariantCulture)";
    }

    private static string GenerateStringToDateOnly(string sourceExpr, bool sourceIsNullable)
    {
        if (sourceIsNullable)
            return
                $"{sourceExpr} is {{ }} __str ? global::System.DateOnly.Parse(__str, global::System.Globalization.CultureInfo.InvariantCulture) : default";
        return
            $"global::System.DateOnly.Parse({sourceExpr}, global::System.Globalization.CultureInfo.InvariantCulture)";
    }

    private static string GenerateStringToTimeOnly(string sourceExpr, bool sourceIsNullable)
    {
        if (sourceIsNullable)
            return
                $"{sourceExpr} is {{ }} __str ? global::System.TimeOnly.Parse(__str, global::System.Globalization.CultureInfo.InvariantCulture) : default";
        return
            $"global::System.TimeOnly.Parse({sourceExpr}, global::System.Globalization.CultureInfo.InvariantCulture)";
    }

    private static string GenerateStringToDateTimeOffset(string sourceExpr, bool sourceIsNullable)
    {
        if (sourceIsNullable)
            return
                $"{sourceExpr} is {{ }} __str ? global::System.DateTimeOffset.Parse(__str, global::System.Globalization.CultureInfo.InvariantCulture) : default";
        return
            $"global::System.DateTimeOffset.Parse({sourceExpr}, global::System.Globalization.CultureInfo.InvariantCulture)";
    }

    /// <summary>
    ///     string → enum. Null-guarded to match the other string conversions: a null source maps to the
    ///     enum default rather than throwing <see cref="System.ArgumentNullException"/> from Enum.Parse.
    /// </summary>
    /// <summary>
    ///     A cast between an enum and its number, or between two enums paired by value.
    /// </summary>
    /// <remarks>
    ///     The nullable form casts the value and keeps the null, rather than casting the nullable
    ///     itself: <c>(TargetEnum?)source</c> compiles for some pairs and not others, and the
    ///     conditional says the same thing for all of them.
    /// </remarks>
    private static string GenerateEnumCast(string sourceExpr, string targetType, bool sourceIsNullable)
    {
        if (sourceIsNullable)
            return $"{sourceExpr} is {{ }} __value ? ({targetType})__value : default";

        return $"({targetType}){sourceExpr}";
    }

    /// <summary>
    ///     The wire name of an enum value, honouring <c>[MapEnum(Alias = …)]</c> on the members.
    /// </summary>
    /// <remarks>
    ///     A switch only over the members that declare one; everything else falls through to
    ///     <c>ToString()</c>, which is also what a <c>[Flags]</c> combination needs — an alias table
    ///     has no entry for <c>"Read, Write"</c>, and inventing one would put a second parser beside
    ///     the runtime's own.
    /// </remarks>
    private static string GenerateEnumToString(
        string sourceExpr, bool sourceIsNullable,
        string? format, System.Collections.Generic.IReadOnlyList<string>? aliases)
    {
        if (aliases is null || aliases.Count == 0)
            return GenerateToString(sourceExpr, sourceIsNullable, format);

        var access = sourceIsNullable ? $"{sourceExpr}?" : sourceExpr;
        var arms = string.Join(", ", aliases.Select(pair =>
        {
            var split = pair.IndexOf('=');
            var member = pair.Substring(0, split);
            var alias = pair.Substring(split + 1);
            return $"{{ }} __m when __m.ToString() == \"{member}\" => \"{EscapeLiteral(alias)}\"";
        }));

        return $"{access} switch {{ {arms}, _ => {access}.ToString() }}";
    }

    private static string GenerateStringToEnum(
        string sourceExpr, string targetType, bool sourceIsNullable, string onUnknown,
        System.Collections.Generic.IReadOnlyList<string>? aliases = null)
    {
        // The wire names first, then the ordinary parse for everything else. Written as a switch so
        // an alias that is not a C# identifier — "in-progress", "2xx" — needs no escaping anywhere.
        if (aliases is { Count: > 0 })
        {
            var arms = string.Join(", ", aliases.Select(pair =>
            {
                var split = pair.IndexOf('=');
                var member = pair.Substring(0, split);
                var alias = pair.Substring(split + 1);
                return $"\"{EscapeLiteral(alias)}\" => {targetType}.{member}";
            }));

            var rest = GenerateStringToEnumCore(sourceExpr, targetType, sourceIsNullable, onUnknown);
            return $"{sourceExpr} switch {{ {arms}, _ => {rest} }}";
        }

        return GenerateStringToEnumCore(sourceExpr, targetType, sourceIsNullable, onUnknown);
    }

    private static string GenerateStringToEnumCore(
        string sourceExpr, string targetType, bool sourceIsNullable, string onUnknown)
    {
        // Throw is what every mapping did before [MapEnum] existed, and it stays the default: turning
        // a loud failure into a quiet one on every existing shape is not a fix.
        if (onUnknown == "Throw")
        {
            if (sourceIsNullable)
                return
                    $"{sourceExpr} is {{ }} __str ? global::System.Enum.Parse<{targetType}>(__str, ignoreCase: true) : default";
            return $"global::System.Enum.Parse<{targetType}>({sourceExpr}, ignoreCase: true)";
        }

        // TryParse, and the out variable is named per-expression: two enum properties on one DTO put
        // two of these in the same object initializer, and one name would not compile.
        var slot = "__enum" + Math.Abs(sourceExpr.GetHashCode()).ToString(CultureInfo.InvariantCulture);

        // Null and Default differ only on a nullable target; on a non-nullable one there is nowhere
        // to put a null, so Null behaves as Default rather than refusing a shape that compiled.
        var fallback = "default";

        return $"global::System.Enum.TryParse<{targetType}>({sourceExpr}, ignoreCase: true, out var {slot}) "
               + $"? {slot} : {fallback}";
    }

    private static string GenerateStringToGuid(string sourceExpr, bool sourceIsNullable)
    {
        if (sourceIsNullable)
            return $"{sourceExpr} is {{ }} __str ? global::System.Guid.Parse(__str) : default";
        return $"global::System.Guid.Parse({sourceExpr})";
    }

    private static string GenerateStringToBool(string sourceExpr, bool sourceIsNullable)
    {
        if (sourceIsNullable)
            return $"{sourceExpr} is {{ }} __str ? bool.Parse(__str) : default";
        return $"bool.Parse({sourceExpr})";
    }

    private static string GenerateDateTimeToString(string sourceExpr, bool sourceIsNullable, string? format)
    {
        var nullCheck = sourceIsNullable ? "?" : "";
        if (!string.IsNullOrEmpty(format))
            return
                $"{sourceExpr}{nullCheck}.ToString(\"{EscapeLiteral(format!)}\", global::System.Globalization.CultureInfo.InvariantCulture)";
        return $"{sourceExpr}{nullCheck}.ToString(\"o\", global::System.Globalization.CultureInfo.InvariantCulture)";
    }

    private static string GenerateNumericToString(string sourceExpr, bool sourceIsNullable, string? format)
    {
        var nullCheck = sourceIsNullable ? "?" : "";
        if (!string.IsNullOrEmpty(format))
            return
                $"{sourceExpr}{nullCheck}.ToString(\"{EscapeLiteral(format!)}\", global::System.Globalization.CultureInfo.InvariantCulture)";
        return $"{sourceExpr}{nullCheck}.ToString(global::System.Globalization.CultureInfo.InvariantCulture)";
    }

    /// <summary>
    ///     Generic ToString with optional format (for Guid, Bool, Enum).
    /// </summary>
    private static string GenerateToString(string sourceExpr, bool sourceIsNullable, string? format)
    {
        var nullCheck = sourceIsNullable ? "?" : "";
        if (!string.IsNullOrEmpty(format))
            return $"{sourceExpr}{nullCheck}.ToString(\"{EscapeLiteral(format!)}\")";
        return $"{sourceExpr}{nullCheck}.ToString()";
    }

    /// <summary>
    ///     Escapes a user-supplied format string for safe embedding in a generated C# string literal —
    ///     a format containing <c>\</c> or <c>"</c> (both legal in .NET custom format strings) would
    ///     otherwise break the generated code.
    /// </summary>
    internal static string EscapeLiteral(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string StripNullable(string type) => type.TrimEnd('?');

    private static bool IsStringType(string type) => KnownTypes.StringTypes.Contains(type);

    private static bool IsNumericType(string type) => KnownTypes.NumericTypes.Contains(type);

    private static bool IsDateTimeType(string type) => KnownTypes.DateTimeTypes.Contains(type);

    private static bool IsDateTimeOffsetType(string type) => type is "DateTimeOffset" or "System.DateTimeOffset";

    private static bool IsDateOnlyType(string type) => KnownTypes.DateOnlyTypes.Contains(type);

    private static bool IsTimeOnlyType(string type) => KnownTypes.TimeOnlyTypes.Contains(type);

    private static bool IsGuidType(string type) => KnownTypes.GuidTypes.Contains(type);

    private static bool IsBoolType(string type) => KnownTypes.BoolTypes.Contains(type);

    /// <summary>
    ///     Checks if a type symbol represents an enum.
    /// </summary>
    public static bool IsEnumType(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        // Handle nullable
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        return type.TypeKind == TypeKind.Enum;
    }

    /// <summary>
    ///     PRAG0304, conservative: both sides are KNOWN simple types (so inheritance and user-defined
    ///     conversions cannot false-positive), they differ, no conversion path exists, and the pair is
    ///     not compiler-convertible. Only IMPLICIT numeric widening (and char widening) is treated as
    ///     compiler-convertible — narrowing pairs (e.g. long→int, double→float) are incompatible so they
    ///     surface as a clear PRAG0304 instead of a cryptic CS0266 in the generated code. Shared by the
    ///     diagnostic pass (report) and the expression templates (emit <c>default!</c> instead of an
    ///     assignment that would double-error).
    /// </summary>
    public static bool IsKnownIncompatible(PropertyMappingModel prop)
    {
        if (prop.IsIgnored || prop.Resolution == MappingResolution.None
            || prop.HasConverter || prop.Conversion != ConversionKind.None
            || prop.IsNestedDto || prop.CollectionKind != CollectionKind.None || prop.IsDictionary
            || prop.SourcePropertyType is null
            // A Format string turns the expression into .ToString("...") → string, whatever the source.
            || prop.Format is not null
            // Concatenation always produces a string expression.
            || prop.Resolution == MappingResolution.Concatenation)
            return false;

        var src = CanonicalSimpleName(prop.SourcePropertyType.TrimEnd('?'));
        var tgt = CanonicalSimpleName(prop.PropertyType.TrimEnd('?'));

        if (src == tgt
            || !KnownTypes.SimpleTypes.Contains(src)
            || !KnownTypes.SimpleTypes.Contains(tgt))
            return false;

        // Compiler-provided implicit conversions between simple types.
        if (IsImplicitNumericWidening(src, tgt))
            return false;
        if (src == "System.DateTime" && tgt == "System.DateTimeOffset")
            return false;

        return true;
    }

    /// <summary>
    ///     The C# implicit numeric conversion table (canonical System.* names). Narrowing and
    ///     signed/unsigned lossy pairs are intentionally excluded — they are NOT implicit in C#, so a
    ///     direct assignment would not compile. char is included (char widens to ushort and larger).
    /// </summary>
    private static bool IsImplicitNumericWidening(string src, string tgt) => src switch
    {
        "System.SByte" => tgt is "System.Int16" or "System.Int32" or "System.Int64"
            or "System.Single" or "System.Double" or "System.Decimal",
        "System.Byte" => tgt is "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32"
            or "System.Int64" or "System.UInt64" or "System.Single" or "System.Double" or "System.Decimal",
        "System.Int16" => tgt is "System.Int32" or "System.Int64"
            or "System.Single" or "System.Double" or "System.Decimal",
        "System.UInt16" => tgt is "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64"
            or "System.Single" or "System.Double" or "System.Decimal",
        "System.Int32" => tgt is "System.Int64" or "System.Single" or "System.Double" or "System.Decimal",
        "System.UInt32" => tgt is "System.Int64" or "System.UInt64"
            or "System.Single" or "System.Double" or "System.Decimal",
        "System.Int64" => tgt is "System.Single" or "System.Double" or "System.Decimal",
        "System.UInt64" => tgt is "System.Single" or "System.Double" or "System.Decimal",
        "System.Char" => tgt is "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64"
            or "System.UInt64" or "System.Single" or "System.Double" or "System.Decimal",
        "System.Single" => tgt is "System.Double",
        _ => false
    };

    /// <summary>Normalizes keyword aliases to their System.* names so "int" and "System.Int32" compare equal.</summary>
    private static string CanonicalSimpleName(string name) => name switch
    {
        "string" => "System.String",
        "int" => "System.Int32",
        "long" => "System.Int64",
        "short" => "System.Int16",
        "byte" => "System.Byte",
        "sbyte" => "System.SByte",
        "uint" => "System.UInt32",
        "ulong" => "System.UInt64",
        "ushort" => "System.UInt16",
        "float" => "System.Single",
        "double" => "System.Double",
        "decimal" => "System.Decimal",
        "bool" => "System.Boolean",
        "char" => "System.Char",
        "DateTime" => "System.DateTime",
        "DateTimeOffset" => "System.DateTimeOffset",
        "Guid" => "System.Guid",
        "TimeSpan" => "System.TimeSpan",
        "DateOnly" => "System.DateOnly",
        "TimeOnly" => "System.TimeOnly",
        _ => name,
    };
}

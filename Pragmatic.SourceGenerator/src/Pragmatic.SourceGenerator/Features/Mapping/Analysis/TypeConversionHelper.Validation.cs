using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     Whether a conversion can fail on the value, and the test that says so.
/// </summary>
/// <remarks>
///     <para>
///         Every <c>string</c> → X conversion this class writes goes through a <c>Parse</c>, and a
///         <c>Parse</c> throws on a value it cannot read. On a write path that exception is not
///         caught anywhere: it leaves the invoker, reaches ASP.NET, and the caller is told 500 —
///         the server taking blame for a body the caller sent.
///     </para>
///     <para>
///         So the generator, which already knows the conversion is there, also emits the check that
///         precedes it. This is the same rule as everywhere else: what is decided at compile time is
///         not looked for at runtime. The check runs in the mutation's own validation, before any
///         mapping, so the failure arrives as a validation error naming the property.
///     </para>
/// </remarks>
internal static partial class TypeConversionHelper
{
    /// <summary>
    ///     True when the conversion parses text, and therefore can fail on the value.
    /// </summary>
    /// <remarks>
    ///     Only the <c>StringTo*</c> family. The numeric and enum conversions in the other direction
    ///     are total — every <c>int</c> has a string form — and <c>NumericToEnum</c> yields an
    ///     undefined enum member rather than throwing, which is a different problem with a different
    ///     answer and does not belong in this one.
    /// </remarks>
    public static bool CanFailOnValue(ConversionKind kind)
        => kind is ConversionKind.StringToNumeric
            or ConversionKind.StringToDateTime
            or ConversionKind.StringToDateTimeOffset
            or ConversionKind.StringToDateOnly
            or ConversionKind.StringToTimeOnly
            or ConversionKind.StringToEnum
            or ConversionKind.StringToGuid
            or ConversionKind.StringToBool;

    /// <summary>
    ///     The boolean expression that is true when <paramref name="valueExpr" /> converts.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Mirrors the <c>Parse</c> call the conversion emits, argument for argument — the culture
    ///     above all. A check that accepted <c>"3,14"</c> under the current culture while the
    ///     conversion parsed it as invariant would pass validation and then throw, which is worse
    ///     than no check: the 500 would be back, minus the reason to look here.
    /// </remarks>
    /// <param name="kind">The conversion, already known to be one that can fail.</param>
    /// <param name="targetType">The fully qualified target type, for the enum and numeric cases.</param>
    /// <param name="valueExpr">The expression holding the string to test.</param>
    public static string GenerateCanConvertCheck(ConversionKind kind, string targetType, string valueExpr)
    {
        var clean = StripNullable(targetType);

        return kind switch
        {
            // The two-argument overloads, because that is what the Parse calls take. Passing a
            // NumberStyles or a DateTimeStyles here would widen the check past the conversion, and a
            // value that validates and then throws is worse than one that was never checked.
            ConversionKind.StringToNumeric =>
                $"{TryParseMethod(clean)}({valueExpr}, "
                + "global::System.Globalization.CultureInfo.InvariantCulture, out _)",
            ConversionKind.StringToDateTime =>
                $"global::System.DateTime.TryParse({valueExpr}, "
                + "global::System.Globalization.CultureInfo.InvariantCulture, out _)",
            ConversionKind.StringToDateTimeOffset =>
                $"global::System.DateTimeOffset.TryParse({valueExpr}, "
                + "global::System.Globalization.CultureInfo.InvariantCulture, out _)",
            ConversionKind.StringToDateOnly =>
                $"global::System.DateOnly.TryParse({valueExpr}, "
                + "global::System.Globalization.CultureInfo.InvariantCulture, out _)",
            ConversionKind.StringToTimeOnly =>
                $"global::System.TimeOnly.TryParse({valueExpr}, "
                + "global::System.Globalization.CultureInfo.InvariantCulture, out _)",
            ConversionKind.StringToEnum =>
                $"global::System.Enum.TryParse<{clean}>({valueExpr}, ignoreCase: true, out _)",
            ConversionKind.StringToGuid => $"global::System.Guid.TryParse({valueExpr}, out _)",
            ConversionKind.StringToBool => $"bool.TryParse({valueExpr}, out _)",
            _ => "true",
        };
    }

    /// <summary>
    ///     What the caller is told the value should have been.
    /// </summary>
    /// <remarks>
    ///     Names the shape, not the CLR type: <c>System.Int32</c> means nothing to whoever is holding
    ///     the JSON that was refused.
    /// </remarks>
    public static string DescribeExpectedShape(ConversionKind kind, string targetType) => kind switch
    {
        ConversionKind.StringToNumeric => IsIntegralType(StripNullable(targetType))
            ? "a whole number"
            : "a number",
        ConversionKind.StringToDateTime => "a date and time in ISO 8601 format",
        ConversionKind.StringToDateTimeOffset => "a date and time with an offset, in ISO 8601 format",
        ConversionKind.StringToDateOnly => "a date in ISO 8601 format",
        ConversionKind.StringToTimeOnly => "a time in ISO 8601 format",
        ConversionKind.StringToEnum => $"one of the values of {ShortName(StripNullable(targetType))}",
        ConversionKind.StringToGuid => "a GUID",
        ConversionKind.StringToBool => "true or false",
        _ => "a convertible value",
    };

    private static string ShortName(string fullyQualified)
    {
        var withoutPrefix = fullyQualified.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualified.Substring("global::".Length)
            : fullyQualified;
        var lastDot = withoutPrefix.LastIndexOf('.');

        return lastDot < 0 ? withoutPrefix : withoutPrefix.Substring(lastDot + 1);
    }

    private static bool IsIntegralType(string targetType) => CanonicalSimpleName(targetType) switch
    {
        "System.Int32" or "System.Int64" or "System.Int16" or "System.Byte" or "System.SByte"
            or "System.UInt32" or "System.UInt64" or "System.UInt16" => true,
        _ => false,
    };

    /// <summary>The <c>TryParse</c> matching the <c>Parse</c> that <c>GetNumericParseMethod</c> picks.</summary>
    private static string TryParseMethod(string targetType) => CanonicalSimpleName(targetType) switch
    {
        "System.Int32" => "int.TryParse",
        "System.Int64" => "long.TryParse",
        "System.Int16" => "short.TryParse",
        "System.Byte" => "byte.TryParse",
        "System.SByte" => "sbyte.TryParse",
        "System.UInt32" => "uint.TryParse",
        "System.UInt64" => "ulong.TryParse",
        "System.UInt16" => "ushort.TryParse",
        "System.Single" => "float.TryParse",
        "System.Double" => "double.TryParse",
        "System.Decimal" => "decimal.TryParse",
        _ => "int.TryParse",
    };
}

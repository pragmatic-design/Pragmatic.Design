namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     Canonical sets of well-known type names used across the mapping feature.
///     Single source of truth — avoids duplicated type lists in TypeAnalyzer, TypeConversionHelper, and expression helpers.
/// </summary>
internal static class KnownTypes
{
    /// <summary>
    ///     Primitive and built-in types that don't need recursive mapping.
    ///     Includes string, numerics, bool, char, date/time, and Guid.
    /// </summary>
    internal static readonly HashSet<string> SimpleTypes = new(StringComparer.Ordinal)
    {
        "string", "System.String",
        "int", "System.Int32",
        "long", "System.Int64",
        "short", "System.Int16",
        "byte", "System.Byte",
        "sbyte", "System.SByte",
        "uint", "System.UInt32",
        "ulong", "System.UInt64",
        "ushort", "System.UInt16",
        "float", "System.Single",
        "double", "System.Double",
        "decimal", "System.Decimal",
        "bool", "System.Boolean",
        "char", "System.Char",
        "System.DateTime", "DateTime",
        "System.DateTimeOffset", "DateTimeOffset",
        "System.Guid", "Guid",
        "System.TimeSpan", "TimeSpan",
        "System.DateOnly", "DateOnly",
        "System.TimeOnly", "TimeOnly"
    };

    /// <summary>
    ///     Built-in types including object. Superset of SimpleTypes for general type detection.
    /// </summary>
    internal static readonly HashSet<string> BuiltInTypes = new(SimpleTypes, StringComparer.Ordinal)
    {
        "object", "System.Object"
    };

    /// <summary>
    ///     Numeric types that can be parsed from/to string.
    /// </summary>
    internal static readonly HashSet<string> NumericTypes = new(StringComparer.Ordinal)
    {
        "int", "System.Int32",
        "long", "System.Int64",
        "short", "System.Int16",
        "byte", "System.Byte",
        "sbyte", "System.SByte",
        "uint", "System.UInt32",
        "ulong", "System.UInt64",
        "ushort", "System.UInt16",
        "float", "System.Single",
        "double", "System.Double",
        "decimal", "System.Decimal"
    };

    internal static readonly HashSet<string> StringTypes = new(StringComparer.Ordinal)
    {
        "string", "System.String"
    };

    internal static readonly HashSet<string> BoolTypes = new(StringComparer.Ordinal)
    {
        "bool", "System.Boolean"
    };

    internal static readonly HashSet<string> CharTypes = new(StringComparer.Ordinal)
    {
        "char", "System.Char"
    };

    internal static readonly HashSet<string> DateTimeTypes = new(StringComparer.Ordinal)
    {
        "System.DateTime", "DateTime"
    };

    internal static readonly HashSet<string> DateOnlyTypes = new(StringComparer.Ordinal)
    {
        "System.DateOnly", "DateOnly"
    };

    internal static readonly HashSet<string> TimeOnlyTypes = new(StringComparer.Ordinal)
    {
        "System.TimeOnly", "TimeOnly"
    };

    internal static readonly HashSet<string> GuidTypes = new(StringComparer.Ordinal)
    {
        "System.Guid", "Guid"
    };

    /// <summary>
    ///     Date and time types (DateTime, DateTimeOffset, TimeSpan, DateOnly, TimeOnly).
    /// </summary>
    internal static readonly HashSet<string> DateTimeAllTypes = new(StringComparer.Ordinal)
    {
        "System.DateTime", "DateTime",
        "System.DateTimeOffset", "DateTimeOffset",
        "System.TimeSpan", "TimeSpan",
        "System.DateOnly", "DateOnly",
        "System.TimeOnly", "TimeOnly"
    };
}

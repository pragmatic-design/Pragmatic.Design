using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>The type-level helpers of the model: nullability, the reader each mapped type takes, converted readers, the table name convention.</summary>
internal sealed partial class EntityModel
{
    /// <summary>The type without its nullability: <c>T</c> for <c>T?</c>, whether a Nullable&lt;T&gt; or an annotated reference type.</summary>
    public static ITypeSymbol Unwrap(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } nullable ? nullable.TypeArguments[0] : type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

    /// <summary>An integer type, nullable or not: its AVG is an integer on SQL Server and a numeric on MySQL, so a mean read as a double multiplies it by 1E0 first.</summary>
    public static bool IsInteger(ITypeSymbol type)
        => Unwrap(type).SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64;

    /// <summary>The reader of a converted column: the store type read, then the converter's FromStore; a null column stays null when the property admits it.</summary>
    private static string? ConvertedReader(string converter, ITypeSymbol store, bool nullable)
    {
        var inner = ReaderFor(Unwrap(store));
        if (inner is null)
            return null;
        return nullable ? $"({{r}}.IsDBNull({{i}}) ? null : {converter}.FromStore({inner}))" : $"{converter}.FromStore({inner})";
    }

    /// <summary>The element reader of an array column (<c>string[]</c>, <c>Guid[]</c>, <c>int[]</c>, <c>long[]</c> or a <c>List</c> of them): <c>SqlArrays</c>' method; null for any other type.</summary>
    public static string? ArrayReader(ITypeSymbol type, out bool list)
    {
        list = false;
        ITypeSymbol? element = type is IArrayTypeSymbol array ? array.ElementType : null;
        if (element is null && type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } generic && generic.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
        {
            element = generic.TypeArguments[0];
            list = true;
        }

        return element?.SpecialType switch
        {
            SpecialType.System_String => "Strings",
            SpecialType.System_Int32 => "Int32s",
            SpecialType.System_Int64 => "Int64s",
            _ => element?.ToDisplayString() == "System.Guid" ? "Guids" : null,
        };
    }

    /// <summary>True for an array column's type: its values bind through <c>SqlArrays.ToStore</c>.</summary>
    public static bool IsArrayColumn(ITypeSymbol type) => ArrayReader(type, out _) is not null;

    /// <summary>
    ///     The <c>SqlValue</c> call that reads a value of this type at an ordinal, with <c>{r}</c>, <c>{i}</c> and <c>{d}</c> to fill in; null when
    ///     the type is not mapped. <paramref name="forceNullable" /> reads a null as null whatever the type says: an optional owned type's
    ///     member projected on its own, outside the initializer that guards the whole object.
    /// </summary>
    public static string? ReaderFor(ITypeSymbol type, bool forceNullable = false)
    {
        if (ArrayReader(type, out var list) is { } elements)
        {
            // An array column: a native array on PostgreSQL, the JSON text elsewhere (SqlArrays); a List built over the array.
            var read = $"global::Pragmatic.Persistence.Sql.Functions.SqlArrays.{elements}({{r}}, {{i}}, {{d}})";
            var typed = list ? $"new {type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}({read})" : read;
            return forceNullable || type.NullableAnnotation == NullableAnnotation.Annotated ? $"({{r}}.IsDBNull({{i}}) ? null : {typed})" : typed;
        }

        var nullable = forceNullable;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } n)
        {
            nullable = true;
            type = n.TypeArguments[0];
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            var enumName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return nullable
                ? $"({{r}}.IsDBNull({{i}}) ? null : ({enumName}?)({enumName}){{r}}.GetInt32({{i}}))"
                : $"({enumName}){{r}}.GetInt32({{i}})";
        }

        if (type.NullableAnnotation == NullableAnnotation.Annotated && type.IsReferenceType)
            nullable = true;
        var prefix = nullable ? "Nullable" : "";
        var name = type.SpecialType switch
        {
            SpecialType.System_String => "String",
            SpecialType.System_Int32 => "Int32",
            SpecialType.System_Int64 => "Int64",
            SpecialType.System_Decimal => "Decimal",
            SpecialType.System_Double => "Double",
            SpecialType.System_Boolean => "Boolean",
            SpecialType.System_DateTime => "DateTime",
            _ => type.ToDisplayString() switch
            {
                "System.Guid" or "System.Guid?" => "Guid",
                "System.DateTimeOffset" or "System.DateTimeOffset?" => "DateTimeOffset",
                "System.DateOnly" or "System.DateOnly?" => "DateOnly",
                "System.TimeOnly" or "System.TimeOnly?" => "TimeOnly",
                "System.TimeSpan" or "System.TimeSpan?" => "TimeSpan", // stored as 64-bit ticks
                "byte[]" or "byte[]?" => "Bytes",
                "float[]" or "float[]?" => "Vector",
                _ => null,
            },
        };
        if (name is null)
            return null;
        var dialectArgument = name is "String" or "Int32" or "Int64" or "Double" or "TimeSpan" or "Bytes" ? "" : ", {d}";
        return $"global::Pragmatic.Persistence.Sql.Planning.SqlValue.{prefix}{name}({{r}}, {{i}}{dialectArgument})";
    }

    public static string Pluralize(string name) => PragmaticNames.Pluralize(name);
}

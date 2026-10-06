using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Logging.Models;

namespace Pragmatic.SourceGenerator.Features.Logging.Transforms;

/// <summary>How a value of a given type is written, decided once at compile time.</summary>
internal static class LogValueKinds
{
    /// <summary>The kind, the JSON format of a formattable value, and the type a number is widened to.</summary>
    /// <param name="type">The type, with a <c>Nullable&lt;T&gt;</c> already unwrapped.</param>
    /// <param name="compilation">The compilation, for <c>IUtf8SpanFormattable</c>.</param>
    public static (LogValueKind Kind, string JsonFormat, string NumberType) Of(ITypeSymbol type, Compilation compilation)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_String:
                return (LogValueKind.String, "", "");
            case SpecialType.System_Boolean:
                return (LogValueKind.Boolean, "", "");
            // Utf8JsonWriter.WriteNumber has int, long, uint, ulong, float, double and decimal overloads;
            // the narrower integers widen to one of them losslessly.
            case SpecialType.System_Int32:
            case SpecialType.System_Int16:
            case SpecialType.System_SByte:
                return (LogValueKind.Number, "", "int");
            case SpecialType.System_UInt32:
            case SpecialType.System_UInt16:
            case SpecialType.System_Byte:
                return (LogValueKind.Number, "", "uint");
            case SpecialType.System_Int64:
                return (LogValueKind.Number, "", "long");
            case SpecialType.System_UInt64:
                return (LogValueKind.Number, "", "ulong");
            case SpecialType.System_Single:
                return (LogValueKind.Number, "", "float");
            case SpecialType.System_Double:
                return (LogValueKind.Number, "", "double");
            case SpecialType.System_Decimal:
                return (LogValueKind.Number, "", "decimal");
            case SpecialType.System_DateTime:
                return (LogValueKind.Formattable, "O", "");
        }

        if (type.TypeKind == TypeKind.Enum)
            return (LogValueKind.Enum, "", "");

        // ⚠️ The runtime's own formattable types only. An application type that formats itself can also
        // declare [PersonalData] members, and writing it through its TryFormat would put them out past the
        // declared redactor; as an Object it goes through the list view, where the redactor masks them.
        var formattable = compilation.GetTypeByMetadataName("System.IUtf8SpanFormattable");
        if (formattable is not null
            && IsRuntimeType(type)
            && type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, formattable)))
            return (LogValueKind.Formattable, JsonFormatOf(type), "");

        return (LogValueKind.Object, "", "");
    }

    /// <summary>
    ///     The same decision for a call site a template writes, where there is a type name and no symbol.
    ///     Only the names those templates use are known; anything else is an <see cref="LogValueKind.Object" />,
    ///     which is correct for any type, only slower.
    /// </summary>
    public static (LogValueKind Kind, string JsonFormat, string NumberType) OfTypeName(string typeName)
        => typeName.Replace("global::", "") switch
        {
            "string" or "System.String" => (LogValueKind.String, "", ""),
            "bool" or "System.Boolean" => (LogValueKind.Boolean, "", ""),
            "int" or "short" or "sbyte" => (LogValueKind.Number, "", "int"),
            "uint" or "ushort" or "byte" => (LogValueKind.Number, "", "uint"),
            "long" => (LogValueKind.Number, "", "long"),
            "ulong" => (LogValueKind.Number, "", "ulong"),
            "float" => (LogValueKind.Number, "", "float"),
            "double" => (LogValueKind.Number, "", "double"),
            "decimal" => (LogValueKind.Number, "", "decimal"),
            "System.DateTime" or "System.DateTimeOffset" => (LogValueKind.Formattable, "O", ""),
            "System.TimeSpan" => (LogValueKind.Formattable, "c", ""),
            "System.Guid" => (LogValueKind.Formattable, "D", ""),
            _ => (LogValueKind.Object, "", ""),
        };

    private static bool IsRuntimeType(ITypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return ns == "System" || ns.StartsWith("System.", System.StringComparison.Ordinal);
    }

    /// <summary>The format the Pragmatic JSON provider writes these types with, so both paths agree.</summary>
    private static string JsonFormatOf(ITypeSymbol type)
        => type.ToDisplayString() switch
        {
            "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly" => "O",
            "System.TimeSpan" => "c",
            "System.Guid" => "D",
            _ => "",
        };
}

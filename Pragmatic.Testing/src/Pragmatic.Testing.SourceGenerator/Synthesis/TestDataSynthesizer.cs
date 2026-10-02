using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Testing.SourceGenerator.Synthesis;

/// <summary>
///     Synthesizes a valid C# value expression for a primitive field type — the per-field building block of a
///     CRUD create body (#7, phase 2). Strings are unique (so they satisfy logic keys); numbers are in-range;
///     an enum takes its first member; ids/timestamps come from the clock. Foreign keys and nested complex
///     types are resolved separately (phase 3 topological synthesis); they fall back to <c>default!</c> here.
/// </summary>
internal static class TestDataSynthesizer
{
    /// <summary>Returns a C# expression producing a valid value for <paramref name="type"/>, or null if unsupported.</summary>
    public static string? Synthesize(ITypeSymbol type)
    {
        // Unwrap Nullable<T>.
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            return Synthesize(nullable.TypeArguments[0]);

        switch (type.SpecialType)
        {
            // Concatenation, not interpolation: global:: as the first token inside an interpolation hole does
            // not compile (CS0103). "test-" + Guid stringifies the guid via the + operator.
            case SpecialType.System_String: return "\"test-\" + global::System.Guid.NewGuid()";
            case SpecialType.System_Boolean: return "true";
            case SpecialType.System_Int16: return "(short)1";
            case SpecialType.System_Int32: return "1";
            case SpecialType.System_Int64: return "1L";
            case SpecialType.System_Byte: return "(byte)1";
            case SpecialType.System_Decimal: return "1.0m";
            case SpecialType.System_Double: return "1.0d";
            case SpecialType.System_Single: return "1.0f";
            case SpecialType.System_DateTime: return "global::System.DateTime.UtcNow";
            case SpecialType.System_Char: return "'x'";
        }

        var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        switch (fullName)
        {
            case "global::System.Guid": return "global::System.Guid.NewGuid()";
            case "global::System.DateTimeOffset": return "global::System.DateTimeOffset.UtcNow";
            case "global::System.DateOnly": return "global::System.DateOnly.FromDateTime(global::System.DateTime.UtcNow)";
            case "global::System.TimeOnly": return "global::System.TimeOnly.FromDateTime(global::System.DateTime.UtcNow)";
            case "global::System.TimeSpan": return "global::System.TimeSpan.FromMinutes(1)";
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            var firstMember = enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.IsConst);
            if (firstMember is not null)
                return $"{fullName}.{firstMember.Name}";
        }

        // Foreign keys / nested complex types / strongly-typed ids — not synthesizable to a literal here.
        // Returning null lets the caller skip the success test (a `default!` would not bind in an anonymous
        // object, and would assert a 2xx against an invalid body anyway).
        return null;
    }
}

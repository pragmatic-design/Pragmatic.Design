using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     Helpers for emitting valid C# identifiers in generated code. A property named after a C# keyword
///     (e.g. <c>Event</c>, <c>Default</c>, <c>Lock</c>) becomes a keyword when camel-cased (<c>event</c>),
///     which is a compile error if used as a parameter/variable name. <see cref="EscapeIfKeyword"/> prefixes
///     such names with <c>@</c> (the verbatim-identifier escape), leaving normal names unchanged.
/// </summary>
internal static class IdentifierHelper
{
    private static readonly HashSet<string> CSharpKeywords = new(System.StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
        "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte",
        "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "virtual", "void", "volatile", "while",
    };

    /// <summary>Returns <paramref name="name"/>, prefixed with <c>@</c> if it is a C# keyword.</summary>
    public static string EscapeIfKeyword(string name)
        => CSharpKeywords.Contains(name) ? "@" + name : name;
}

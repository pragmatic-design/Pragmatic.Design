// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     Shared string utilities for source generators (naming, pluralization, etc.).
/// </summary>
internal static class StringHelper
{
    /// <summary>
    ///     Basic English pluralization for entity/table/route names.
    /// </summary>
    public static string Pluralize(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        if (name.EndsWith("y", StringComparison.Ordinal) &&
            name.Length > 1 &&
            !IsVowel(name[name.Length - 2]))
            return name.Substring(0, name.Length - 1) + "ies";

        if (name.EndsWith("s", StringComparison.Ordinal) ||
            name.EndsWith("sh", StringComparison.Ordinal) ||
            name.EndsWith("ch", StringComparison.Ordinal) ||
            name.EndsWith("x", StringComparison.Ordinal) ||
            name.EndsWith("z", StringComparison.Ordinal))
            return name + "es";

        return name + "s";
    }

    /// <summary>
    ///     Turns a route segment into a camelCase C# identifier: <c>"case-files"</c> becomes
    ///     <c>"caseFile"</c>.
    /// </summary>
    /// <remarks>
    ///     A route segment is author-controlled and need not be an identifier at all. The obvious
    ///     <c>segment.TrimEnd('s') + "Id"</c> has two faults in one line: a kebab-case segment produces
    ///     <c>case-fileId</c>, C# that does not compile and that the author can only work around by
    ///     renaming the resource; and <c>TrimEnd</c> strips <em>every</em> trailing <c>s</c>, so
    ///     <c>"class"</c> becomes <c>"cla"</c> and <c>"addresses"</c> becomes <c>"addresse"</c>.
    /// </remarks>
    public static string ToCamelCaseIdentifier(string segment)
    {
        if (string.IsNullOrEmpty(segment))
            return segment;

        var builder = new System.Text.StringBuilder(segment.Length);
        var upperNext = false;

        foreach (var c in segment)
        {
            if (!char.IsLetterOrDigit(c))
            {
                // Any separator — '-', '_', '.', '/' — starts a new word rather than surviving into
                // the identifier.
                upperNext = builder.Length > 0;
                continue;
            }

            if (builder.Length == 0)
                builder.Append(char.IsDigit(c) ? '_' : char.ToLowerInvariant(c));
            else
                builder.Append(upperNext ? char.ToUpperInvariant(c) : c);

            upperNext = false;
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Removes one plural suffix. The inverse of <see cref="Pluralize" /> for the cases it
    ///     produces, and a no-op for anything it does not recognise.
    /// </summary>
    public static string Singularize(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        if (name.EndsWith("ies", StringComparison.Ordinal) && name.Length > 3)
            return name.Substring(0, name.Length - 3) + "y";

        // "sses"/"shes"/"ches"/"xes"/"zes" — never a bare "ses". English cannot separate
        // "buses" → "bus" from "cases" → "case" without a dictionary, and route roots of the second
        // shape (cases, invoices, prices, services, licences) are far more common than of the first.
        // "buses" therefore yields "buse". It is a parameter name, and a readable wrong singular
        // beats a wrong-for-most-people rule.
        if (name.EndsWith("sses", StringComparison.Ordinal) ||
            name.EndsWith("shes", StringComparison.Ordinal) ||
            name.EndsWith("ches", StringComparison.Ordinal) ||
            name.EndsWith("xes", StringComparison.Ordinal) ||
            name.EndsWith("zes", StringComparison.Ordinal))
            return name.Substring(0, name.Length - 2);

        // One trailing "s", never a run of them: "class" is not a plural.
        if (name.EndsWith("s", StringComparison.Ordinal) && !name.EndsWith("ss", StringComparison.Ordinal))
            return name.Substring(0, name.Length - 1);

        return name;
    }

    private static bool IsVowel(char c)
    {
        return c is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'I' or 'O' or 'U';
    }

    /// <summary>
    ///     Escapes <paramref name="value" /> for emission inside a NORMAL (non-verbatim) C# string
    ///     literal: <c>"..."</c>. Escapes backslash, double-quote and the common control characters
    ///     (CR, LF, TAB) so author-controlled strings (route literals, regex patterns, header names,
    ///     tags, cron expressions, …) cannot break out of the literal or produce invalid escapes.
    ///     Null is treated as empty. Caller is responsible for the surrounding quotes.
    /// </summary>
    public static string CSharpLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return value!
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }
}

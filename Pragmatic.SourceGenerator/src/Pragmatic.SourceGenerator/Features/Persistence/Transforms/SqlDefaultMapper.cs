using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Turns the C# literal of a <c>[DefaultValue]</c> into the SQL literal a column default needs.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>One value, two languages.</b> <c>EntityTransform.ToLiteral</c> produces a literal that
///         <em>compiles</em> — it is what the generated <c>Create</c> factory assigns — and the schema
///         interpolates its default raw into DDL, so the same string cannot serve both. In
///         PostgreSQL <c>"pending"</c> between double quotes is an <b>identifier</b>, so a
///         <c>[DefaultValue("pending")]</c> on a string would make the migration fail with
///         <c>0A000: cannot use column reference in DEFAULT expression</c> — the application would
///         not start.
///     </para>
///     <para>
///         A numeric default does not show the difference, because there the two literals coincide
///         by accident. The attribute's own documented example is <c>[DefaultValue("EUR")]</c>.
///     </para>
///     <para>
///         The mapping lives beside <see cref="SqlTypeMapper" /> and takes the provider for the same
///         reason that one does: a boolean is <c>TRUE</c> in PostgreSQL and <c>1</c> in SQL Server, so
///         there is no neutral rendering to fall back on.
///     </para>
/// </remarks>
internal static class SqlDefaultMapper
{
    /// <summary>
    ///     The SQL literal for a C# one, or <c>null</c> when there is no default to write.
    /// </summary>
    /// <param name="csharpLiteral">What <c>ToLiteral</c> produced, e.g. <c>"pending"</c> or <c>1</c>.</param>
    /// <param name="provider">The provider whose dialect the literal is written in.</param>
    /// <returns>A literal safe to interpolate into a <c>DEFAULT</c> clause.</returns>
    public static string? ToSqlLiteral(string? csharpLiteral, EfCoreProvider provider)
    {
        if (string.IsNullOrWhiteSpace(csharpLiteral))
            return null;

        var literal = csharpLiteral!.Trim();

        // A string: C# quotes it with ", SQL with '. Both escape their own quote by doubling it in
        // SQL and by a backslash in C#, so the value is unescaped first and re-escaped after.
        if (literal.Length >= 2 && literal[0] == '"' && literal[literal.Length - 1] == '"')
        {
            var inner = literal.Substring(1, literal.Length - 2);
            return Quote(Unescape(inner));
        }

        // A char: C# already uses single quotes, but the content still needs SQL escaping.
        if (literal.Length >= 2 && literal[0] == '\'' && literal[literal.Length - 1] == '\'')
        {
            var inner = literal.Substring(1, literal.Length - 2);
            return Quote(Unescape(inner));
        }

        // A boolean is the one value with no neutral spelling.
        if (literal == "true" || literal == "false")
        {
            var isTrue = literal == "true";
            return provider switch
            {
                EfCoreProvider.PostgreSql => isTrue ? "TRUE" : "FALSE",
                _ => isTrue ? "1" : "0",
            };
        }

        // An enum arrives as a cast over its underlying number — `(global::Ns.Colour)2`. The column
        // holds the number (a user enum is stored in an integer column), so the cast goes.
        if (literal.Length > 0 && literal[0] == '(')
        {
            var close = literal.IndexOf(')');
            if (close > 0 && close < literal.Length - 1)
            {
                var underlying = literal.Substring(close + 1).Trim();
                return IsNumeric(underlying) ? underlying : null;
            }
        }

        // A number is written the same way in both languages. Anything else is a shape this mapper
        // does not know, and emitting it raw is exactly the defect above: no default is a column that
        // behaves predictably, an unquoted word is a migration that fails.
        return IsNumeric(literal) ? literal : null;
    }

    private static bool IsNumeric(string value)
    {
        if (value.Length == 0)
            return false;

        var start = value[0] == '-' ? 1 : 0;
        if (start >= value.Length)
            return false;

        var seenDot = false;

        for (var i = start; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '.')
            {
                if (seenDot)
                    return false;

                seenDot = true;
                continue;
            }

            if (c is < '0' or > '9')
                return false;
        }

        return true;
    }

    /// <summary>C# escapes with a backslash; the value underneath is what SQL has to quote.</summary>
    private static string Unescape(string value)
        => value
            .Replace("\\\"", "\"")
            .Replace("\\'", "'")
            .Replace("\\\\", "\\");

    /// <summary>SQL escapes a quote by doubling it, in every dialect this targets.</summary>
    private static string Quote(string value)
        => "'" + value.Replace("'", "''") + "'";
}

using System.Text;
using System.Text.RegularExpressions;

namespace Pragmatic.Migrations.Diff;

/// <summary>
///     A partial index's predicate in a form two spellings of the same predicate share, for comparison only.
/// </summary>
/// <remarks>
///     <para>
///         The database does not hand back the text it was given. For <c>"Status" = 0</c> PostgreSQL returns
///         <c>("Status" = 0)</c> and SQL Server <c>([Status]=(0))</c>; compared as strings, the index was
///         dropped and recreated on every run and the schema never converged.
///     </para>
///     <para>
///         Outside string literals: whitespace and identifier quoting (<c>"</c>, <c>[</c>, <c>]</c>, a
///         backtick) are removed and the text lowercased; then parentheses around a single identifier, number
///         or literal, and parentheses around the whole predicate. Parentheses that group operators stay, so
///         <c>a AND (b OR c)</c> and <c>(a AND b) OR c</c> remain different. A literal is left exactly as
///         written: <c>'a b'</c> and <c>'ab'</c> are different predicates.
///     </para>
/// </remarks>
internal static partial class IndexFilterExpression
{
    /// <summary>The comparable form of <paramref name="filter" />; <c>null</c> for no filter.</summary>
    internal static string? Normalize(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        var text = Unquoted(filter);

        string previous;
        do
        {
            previous = text;
            text = AtomInParentheses().Replace(text, "$1");
            while (text.Length >= 2 && text[0] == '(' && text[^1] == ')' && WrapsTheWhole(text))
                text = text[1..^1];
        }
        while (text != previous);

        return text;
    }

    private static string Unquoted(string filter)
    {
        var result = new StringBuilder(filter.Length);
        var inLiteral = false;
        foreach (var c in filter)
        {
            if (c == '\'')
            {
                inLiteral = !inLiteral;
                result.Append(c);
            }
            else if (inLiteral)
            {
                result.Append(c);
            }
            else if (!char.IsWhiteSpace(c) && c is not ('"' or '[' or ']' or '`'))
            {
                result.Append(char.ToLowerInvariant(c));
            }
        }

        return result.ToString();
    }

    private static bool WrapsTheWhole(string text)
    {
        var depth = 0;
        var inLiteral = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\'') inLiteral = !inLiteral;
            if (inLiteral) continue;

            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0)
                return i == text.Length - 1;
        }

        return false;
    }

    /// <summary>An identifier, a number or a string literal alone inside parentheses.</summary>
    [GeneratedRegex(@"\(([a-z0-9_.]+|'[^']*')\)")]
    private static partial Regex AtomInParentheses();
}

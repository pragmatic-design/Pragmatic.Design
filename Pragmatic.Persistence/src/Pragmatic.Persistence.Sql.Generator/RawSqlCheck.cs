using System;
using System.Collections.Generic;
using System.Linq;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     A hand-written statement (<c>session.Sql&lt;T&gt;($"…")</c>) checked against the model at build time, by its double-quoted
///     identifiers (the fragments' convention; bare words are the dialect's business and are not read): a table after FROM/JOIN/INTO/UPDATE
///     must be an entity's table, a column must be a column of a table the statement names (of its alias's table when qualified), and
///     the top-level select list must return every column the row type reads (<c>*</c> returns the tables'). A renamed column in the
///     database, once the entity follows it, breaks the build at the statement (PRAG3026). Unquoted identifiers reach the database as
///     written: the PragmaticExplain target reports those the database refuses.
/// </summary>
internal static class RawSqlCheck
{
    private static readonly HashSet<string> TableKeywords = new(StringComparer.OrdinalIgnoreCase) { "FROM", "JOIN", "INTO", "UPDATE" };
    private static readonly HashSet<string> NotAnAlias = new(StringComparer.OrdinalIgnoreCase) { "WHERE", "ON", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "CROSS", "FULL", "GROUP", "ORDER", "LIMIT", "OFFSET", "UNION", "EXCEPT", "INTERSECT", "SET", "USING", "HAVING", "WINDOW", "FETCH", "FOR", "RETURNING", "AS", "NATURAL", "LATERAL", "VALUES" };

    private enum Kind
    {
        Identifier,
        Word,
        Symbol,
    }

    private sealed class Token(Kind kind, string text)
    {
        public Kind Kind { get; } = kind;

        public string Text { get; } = text;

        public bool Is(string word) => Kind == Kind.Word && string.Equals(Text, word, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The mismatches, each a sentence; empty when the statement agrees with the model as far as its quoted identifiers say.</summary>
    public static List<string> Problems(string sql, EntityModel shape, Func<string, EntityModel?> tables)
    {
        var problems = new List<string>();
        var tokens = Tokenize(sql);
        var referenced = new List<EntityModel>();
        var aliases = new Dictionary<string, EntityModel>(StringComparer.OrdinalIgnoreCase);
        var consumed = new HashSet<int>();

        // The tables and their aliases.
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Kind != Kind.Word || !TableKeywords.Contains(tokens[i].Text) || tokens[i + 1].Kind != Kind.Identifier)
                continue;
            var table = tables(tokens[i + 1].Text);
            consumed.Add(i + 1);
            if (table is null)
            {
                problems.Add($"\"{tokens[i + 1].Text}\" is not a table of the model");
                continue;
            }

            referenced.Add(table);
            aliases[table.Table] = table;
            var next = i + 2;
            if (next < tokens.Count && tokens[next].Is("AS"))
                next++;
            if (next < tokens.Count && (tokens[next].Kind == Kind.Identifier || (tokens[next].Kind == Kind.Word && !NotAnAlias.Contains(tokens[next].Text))))
            {
                aliases[tokens[next].Text] = table;
                consumed.Add(next);
            }
        }

        // The columns, and what the top-level select list returns.
        var output = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var star = false;
        var depth = 0;
        var inSelectList = false;
        var selectSeen = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind == Kind.Symbol)
            {
                if (token.Text == "(")
                    depth++;
                else if (token.Text == ")")
                    depth--;
                else if (token.Text == "*" && inSelectList && depth == 0)
                    star = true;
                continue;
            }

            if (depth == 0 && token.Is("SELECT") && !selectSeen)
            {
                selectSeen = inSelectList = true;
                continue;
            }

            if (depth == 0 && inSelectList && token.Is("FROM"))
                inSelectList = false;
            if (token.Kind != Kind.Identifier || consumed.Contains(i))
                continue;
            if (i > 0 && tokens[i - 1].Is("AS"))
            {
                if (inSelectList && depth == 0)
                    output.Add(token.Text); // an output alias
                continue;
            }

            var qualified = i >= 2 && tokens[i - 1].Kind == Kind.Symbol && tokens[i - 1].Text == "." && tokens[i - 2].Kind is Kind.Identifier or Kind.Word;
            if (qualified)
            {
                var qualifier = tokens[i - 2].Text;
                if (aliases.TryGetValue(qualifier, out var owner))
                {
                    if (owner.Columns.All(c => !string.Equals(c.Column, token.Text, StringComparison.OrdinalIgnoreCase)))
                        problems.Add($"\"{token.Text}\" is not a column of {owner.Table} (its columns: {string.Join(", ", owner.Columns.Select(c => c.Column))})");
                }
            }
            else if (referenced.Count > 0 && referenced.All(t => t.Columns.All(c => !string.Equals(c.Column, token.Text, StringComparison.OrdinalIgnoreCase))))
            {
                problems.Add(referenced.Count == 1
                    ? $"\"{token.Text}\" is not a column of {referenced[0].Table} (its columns: {string.Join(", ", referenced[0].Columns.Select(c => c.Column))})"
                    : $"\"{token.Text}\" is not a column of {string.Join(", ", referenced.Select(t => t.Table))}");
            }

            if (inSelectList && depth == 0 && !(i + 1 < tokens.Count && tokens[i + 1].Is("AS")))
                output.Add(token.Text);
        }

        // The row type's columns, each returned by name (the plan reads them from the statement as a derived table).
        if (selectSeen && referenced.Count > 0 && !shape.Columns.Any(c => c.IsDiscriminator))
        {
            if (star)
            {
                foreach (var table in referenced)
                {
                    foreach (var column in table.Columns)
                        output.Add(column.Column);
                }
            }

            var missing = shape.Columns.Where(c => !output.Contains(c.Column)).Select(c => c.Column).ToList();
            if (missing.Count > 0)
                problems.Add($"the statement does not return {string.Join(", ", missing.Select(m => "\"" + m + "\""))}, which {shape.Type.Name} reads");
        }

        return problems;
    }

    private static List<Token> Tokenize(string sql)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '"')
            {
                var end = sql.IndexOf('"', i + 1);
                if (end < 0)
                    end = sql.Length;
                tokens.Add(new Token(Kind.Identifier, sql.Substring(i + 1, end - i - 1)));
                i = end + 1;
            }
            else if (c == '\'')
            {
                // a string literal: skipped whole ('' is an escaped quote)
                var end = i + 1;
                while (end < sql.Length && (sql[end] != '\'' || (end + 1 < sql.Length && sql[end + 1] == '\'' && ++end > 0)))
                    end++;
                i = end + 1;
            }
            else if (char.IsLetter(c) || c == '_' || c == '@')
            {
                var start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '@'))
                    i++;
                tokens.Add(new Token(Kind.Word, sql.Substring(start, i - start)));
            }
            else if (char.IsDigit(c))
            {
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '.'))
                    i++;
            }
            else
            {
                tokens.Add(new Token(Kind.Symbol, c.ToString()));
                i++;
            }
        }

        return tokens;
    }
}

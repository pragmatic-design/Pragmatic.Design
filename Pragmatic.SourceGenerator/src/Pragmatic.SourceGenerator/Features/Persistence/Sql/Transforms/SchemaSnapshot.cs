using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The indexes of the database, read from the committed schema snapshot (<c>*.schema.json</c> among the project's
///     AdditionalFiles: the file the repository's migrations write, <c>tables[].indexes[].columns</c> and the primary keys in
///     <c>tables[].columns[].isPrimaryKey</c>). A column is indexed when it leads an index of its table (the second column of a
///     composite index does not help a WHERE on it alone) or is in the primary key. Where the snapshot knows a table, it is the
///     truth about its indexes and <c>[Indexed]</c> is not read; a table the snapshot does not list keeps the attribute.
/// </summary>
internal sealed class SchemaIndexes
{
    private readonly Dictionary<string, HashSet<string>> _leading = new(StringComparer.OrdinalIgnoreCase);

    public int Tables => _leading.Count;

    public int Indexes { get; private set; }

    public bool HasTable(string table) => _leading.ContainsKey(table);

    public bool IsIndexed(string table, string column) => _leading.TryGetValue(table, out var columns) && columns.Contains(column);

    /// <summary>Adds the tables of one snapshot; false with the reason when the text is not a snapshot.</summary>
    public bool Add(string json, out string? error)
    {
        error = null;
        object? root;
        try
        {
            root = new JsonReader(json).Read();
        }
        catch (FormatException e)
        {
            error = e.Message;
            return false;
        }

        if (root is not Dictionary<string, object?> document || !document.TryGetValue("tables", out var tablesValue) || tablesValue is not List<object?> tables)
        {
            error = "no 'tables' array at the root";
            return false;
        }

        foreach (var entry in tables)
        {
            if (entry is not Dictionary<string, object?> table || !table.TryGetValue("name", out var nameValue) || nameValue is not string name)
            {
                error = "a table without a 'name'";
                return false;
            }

            if (!_leading.TryGetValue(name, out var leading))
                _leading[name] = leading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (table.TryGetValue("columns", out var columnsValue) && columnsValue is List<object?> columns)
            {
                foreach (var column in columns)
                {
                    if (column is Dictionary<string, object?> c && c.TryGetValue("name", out var columnName) && columnName is string cn && c.TryGetValue("isPrimaryKey", out var pk) && pk is true)
                        leading.Add(cn);
                }
            }

            if (!table.TryGetValue("indexes", out var indexesValue) || indexesValue is not List<object?> indexes)
                continue;
            foreach (var index in indexes)
            {
                if (index is not Dictionary<string, object?> i || !i.TryGetValue("columns", out var indexColumns) || indexColumns is not List<object?> { Count: > 0 } list || list[0] is not string first)
                {
                    error = $"an index of '{name}' without its columns";
                    return false;
                }

                leading.Add(first);
                Indexes++;
            }
        }

        return true;
    }

    /// <summary>A small JSON reader (the generator targets netstandard2.0 without System.Text.Json): objects as case-insensitive dictionaries, arrays as lists, strings, numbers as double, true/false/null.</summary>
    private sealed class JsonReader(string text)
    {
        private int _at;

        public object? Read()
        {
            var value = Value();
            Space();
            if (_at != text.Length)
                throw new FormatException($"unexpected text at {_at}");
            return value;
        }

        private object? Value()
        {
            Space();
            if (_at >= text.Length)
                throw new FormatException("unexpected end");
            var c = text[_at];
            switch (c)
            {
                case '{':
                {
                    _at++;
                    var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    Space();
                    if (Peek('}'))
                        return result;
                    while (true)
                    {
                        Space();
                        var key = Text();
                        Space();
                        Expect(':');
                        result[key] = Value();
                        Space();
                        if (Peek('}'))
                            return result;
                        Expect(',');
                    }
                }

                case '[':
                {
                    _at++;
                    var result = new List<object?>();
                    Space();
                    if (Peek(']'))
                        return result;
                    while (true)
                    {
                        result.Add(Value());
                        Space();
                        if (Peek(']'))
                            return result;
                        Expect(',');
                    }
                }

                case '"':
                    return Text();
                case 't':
                    Word("true");
                    return true;
                case 'f':
                    Word("false");
                    return false;
                case 'n':
                    Word("null");
                    return null;
                default:
                {
                    var start = _at;
                    while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] is '-' or '+' or '.' or 'e' or 'E'))
                        _at++;
                    if (start == _at || !double.TryParse(text.Substring(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                        throw new FormatException($"unexpected character '{c}' at {start}");
                    return number;
                }
            }
        }

        private string Text()
        {
            Expect('"');
            var result = new StringBuilder();
            while (_at < text.Length)
            {
                var c = text[_at++];
                if (c == '"')
                    return result.ToString();
                if (c != '\\')
                {
                    result.Append(c);
                    continue;
                }

                if (_at >= text.Length)
                    break;
                var escaped = text[_at++];
                switch (escaped)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (_at + 4 > text.Length)
                            throw new FormatException("a truncated \\u escape");
                        result.Append((char)int.Parse(text.Substring(_at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _at += 4;
                        break;
                    default:
                        throw new FormatException($"unknown escape '\\{escaped}'");
                }
            }

            throw new FormatException("an unterminated string");
        }

        private void Space()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at]))
                _at++;
        }

        private bool Peek(char c)
        {
            if (_at < text.Length && text[_at] == c)
            {
                _at++;
                return true;
            }

            return false;
        }

        private void Expect(char c)
        {
            if (!Peek(c))
                throw new FormatException($"'{c}' expected at {_at}");
        }

        private void Word(string word)
        {
            if (string.CompareOrdinal(text, _at, word, 0, word.Length) != 0)
                throw new FormatException($"'{word}' expected at {_at}");
            _at += word.Length;
        }
    }
}

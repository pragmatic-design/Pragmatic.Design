// ReSharper disable once CheckNamespace

namespace Pragmatic.SourceGen;

/// <summary>
///     Structural JSON validator for source generators (netstandard2.0 — no
///     System.Text.Json available). Validates token structure, bracket balance, string escapes
///     (including <c>\uXXXX</c>) and the RFC 8259 number grammar.
/// </summary>
/// <remarks>
///     Callers use this as a gate before embedding a fragment verbatim into generated JSON
///     (see <c>MetadataJsonBuilder.RawValue</c>), so a false positive means corrupt output.
///     The validator is therefore strict: unknown escapes, unescaped control characters,
///     leading zeroes and malformed exponents are all rejected.
/// </remarks>
public static class JsonValidator
{
    /// <summary>
    ///     Returns true when <paramref name="json" /> is structurally valid JSON
    ///     (single value: object, array, string, number, true/false/null).
    /// </summary>
    public static bool IsValid(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        var position = 0;
        if (!SkipValue(json!, ref position))
            return false;

        SkipWhitespace(json!, ref position);
        return position == json!.Length;
    }

    private static bool SkipValue(string json, ref int position)
    {
        SkipWhitespace(json, ref position);
        if (position >= json.Length)
            return false;

        return json[position] switch
        {
            '{' => SkipObject(json, ref position),
            '[' => SkipArray(json, ref position),
            '"' => SkipString(json, ref position),
            't' => SkipLiteral(json, ref position, "true"),
            'f' => SkipLiteral(json, ref position, "false"),
            'n' => SkipLiteral(json, ref position, "null"),
            _ => SkipNumber(json, ref position)
        };
    }

    private static bool SkipObject(string json, ref int position)
    {
        position++; // '{'
        SkipWhitespace(json, ref position);
        if (position < json.Length && json[position] == '}')
        {
            position++;
            return true;
        }

        while (position < json.Length)
        {
            SkipWhitespace(json, ref position);
            if (position >= json.Length || json[position] != '"' || !SkipString(json, ref position))
                return false;

            SkipWhitespace(json, ref position);
            if (position >= json.Length || json[position] != ':')
                return false;
            position++; // ':'

            if (!SkipValue(json, ref position))
                return false;

            SkipWhitespace(json, ref position);
            if (position >= json.Length)
                return false;

            if (json[position] == '}')
            {
                position++;
                return true;
            }

            if (json[position] != ',')
                return false;
            position++; // ','
        }

        return false;
    }

    private static bool SkipArray(string json, ref int position)
    {
        position++; // '['
        SkipWhitespace(json, ref position);
        if (position < json.Length && json[position] == ']')
        {
            position++;
            return true;
        }

        while (position < json.Length)
        {
            if (!SkipValue(json, ref position))
                return false;

            SkipWhitespace(json, ref position);
            if (position >= json.Length)
                return false;

            if (json[position] == ']')
            {
                position++;
                return true;
            }

            if (json[position] != ',')
                return false;
            position++; // ','
        }

        return false;
    }

    private static bool SkipString(string json, ref int position)
    {
        position++; // opening '"'
        while (position < json.Length)
        {
            var c = json[position];
            if (c == '\\')
            {
                if (!SkipEscape(json, ref position))
                    return false;
                continue;
            }

            if (c == '"')
            {
                position++;
                return true;
            }

            // RFC 8259 §7: characters below U+0020 must be escaped inside a string.
            if (c < ' ')
                return false;

            position++;
        }

        return false; // unterminated string
    }

    /// <summary>
    ///     Consumes a <c>\</c> escape sequence. Only the eight escapes of RFC 8259 §7 are legal, and
    ///     <c>\u</c> must be followed by exactly four hexadecimal digits.
    /// </summary>
    private static bool SkipEscape(string json, ref int position)
    {
        position++; // '\'
        if (position >= json.Length)
            return false;

        var escape = json[position];
        position++;

        switch (escape)
        {
            case '"':
            case '\\':
            case '/':
            case 'b':
            case 'f':
            case 'n':
            case 'r':
            case 't':
                return true;
            case 'u':
                if (position + 4 > json.Length)
                    return false;
                for (var i = 0; i < 4; i++)
                {
                    if (!IsHexDigit(json[position]))
                        return false;
                    position++;
                }

                return true;
            default:
                return false;
        }
    }

    private static bool IsHexDigit(char c)
        => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static bool SkipLiteral(string json, ref int position, string literal)
    {
        if (position + literal.Length > json.Length ||
            json.Substring(position, literal.Length) != literal)
            return false;

        position += literal.Length;
        return true;
    }

    /// <summary>
    ///     Consumes a number per the RFC 8259 grammar:
    ///     <c>-? ( 0 | [1-9][0-9]* ) ( '.' [0-9]+ )? ( [eE] [+-]? [0-9]+ )?</c>.
    ///     A permissive "any mix of digits, dot, e, sign" scan accepts <c>1.2.3</c> and <c>1e+e5</c>,
    ///     which would then be emitted verbatim into generated JSON.
    /// </summary>
    private static bool SkipNumber(string json, ref int position)
    {
        if (position < json.Length && json[position] == '-')
            position++;

        // Integer part: a single '0', or a non-zero digit followed by any digits. No leading zeroes.
        if (position >= json.Length || !IsDigit(json[position]))
            return false;

        if (json[position] == '0')
        {
            position++;
        }
        else
        {
            while (position < json.Length && IsDigit(json[position]))
                position++;
        }

        // Fraction: '.' must be followed by at least one digit.
        if (position < json.Length && json[position] == '.')
        {
            position++;
            if (position >= json.Length || !IsDigit(json[position]))
                return false;
            while (position < json.Length && IsDigit(json[position]))
                position++;
        }

        // Exponent: [eE] optional sign, then at least one digit.
        if (position < json.Length && json[position] is 'e' or 'E')
        {
            position++;
            if (position < json.Length && json[position] is '+' or '-')
                position++;
            if (position >= json.Length || !IsDigit(json[position]))
                return false;
            while (position < json.Length && IsDigit(json[position]))
                position++;
        }

        return true;
    }

    // char.IsDigit is Unicode-aware and accepts e.g. Arabic-Indic digits; JSON allows only U+0030..U+0039.
    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static void SkipWhitespace(string json, ref int position)
    {
        while (position < json.Length && char.IsWhiteSpace(json[position]))
            position++;
    }
}

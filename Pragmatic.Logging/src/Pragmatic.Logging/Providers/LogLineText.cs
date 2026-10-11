using System.Buffers;
using System.Text;

namespace Pragmatic.Logging.Providers;

/// <summary>Puts a value into a line of text so that it stays on that line.</summary>
/// <remarks>
///     <para>
///         A provider that writes text writes one entry as one line, and a reader of the log takes every line for an
///         entry. A value that carries CR or LF — a decoded request path, a header, anything a client sent — would end
///         the entry where the client chose and begin a line the client wrote (log forging). The other C0 controls are
///         escaped too: ESC starts a terminal sequence, and NUL or BEL have no business in a line of text either.
///     </para>
///     <para>
///         Each control is written as an escape the reader can still read: <c>\r</c>, <c>\n</c>, <c>\t</c>, and caret
///         notation for the rest (<c>\^G</c> for BEL, <c>\^[</c> for ESC). The JSON providers do the same through JSON's
///         own escaping. Applied to the rendered message as a whole, since the provider receives it already rendered:
///         a line break the template itself writes is escaped as well. Exception details are not passed through here,
///         and stay on lines of their own.
///     </para>
/// </remarks>
internal static class LogLineText
{
    // ⚠️ SearchValues, not IndexOfAnyInRange: measured, the range overload allocates 96 bytes a call on .NET 10, and
    // this scan runs on every value of every entry.
    private static readonly SearchValues<char> Controls = SearchValues.Create(
        "\0\u0001\u0002\u0003\u0004\u0005\u0006\u0007\b\t\n\u000B\f\r\u000E\u000F" +
        "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F");

    /// <summary>
    ///     The value with every C0 control written as an escape; the same instance, with nothing allocated, when it
    ///     holds none.
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        var first = value.AsSpan().IndexOfAny(Controls);
        if (first < 0)
            return value;

        var escaped = new StringBuilder(value.Length + 8);
        escaped.Append(value, 0, first);

        for (var i = first; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\r':
                    escaped.Append("\\r");
                    break;
                case '\n':
                    escaped.Append("\\n");
                    break;
                case '\t':
                    escaped.Append("\\t");
                    break;
                case < ' ':
                    escaped.Append("\\^").Append((char)(c + '@'));
                    break;
                default:
                    escaped.Append(c);
                    break;
            }
        }

        return escaped.ToString();
    }
}

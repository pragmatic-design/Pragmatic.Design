using System.Collections;
using System.Globalization;
using System.Text;

namespace Pragmatic.Redaction;

/// <summary>
///     A log entry's structured values with the declared members already masked, and the message
///     rendered from those same masked values.
/// </summary>
/// <remarks>
///     Rendering here rather than reusing the original formatter is the whole point. The formatter
///     closes over the ORIGINAL state, so calling it would put the unmasked value back into the
///     message text while the structured property beside it was masked. Both loggers that redact go
///     through here (<see cref="DeclaredRedactor.RedactState" />): <c>RedactingLogger</c>, and
///     Pragmatic.Logging's own logger, which used to mask <c>LogEntry.Properties</c> after the message
///     was already rendered and so wrote the member in clear.
/// </remarks>
internal sealed class RedactedLogValues : IReadOnlyList<KeyValuePair<string, object?>>
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    private readonly KeyValuePair<string, object?>[] _values;
    private string? _rendered;

    public RedactedLogValues(KeyValuePair<string, object?>[] values) => _values = values;

    public int Count => _values.Length;

    public KeyValuePair<string, object?> this[int index] => _values[index];

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        => ((IEnumerable<KeyValuePair<string, object?>>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();

    public override string ToString() => _rendered ??= Render();

    /// <summary>
    ///     Renders the message the way <c>Microsoft.Extensions.Logging</c>'s formatter does, from the masked
    ///     values.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Positional, as MEL is: the n-th hole takes the n-th value whatever it is called, so a
    ///         template that repeats a name takes each value in turn. A <c>null</c> is <c>(null)</c>, an
    ///         enumerable that is not a string is its items joined with <c>", "</c>, and the whole is
    ///         formatted with the invariant culture.
    ///     </para>
    ///     <para>
    ///         Same output is the point: an entry whose arguments include a declared value must not log
    ///         its other values differently from the same call without one. The tests compare this with
    ///         MEL's own rendering rather than with expected strings.
    ///     </para>
    /// </remarks>
    private string Render()
    {
        var template = FindTemplate();
        if (template is null)
            return string.Join(" ", _values.Select(v => $"{v.Key}={v.Value}"));

        var composite = ToCompositeFormat(template, out var holes);
        if (holes == 0)
            return composite;

        var arguments = new object?[holes];
        var position = 0;
        foreach (var value in _values)
        {
            if (string.Equals(value.Key, OriginalFormatKey, StringComparison.Ordinal))
                continue;
            if (position == holes)
                break;
            arguments[position++] = FormatArgument(value.Value);
        }

        return string.Format(CultureInfo.InvariantCulture, composite, arguments);
    }

    /// <summary>
    ///     Turns a message template into a composite format string, each named hole replaced by its
    ///     position: <c>"{Who} paid {Amount,8:F2}"</c> becomes <c>"{0} paid {1,8:F2}"</c>.
    /// </summary>
    /// <remarks>Escaped braces are copied as they are, since a composite format escapes them the same way.</remarks>
    private static string ToCompositeFormat(string template, out int holes)
    {
        holes = 0;
        var result = new StringBuilder(template.Length);
        var scan = 0;
        var end = template.Length;

        while (scan < end)
        {
            var open = FindBrace(template, '{', scan, end);
            if (scan == 0 && open == end)
                return template;

            var close = FindBrace(template, '}', open, end);
            if (close == end)
            {
                result.Append(template, scan, end - scan);
                break;
            }

            var delimiter = template.IndexOfAny([',', ':'], open, close - open);
            if (delimiter < 0)
                delimiter = close;

            result.Append(template, scan, open - scan + 1);
            result.Append(holes.ToString(CultureInfo.InvariantCulture));
            result.Append(template, delimiter, close - delimiter + 1);
            holes++;
            scan = close + 1;
        }

        return result.ToString();
    }

    /// <summary>
    ///     The index of the next unescaped <paramref name="brace" />, or <paramref name="end" />: a run of
    ///     an even number of the same brace is an escape and is skipped.
    /// </summary>
    private static int FindBrace(string template, char brace, int start, int end)
    {
        var found = end;
        var run = 0;

        for (var scan = start; scan < end; scan++)
        {
            if (run > 0 && template[scan] != brace)
            {
                if (run % 2 == 0)
                {
                    run = 0;
                    found = end;
                }
                else
                {
                    break;
                }
            }
            else if (template[scan] == brace)
            {
                if (brace == '}')
                {
                    if (run == 0)
                        found = scan;
                }
                else
                {
                    found = scan;
                }

                run++;
            }
        }

        return found;
    }

    private static object FormatArgument(object? value)
    {
        const string Null = "(null)";

        if (value is null)
            return Null;

        if (value is string || value is not IEnumerable enumerable)
            return value;

        var joined = new StringBuilder();
        var first = true;
        foreach (var item in enumerable)
        {
            if (!first)
                joined.Append(", ");
            joined.Append(item is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : item?.ToString() ?? Null);
            first = false;
        }

        return joined.ToString();
    }

    private string? FindTemplate()
    {
        foreach (var value in _values)
            if (string.Equals(value.Key, OriginalFormatKey, StringComparison.Ordinal))
                return value.Value as string;

        return null;
    }
}

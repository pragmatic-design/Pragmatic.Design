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
///     message text while the structured property beside it was masked — which is what
///     <c>PragmaticLoggerProviderBase</c> does today: it redacts <c>LogEntry.Properties</c> and leaves
///     <c>LogEntry.Message</c>, already produced by <c>formatter(state, exception)</c>, untouched.
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

    private string Render()
    {
        var template = FindTemplate();
        if (template is null)
            return string.Join(" ", _values.Select(v => $"{v.Key}={v.Value}"));

        var result = new StringBuilder(template.Length + 32);

        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];

            // "{{" and "}}" are escapes for a literal brace, exactly as in a composite format string.
            if (c is '{' or '}' && i + 1 < template.Length && template[i + 1] == c)
            {
                result.Append(c);
                i++;
                continue;
            }

            if (c != '{')
            {
                result.Append(c);
                continue;
            }

            var close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                // Unbalanced: emit the rest verbatim rather than guess where the hole ends.
                result.Append(template, i, template.Length - i);
                break;
            }

            var hole = template.Substring(i + 1, close - i - 1);
            result.Append(Format(hole));
            i = close;
        }

        return result.ToString();
    }

    /// <summary>Renders one hole, honouring the alignment and format specifiers the BCL accepts.</summary>
    private string Format(string hole)
    {
        var name = hole;
        string? format = null;

        var colon = hole.IndexOf(':');
        if (colon >= 0)
        {
            format = hole.Substring(colon + 1);
            name = hole.Substring(0, colon);
        }

        var comma = name.IndexOf(',');
        var alignment = 0;
        if (comma >= 0)
        {
            _ = int.TryParse(name.Substring(comma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out alignment);
            name = name.Substring(0, comma);
        }

        // Serilog-style destructuring prefixes name the same value.
        if (name.Length > 0 && name[0] is '@' or '$')
            name = name.Substring(1);

        var value = Lookup(name);
        var text = value switch
        {
            null => "(null)",
            IFormattable formattable when format is not null =>
                formattable.ToString(format, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        if (alignment == 0)
            return text;

        return alignment < 0 ? text.PadRight(-alignment) : text.PadLeft(alignment);
    }

    private object? Lookup(string name)
    {
        foreach (var value in _values)
            if (string.Equals(value.Key, name, StringComparison.Ordinal))
                return value.Value;

        // A hole with no matching value: keep the placeholder rather than print an empty string, so a
        // template and its arguments drifting apart stays visible in the output.
        return "{" + name + "}";
    }

    private string? FindTemplate()
    {
        foreach (var value in _values)
            if (string.Equals(value.Key, OriginalFormatKey, StringComparison.Ordinal))
                return value.Value as string;

        return null;
    }
}

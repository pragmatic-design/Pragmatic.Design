using System.Collections;
using System.Globalization;
using System.Text;

namespace Pragmatic.Redaction;

/// <summary>
///     Renders a log message template from its values the way <c>Microsoft.Extensions.Logging</c>'s
///     formatter does: a <c>null</c> is <c>(null)</c>, an enumerable that is not a string is its items
///     joined with <c>", "</c>, and the whole is formatted with the invariant culture.
/// </summary>
/// <remarks>
///     <para>
///         For a message that has to be rendered again after a value was masked. The caller's formatter
///         closes over the original state, so calling it would put the clear value back into the text
///         while the structured property beside it was masked.
///     </para>
///     <para>
///         One renderer for both redaction paths: declared redaction (<see cref="DeclaredRedactor.RedactState" />)
///         and Pragmatic.Logging's redaction by property name, so the two cannot drift into formatting the
///         same value differently.
///     </para>
/// </remarks>
public static class LogMessageTemplate
{
    private const string Null = "(null)";

    /// <summary>
    ///     Renders <paramref name="template" /> with <paramref name="arguments" /> by position, as MEL does:
    ///     the n-th hole takes the n-th argument whatever it is called, so a template that repeats a name
    ///     takes each argument in turn.
    /// </summary>
    public static string Render(string template, IReadOnlyList<object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(arguments);

        var composite = ToCompositeFormat(template, names: null, out var holes);
        if (holes == 0)
            return composite;

        var formatted = new object?[holes];
        for (var i = 0; i < holes; i++)
            formatted[i] = i < arguments.Count ? FormatArgument(arguments[i]) : null;

        return string.Format(CultureInfo.InvariantCulture, composite, formatted);
    }

    /// <summary>
    ///     Renders <paramref name="template" /> with each hole taking the value of the same name in
    ///     <paramref name="values" />; a name with no value is <c>(null)</c>.
    /// </summary>
    /// <remarks>
    ///     For an entry whose values were already lifted into a dictionary, where their order is gone. A
    ///     template that repeats a name renders the one value under it each time, where MEL would take the
    ///     next argument: the dictionary only ever held one.
    /// </remarks>
    public static string Render(string template, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        var names = new List<string>();
        var composite = ToCompositeFormat(template, names, out var holes);
        if (holes == 0)
            return composite;

        var formatted = new object?[holes];
        for (var i = 0; i < holes; i++)
            formatted[i] = values.TryGetValue(names[i], out var value) ? FormatArgument(value) : Null;

        return string.Format(CultureInfo.InvariantCulture, composite, formatted);
    }

    /// <summary>
    ///     Turns a message template into a composite format string, each named hole replaced by its
    ///     position: <c>"{Who} paid {Amount,8:F2}"</c> becomes <c>"{0} paid {1,8:F2}"</c>. The names, in
    ///     order, go into <paramref name="names" /> when it is given.
    /// </summary>
    /// <remarks>Escaped braces are copied as they are, since a composite format escapes them the same way.</remarks>
    private static string ToCompositeFormat(string template, List<string>? names, out int holes)
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

            names?.Add(template.Substring(open + 1, delimiter - open - 1));
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
}

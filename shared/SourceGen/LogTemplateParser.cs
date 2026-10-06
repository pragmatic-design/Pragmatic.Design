using System.Collections.Generic;
using System.Text;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     Splits a log message template into literal text and placeholders, the way
///     <c>Microsoft.Extensions.Logging</c> reads one.
/// </summary>
/// <remarks>
///     Shared by the generator, which writes the message from the segments, and by the analyzer, which
///     reports a malformed template where it was written. Two parsers would be two answers to whether a
///     template is valid.
/// </remarks>
internal static class LogTemplateParser
{
    /// <summary>The segments of <paramref name="template" />, or null when it is malformed.</summary>
    /// <param name="template">The template.</param>
    /// <param name="error">Why it is malformed, when it is.</param>
    public static List<LogTemplateSegment>? Parse(string template, out string? error)
    {
        var segments = new List<LogTemplateSegment>();
        var literal = new StringBuilder();
        error = null;

        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];

            if (c == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    literal.Append('{');
                    i++;
                    continue;
                }

                var close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    error = $"the placeholder at position {i} is never closed";
                    return null;
                }

                var hole = template.Substring(i + 1, close - i - 1);
                if (!TryReadHole(hole, out var segment))
                {
                    error = $"'{{{hole}}}' is not a placeholder name";
                    return null;
                }

                if (literal.Length > 0)
                {
                    segments.Add(new LogTemplateSegment(literal.ToString(), false, null, null));
                    literal.Clear();
                }

                segments.Add(segment);
                i = close;
                continue;
            }

            if (c == '}')
            {
                if (i + 1 < template.Length && template[i + 1] == '}')
                {
                    literal.Append('}');
                    i++;
                    continue;
                }

                error = $"the '}}' at position {i} closes nothing; write '}}}}' for a literal brace";
                return null;
            }

            literal.Append(c);
        }

        if (literal.Length > 0)
            segments.Add(new LogTemplateSegment(literal.ToString(), false, null, null));

        return segments;
    }

    private static bool TryReadHole(string hole, out LogTemplateSegment segment)
    {
        segment = null!;

        var formatStart = hole.IndexOf(':');
        var head = formatStart < 0 ? hole : hole.Substring(0, formatStart);
        var format = formatStart < 0 ? null : hole.Substring(formatStart + 1);

        var alignmentStart = head.IndexOf(',');
        var name = (alignmentStart < 0 ? head : head.Substring(0, alignmentStart)).Trim();
        var alignment = alignmentStart < 0 ? null : head.Substring(alignmentStart + 1).Trim();

        if (name.Length == 0 || name.IndexOf('{') >= 0)
            return false;

        segment = new LogTemplateSegment(name, true, format, alignment);
        return true;
    }
}

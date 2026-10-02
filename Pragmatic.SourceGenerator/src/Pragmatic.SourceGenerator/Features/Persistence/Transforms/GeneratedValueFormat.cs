using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     The kind of a parsed <c>[GeneratedValue]</c> format segment.
/// </summary>
internal enum GeneratedValueTokenKind
{
    /// <summary>A verbatim literal (everything outside a recognised token).</summary>
    Literal,

    /// <summary><c>{YYYY}</c> — 4-digit year.</summary>
    Year4,

    /// <summary><c>{YY}</c> — 2-digit year.</summary>
    Year2,

    /// <summary><c>{MM}</c> — 2-digit month.</summary>
    Month,

    /// <summary><c>{DD}</c> — 2-digit day.</summary>
    Day,

    /// <summary><c>{SEQ:N}</c> — zero-padded database sequence value of width <c>Length</c>.</summary>
    Sequence,

    /// <summary><c>{RANDOM:N}</c> — <c>Length</c> random alphanumeric characters.</summary>
    Random,

    /// <summary><c>{GUID:N}</c> — first <c>Length</c> characters of a GUID (no dashes).</summary>
    Guid
}

/// <summary>
///     One parsed segment of an <c>[GeneratedValue]</c> format string.
/// </summary>
internal readonly record struct GeneratedValueSegment(GeneratedValueTokenKind Kind, string Literal, int Length);

/// <summary>
///     Parses an <c>[GeneratedValue]</c> format template (e.g. <c>"INV-{YYYY}{MM}-{SEQ:5}"</c>) into an
///     ordered list of segments the source generator turns into explicit, reflection-free formatting code.
/// </summary>
/// <remarks>
///     Unrecognised <c>{...}</c> groups are preserved verbatim as literals rather than throwing, so a typo
///     degrades to a visible constant instead of a build break. Supported tokens mirror the attribute's
///     XML doc: <c>{YYYY}</c>, <c>{YY}</c>, <c>{MM}</c>, <c>{DD}</c>, <c>{SEQ:N}</c>, <c>{RANDOM:N}</c>, <c>{GUID:N}</c>.
/// </remarks>
internal static class GeneratedValueFormat
{
    public static ImmutableArray<GeneratedValueSegment> Parse(string format)
    {
        if (string.IsNullOrEmpty(format))
            return ImmutableArray<GeneratedValueSegment>.Empty;

        var segments = ImmutableArray.CreateBuilder<GeneratedValueSegment>();
        var literal = new StringBuilder();
        var i = 0;

        while (i < format.Length)
        {
            if (format[i] == '{')
            {
                var close = format.IndexOf('}', i);
                if (close > i)
                {
                    var token = format.Substring(i + 1, close - i - 1);
                    if (TryParseToken(token, out var segment))
                    {
                        FlushLiteral(segments, literal);
                        segments.Add(segment);
                        i = close + 1;
                        continue;
                    }
                }
            }

            literal.Append(format[i]);
            i++;
        }

        FlushLiteral(segments, literal);
        return segments.ToImmutable();
    }

    /// <summary>True when any segment draws from a database sequence (requires a sequence to exist).</summary>
    public static bool HasSequence(ImmutableArray<GeneratedValueSegment> segments)
        => segments.Any(s => s.Kind == GeneratedValueTokenKind.Sequence);

    private static void FlushLiteral(ImmutableArray<GeneratedValueSegment>.Builder segments, StringBuilder literal)
    {
        if (literal.Length == 0)
            return;

        segments.Add(new GeneratedValueSegment(GeneratedValueTokenKind.Literal, literal.ToString(), 0));
        literal.Clear();
    }

    private static bool TryParseToken(string token, out GeneratedValueSegment segment)
    {
        switch (token)
        {
            case "YYYY": segment = new GeneratedValueSegment(GeneratedValueTokenKind.Year4, string.Empty, 0); return true;
            case "YY": segment = new GeneratedValueSegment(GeneratedValueTokenKind.Year2, string.Empty, 0); return true;
            case "MM": segment = new GeneratedValueSegment(GeneratedValueTokenKind.Month, string.Empty, 0); return true;
            case "DD": segment = new GeneratedValueSegment(GeneratedValueTokenKind.Day, string.Empty, 0); return true;
        }

        // Width-parameterised tokens: {SEQ:N}, {RANDOM:N}, {GUID:N}.
        var colon = token.IndexOf(':');
        if (colon > 0 && int.TryParse(token.Substring(colon + 1), out var width) && width > 0)
        {
            switch (token.Substring(0, colon))
            {
                case "SEQ": segment = new GeneratedValueSegment(GeneratedValueTokenKind.Sequence, string.Empty, width); return true;
                case "RANDOM": segment = new GeneratedValueSegment(GeneratedValueTokenKind.Random, string.Empty, width); return true;
                case "GUID": segment = new GeneratedValueSegment(GeneratedValueTokenKind.Guid, string.Empty, width); return true;
            }
        }

        segment = default;
        return false;
    }
}

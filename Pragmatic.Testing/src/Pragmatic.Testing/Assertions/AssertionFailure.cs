using System.Collections;
using System.Globalization;

namespace Pragmatic.Testing.Assertions;

/// <summary>
///     Builds the message an assertion fails with, and throws it.
/// </summary>
/// <remarks>
///     One place decides how a failure reads, so every assertion says the same kind of thing:
///     what was expected, of what, and what was there instead. The subject's name comes from
///     <c>CallerArgumentExpression</c> on <c>Should()</c> — the compiler knows the caller wrote
///     <c>result.Value</c>, which is more reliable than recovering it from a stack trace.
/// </remarks>
public static class AssertionFailure
{
    /// <summary>Throws with "Expected {subject} {expectation}{because}, but {actual}."</summary>
    /// <param name="subject">The expression under test, or null when the caller did not name it.</param>
    /// <param name="expectation">Reads on from the subject: <c>"to be 42"</c>.</param>
    /// <param name="actual">Reads on from "but": <c>"found 43"</c>.</param>
    /// <param name="because">The caller's reason, with or without a leading "because".</param>
    /// <param name="becauseArgs">Format arguments for <paramref name="because"/>.</param>
    /// <exception cref="PragmaticTestAssertionException">Always.</exception>
    public static void Throw(
        string? subject,
        string expectation,
        string actual,
        string? because = null,
        params object[] becauseArgs)
    {
        var name = string.IsNullOrWhiteSpace(subject) ? "value" : subject;
        throw new PragmaticTestAssertionException(
            $"Expected {name} {expectation}{Reason(because, becauseArgs)}, but {actual}.");
    }

    /// <summary>
    ///     Renders a value the way a failure message should read: quoted strings, a spelled-out null,
    ///     and a collection shown by its elements rather than by its type name.
    /// </summary>
    public static string Format(object? value) => value switch
    {
        null => "<null>",
        string text => $"\"{text}\"",
        bool flag => flag ? "True" : "False",
        // A collection printed as its type name says nothing about why the test failed.
        IEnumerable items and not string => "{" + string.Join(", ", Take(items, 10)) + "}",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "<null>"
    };

    /// <summary>
    ///     The caller's reason, normalised. <c>"because it is cached"</c> and <c>"it is cached"</c>
    ///     both read the same way, which is how the callers in this repository write them.
    /// </summary>
    private static string Reason(string? because, object[] args)
    {
        if (string.IsNullOrWhiteSpace(because))
            return string.Empty;

        var text = args.Length > 0
            ? string.Format(CultureInfo.InvariantCulture, because, args)
            : because;

        return text.TrimStart().StartsWith("because", StringComparison.OrdinalIgnoreCase)
            ? " " + text.Trim()
            : " because " + text.Trim();
    }

    /// <summary>The first <paramref name="limit"/> elements, formatted; long collections are elided.</summary>
    private static IEnumerable<string> Take(IEnumerable items, int limit)
    {
        var count = 0;
        foreach (var item in items)
        {
            if (count++ == limit)
            {
                yield return "…";
                yield break;
            }

            yield return Format(item);
        }
    }
}

namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Shared string parsing over provider error messages, used by
///     <see cref="HeuristicDbExceptionParser" />.
/// </summary>
internal static class SqlMessageParsing
{
    /// <summary>
    ///     Extracts the text between <paramref name="prefix"/> and the next <paramref name="endQuote"/>.
    ///     Returns null when the prefix is absent or the quote never closes.
    /// </summary>
    public static string? ExtractQuotedValue(string message, string prefix, char endQuote = '\'')
    {
        var start = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += prefix.Length;
        var end = message.IndexOf(endQuote, start);
        return end > start ? message[start..end] : null;
    }

    /// <summary>
    ///     Extracts a constraint name from an error message: first via the <c>constraint "…"</c>
    ///     keyword patterns, then by locating a quoted identifier with a conventional prefix
    ///     (<c>FK_</c>/<c>IX_</c>) — KEEPING the prefix, which is part of the constraint name.
    /// </summary>
    public static string? ExtractConstraintName(string message)
    {
        var byKeyword = ExtractQuotedValue(message, "constraint \"", '"')
                        ?? ExtractQuotedValue(message, "constraint '");
        if (byKeyword is not null)
            return byKeyword;

        foreach (var prefix in (ReadOnlySpan<string>)["FK_", "IX_"])
        {
            var idx = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            while (idx > 0)
            {
                var quote = message[idx - 1];
                if (quote is '"' or '\'')
                {
                    var end = message.IndexOf(quote, idx);
                    if (end > idx)
                        return message[idx..end];
                }

                idx = message.IndexOf(prefix, idx + 1, StringComparison.OrdinalIgnoreCase);
            }
        }

        return null;
    }
}

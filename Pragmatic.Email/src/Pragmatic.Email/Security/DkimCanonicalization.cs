using System.Text;
using System.Text.RegularExpressions;

namespace Pragmatic.Email.Security;

/// <summary>
///     DKIM canonicalization algorithms per RFC 6376 §3.4.
///     Supports "relaxed" canonicalization for both headers and body.
/// </summary>
internal static partial class DkimCanonicalization
{
    /// <summary>
    ///     Relaxed header canonicalization: lowercase name, unfold, compress whitespace, trim.
    /// </summary>
    public static string CanonicalizeHeaderRelaxed(string name, string value)
    {
        var canonName = name.ToLowerInvariant();
        // RFC 6376 §3.4.2: header unfolding replaces CRLF+WSP with a single SP.
        // Collapsing "\r\n" without preserving the following whitespace character produces
        // "foo:bar" instead of "foo: bar" — incorrect DKIM signatures.
        var canonValue = value.Replace("\r\n\t", " ").Replace("\r\n ", " "); // Unfold
        canonValue = WhitespaceRegex().Replace(canonValue, " "); // Compress
        canonValue = canonValue.Trim();
        return $"{canonName}:{canonValue}";
    }

    /// <summary>
    ///     Relaxed body canonicalization: strip trailing whitespace per line,
    ///     compress whitespace, remove trailing empty lines.
    /// </summary>
    public static string CanonicalizeBodyRelaxed(string body)
    {
        var lines = body.Split("\r\n");
        var sb = new StringBuilder(body.Length);

        foreach (var line in lines)
        {
            var trimmed = TrailingWsRegex().Replace(line, "");
            trimmed = WhitespaceRegex().Replace(trimmed, " ");
            sb.Append(trimmed).Append("\r\n");
        }

        // Remove trailing empty lines (but keep one final CRLF)
        var result = sb.ToString();
        while (result.EndsWith("\r\n\r\n", StringComparison.Ordinal))
            result = result[..^2];

        return result;
    }

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[ \t]+$")]
    private static partial Regex TrailingWsRegex();
}

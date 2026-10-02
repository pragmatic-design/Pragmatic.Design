using System.Text;

namespace Pragmatic.Email.Smtp;

/// <summary>
///     SMTP transparency ("dot stuffing") per RFC 5321 §4.5.2.
/// </summary>
/// <remarks>
///     <para>
///         Inside a DATA block a line consisting solely of <c>"."</c> terminates the message. Any line
///         of the payload that begins with <c>"."</c> must therefore be sent with an extra leading dot,
///         which the receiving server strips again.
///     </para>
///     <para>
///         This is a <b>transport</b> concern, not a MIME one: the stuffing must cover the whole DATA
///         block — including its very first line — and must not be part of the message that gets signed
///         (a DKIM/S-MIME verifier sees the payload after the server has removed the stuffing).
///     </para>
/// </remarks>
internal static class SmtpDotStuffing
{
    /// <summary>
    ///     Returns <paramref name="data"/> with every line that starts with '.' prefixed by a second '.'.
    ///     Line endings are normalised to CRLF first, so a payload carrying bare LF (or bare CR) is
    ///     protected too — a lone LF would not otherwise be recognised as a line start by the naive
    ///     "CRLF followed by dot" scan.
    /// </summary>
    public static string Apply(string data)
    {
        if (data.Length == 0)
            return data;

        var normalized = NormalizeLineEndings(data);

        // The first line has no preceding CRLF, so it needs an explicit check: this is exactly the
        // case that lets a body beginning with "." close the DATA block early and turn the remainder
        // into SMTP commands on an authenticated session.
        var needsLeadingDot = normalized[0] == '.';

        if (!needsLeadingDot && !normalized.Contains("\r\n.", StringComparison.Ordinal))
            return normalized;

        var stuffed = normalized.Replace("\r\n.", "\r\n..", StringComparison.Ordinal);
        return needsLeadingDot ? "." + stuffed : stuffed;
    }

    /// <summary>Normalises CRLF / bare LF / bare CR line endings to CRLF.</summary>
    private static string NormalizeLineEndings(string value)
    {
        if (value.IndexOf('\r') < 0 && value.IndexOf('\n') < 0)
            return value;

        var sb = new StringBuilder(value.Length + 16);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\r')
            {
                sb.Append("\r\n");
                if (i + 1 < value.Length && value[i + 1] == '\n')
                    i++; // CRLF consumed as one
            }
            else if (c == '\n')
            {
                sb.Append("\r\n");
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

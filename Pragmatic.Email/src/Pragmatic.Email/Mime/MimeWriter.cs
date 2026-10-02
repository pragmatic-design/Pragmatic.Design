using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.Email.Mime;

/// <summary>
///     Generates RFC 2045/2046 compliant MIME messages from <see cref="EmailMessage"/>.
///     Supports multipart/mixed (attachments), multipart/alternative (text+HTML),
///     multipart/related (inline images), and base64 binary encoding.
/// </summary>
/// <remarks>
///     Public because rendering is needed outside the library: a custom <see cref="Transport.IEmailTransport"/>
///     has to produce the wire format itself, and <see cref="Security.DkimSigner"/> signs a rendered message.
/// </remarks>
public static class MimeWriter
{
    private const int Base64LineLength = 76;

    /// <summary>
    ///     Renders a complete MIME message — headers, blank separator line, body — ready for SMTP DATA.
    /// </summary>
    /// <remarks>
    ///     Deterministic: the same <paramref name="message"/> always renders to the same string, which is
    ///     what allows a signature computed over one render to remain valid for the render the transport
    ///     sends. SMTP dot-stuffing is applied later by the transport and is not part of this output.
    /// </remarks>
    public static string Write(EmailMessage message)
    {
        var sb = new StringBuilder(2048);

        WriteHeaders(sb, message);
        WriteBody(sb, message);

        return sb.ToString();
    }

    private static void WriteHeaders(StringBuilder sb, EmailMessage message)
    {
        sb.Append("From: ").Append(FormatAddress(message.From)).Append("\r\n");

        sb.Append("To: ").Append(string.Join(", ", message.To.Select(FormatAddress))).Append("\r\n");

        if (message.Cc.Count > 0)
            sb.Append("Cc: ").Append(string.Join(", ", message.Cc.Select(FormatAddress))).Append("\r\n");

        if (message.ReplyTo is { } replyTo)
            sb.Append("Reply-To: ").Append(FormatAddress(replyTo)).Append("\r\n");

        sb.Append("Subject: ").Append(EncodeHeaderValue(message.Subject)).Append("\r\n");

        // "R" formats as RFC 1123 and always writes "GMT" without converting, so a Date carrying a
        // non-zero offset would be transmitted as the wrong instant. Convert first.
        sb.Append("Date: ").Append(message.Date.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture)).Append("\r\n");

        sb.Append("Message-ID: <").Append(message.MessageId).Append(">\r\n");
        sb.Append("MIME-Version: 1.0\r\n");

        foreach (var (name, value) in message.Headers)
        {
            if (!IsValidHeaderName(name))
                throw new ArgumentException(
                    $"Invalid MIME header name '{name}'. Header names must be printable ASCII without ':'.",
                    nameof(message));

            // A custom header may not restate one the writer already emitted, or define the structural
            // ones it is about to emit. Allowing "Bcc" would add hidden recipients that never appear in
            // the message model — and never reach RCPT TO — while a second Content-Type would break the
            // MIME structure outright.
            if (ReservedHeaders.Contains(name))
                throw new ArgumentException(
                    $"Header '{name}' is controlled by the message itself and cannot be set through "
                    + "Headers. Use the corresponding EmailMessage property.",
                    nameof(message));

            sb.Append(name).Append(": ").Append(EncodeHeaderValue(value)).Append("\r\n");
        }
    }

    /// <summary>
    ///     Headers the writer emits itself, or that define the MIME structure, and which therefore
    ///     cannot be supplied through <see cref="EmailMessage.Headers"/>.
    /// </summary>
    private static readonly FrozenSet<string> ReservedHeaders = new[]
    {
        "From", "To", "Cc", "Bcc", "Reply-To", "Subject", "Date", "Message-ID", "MIME-Version",
        "Content-Type", "Content-Transfer-Encoding", "Content-Disposition", "Content-ID",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Validates an RFC 5322 header field name: non-empty, printable ASCII (33-126),
    ///     excluding the ':' separator. Prevents header injection via crafted header names.
    /// </summary>
    private static bool IsValidHeaderName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var c in name)
        {
            if (c < 33 || c > 126 || c == ':')
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Strips CR/LF and double-quote characters from a value used inside a quoted
    ///     header parameter (e.g. attachment <c>filename</c>). Prevents header injection
    ///     and quoted-string breakout from caller-supplied file names.
    /// </summary>
    private static string SanitizeParameterValue(string value)
        => value.Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Replace("\"", "'");

    /// <summary>
    ///     Renders only the content block of a message — content headers, blank line, body — without the
    ///     message headers (From/To/Subject/…).
    /// </summary>
    /// <remarks>
    ///     This is the unit an S/MIME detached signature covers: RFC 8551 signs the content part exactly
    ///     as it appears inside the <c>multipart/signed</c>, not the whole message.
    /// </remarks>
    internal static string RenderContent(EmailMessage message)
    {
        var sb = new StringBuilder(1024);
        WriteBody(sb, message);
        return sb.ToString();
    }

    private static void WriteBody(StringBuilder sb, EmailMessage message)
    {
        // A pre-rendered block (S/MIME multipart/signed) already carries its own content headers and
        // must be emitted byte-for-byte: re-deriving it would break the signature it contains.
        if (message.PreRenderedContent is { } preRendered)
        {
            sb.Append(preRendered);
            return;
        }

        // Materialize Attachments once into the two disjoint buckets so that an
        // `IEnumerable<>` backed by a deferred query (LINQ, generator) is not
        // re-evaluated four times across the method + WriteContentParts.
        var normalAttachments = new List<EmailAttachment>();
        var inlineAttachments = new List<EmailAttachment>();
        foreach (var att in message.Attachments)
        {
            // An inline attachment can only live inside a multipart/related next to an HTML body that
            // references it via cid:. With no HTML body there is nowhere to put it, so it is demoted
            // to a regular attachment: dropping it would send the message without it, with no error
            // and no log.
            var isInline = att.IsInline && message.HtmlBody is not null;
            (isInline ? inlineAttachments : normalAttachments).Add(att);
        }

        var hasAttachments = normalAttachments.Count > 0;
        var hasInlineImages = inlineAttachments.Count > 0;
        var hasAlternative = message.TextBody is not null && message.HtmlBody is not null;

        if (hasAttachments)
        {
            // multipart/mixed → content + attachments
            var mixedBoundary = GenerateBoundary(message, "mixed");
            sb.Append($"Content-Type: multipart/mixed; boundary=\"{mixedBoundary}\"\r\n\r\n");

            sb.Append($"--{mixedBoundary}\r\n");
            WriteContentParts(sb, message, inlineAttachments, hasAlternative);

            foreach (var attachment in normalAttachments)
            {
                sb.Append($"--{mixedBoundary}\r\n");
                WriteAttachment(sb, attachment);
            }

            sb.Append($"--{mixedBoundary}--\r\n");
        }
        else
        {
            WriteContentParts(sb, message, inlineAttachments, hasAlternative);
        }
    }

    private static void WriteContentParts(StringBuilder sb, EmailMessage message, IReadOnlyList<EmailAttachment> inlineAttachments, bool hasAlternative)
    {
        if (inlineAttachments.Count > 0 && message.HtmlBody is not null)
        {
            // multipart/related → alternative + inline images
            var relatedBoundary = GenerateBoundary(message, "related");
            sb.Append($"Content-Type: multipart/related; boundary=\"{relatedBoundary}\"\r\n\r\n");

            sb.Append($"--{relatedBoundary}\r\n");
            WriteTextParts(sb, message, hasAlternative);

            foreach (var inline in inlineAttachments)
            {
                sb.Append($"--{relatedBoundary}\r\n");
                WriteInlineImage(sb, inline);
            }

            sb.Append($"--{relatedBoundary}--\r\n");
        }
        else
        {
            WriteTextParts(sb, message, hasAlternative);
        }
    }

    private static void WriteTextParts(StringBuilder sb, EmailMessage message, bool hasAlternative)
    {
        if (hasAlternative)
        {
            var altBoundary = GenerateBoundary(message, "alt");
            sb.Append($"Content-Type: multipart/alternative; boundary=\"{altBoundary}\"\r\n\r\n");

            sb.Append($"--{altBoundary}\r\n");
            WriteTextPart(sb, message.TextBody!);

            sb.Append($"--{altBoundary}\r\n");
            WriteHtmlPart(sb, message.HtmlBody!);

            sb.Append($"--{altBoundary}--\r\n");
        }
        else if (message.HtmlBody is not null)
        {
            WriteHtmlPart(sb, message.HtmlBody);
        }
        else if (message.TextBody is not null)
        {
            WriteTextPart(sb, message.TextBody);
        }
    }

    private static void WriteTextPart(StringBuilder sb, string text)
    {
        sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
        sb.Append("Content-Transfer-Encoding: 8bit\r\n\r\n");
        sb.Append(NormalizeLineEndings(text)).Append("\r\n");
    }

    private static void WriteHtmlPart(StringBuilder sb, string html)
    {
        sb.Append("Content-Type: text/html; charset=utf-8\r\n");
        sb.Append("Content-Transfer-Encoding: 8bit\r\n\r\n");
        sb.Append(NormalizeLineEndings(html)).Append("\r\n");
    }

    private static void WriteAttachment(StringBuilder sb, EmailAttachment attachment)
    {
        var contentType = SanitizeParameterValue(attachment.ContentType);
        var fileName = SanitizeParameterValue(attachment.FileName);

        sb.Append($"Content-Type: {contentType}; name=\"{fileName}\"\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n");
        sb.Append($"Content-Disposition: attachment; filename=\"{fileName}\"\r\n\r\n");
        WriteBase64(sb, attachment.Data.Span);
        sb.Append("\r\n");
    }

    private static void WriteInlineImage(StringBuilder sb, EmailAttachment inline)
    {
        var contentType = SanitizeParameterValue(inline.ContentType);
        var fileName = SanitizeParameterValue(inline.FileName);
        var contentId = SanitizeParameterValue(inline.ContentId ?? string.Empty);

        sb.Append($"Content-Type: {contentType}; name=\"{fileName}\"\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n");
        sb.Append($"Content-Disposition: inline; filename=\"{fileName}\"\r\n");
        sb.Append($"Content-ID: <{contentId}>\r\n\r\n");
        WriteBase64(sb, inline.Data.Span);
        sb.Append("\r\n");
    }

    private static void WriteBase64(StringBuilder sb, ReadOnlySpan<byte> data)
    {
        var base64 = Convert.ToBase64String(data);
        for (var i = 0; i < base64.Length; i += Base64LineLength)
        {
            var length = Math.Min(Base64LineLength, base64.Length - i);
            sb.Append(base64.AsSpan(i, length));
            sb.Append("\r\n");
        }
    }

    internal static string FormatAddress(EmailAddress address)
    {
        // Strip control characters (CR/LF in particular) from the address before it is written into the
        // MIME header. EmailAddress.Validated rejects them, but the implicit string→EmailAddress operator
        // bypasses Validated, so an address built that way could otherwise inject headers (e.g. Bcc:) into
        // the DATA section. The DisplayName is already neutralized by EncodeHeaderValue.
        var addr = StripControlChars(address.Address);

        if (address.DisplayName is null)
            return addr;

        return $"{EncodeHeaderValue(address.DisplayName)} <{addr}>";
    }

    private static string StripControlChars(string value)
    {
        var hasControl = false;
        foreach (var c in value)
        {
            if (c < ' ')
            {
                hasControl = true;
                break;
            }
        }

        if (!hasControl)
            return value;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c >= ' ')
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    ///     RFC 2047 B-encoding for header values that contain non-ASCII characters
    ///     <em>or</em> control characters. Encoding on control characters (CR/LF in
    ///     particular) neutralizes header injection: the encoded form is pure base64
    ///     and cannot break out of the header line.
    /// </summary>
    internal static string EncodeHeaderValue(string value)
    {
        foreach (var c in value)
        {
            if (c > 127 || c < 32)
                return $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";
        }

        return value;
    }

    /// <summary>
    ///     Normalises body line endings to CRLF as required for MIME bodies on the wire.
    /// </summary>
    /// <remarks>
    ///     SMTP dot-stuffing is deliberately NOT done here: it is a transport-level encoding
    ///     (RFC 5321 §4.5.2) that must cover the whole DATA block including its first line, and it
    ///     must not be part of the signed message. See <see cref="Smtp.SmtpDotStuffing"/>.
    /// </remarks>
    internal static string NormalizeLineEndings(string body)
    {
        if (body.IndexOf('\r') < 0 && body.IndexOf('\n') < 0)
            return body;

        var sb = new StringBuilder(body.Length + 16);
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '\r')
            {
                sb.Append("\r\n");
                if (i + 1 < body.Length && body[i + 1] == '\n')
                    i++;
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

    /// <summary>
    ///     Derives the MIME boundary for <paramref name="tag"/> deterministically from the message.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Determinism is a correctness requirement, not an optimisation.</b> A message is rendered
    ///         more than once — once by each signing middleware to compute what it signs, and once by the
    ///         transport to produce what is actually sent. A random boundary per call made those renders
    ///         differ, so every signature covered a document that was never transmitted and no DKIM
    ///         signature on a multipart message could ever verify.
    ///     </para>
    ///     <para>
    ///         The boundary is the truncated SHA-256 of the message id and the tag. Uniqueness across
    ///         messages therefore rests on the message id, which defaults to a random GUID v7; a caller
    ///         setting predictable ids of its own gets predictable boundaries, which is why the value is
    ///         additionally checked against the payload below.
    ///     </para>
    /// </remarks>
    internal static string GenerateBoundary(EmailMessage message, string tag)
    {
        for (var attempt = 0; ; attempt++)
        {
            var seed = attempt == 0
                ? $"{message.MessageId}|{tag}"
                : $"{message.MessageId}|{tag}|{attempt.ToString(CultureInfo.InvariantCulture)}";

            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
            var boundary = $"----=_Pragmatic_{tag}_{Convert.ToHexString(digest)[..24]}";

            // A boundary that occurs inside the payload would split the message at the wrong place.
            // Astronomically unlikely, but the recovery must stay deterministic: re-derive with a
            // counter rather than fall back to randomness.
            if (!OccursInPayload(message, boundary))
                return boundary;
        }
    }

    private static bool OccursInPayload(EmailMessage message, string boundary)
    {
        if (message.TextBody is not null && message.TextBody.Contains(boundary, StringComparison.Ordinal))
            return true;

        if (message.HtmlBody is not null && message.HtmlBody.Contains(boundary, StringComparison.Ordinal))
            return true;

        foreach (var attachment in message.Attachments)
        {
            if (attachment.FileName.Contains(boundary, StringComparison.Ordinal)
                || attachment.ContentType.Contains(boundary, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

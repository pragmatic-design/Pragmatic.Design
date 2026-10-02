using System.Text;
using Microsoft.Extensions.Options;
using Pragmatic.Email.Middleware;
using Pragmatic.Email.Mime;

namespace Pragmatic.Email.Security;

/// <summary>
///     Middleware that applies an S/MIME detached signature, producing a
///     <c>multipart/signed</c> message per RFC 8551 §3.4.3.
/// </summary>
/// <remarks>
///     <para>
///         Runs at Order = 50, <b>before</b> <see cref="DkimMiddleware"/> (Order = 100). S/MIME rewrites
///         the body — it wraps the original content next to the signature part — so it has to happen
///         before DKIM, which signs the final message. Signing in the other order would leave a DKIM
///         signature covering a body that no longer exists.
///     </para>
/// </remarks>
internal sealed class SmimeMiddleware(IOptions<SmimeOptions> options) : IEmailMiddleware
{
    private const string SignatureFileName = "smime.p7s";

    private readonly SmimeSigner _signer = new(options.Value);

    public int Order => 50;

    public Task<EmailMessage> ProcessAsync(
        EmailMessage message,
        Func<EmailMessage, Task<EmailMessage>> next,
        CancellationToken ct)
    {
        // The signature must cover exactly what a receiving client extracts as the content part.
        // Per RFC 2046 §5.1.1 the CRLF in front of a boundary delimiter belongs to the delimiter, not
        // to the preceding part — so it is excluded from what gets signed, otherwise every verifier
        // would hash two bytes less than we did and reject the signature.
        var rendered = MimeWriter.RenderContent(message);
        var content = rendered.EndsWith("\r\n", StringComparison.Ordinal) ? rendered[..^2] : rendered;
        var signature = _signer.Sign(content);

        var boundary = MimeWriter.GenerateBoundary(message, "signed");

        var sb = new StringBuilder(content.Length + signature.Length * 2 + 512);
        sb.Append("Content-Type: multipart/signed; ")
          .Append("protocol=\"application/pkcs7-signature\"; ")
          .Append("micalg=sha-256; ")
          .Append($"boundary=\"{boundary}\"\r\n\r\n");

        sb.Append($"--{boundary}\r\n");
        sb.Append(content).Append("\r\n");

        sb.Append($"--{boundary}\r\n");
        sb.Append($"Content-Type: application/pkcs7-signature; name=\"{SignatureFileName}\"\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n");
        sb.Append($"Content-Disposition: attachment; filename=\"{SignatureFileName}\"\r\n\r\n");
        AppendBase64(sb, signature);
        sb.Append($"--{boundary}--\r\n");

        var signed = message with { PreRenderedContent = sb.ToString() };
        return next(signed);
    }

    private static void AppendBase64(StringBuilder sb, byte[] data)
    {
        const int lineLength = 76;
        var base64 = Convert.ToBase64String(data);

        for (var i = 0; i < base64.Length; i += lineLength)
        {
            var length = Math.Min(lineLength, base64.Length - i);
            sb.Append(base64.AsSpan(i, length)).Append("\r\n");
        }
    }
}

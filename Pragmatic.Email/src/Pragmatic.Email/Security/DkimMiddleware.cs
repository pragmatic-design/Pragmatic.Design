using Microsoft.Extensions.Options;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Middleware;
using Pragmatic.Email.Mime;

namespace Pragmatic.Email.Security;

/// <summary>
///     Middleware that adds DKIM-Signature header to outgoing emails.
///     Runs late (Order=100) — after all content transformations.
/// </summary>
internal sealed class DkimMiddleware(IOptions<DkimOptions> options) : IEmailMiddleware
{
    private readonly DkimSigner _signer = new(options.Value);

    public int Order => 100;

    public Task<EmailMessage> ProcessAsync(
        EmailMessage message,
        Func<EmailMessage, Task<EmailMessage>> next,
        CancellationToken ct)
    {
        // Render the message exactly as the transport will render it, and sign that.
        // This holds because MimeWriter is deterministic: the same EmailMessage always produces the
        // same bytes, so the render made here and the one made later by the transport agree. Adding
        // the DKIM-Signature header afterwards is safe — it is not among the signed headers and does
        // not affect the body. Any middleware that changes the content MUST run before this one
        // (Order < 100), otherwise it would invalidate the signature.
        var rendered = MimeWriter.Write(message);
        var signature = _signer.Sign(rendered);

        // Add DKIM-Signature header
        var headers = new Dictionary<string, string>(message.Headers)
        {
            ["DKIM-Signature"] = signature,
        };

        var signed = message with { Headers = headers };
        return next(signed);
    }
}

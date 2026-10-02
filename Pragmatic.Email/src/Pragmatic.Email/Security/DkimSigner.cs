using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Pragmatic.Email.Configuration;

namespace Pragmatic.Email.Security;

/// <summary>
///     DKIM signature generator per RFC 6376. Signs email headers with RSA-SHA256.
///     Uses only System.Security.Cryptography — zero external dependencies.
/// </summary>
public sealed class DkimSigner : IDisposable
{
    /// <summary>
    ///     Headers that get signed when present, in the order they are listed in the <c>h=</c> tag.
    ///     Content-Type and Content-Transfer-Encoding are included so the signature also pins the
    ///     structure of the message, not just its addressing.
    /// </summary>
    private static readonly string[] CandidateHeaders =
    [
        "from", "to", "cc", "subject", "date", "message-id",
        "mime-version", "content-type", "content-transfer-encoding",
    ];

    private readonly DkimOptions _options;
    private readonly RSA _rsa;

    public DkimSigner(DkimOptions options)
    {
        _options = options;
        _rsa = RSA.Create();
        _rsa.ImportFromPem(options.PrivateKeyPem);
    }

    /// <inheritdoc />
    public void Dispose() => _rsa.Dispose();

    /// <summary>
    ///     Generates the <c>DKIM-Signature</c> header value for a rendered MIME message.
    /// </summary>
    /// <param name="mimeMessage">
    ///     The complete rendered message — headers, the blank separator line, then the body. This must be
    ///     byte-for-byte the message the transport sends (minus the DKIM-Signature header being produced),
    ///     which is why <see cref="Mime.MimeWriter"/> renders deterministically.
    /// </param>
    /// <remarks>
    ///     The signature is computed over the message as it will actually appear on the wire: header values
    ///     are taken verbatim from <paramref name="mimeMessage"/> (already RFC 2047 encoded where needed)
    ///     and <c>bh=</c> covers only the body after the header/body separator. Reconstructing those values
    ///     from the <c>EmailMessage</c> instead would sign something the recipient never receives — for
    ///     example an unencoded non-ASCII subject — and the signature would fail verification.
    /// </remarks>
    public string Sign(string mimeMessage)
    {
        var (headers, body) = SplitHeadersAndBody(mimeMessage);

        // 1. Body hash over the canonicalized body only.
        var canonBody = DkimCanonicalization.CanonicalizeBodyRelaxed(body);
        var bh = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(canonBody)));

        // 2. Only sign headers that are actually present: h= must not name an absent header.
        var signedHeaders = new List<KeyValuePair<string, string>>();
        foreach (var candidate in CandidateHeaders)
        {
            foreach (var header in headers)
            {
                if (string.Equals(header.Key, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    signedHeaders.Add(header);
                    break;
                }
            }
        }

        // 3. Build the DKIM-Signature header without its b= value.
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var dkimHeader =
            $"v=1; a=rsa-sha256; c=relaxed/relaxed; d={_options.Domain}; " +
            $"s={_options.Selector}; t={timestamp}; " +
            $"h={string.Join(":", signedHeaders.Select(h => h.Key.ToLowerInvariant()))}; " +
            $"bh={bh}; b=";

        // 4. Canonicalize the signed headers, then the DKIM-Signature header itself (b= empty).
        var sb = new StringBuilder();
        foreach (var header in signedHeaders)
            sb.Append(DkimCanonicalization.CanonicalizeHeaderRelaxed(header.Key, header.Value)).Append("\r\n");

        sb.Append(DkimCanonicalization.CanonicalizeHeaderRelaxed("dkim-signature", dkimHeader));

        var signature = _rsa.SignData(
            Encoding.UTF8.GetBytes(sb.ToString()), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{dkimHeader}{Convert.ToBase64String(signature)}";
    }

    /// <summary>
    ///     Splits a rendered message at the first empty line into its header fields and its body,
    ///     unfolding header continuation lines (RFC 5322 §2.2.3) as it goes.
    /// </summary>
    internal static (IReadOnlyList<KeyValuePair<string, string>> Headers, string Body) SplitHeadersAndBody(
        string mimeMessage)
    {
        var separator = mimeMessage.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerBlock = separator < 0 ? mimeMessage : mimeMessage[..separator];
        var body = separator < 0 ? string.Empty : mimeMessage[(separator + 4)..];

        var headers = new List<KeyValuePair<string, string>>();
        string? name = null;
        var value = new StringBuilder();

        foreach (var line in headerBlock.Split("\r\n"))
        {
            if (line.Length == 0)
                continue;

            if (line[0] is ' ' or '\t' && name is not null)
            {
                // Folded continuation: keep the CRLF+WSP so relaxed canonicalization unfolds it.
                value.Append("\r\n").Append(line);
                continue;
            }

            if (name is not null)
            {
                headers.Add(new KeyValuePair<string, string>(name, value.ToString()));
                value.Clear();
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                name = null;
                continue;
            }

            name = line[..colon];
            value.Append(line[(colon + 1)..].TrimStart());
        }

        if (name is not null)
            headers.Add(new KeyValuePair<string, string>(name, value.ToString()));

        return (headers, body);
    }
}

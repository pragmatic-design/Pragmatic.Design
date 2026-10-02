using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.Email.Tests.Unit.Security;

/// <summary>
///     A minimal, independent DKIM verifier used by the tests to check that a produced signature is
///     actually valid — the property that matters and that assertions on the signature's text cannot show.
/// </summary>
/// <remarks>
///     The relaxed canonicalization here is written from RFC 6376 §3.4.1-3.4.2 on purpose, rather than
///     calling the production <c>DkimCanonicalization</c>. Reusing the implementation under test would
///     make the check self-consistent: a canonicalization bug would cancel out on both sides and the
///     test would still pass.
/// </remarks>
internal static class DkimVerifier
{
    internal sealed record Result(bool BodyHashMatches, bool SignatureValid)
    {
        public bool IsValid => BodyHashMatches && SignatureValid;
    }

    public static Result Verify(string mimeMessage, RSA publicKey)
    {
        var separator = mimeMessage.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerBlock = separator < 0 ? mimeMessage : mimeMessage[..separator];
        var body = separator < 0 ? string.Empty : mimeMessage[(separator + 4)..];

        var headers = ParseHeaders(headerBlock);

        var dkim = headers.Last(h => h.Key.Equals("DKIM-Signature", StringComparison.OrdinalIgnoreCase));
        var tags = ParseTags(dkim.Value);

        // 1. Body hash
        var computedBh = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalizeBody(body))));
        var bodyHashMatches = computedBh == tags["bh"];

        // 2. Rebuild the signed header block, with the DKIM-Signature header's b= value emptied.
        var signedNames = tags["h"].Split(':', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var signedName in signedNames)
        {
            var header = headers.First(h => h.Key.Equals(signedName.Trim(), StringComparison.OrdinalIgnoreCase));
            sb.Append(CanonicalizeHeader(header.Key, header.Value)).Append("\r\n");
        }

        var bIndex = dkim.Value.LastIndexOf("b=", StringComparison.Ordinal);
        var dkimWithoutSignature = dkim.Value[..(bIndex + 2)];
        sb.Append(CanonicalizeHeader("dkim-signature", dkimWithoutSignature));

        var signatureValid = publicKey.VerifyData(
            Encoding.UTF8.GetBytes(sb.ToString()),
            Convert.FromBase64String(tags["b"]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return new Result(bodyHashMatches, signatureValid);
    }

    private static List<KeyValuePair<string, string>> ParseHeaders(string headerBlock)
    {
        var headers = new List<KeyValuePair<string, string>>();
        string? name = null;
        var value = new StringBuilder();

        foreach (var line in headerBlock.Split("\r\n"))
        {
            if (line.Length == 0)
                continue;

            if (line[0] is ' ' or '\t' && name is not null)
            {
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

        return headers;
    }

    private static Dictionary<string, string> ParseTags(string dkimValue)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in dkimValue.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
                continue;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
                continue;

            tags[trimmed[..eq].Trim()] = trimmed[(eq + 1)..].Trim();
        }

        return tags;
    }

    /// <summary>RFC 6376 §3.4.2 — relaxed header canonicalization.</summary>
    private static string CanonicalizeHeader(string name, string value)
    {
        var unfolded = value.Replace("\r\n", string.Empty, StringComparison.Ordinal);

        var sb = new StringBuilder(unfolded.Length);
        var previousWasWhitespace = false;
        foreach (var c in unfolded)
        {
            if (c is ' ' or '\t')
            {
                if (!previousWasWhitespace)
                    sb.Append(' ');
                previousWasWhitespace = true;
            }
            else
            {
                sb.Append(c);
                previousWasWhitespace = false;
            }
        }

        return $"{name.ToLowerInvariant()}:{sb.ToString().Trim()}";
    }

    /// <summary>RFC 6376 §3.4.1 — relaxed body canonicalization.</summary>
    private static string CanonicalizeBody(string body)
    {
        var sb = new StringBuilder(body.Length);

        foreach (var line in body.Split("\r\n"))
        {
            var compressed = new StringBuilder(line.Length);
            var previousWasWhitespace = false;
            foreach (var c in line)
            {
                if (c is ' ' or '\t')
                {
                    if (!previousWasWhitespace)
                        compressed.Append(' ');
                    previousWasWhitespace = true;
                }
                else
                {
                    compressed.Append(c);
                    previousWasWhitespace = false;
                }
            }

            sb.Append(compressed.ToString().TrimEnd()).Append("\r\n");
        }

        var result = sb.ToString();
        while (result.EndsWith("\r\n\r\n", StringComparison.Ordinal))
            result = result[..^2];

        return result;
    }
}

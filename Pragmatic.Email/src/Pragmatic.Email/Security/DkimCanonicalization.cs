using System.Buffers;
using System.Security.Cryptography;
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
    ///     The SHA-256 of the relaxed canonical body: whitespace at the end of each line stripped,
    ///     runs of whitespace compressed to one space, trailing empty lines removed, every line ended
    ///     with CRLF.
    /// </summary>
    /// <remarks>
    ///     The canonical body is hashed line by line as it is produced, never built whole: a message
    ///     with large attachments would otherwise be copied several times per send (the body string,
    ///     its lines, the canonical string, its UTF-8 bytes). Empty lines are held back as a count and
    ///     written only when a non-empty line follows, which is how trailing ones are dropped. A body
    ///     with no non-empty line hashes as a single CRLF.
    /// </remarks>
    public static byte[] HashBodyRelaxed(ReadOnlySpan<char> body)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var chars = ArrayPool<char>.Shared.Rent(256);
        var bytes = ArrayPool<byte>.Shared.Rent(1024);
        try
        {
            var heldEmptyLines = 0;
            var wroteALine = false;
            while (true)
            {
                var end = body.IndexOf("\r\n", StringComparison.Ordinal);
                var line = end < 0 ? body : body[..end];

                if (chars.Length < line.Length)
                    Regrow(ref chars, line.Length);
                var length = CanonicalizeLineRelaxed(line, chars);

                if (length == 0)
                {
                    heldEmptyLines++;
                }
                else
                {
                    for (; heldEmptyLines > 0; heldEmptyLines--)
                        hash.AppendData(Crlf);

                    var byteCount = Encoding.UTF8.GetMaxByteCount(length);
                    if (bytes.Length < byteCount)
                        Regrow(ref bytes, byteCount);
                    var written = Encoding.UTF8.GetBytes(chars.AsSpan(0, length), bytes);
                    hash.AppendData(bytes.AsSpan(0, written));
                    hash.AppendData(Crlf);
                    wroteALine = true;
                }

                if (end < 0)
                    break;
                body = body[(end + 2)..];
            }

            if (!wroteALine)
                hash.AppendData(Crlf);

            return hash.GetHashAndReset();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private static ReadOnlySpan<byte> Crlf => "\r\n"u8;

    /// <summary>
    ///     Writes the relaxed form of one line into <paramref name="destination" /> and returns its
    ///     length: spaces and tabs at the end removed, every other run of them written as one space.
    /// </summary>
    private static int CanonicalizeLineRelaxed(ReadOnlySpan<char> line, Span<char> destination)
    {
        var trimmed = line.TrimEnd(" \t");
        var length = 0;
        var inWhitespace = false;
        foreach (var c in trimmed)
        {
            if (c is ' ' or '\t')
            {
                if (!inWhitespace)
                    destination[length++] = ' ';
                inWhitespace = true;
            }
            else
            {
                destination[length++] = c;
                inWhitespace = false;
            }
        }

        return length;
    }

    private static void Regrow<T>(ref T[] rented, int atLeast)
    {
        ArrayPool<T>.Shared.Return(rented);
        rented = ArrayPool<T>.Shared.Rent(atLeast);
    }

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex WhitespaceRegex();
}

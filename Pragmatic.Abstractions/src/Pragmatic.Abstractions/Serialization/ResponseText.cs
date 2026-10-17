using System.Buffers;
using System.Text;

namespace Pragmatic.Serialization;

/// <summary>
///     A string value as <see cref="System.Text.Json.Utf8JsonWriter" /> writes it under
///     <see cref="GeneratedJsonDefaults.ResponseEncoder" />, quotes included: the same bytes, written in one pass.
/// </summary>
/// <remarks>
///     <para>
///         The writer asks the encoder where the first character to escape is, transcodes up to it, and hands the rest
///         to the encoder. Here ASCII is searched vectorized, and each character past ASCII is looked up and written as
///         its UTF-8 bytes as it is read; only what the encoder escapes goes to the encoder. Measured on the strings of
///         <c>twitter.json</c> (#152, <c>TwitterStringRedoxProbeBenchmarks</c>): 0.72 of the writer's time, where a
///         check over each non-ASCII run before transcoding it was 0.81, and one search over every allowed character
///         0.76.
///     </para>
///     <para>
///         What is written as it is comes from the encoder itself (<see cref="System.Text.Encodings.Web.TextEncoder.WillEncode" />),
///         never from a list of ours, and the tests compare every UTF-16 code unit, every surrogate pair and every lone
///         surrogate with the writer.
///     </para>
/// </remarks>
internal static class ResponseText
{
    /// <summary>The longest string the writer accepts as a value, in UTF-16 code units.</summary>
    public const int MaxLength = 166_666_666;

    /// <summary>The ASCII characters the encoder writes as they are.</summary>
    private static readonly SearchValues<char> SafeAscii = SearchValues.Create(Safe(0, 0x80));

    /// <summary>One bit per code unit of the Basic Multilingual Plane: set where the encoder writes it as it is.</summary>
    private static readonly ulong[] Allowed = AllowedCodeUnits();

    /// <summary>The most bytes <paramref name="length" /> code units can take, quotes included.</summary>
    /// <remarks>Six for each, as <c>\u0000</c>: a scalar past the plane is two of them, escaped as two.</remarks>
    public static int MaxBytes(int length) => length * 6 + 2;

    /// <summary>Writes <paramref name="text" /> quoted into <paramref name="destination" />, and returns how many bytes.</summary>
    /// <remarks><paramref name="destination" /> holds at least <see cref="MaxBytes" /> of the text's length.</remarks>
    public static int Write(ReadOnlySpan<char> text, Span<byte> destination)
    {
        var at = 0;
        destination[at++] = (byte)'"';
        Span<char> escaped = stackalloc char[16];
        var rest = text;
        while (!rest.IsEmpty)
        {
            var next = rest.IndexOfAnyExcept(SafeAscii);
            if (next < 0)
            {
                at += Encoding.UTF8.GetBytes(rest, destination[at..]);
                break;
            }

            if (next > 0)
            {
                at += Encoding.UTF8.GetBytes(rest[..next], destination[at..]);
                rest = rest[next..];
            }

            var read = 0;
            for (; read < rest.Length; read++)
            {
                int c = rest[read];
                if (c < 0x80 || (Allowed[c >> 6] & (1UL << (c & 63))) == 0)
                    break;

                if (c < 0x800)
                {
                    destination[at] = (byte)(0xC0 | (c >> 6));
                    destination[at + 1] = (byte)(0x80 | (c & 0x3F));
                    at += 2;
                }
                else
                {
                    destination[at] = (byte)(0xE0 | (c >> 12));
                    destination[at + 1] = (byte)(0x80 | ((c >> 6) & 0x3F));
                    destination[at + 2] = (byte)(0x80 | (c & 0x3F));
                    at += 3;
                }
            }

            if (read > 0)
            {
                rest = rest[read..];
                continue;
            }

            // A pair is escaped as one scalar; a lone surrogate on its own, as the encoder replaces it.
            var take = char.IsHighSurrogate(rest[0]) && rest.Length > 1 && char.IsLowSurrogate(rest[1]) ? 2 : 1;
            GeneratedJsonDefaults.ResponseEncoder.Encode(rest[..take], escaped, out _, out var written);
            for (var i = 0; i < written; i++)
                destination[at++] = (byte)escaped[i];
            rest = rest[take..];
        }

        destination[at++] = (byte)'"';
        return at;
    }

    private static char[] Safe(int from, int to)
    {
        var safe = new List<char>(to - from);
        for (var c = from; c < to; c++)
            if (!GeneratedJsonDefaults.ResponseEncoder.WillEncode(c))
                safe.Add((char)c);

        return [.. safe];
    }

    private static ulong[] AllowedCodeUnits()
    {
        var bits = new ulong[0x10000 / 64];
        for (var c = 0x80; c <= 0xFFFF; c++)
            if (!char.IsSurrogate((char)c) && !GeneratedJsonDefaults.ResponseEncoder.WillEncode(c))
                bits[c >> 6] |= 1UL << (c & 63);

        return bits;
    }
}

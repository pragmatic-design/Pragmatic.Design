using System.Buffers;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Pragmatic.Endpoints.Benchmarks.Documents;
using Pragmatic.Endpoints.Benchmarks.Serialization;
using Pragmatic.Serialization;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     Where RE:Dox's lead on <c>twitter.json</c> is (#152): its string path against
///     <see cref="Utf8JsonWriter.WriteStringValue(string)" /> over the document's own strings, the ASCII ones and the
///     others apart.
/// </summary>
/// <remarks>
///     <para>
///         Every competitor writes a JSON array of the same strings into a buffer kept across calls and returns how
///         many bytes it wrote, so no call pays for a copy of its result (#150).
///     </para>
///     <para>
///         RE:Dox escapes with its own policy, by default <c>TextEncoderPolicy.Minimum</c>: only what RFC 8259
///         requires, a quotation mark, a reverse solidus and the controls below U+0020. The host's encoder escapes
///         more, and has to look up every character past ASCII to know whether to. <see cref="Rfc_Minimal" /> is
///         RE:Dox's technique (a search for the few characters to escape, the text between them transcoded whole)
///         under that same minimal policy, to tell the lead the technique buys from the one the contract buys.
///     </para>
/// </remarks>
[MemoryDiagnoser]
public class TwitterStringRedoxProbeBenchmarks
{
    /// <summary>What RFC 8259 requires escaped in a string: the quotation mark, the reverse solidus, U+0000–U+001F.</summary>
    private static readonly SearchValues<char> RfcEscaped = SearchValues.Create(
        [.. Enumerable.Range(0, 0x20).Select(c => (char)c), '"', '\\']);

    /// <summary>The ASCII characters the response encoder writes as they are, read from the encoder itself.</summary>
    private static readonly SearchValues<char> HostSafeAscii = SearchValues.Create(
        [.. Enumerable.Range(0, 0x80).Select(c => (char)c).Where(c => !EncoderEscapes(c))]);

    /// <summary>One bit per BMP code unit past ASCII: set where the response encoder writes it as it is.</summary>
    private static readonly ulong[] HostAllowedBmp = AllowedBmp();

    private readonly ArrayBufferWriter<byte> _buffer = new(1024 * 1024);
    private string[] _strings = [];
    private int _worstCase;
    private Utf8JsonWriter _writer = null!;

    /// <summary>Which strings: every one, the ASCII ones, or the ones with a character past ASCII.</summary>
    /// <remarks>
    ///     <c>plain</c> and <c>escaped</c> split the non-ASCII ones: text the response encoder writes as it is, and
    ///     text where it escapes something (a scalar past the Basic Multilingual Plane, for one).
    /// </remarks>
    [Params("all", "ascii", "non-ascii", "plain", "escaped")]
    public string Set { get; set; } = "all";

    [GlobalSetup]
    public void Setup()
    {
        var all = TwitterStrings.Read();
        _strings = Set switch
        {
            "all" => all,
            "ascii" => [.. all.Where(s => System.Text.Ascii.IsValid(s))],
            "non-ascii" => [.. all.Where(s => !System.Text.Ascii.IsValid(s))],
            "plain" => [.. all.Where(s => !System.Text.Ascii.IsValid(s) && !Escapes(s))],
            "escaped" => [.. all.Where(s => !System.Text.Ascii.IsValid(s) && Escapes(s))],
            _ => throw new ArgumentOutOfRangeException(nameof(Set), Set, "all, ascii, non-ascii, plain or escaped"),
        };
        // A char is at most 6 bytes escaped (\u0000), a quote pair and a comma each string, and the brackets.
        _worstCase = _strings.Sum(s => s.Length * 6 + 3) + 2;
        _writer = new Utf8JsonWriter(_buffer, GeneratedJsonDefaults.ResponseWriterOptions);

        var writer = Written(Writer_WriteStringValue());
        var redox = Written(REDox_Serialize());
        var minimal = Written(Rfc_Minimal());
        if (!Written(Host_Fused()).AsSpan().SequenceEqual(writer))
            throw new InvalidOperationException("The fused path does not write the writer's very bytes.");
        var strings = Strings(writer);
        if (!strings.SequenceEqual(Strings(redox)))
            throw new InvalidOperationException("RE:Dox does not write the same strings as the writer.");
        if (!strings.SequenceEqual(Strings(minimal)))
            throw new InvalidOperationException("The minimal path does not write the same strings as the writer.");
        if (!minimal.AsSpan().SequenceEqual(redox))
            throw new InvalidOperationException("The minimal path does not escape as RE:Dox does.");

        Console.WriteLine($"// {Set}: {_strings.Length} strings, {_strings.Sum(s => s.Length)} chars; "
                          + $"writer {writer.Length} bytes, RE:Dox {redox.Length} bytes, minimal {minimal.Length} bytes"
                          + (writer.AsSpan().SequenceEqual(redox) ? ", the same bytes." : ", escaped differently."));
    }

    private byte[] Written(int count) => _buffer.WrittenSpan[..count].ToArray();

    private static string?[] Strings(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Select(e => e.GetString())];
    }

    private static unsafe bool Escapes(string text)
    {
        fixed (char* chars = text)
            return GeneratedJsonDefaults.ResponseEncoder.FindFirstCharacterToEncode(chars, text.Length) >= 0;
    }

    private static unsafe bool EncoderEscapes(char c) =>
        GeneratedJsonDefaults.ResponseEncoder.FindFirstCharacterToEncode(&c, 1) >= 0;

    private static ulong[] AllowedBmp()
    {
        var bits = new ulong[0x10000 / 64];
        for (var c = 0x80; c <= 0xFFFF; c++)
        {
            if (!char.IsSurrogate((char)c) && !EncoderEscapes((char)c))
                bits[c >> 6] |= 1UL << (c & 63);
        }

        return bits;
    }

    [Benchmark(Baseline = true)]
    public int Writer_WriteStringValue()
    {
        _buffer.ResetWrittenCount();
        _writer.Reset(_buffer);
        _writer.WriteStartArray();
        foreach (var text in _strings)
            _writer.WriteStringValue(text);
        _writer.WriteEndArray();
        _writer.Flush();
        return _buffer.WrittenCount;
    }

    [Benchmark]
    public int REDox_Serialize()
    {
        _buffer.ResetWrittenCount();
        global::REDox.Json.JsonSerializer.Serialize(_buffer, _strings, Competitors.REDox);
        return _buffer.WrittenCount;
    }

    /// <summary>RE:Dox's technique under RE:Dox's policy, written here.</summary>
    [Benchmark]
    public int Rfc_Minimal()
    {
        _buffer.ResetWrittenCount();
        var destination = _buffer.GetSpan(_worstCase);
        var at = 0;
        destination[at++] = (byte)'[';
        for (var i = 0; i < _strings.Length; i++)
        {
            if (i > 0)
                destination[at++] = (byte)',';
            destination[at++] = (byte)'"';
            var rest = _strings[i].AsSpan();
            while (true)
            {
                var next = rest.IndexOfAny(RfcEscaped);
                if (next < 0)
                {
                    at += Encoding.UTF8.GetBytes(rest, destination[at..]);
                    break;
                }

                at += Encoding.UTF8.GetBytes(rest[..next], destination[at..]);
                at += Escape(rest[next], destination[at..]);
                rest = rest[(next + 1)..];
            }

            destination[at++] = (byte)'"';
        }

        destination[at++] = (byte)']';
        _buffer.Advance(at);
        return at;
    }

    /// <summary>
    ///     RE:Dox's technique under the host's contract: the writer's very bytes. The search stops at an ASCII
    ///     character the encoder escapes and at anything past ASCII; each non-ASCII character the encoder allows,
    ///     read off the bitmap, is written as its two or three UTF-8 bytes as it is read; what it escapes, the
    ///     encoder escapes.
    /// </summary>
    /// <remarks>
    ///     The fastest of three shapes measured under that contract (#152): checking a non-ASCII run first and
    ///     transcoding it after was slower, and so was one <see cref="SearchValues{T}" /> over every character the
    ///     encoder allows.
    /// </remarks>
    [Benchmark]
    public int Host_Fused()
    {
        _buffer.ResetWrittenCount();
        var destination = _buffer.GetSpan(_worstCase);
        var at = 0;
        destination[at++] = (byte)'[';
        Span<char> escaped = stackalloc char[16];
        for (var i = 0; i < _strings.Length; i++)
        {
            if (i > 0)
                destination[at++] = (byte)',';
            destination[at++] = (byte)'"';
            var rest = _strings[i].AsSpan();
            while (!rest.IsEmpty)
            {
                var next = rest.IndexOfAnyExcept(HostSafeAscii);
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

                var k = 0;
                for (; k < rest.Length; k++)
                {
                    int c = rest[k];
                    if (c < 0x80 || (HostAllowedBmp[c >> 6] & (1UL << (c & 63))) == 0)
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

                if (k > 0)
                {
                    rest = rest[k..];
                    continue;
                }

                var take = char.IsHighSurrogate(rest[0]) && rest.Length > 1 && char.IsLowSurrogate(rest[1]) ? 2 : 1;
                GeneratedJsonDefaults.ResponseEncoder.Encode(rest[..take], escaped, out _, out var written);
                for (var e = 0; e < written; e++)
                    destination[at++] = (byte)escaped[e];
                rest = rest[take..];
            }

            destination[at++] = (byte)'"';
        }

        destination[at++] = (byte)']';
        _buffer.Advance(at);
        return at;
    }

    /// <summary>A character RFC 8259 requires escaped, as RE:Dox writes it: the short form where JSON has one.</summary>
    private static int Escape(char c, Span<byte> destination)
    {
        destination[0] = (byte)'\\';
        var shortForm = c switch
        {
            '"' => '"',
            '\\' => '\\',
            '\b' => 'b',
            '\f' => 'f',
            '\n' => 'n',
            '\r' => 'r',
            '\t' => 't',
            _ => '\0',
        };
        if (shortForm != '\0')
        {
            destination[1] = (byte)shortForm;
            return 2;
        }

        destination[1] = (byte)'u';
        destination[2] = (byte)'0';
        destination[3] = (byte)'0';
        destination[4] = (byte)"0123456789abcdef"[c >> 4];
        destination[5] = (byte)"0123456789abcdef"[c & 0xF];
        return 6;
    }
}

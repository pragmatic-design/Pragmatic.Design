using System.Buffers;
using System.Text.Json;
using System.Text.Unicode;
using BenchmarkDotNet.Attributes;
using Pragmatic.Endpoints.Benchmarks.Documents;
using Pragmatic.Serialization;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     The strings of <c>twitter.json</c>, every one of them, written as an array (#130): what writing text costs the
///     writer on the one workload made of it, and whether a string checked and copied as a raw value is cheaper.
/// </summary>
/// <remarks>
///     The strings are read in document order from the graph the workload serializes, so the encoder sees the same
///     text. <c>Raw_CheckedCopy</c> transcodes a string to UTF-8, asks the host's encoder whether any of it needs
///     escaping, and writes it raw when none does; a string that needs escaping goes to the writer. It writes the
///     writer's very bytes, which the setup checks.
/// </remarks>
[MemoryDiagnoser]
public class TwitterStringProbeBenchmarks
{
    private readonly ArrayBufferWriter<byte> _buffer = new(1024 * 1024);
    private readonly byte[] _scratch = new byte[64 * 1024];
    private string[] _strings = [];
    private Utf8JsonWriter _writer = null!;

    [GlobalSetup]
    public void Setup()
    {
        var root = Workloads.Create<Twitter.Root>();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(root, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<Twitter.Root>)
            Serialization.Competitors.HostReflection.GetTypeInfo(typeof(Twitter.Root)));
        var strings = new List<string>();
        Collect(JsonDocument.Parse(bytes).RootElement, strings);
        _strings = [.. strings];
        _writer = new Utf8JsonWriter(_buffer, GeneratedJsonDefaults.ResponseWriterOptions);

        var expected = Writer_WriteStringValue();
        if (!expected.AsSpan().SequenceEqual(Raw_CheckedCopy()))
            throw new InvalidOperationException($"{nameof(Raw_CheckedCopy)} does not write the writer's bytes.");
        if (!expected.AsSpan().SequenceEqual(Raw_AsciiCopy()))
            throw new InvalidOperationException($"{nameof(Raw_AsciiCopy)} does not write the writer's bytes.");
        if (!expected.AsSpan().SequenceEqual(Raw_AsciiOrEncoded()))
            throw new InvalidOperationException($"{nameof(Raw_AsciiOrEncoded)} does not write the writer's bytes.");
        if (!expected.AsSpan().SequenceEqual(Raw_AsciiOrUtf8Check()))
            throw new InvalidOperationException($"{nameof(Raw_AsciiOrUtf8Check)} does not write the writer's bytes.");
        if (!expected.AsSpan().SequenceEqual(Raw_AsciiOrUtf16Check()))
            throw new InvalidOperationException($"{nameof(Raw_AsciiOrUtf16Check)} does not write the writer's bytes.");
    }

    [Benchmark(Baseline = true)]
    public byte[] Writer_WriteStringValue()
    {
        Start();
        foreach (var text in _strings)
            _writer.WriteStringValue(text);
        return End();
    }

    [Benchmark]
    public byte[] Raw_CheckedCopy()
    {
        Start();
        var encoder = GeneratedJsonDefaults.ResponseEncoder;
        foreach (var text in _strings)
        {
            var target = _scratch.AsSpan(1);
            Utf8.FromUtf16(text, target, out _, out var written);
            if (encoder.FindFirstCharacterToEncodeUtf8(target[..written]) >= 0)
            {
                _writer.WriteStringValue(text);
                continue;
            }

            _scratch[0] = (byte)'"';
            _scratch[written + 1] = (byte)'"';
            _writer.WriteRawValue(_scratch.AsSpan(0, written + 2), skipInputValidation: true);
        }

        return End();
    }

    /// <summary>
    ///     A string that is all ASCII and holds no quote, backslash, control character or DEL is copied raw; anything
    ///     else goes to the writer. Conservative by construction: a character this does not know about is the writer's.
    /// </summary>
    [Benchmark]
    public byte[] Raw_AsciiCopy()
    {
        Start();
        foreach (var text in _strings)
        {
            var target = _scratch.AsSpan(1);
            if (System.Text.Ascii.FromUtf16(text, target, out var written) != OperationStatus.Done
                || target[..written].IndexOfAny(NeedsTheWriter) >= 0)
            {
                _writer.WriteStringValue(text);
                continue;
            }

            _scratch[0] = (byte)'"';
            _scratch[written + 1] = (byte)'"';
            _writer.WriteRawValue(_scratch.AsSpan(0, written + 2), skipInputValidation: true);
        }

        return End();
    }

    /// <summary>
    ///     Every string without the writer: ASCII with nothing to escape copied, anything else transcoded and escaped by
    ///     the host's encoder (<c>EncodeUtf8</c>, what the writer itself calls when it has an encoder). What a run would
    ///     do with a string; one raw value per string here, so the comparison is per string.
    /// </summary>
    [Benchmark]
    public byte[] Raw_AsciiOrEncoded()
    {
        Start();
        var encoder = GeneratedJsonDefaults.ResponseEncoder;
        foreach (var text in _strings)
        {
            var target = _scratch.AsSpan(1);
            int written;
            if (System.Text.Ascii.FromUtf16(text, target, out written) != OperationStatus.Done
                || target[..written].IndexOfAny(NeedsTheWriter) >= 0)
            {
                var utf8 = _transcoded.AsSpan();
                Utf8.FromUtf16(text, utf8, out _, out var length, replaceInvalidSequences: false);
                encoder.EncodeUtf8(utf8[..length], target, out _, out written);
            }

            _scratch[0] = (byte)'"';
            _scratch[written + 1] = (byte)'"';
            _writer.WriteRawValue(_scratch.AsSpan(0, written + 2), skipInputValidation: true);
        }

        return End();
    }

    /// <summary>
    ///     ASCII copied; a non-ASCII string transcoded, then checked by the encoder on its UTF-8, and escaped by it only
    ///     when the check finds something.
    /// </summary>
    [Benchmark]
    public byte[] Raw_AsciiOrUtf8Check()
    {
        Start();
        var encoder = GeneratedJsonDefaults.ResponseEncoder;
        foreach (var text in _strings)
        {
            var target = _scratch.AsSpan(1);
            int written;
            if (System.Text.Ascii.FromUtf16(text, target, out written) != OperationStatus.Done
                || target[..written].IndexOfAny(NeedsTheWriter) >= 0)
            {
                Utf8.FromUtf16(text, target, out _, out written, replaceInvalidSequences: false);
                if (encoder.FindFirstCharacterToEncodeUtf8(target[..written]) >= 0)
                {
                    var utf8 = _transcoded.AsSpan();
                    target[..written].CopyTo(utf8);
                    encoder.EncodeUtf8(utf8[..written], target, out _, out written);
                }
            }

            _scratch[0] = (byte)'"';
            _scratch[written + 1] = (byte)'"';
            _writer.WriteRawValue(_scratch.AsSpan(0, written + 2), skipInputValidation: true);
        }

        return End();
    }

    /// <summary>
    ///     ASCII copied; a non-ASCII string checked by the encoder on its UTF-16, as the writer checks it, then
    ///     transcoded, and escaped by the encoder only when the check finds something.
    /// </summary>
    [Benchmark]
    public unsafe byte[] Raw_AsciiOrUtf16Check()
    {
        Start();
        var encoder = GeneratedJsonDefaults.ResponseEncoder;
        foreach (var text in _strings)
        {
            var target = _scratch.AsSpan(1);
            int written;
            if (System.Text.Ascii.FromUtf16(text, target, out written) != OperationStatus.Done
                || target[..written].IndexOfAny(NeedsTheWriter) >= 0)
            {
                int first;
                fixed (char* chars = text)
                    first = encoder.FindFirstCharacterToEncode(chars, text.Length);

                Utf8.FromUtf16(text, target, out _, out written, replaceInvalidSequences: false);
                if (first >= 0)
                {
                    var utf8 = _transcoded.AsSpan();
                    target[..written].CopyTo(utf8);
                    encoder.EncodeUtf8(utf8[..written], target, out _, out written);
                }
            }

            _scratch[0] = (byte)'"';
            _scratch[written + 1] = (byte)'"';
            _writer.WriteRawValue(_scratch.AsSpan(0, written + 2), skipInputValidation: true);
        }

        return End();
    }

    private readonly byte[] _transcoded = new byte[64 * 1024];

    private static readonly SearchValues<byte> NeedsTheWriter = SearchValues.Create(
    [
        .. Enumerable.Range(0, 0x20).Select(b => (byte)b), (byte)'"', (byte)'\\', 0x7F,
    ]);

    /// <summary>Subtraction: the same array with every string empty, already encoded.</summary>
    [Benchmark]
    public byte[] Minus_Text()
    {
        Start();
        var empty = JsonEncodedText.Encode("");
        for (var i = 0; i < _strings.Length; i++)
            _writer.WriteStringValue(empty);
        return End();
    }

    private void Start()
    {
        _buffer.ResetWrittenCount();
        _writer.Reset(_buffer);
        _writer.WriteStartArray();
    }

    private byte[] End()
    {
        _writer.WriteEndArray();
        _writer.Flush();
        return _buffer.WrittenSpan.ToArray();
    }

    private static void Collect(JsonElement element, List<string> strings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Collect(property.Value, strings);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, strings);
                break;
            case JsonValueKind.String:
                strings.Add(element.GetString()!);
                break;
        }
    }
}

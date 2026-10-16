using System.Buffers;
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
///     Both write a JSON array of the same strings into a buffer kept across calls. RE:Dox escapes with its own
///     policy, so its bytes may differ from the writer's where the text holds a character one of them escapes; the
///     setup reports how many strings that is, since a lead bought by escaping less is not one the writer can match.
/// </remarks>
[MemoryDiagnoser]
public class TwitterStringRedoxProbeBenchmarks
{
    private readonly ArrayBufferWriter<byte> _buffer = new(1024 * 1024);
    private string[] _strings = [];
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
        _writer = new Utf8JsonWriter(_buffer, GeneratedJsonDefaults.ResponseWriterOptions);

        var writer = Writer_WriteStringValue();
        var redox = REDox_Serialize();
        using var written = JsonDocument.Parse(writer);
        using var serialized = JsonDocument.Parse(redox);
        if (!written.RootElement.EnumerateArray().Select(e => e.GetString())
                .SequenceEqual(serialized.RootElement.EnumerateArray().Select(e => e.GetString())))
            throw new InvalidOperationException("RE:Dox does not write the same strings as the writer.");

        Console.WriteLine($"// {Set}: {_strings.Length} strings, {_strings.Sum(s => s.Length)} chars; "
                          + $"writer {writer.Length} bytes, RE:Dox {redox.Length} bytes"
                          + (writer.AsSpan().SequenceEqual(redox) ? ", the same bytes." : ", escaped differently."));
    }

    private static unsafe bool Escapes(string text)
    {
        fixed (char* chars = text)
            return GeneratedJsonDefaults.ResponseEncoder.FindFirstCharacterToEncode(chars, text.Length) >= 0;
    }

    [Benchmark(Baseline = true)]
    public byte[] Writer_WriteStringValue()
    {
        _buffer.ResetWrittenCount();
        _writer.Reset(_buffer);
        _writer.WriteStartArray();
        foreach (var text in _strings)
            _writer.WriteStringValue(text);
        _writer.WriteEndArray();
        _writer.Flush();
        return _buffer.WrittenSpan.ToArray();
    }

    [Benchmark]
    public byte[] REDox_Serialize()
    {
        _buffer.ResetWrittenCount();
        global::REDox.Json.JsonSerializer.Serialize(_buffer, _strings, Competitors.REDox);
        return _buffer.WrittenSpan.ToArray();
    }
}

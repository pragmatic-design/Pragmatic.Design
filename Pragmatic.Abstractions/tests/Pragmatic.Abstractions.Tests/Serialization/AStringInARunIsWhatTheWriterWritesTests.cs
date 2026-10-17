using System.Buffers;
using System.Text;
using System.Text.Json;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Abstractions.Tests.Serialization;

/// <summary>
///     <see cref="Utf8JsonRun.StringValue(string)" /> writes what <see cref="Utf8JsonWriter.WriteStringValue(string)" />
///     writes under the response encoder (#152), for every UTF-16 code unit, every surrogate pair and every lone
///     surrogate. The reference is the writer itself, never a copy of the encoder's rules.
/// </summary>
public sealed class AStringInARunIsWhatTheWriterWritesTests
{
    /// <summary>Every code unit, surrogates included, alone, between ASCII, and at the end after ASCII.</summary>
    [Fact]
    public void EveryCodeUnit_IsWrittenAsTheWriterWritesIt()
    {
        var mismatches = new List<string>();
        for (var c = 0; c <= 0xFFFF; c++)
        {
            var unit = ((char)c).ToString();
            Compare(unit, mismatches);
            Compare("a" + unit + "b", mismatches);
            Compare("ab" + unit, mismatches);
        }

        mismatches.Should().BeEmpty();
    }

    /// <summary>Every surrogate pair, the 1,024 with the same high surrogate in one string, and again between ASCII.</summary>
    [Fact]
    public void EverySurrogatePair_IsWrittenAsTheWriterWritesIt()
    {
        var mismatches = new List<string>();
        var together = new StringBuilder();
        var apart = new StringBuilder();
        for (var high = 0xD800; high <= 0xDBFF; high++)
        {
            together.Clear();
            apart.Clear();
            for (var low = 0xDC00; low <= 0xDFFF; low++)
            {
                together.Append((char)high).Append((char)low);
                apart.Append('a').Append((char)high).Append((char)low);
            }

            Compare(together.ToString(), mismatches);
            Compare(apart.ToString(), mismatches);
        }

        mismatches.Should().BeEmpty();
    }

    /// <summary>Each surrogate twice, before a high one and after a low one: pairs in the wrong order, and none.</summary>
    [Fact]
    public void ASurrogateThatIsNotAPair_IsWrittenAsTheWriterWritesIt()
    {
        var mismatches = new List<string>();
        for (var c = 0xD800; c <= 0xDFFF; c++)
        {
            var unit = (char)c;
            Compare($"{unit}{unit}", mismatches);
            Compare($"{unit}\uD800", mismatches);
            Compare($"\uDC00{unit}", mismatches);
            Compare($"x{unit}\uDBFF", mismatches);
        }

        mismatches.Should().BeEmpty();
    }

    /// <summary>A string longer than the run's scratch buffer, mixing ASCII, escapes, other planes and lone surrogates.</summary>
    [Fact]
    public void AStringLargerThanTheScratch_IsWrittenAsTheWriterWritesIt()
    {
        var text = new StringBuilder();
        for (var i = 0; i < 4_000; i++)
            text.Append("ab\"\\\n").Append((char)(0x80 + i)).Append("€😀").Append('\uD800').Append('<');

        var mismatches = new List<string>();
        Compare(text.ToString(), mismatches);

        mismatches.Should().BeEmpty();
    }

    /// <summary>Strings in a run are separated as the writer separates values.</summary>
    [Fact]
    public void StringsInARun_AreSeparatedAsTheWriterSeparatesThem()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, GeneratedJsonDefaults.ResponseWriterOptions))
        {
            writer.WriteStartArray();
            writer.WriteStringValue("");
            writer.WriteStringValue("é");
            writer.WriteNumberValue(1);
            writer.WriteStringValue("\"");
            writer.WriteEndArray();
        }

        var run = Utf8JsonRun.Start();
        run.StartArray();
        run.StringValue("");
        run.StringValue("é");
        run.NumberValue(1);
        run.StringValue("\"");
        run.EndArray();
        var written = run.Written.ToArray();
        run.Dispose();

        Encoding.UTF8.GetString(written).Should().Be(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private static void Compare(string text, List<string> mismatches)
    {
        var expected = ThroughTheWriter(text);
        var actual = ThroughARun(text);
        if (mismatches.Count >= 20 || actual.AsSpan().SequenceEqual(expected))
            return;

        // From a little before the first byte that differs: a long string's whole output buries the difference.
        var from = Math.Max(0, actual.AsSpan().CommonPrefixLength(expected) - 16);
        mismatches.Add($"{Describe(text)}, from byte {from}: writer {Window(expected, from)}, run {Window(actual, from)}");
    }

    private static string Window(byte[] bytes, int from)
        => Encoding.UTF8.GetString(bytes, from, Math.Min(48, bytes.Length - from));

    private static byte[] ThroughTheWriter(string text)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, GeneratedJsonDefaults.ResponseWriterOptions))
            writer.WriteStringValue(text);

        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] ThroughARun(string text)
    {
        var run = Utf8JsonRun.Start();
        run.StringValue(text);
        var written = run.Written.ToArray();
        run.Dispose();
        return written;
    }

    private static string Describe(string text)
        => text.Length > 8 ? $"{text.Length} code units" : string.Join(" ", text.Select(c => ((int)c).ToString("X4")));
}

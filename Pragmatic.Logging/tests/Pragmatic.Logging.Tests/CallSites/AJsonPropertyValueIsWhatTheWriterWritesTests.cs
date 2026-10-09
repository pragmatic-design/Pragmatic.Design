using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Pragmatic.Logging.CallSites;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.CallSites;

/// <summary>
///     A property value a generated state writes as JSON bytes is the one a <see cref="Utf8JsonWriter" /> with the
///     JSON provider's encoder writes for it; a string the writer would escape is left to the writer.
/// </summary>
public class AJsonPropertyValueIsWhatTheWriterWritesTests
{
    private static readonly JsonWriterOptions Options = new()
    {
        SkipValidation = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static TheoryData<decimal> Decimals =>
    [
        0m, 1m, -1m, 1.0m, 1.00m, 19.99m, -19.990m, 0.1m, 0.0000000000000000000000000001m,
        decimal.MaxValue, decimal.MinValue, 1234567890.123456789m, 100m, 1e10m,
    ];

    [Theory]
    [MemberData(nameof(Decimals))]
    public void ADecimal(decimal value) => Number(value).Should().Be(Written(w => w.WriteNumberValue(value)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void AnInt(int value) => Number(value).Should().Be(Written(w => w.WriteNumberValue(value)));

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void ALong(long value) => Number(value).Should().Be(Written(w => w.WriteNumberValue(value)));

    [Fact]
    public void AnUnsignedLong() => Number(ulong.MaxValue).Should().Be(Written(w => w.WriteNumberValue(ulong.MaxValue)));

    [Fact]
    public void ANullNumber() => Number<int>(null).Should().Be("null");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void ABoolean(bool? value)
    {
        Span<byte> buffer = stackalloc byte[16];
        var written = 0;
        Utf8LogJsonValues.TryAppendJsonBoolean(buffer, ref written, value).Should().BeTrue();

        var expected = value is { } present ? Written(w => w.WriteBooleanValue(present)) : "null";
        Encoding.UTF8.GetString(buffer[..written]).Should().Be(expected);
    }

    [Theory]
    [InlineData("jane")]
    [InlineData("Café <EU> & 'ok' ünïcode 日本")]
    [InlineData("")]
    public void AStringTheWriterDoesNotEscape(string value)
    {
        var buffer = new byte[256];
        var written = 0;
        var needsWriter = false;

        Utf8LogJsonValues.TryAppendJsonString(buffer, ref written, value, ref needsWriter).Should().BeTrue();

        needsWriter.Should().BeFalse();
        Encoding.UTF8.GetString(buffer, 0, written).Should().Be(Written(w => w.WriteStringValue(value)));
    }

    [Theory]
    [InlineData("a \"quoted\" name")]
    [InlineData("back\\slash")]
    [InlineData("line\nbreak")]
    public void AStringTheWriterEscapes_IsLeftToTheWriter(string value)
    {
        var buffer = new byte[256];
        var written = 0;
        var needsWriter = false;

        Utf8LogJsonValues.TryAppendJsonString(buffer, ref written, value, ref needsWriter).Should().BeFalse();
        needsWriter.Should().BeTrue();
    }

    [Fact]
    public void AStringFromItsRenderedBytes()
    {
        var buffer = new byte[64];
        var written = 0;
        var needsWriter = false;

        Utf8LogJsonValues.TryAppendJsonString(buffer, ref written, "jane", "jane"u8, ref needsWriter).Should().BeTrue();
        Encoding.UTF8.GetString(buffer, 0, written).Should().Be("\"jane\"");

        written = 0;
        Utf8LogJsonValues.TryAppendJsonString(buffer, ref written, null, "(null)"u8, ref needsWriter).Should().BeTrue();
        Encoding.UTF8.GetString(buffer, 0, written).Should().Be("null");
    }

    [Fact]
    public void TheMask()
    {
        Span<byte> buffer = stackalloc byte[32];
        var written = 0;
        Utf8LogJsonValues.TryAppendJsonMask(buffer, ref written).Should().BeTrue();

        Encoding.UTF8.GetString(buffer[..written]).Should().Be(Written(w => w.WriteStringValue(RedactionMask.Utf8)));
    }

    [Fact]
    public void ABufferTooSmall_IsNotAStringToEscape()
    {
        Span<byte> buffer = stackalloc byte[3];
        var written = 0;
        var needsWriter = false;

        Utf8LogJsonValues.TryAppendJsonString(buffer, ref written, "jane", ref needsWriter).Should().BeFalse();
        needsWriter.Should().BeFalse();
    }

    private static string Number<T>(T value) where T : IUtf8SpanFormattable
    {
        Span<byte> buffer = stackalloc byte[64];
        var written = 0;
        Utf8LogJsonValues.TryAppendJsonNumber(buffer, ref written, value).Should().BeTrue();
        return Encoding.UTF8.GetString(buffer[..written]);
    }

    private static string Number<T>(T? value) where T : struct, IUtf8SpanFormattable
    {
        Span<byte> buffer = stackalloc byte[64];
        var written = 0;
        Utf8LogJsonValues.TryAppendJsonNumber(buffer, ref written, value).Should().BeTrue();
        return Encoding.UTF8.GetString(buffer[..written]);
    }

    private static string Written(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Options))
            write(writer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}

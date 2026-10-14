using System.Globalization;
using System.Text;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Abstractions.Tests.Serialization;

/// <summary>
///     <see cref="ShortestRoundTrip" /> writes what <c>TryFormat</c> writes (#149): the same digits, the same choice
///     between fixed and exponent notation, the same exponent. The reference is <c>TryFormat</c> itself, called the way
///     <c>Utf8JsonWriter</c> calls it, never a copy of its rules.
/// </summary>
public sealed class ShortestRoundTripTests
{
    /// <summary>
    ///     Integers, both notations and both exponent signs, the thresholds of each, subnormals, the extremes, negative
    ///     zero, and values whose shortest form has 15, 16 and 17 significant digits.
    /// </summary>
    public static TheoryData<double> Branches =>
    [
        0.0, -0.0, 1.0, -1.0, 60.0, 123456.0, 9007199254740992.0, 1e15, 1e16, 1e17, 1e18, 1e21,
        1.2345678901234568E+17, 1e22, 1e23, 1e100, 1e300, 1e308,
        0.001, 0.0001, 0.00012345, 0.00001, 0.000012345, 1e-7, 0.1, 0.3, 0.5, 2.0 / 3.0, 1.0 / 3.0, Math.PI, Math.E,
        -Math.PI, -0.0001, -0.00001, -1e21, double.Epsilon, -double.Epsilon, 2.2250738585072009E-308,
        2.2250738585072014E-308, 1e-300, 1e-310, double.MaxValue, double.MinValue,
        123456789012345.0, 1234567890123456.0, 0.1 + 0.2,
        -71.05, 42.4999999, 4.35, 1.5, 2.5, 1e16 + 2, 8.41E+21, 4.294967296E+9,
    ];

    [Theory]
    [MemberData(nameof(Branches))]
    public void Format_ABranchValue_WritesWhatTryFormatWrites(double value)
    {
        Formatted(value).Should().Be(Expected(value));
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void Format_ABranchValueAsFloat_WritesWhatTryFormatWrites(double value)
    {
        var single = (float)value;
        if (!float.IsFinite(single))
            return;

        Formatted(single).Should().Be(Expected(single));
    }

    /// <summary>
    ///     Doubles from a fixed seed: eight million random bit patterns, which are mostly far from 1, and eight million
    ///     values with few decimals, the kind a coordinate or a price is.
    /// </summary>
    [Fact]
    public void Format_SeededDoubles_WritesWhatTryFormatWrites()
    {
        var mismatches = new List<string>();
        Parallel.For(0, 64, chunk =>
        {
            var random = new Random(149 * 1000 + chunk);
            Span<byte> actual = stackalloc byte[32];
            Span<byte> expected = stackalloc byte[32];
            for (var n = 0; n < 125_000; n++)
            {
                var bits = (ulong)random.NextInt64() ^ ((ulong)random.Next(2) << 63);
                Compare(BitConverter.UInt64BitsToDouble(bits), actual, expected, mismatches);

                var scale = Math.Pow(10, random.Next(-12, 13));
                Compare(Math.Round(random.NextDouble() * 1000 - 500, random.Next(0, 16)) * scale, actual, expected, mismatches);
            }
        });

        mismatches.Should().BeEmpty();
    }

    /// <summary>
    ///     Every finite binade of float, both signs: the first and last 256 mantissas of each, where the interval
    ///     around a power of two is asymmetric, and every 61st mantissa between them, shifted by the exponent so the low
    ///     bits vary. About 70 million values. The check over all 4,278,190,080 finite floats was run once by hand for
    ///     #149 and matched; it takes longer on a two-core CI runner than the gate allows a test to stay silent.
    /// </summary>
    [Fact]
    public void Format_FloatsAcrossEveryBinade_WritesWhatTryFormatWrites()
    {
        const int mantissas = 1 << 23;
        const int edge = 256;
        const int stride = 61;
        var mismatches = new List<string>();
        Parallel.For(0, 2 * 255, signAndExponent =>
        {
            Span<byte> actual = stackalloc byte[32];
            Span<byte> expected = stackalloc byte[32];
            var sign = (uint)(signAndExponent / 255) << 31;
            var exponent = signAndExponent % 255;
            var prefix = sign | ((uint)exponent << 23);
            for (var mantissa = 0; mantissa < mantissas; mantissa++)
            {
                if (mantissa == edge)
                    mantissa = edge + exponent % stride;
                else if (mantissa > edge && mantissa < mantissas - edge)
                    mantissa = Math.Min(mantissa + stride - 1, mantissas - edge);

                var value = BitConverter.UInt32BitsToSingle(prefix | (uint)mantissa);
                var expectedLength = Write(value, expected);
                var actualLength = ShortestRoundTrip.Format(value, actual);
                if (!actual[..actualLength].SequenceEqual(expected[..expectedLength]))
                    Record(mismatches, value.ToString("R", CultureInfo.InvariantCulture), actual[..actualLength]);
            }
        });

        mismatches.Should().BeEmpty();
    }

    /// <summary>Where the formatter is used: a run's number is the writer's number.</summary>
    [Theory]
    [MemberData(nameof(Branches))]
    public void NumberValue_ADouble_WritesWhatTheWriterWrites(double value)
    {
        var run = Utf8JsonRun.Start();
        try
        {
            run.NumberValue(value);
            Encoding.UTF8.GetString(run.Written).Should().Be(Writer(value));
        }
        finally
        {
            run.Dispose();
        }
    }

    private static void Compare(double value, Span<byte> actual, Span<byte> expected, List<string> mismatches)
    {
        if (!double.IsFinite(value))
            return;

        var expectedLength = Write(value, expected);
        var actualLength = ShortestRoundTrip.Format(value, actual);
        if (!actual[..actualLength].SequenceEqual(expected[..expectedLength]))
            Record(mismatches, value.ToString("R", CultureInfo.InvariantCulture), actual[..actualLength]);
    }

    // The first few are enough to read; the count says how many more.
    private static void Record(List<string> mismatches, string expected, ReadOnlySpan<byte> actual)
    {
        var line = $"{expected} written as {Encoding.UTF8.GetString(actual)}";
        lock (mismatches)
        {
            if (mismatches.Count < 20)
                mismatches.Add(line);
        }
    }

    private static int Write(double value, Span<byte> destination)
    {
        value.TryFormat(destination, out var written, provider: CultureInfo.InvariantCulture);
        return written;
    }

    private static int Write(float value, Span<byte> destination)
    {
        value.TryFormat(destination, out var written, provider: CultureInfo.InvariantCulture);
        return written;
    }

    private static string Expected(double value)
    {
        Span<byte> buffer = stackalloc byte[32];
        return Encoding.UTF8.GetString(buffer[..Write(value, buffer)]);
    }

    private static string Expected(float value)
    {
        Span<byte> buffer = stackalloc byte[32];
        return Encoding.UTF8.GetString(buffer[..Write(value, buffer)]);
    }

    private static string Formatted(double value)
    {
        Span<byte> buffer = stackalloc byte[32];
        return Encoding.UTF8.GetString(buffer[..ShortestRoundTrip.Format(value, buffer)]);
    }

    private static string Formatted(float value)
    {
        Span<byte> buffer = stackalloc byte[32];
        return Encoding.UTF8.GetString(buffer[..ShortestRoundTrip.Format(value, buffer)]);
    }

    private static string Writer(double value)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
            writer.WriteNumberValue(value);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}

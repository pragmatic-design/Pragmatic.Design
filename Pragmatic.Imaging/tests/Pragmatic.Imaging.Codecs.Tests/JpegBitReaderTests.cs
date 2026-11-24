using Pragmatic.Imaging.Codecs.Jpeg;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

/// <summary>
///     The bit reader against a reader written the obvious way, one bit at a time: the same bits, in
///     the same order, through stuffed bytes, past the marker that ends the segment and past the end of
///     the data. The eight-byte fill and the byte-by-byte one both get their turn: the segments hold long
///     runs without 0xFF and stuffed bytes close together.
/// </summary>
public class JpegBitReaderTests
{
    [Theory]
    [InlineData(1, 4000, 40)]
    [InlineData(7, 4000, 0)]
    [InlineData(42, 300, 100)]
    [InlineData(2026, 9, 3)]
    public void Reads_the_same_bits_as_a_bit_by_bit_reader(int seed, int length, int stuffedBytes)
    {
        var random = new Random(seed);
        var segment = Segment(random, length, stuffedBytes);
        var expected = Unstuff(segment);

        var reader = new JpegBitReader(segment, 0);
        var bitIndex = 0;
        // Past the marker the reader hands out zeros: read well beyond the data to see them.
        while (bitIndex < expected.Length * 8 + 200)
        {
            var count = random.Next(1, 17);
            reader.EnsureBits(32);
            var peeked = reader.PeekUnchecked(count);
            var read = reader.ReadBitsUnchecked(count);
            Assert.Equal(peeked, read);
            Assert.Equal(BitsAt(expected, bitIndex, count), read);
            bitIndex += count;
        }

        // The reader stops on the marker, where the caller picks up after the scan.
        Assert.Equal(segment.Length - 2, reader.Position);
    }

    [Fact]
    public unsafe void Fills_from_locals_the_way_the_hot_loop_does()
    {
        var random = new Random(3);
        var segment = Segment(random, 2000, 25);
        var expected = Unstuff(segment);

        var reader = new JpegBitReader(segment, 0);
        reader.EnsureBits(32);
        var (buffer, bitCount, position) = reader.Export();
        var bitIndex = 0;
        fixed (byte* start = segment)
        {
            var cursor = start + position;
            var end = start + segment.Length;
            while (bitIndex < expected.Length * 8)
            {
                if (bitCount < 32 && !JpegBitReader.TryFillFast(ref cursor, end, ref buffer, ref bitCount))
                {
                    reader.Import(buffer, bitCount, (int)(cursor - start));
                    reader.EnsureBits(32);
                    (buffer, bitCount, position) = reader.Export();
                    cursor = start + position;
                }

                Assert.True(bitCount >= 32);
                Assert.True(bitCount == 64 || (buffer << bitCount) == 0, "the bits under the count must be zero");

                var count = random.Next(1, 17);
                var read = (int)(buffer >> (64 - count));
                buffer <<= count;
                bitCount -= count;
                Assert.Equal(BitsAt(expected, bitIndex, count), read);
                bitIndex += count;
            }
        }
    }

    [Fact]
    public void A_count_of_zero_reads_nothing()
    {
        var reader = new JpegBitReader(new byte[] { 0xAB, 0xCD, 0xEF, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC }, 0);
        reader.EnsureBits(32);
        Assert.Equal(0, reader.ReadBitsUnchecked(0));
        Assert.Equal(0, reader.ReadBits(0));
        Assert.Equal(0, reader.ReceiveUnchecked(0));
        Assert.Equal(0xAB, reader.ReadBitsUnchecked(8));
    }

    /// <summary>Random bytes, some stuffed 0xFF 0x00 pairs among them, an 0xFF 0xD9 marker at the end.</summary>
    private static byte[] Segment(Random random, int length, int stuffedBytes)
    {
        var bytes = new List<byte>(length + 2 * stuffedBytes + 2);
        var stuffAt = new HashSet<int>();
        while (stuffAt.Count < Math.Min(stuffedBytes, length))
            stuffAt.Add(random.Next(length));
        for (var i = 0; i < length; i++)
        {
            if (stuffAt.Contains(i))
            {
                bytes.Add(0xFF);
                bytes.Add(0x00);
                continue;
            }

            var b = (byte)random.Next(256);
            if (b == 0xFF)
                b = 0xFE;
            bytes.Add(b);
        }

        bytes.Add(0xFF);
        bytes.Add(0xD9);
        return bytes.ToArray();
    }

    /// <summary>The bytes the segment stands for: stuffing undone, nothing from the marker on.</summary>
    private static byte[] Unstuff(byte[] segment)
    {
        var bytes = new List<byte>(segment.Length);
        for (var i = 0; i < segment.Length; i++)
        {
            if (segment[i] == 0xFF)
            {
                if (i + 1 < segment.Length && segment[i + 1] == 0x00)
                {
                    bytes.Add(0xFF);
                    i++;
                    continue;
                }

                break;
            }

            bytes.Add(segment[i]);
        }

        return bytes.ToArray();
    }

    /// <summary><paramref name="count" /> bits from bit <paramref name="index" /> of the data, most significant first, zeros past the end.</summary>
    private static int BitsAt(byte[] data, int index, int count)
    {
        var value = 0;
        for (var i = 0; i < count; i++)
        {
            var bit = index + i;
            var byteIndex = bit >> 3;
            var b = byteIndex < data.Length ? (data[byteIndex] >> (7 - (bit & 7))) & 1 : 0;
            value = (value << 1) | b;
        }

        return value;
    }
}

using System.Buffers;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The lossy format: a VP8 key frame (RFC 6386) from a <c>VP8 </c> chunk, to its Y, U and V planes. The
///     first partition carries the header and the modes, the token partitions the coefficients, and the
///     loop filter runs over the whole frame once every macroblock is rebuilt, in the order libwebp runs it.
/// </summary>
internal static unsafe class Vp8Decoder
{
    private const int FrameHeaderSize = 10;

    /// <summary>The size of a <c>VP8 </c> chunk's frame, from its first ten bytes.</summary>
    public static (int Width, int Height) ReadHeader(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length < FrameHeaderSize)
            throw new ImageDecodeException("WebP: the lossy frame header is truncated.");
        var tag = chunk[0] | (chunk[1] << 8) | (chunk[2] << 16);
        if ((tag & 1) != 0)
            throw new ImageDecodeException("WebP: the lossy frame is not a key frame.");
        if (((tag >> 1) & 7) > 3)
            throw new ImageDecodeException("WebP: the lossy frame has an unknown profile.");
        if (((tag >> 4) & 1) == 0)
            throw new ImageDecodeException("WebP: the lossy frame is not meant to be shown.");
        if (chunk[3] != 0x9D || chunk[4] != 0x01 || chunk[5] != 0x2A)
            throw new ImageDecodeException("WebP: the lossy frame has no start code.");
        var width = (chunk[6] | (chunk[7] << 8)) & 0x3FFF;
        var height = (chunk[8] | (chunk[9] << 8)) & 0x3FFF;
        if (width == 0 || height == 0)
            throw new ImageDecodeException("WebP: the lossy frame has no pixels.");
        return (width, height);
    }

    public static Vp8Frame Decode(ReadOnlySpan<byte> chunk)
    {
        var (width, height) = ReadHeader(chunk);
        var firstPartition = (chunk[0] | (chunk[1] << 8) | (chunk[2] << 16)) >> 5;
        if (firstPartition > chunk.Length - FrameHeaderSize)
            throw new ImageDecodeException("WebP: the lossy frame's first partition is truncated.");

        fixed (byte* data = chunk)
        {
            var modesReader = new Vp8BoolDecoder(data + FrameHeaderSize, firstPartition);
            var header = Vp8FrameHeader.Read(modesReader);
            var tokens = Partitions(data + FrameHeaderSize + firstPartition, chunk.Length - FrameHeaderSize - firstPartition, header.Partitions);
            var frame = new Vp8Frame(width, height);
            try
            {
                Decode(frame, header, modesReader, tokens);
                return frame;
            }
            catch
            {
                frame.Dispose();
                throw;
            }
        }
    }

    private static Vp8BoolDecoder[] Partitions(byte* data, int length, int count)
    {
        var sizes = 3 * (count - 1);
        if (length < sizes)
            throw new ImageDecodeException("WebP: the lossy frame's partition sizes are truncated.");
        var partitions = new Vp8BoolDecoder[count];
        var start = sizes;
        var left = length - sizes;
        for (var p = 0; p < count - 1; p++)
        {
            var size = data[3 * p] | (data[3 * p + 1] << 8) | (data[3 * p + 2] << 16);
            size = Math.Min(size, left);
            partitions[p] = new Vp8BoolDecoder(data + start, size);
            start += size;
            left -= size;
        }

        if (left <= 0)
            throw new ImageDecodeException("WebP: the lossy frame's last partition is empty.");
        partitions[count - 1] = new Vp8BoolDecoder(data + start, left);
        return partitions;
    }

    private static void Decode(Vp8Frame frame, Vp8FrameHeader header, Vp8BoolDecoder modesReader, Vp8BoolDecoder[] tokens)
    {
        var wide = frame.MacroblocksWide;
        var strengths = header.FilterLevel == 0 ? null : Vp8FilterStrength.ForSegments(header);
        var filters = strengths is null ? null : ArrayPool<Vp8FilterStrength>.Shared.Rent(wide * frame.MacroblocksHigh);
        try
        {
            DecodeMacroblocks(frame, header, modesReader, tokens, strengths, filters);
            if (filters is not null)
                Vp8FrameFilter.Apply(frame, filters, header.SimpleFilter);
        }
        finally
        {
            if (filters is not null)
                ArrayPool<Vp8FilterStrength>.Shared.Return(filters);
        }
    }

    private static void DecodeMacroblocks(Vp8Frame frame, Vp8FrameHeader header, Vp8BoolDecoder modesReader, Vp8BoolDecoder[] tokens,
        Vp8FilterStrength[]? strengths, Vp8FilterStrength[]? filters)
    {
        var wide = frame.MacroblocksWide;
        using var reconstructor = new Vp8Reconstructor(frame);
        var modes = new Vp8MacroblockModes();
        var contexts = ArrayPool<byte>.Shared.Rent(wide * (4 + Vp8Residuals.ContextCount));
        try
        {
            DecodeRows(frame, header, modesReader, tokens, strengths, filters, reconstructor, modes, contexts);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(contexts);
        }
    }

    private static void DecodeRows(Vp8Frame frame, Vp8FrameHeader header, Vp8BoolDecoder modesReader, Vp8BoolDecoder[] tokens,
        Vp8FilterStrength[]? strengths, Vp8FilterStrength[]? filters, Vp8Reconstructor reconstructor, Vp8MacroblockModes modes, byte[] contexts)
    {
        var wide = frame.MacroblocksWide;

        // Above every macroblock column: its four subblock modes, then its non-zero flags. Both start
        // at zero, which is B_DC_PRED and "no coefficients".
        var aboveModes = contexts.AsSpan(0, wide * 4);
        var aboveFlags = contexts.AsSpan(wide * 4, wide * Vp8Residuals.ContextCount);
        aboveModes.Clear();
        aboveFlags.Clear();
        Span<byte> leftModes = stackalloc byte[4];
        Span<byte> leftFlags = stackalloc byte[Vp8Residuals.ContextCount];
        Span<short> coefficients = stackalloc short[384];

        for (var mbY = 0; mbY < frame.MacroblocksHigh; mbY++)
        {
            leftModes.Clear();
            leftFlags.Clear();
            var reader = tokens[mbY & (tokens.Length - 1)];
            for (var mbX = 0; mbX < wide; mbX++)
            {
                modes.Read(modesReader, header, aboveModes.Slice(mbX * 4, 4), leftModes);
                var above = aboveFlags.Slice(mbX * Vp8Residuals.ContextCount, Vp8Residuals.ContextCount);
                var nonZero = 0;
                coefficients.Clear();
                if (!modes.Skip)
                    nonZero = Vp8Residuals.Read(reader, header.CoefficientProbabilities, header.Quantizers[modes.Segment],
                        !modes.IsSubblocks, above, leftFlags, coefficients);
                else
                    Vp8Residuals.Skip(above, leftFlags, !modes.IsSubblocks);

                if (reader.IsPastEnd)
                    throw new ImageDecodeException("WebP: a token partition of the lossy frame is truncated.");

                reconstructor.Reconstruct(mbX, mbY, modes, coefficients, nonZero);
                if (filters is not null)
                {
                    var strength = strengths![modes.Segment * 2 + (modes.IsSubblocks ? 1 : 0)];
                    filters[mbY * wide + mbX] = strength with { Inner = strength.Inner || nonZero != 0 };
                }
            }

            if (modesReader.IsPastEnd)
                throw new ImageDecodeException("WebP: the first partition of the lossy frame is truncated.");
            reconstructor.EndRow();
        }
    }
}

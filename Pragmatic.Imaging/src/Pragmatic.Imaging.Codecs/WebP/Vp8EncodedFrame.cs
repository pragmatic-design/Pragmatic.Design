using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     A written VP8 key frame: the ten bytes of frame tag, start code and dimensions (RFC 6386, section 9.1),
///     the first partition (header and modes) and the one token partition. Dispose it to give the partitions'
///     buffers back.
/// </summary>
internal sealed class Vp8EncodedFrame : IDisposable
{
    private const int TagSize = 10;

    private readonly Vp8BoolEncoder _first;
    private readonly Vp8BoolEncoder _tokens;
    private readonly int _width;
    private readonly int _height;

    private Vp8EncodedFrame(Vp8BoolEncoder first, Vp8BoolEncoder tokens, int width, int height)
    {
        _first = first;
        _tokens = tokens;
        _width = width;
        _height = height;
    }

    public int FirstPartitionLength => _first.Written.Length;

    /// <summary>The frame's size in bytes, as the payload of a <c>VP8 </c> chunk.</summary>
    public int Length => TagSize + _first.Written.Length + _tokens.Written.Length;

    public static Vp8EncodedFrame Write(Vp8Picture picture, Vp8SegmentPlan plan, int filterLevel, Vp8EncodedModes modes, Vp8Tokens tokens)
    {
        var count = picture.MacroblocksWide * picture.MacroblocksHigh;
        var first = new Vp8BoolEncoder(count + 1024);
        var partition = new Vp8BoolEncoder(count * 4);
        try
        {
            first.PutBitUniform(0); // colour space
            first.PutBitUniform(0); // clamping required
            WriteSegmentation(first, plan);

            first.PutBitUniform(0); // the normal loop filter, not the simple one
            first.PutLiteral(filterLevel, 6);
            first.PutLiteral(0, 3); // sharpness
            first.PutBitUniform(0); // no filter deltas by mode or reference

            first.PutLiteral(0, 2); // one token partition
            first.PutLiteral(plan.Segments[0].Quantizer, 7);
            first.PutOptionalSigned(0, 4); // luma DC
            first.PutOptionalSigned(0, 4); // Y2 DC
            first.PutOptionalSigned(0, 4); // Y2 AC
            first.PutOptionalSigned(plan.UvDcDelta, 4);
            first.PutOptionalSigned(plan.UvAcDelta, 4);
            first.PutBitUniform(0); // refresh entropy probabilities: meaningless for a still image
            tokens.WriteProbabilities(first);
            first.PutBitUniform(0); // no skip flags: every macroblock codes its tokens

            modes.Write(first, picture.MacroblocksWide, plan);
            first.Finish();

            tokens.Emit(partition);
            partition.Finish();
            return new Vp8EncodedFrame(first, partition, picture.Width, picture.Height);
        }
        catch
        {
            first.Dispose();
            partition.Dispose();
            throw;
        }
    }

    public void CopyTo(Span<byte> destination)
    {
        // A key frame (bit 0 clear), version 0 (bicubic reconstruction, normal filter), shown, then the first
        // partition's size.
        var tag = (1u << 4) | ((uint)_first.Written.Length << 5);
        destination[0] = (byte)tag;
        destination[1] = (byte)(tag >> 8);
        destination[2] = (byte)(tag >> 16);
        destination[3] = 0x9D;
        destination[4] = 0x01;
        destination[5] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], (ushort)_width);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], (ushort)_height);
        _first.Written.CopyTo(destination[TagSize..]);
        _tokens.Written.CopyTo(destination[(TagSize + _first.Written.Length)..]);
    }

    public void Dispose()
    {
        _first.Dispose();
        _tokens.Dispose();
    }

    private static void WriteSegmentation(Vp8BoolEncoder output, Vp8SegmentPlan plan)
    {
        output.PutBitUniform(plan.Count > 1 ? 1 : 0);
        if (plan.Count <= 1)
            return;

        var updateMap = plan.UpdateMap;
        output.PutBitUniform(updateMap ? 1 : 0);
        output.PutBitUniform(1); // the segments' values follow
        output.PutBitUniform(1); // as absolute values, not deltas
        foreach (var segment in plan.Segments)
            output.PutOptionalSigned(segment.Quantizer, 7);
        foreach (var segment in plan.Segments)
            output.PutOptionalSigned(segment.FilterStrength, 6);
        if (!updateMap)
            return;
        foreach (var probability in plan.Probabilities)
        {
            output.PutBitUniform(probability != 255 ? 1 : 0);
            if (probability != 255)
                output.PutLiteral(probability, 8);
        }
    }
}

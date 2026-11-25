using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>What SOF says: the image size, the components and their geometry in MCUs.</summary>
internal sealed class JpegFrame
{
    private JpegFrame(int width, int height, bool progressive, JpegComponent[] components, JpegScale scale)
    {
        Width = width;
        Height = height;
        Progressive = progressive;
        Components = components;
        Scale = scale;
        var denominator = (int)scale;
        OutputWidth = (width + denominator - 1) / denominator;
        OutputHeight = (height + denominator - 1) / denominator;
        MaxHorizontalFactor = components.Max(c => c.HorizontalFactor);
        MaxVerticalFactor = components.Max(c => c.VerticalFactor);
        McuWidth = 8 * MaxHorizontalFactor;
        McuHeight = 8 * MaxVerticalFactor;
        McusPerLine = (width + McuWidth - 1) / McuWidth;
        McusPerColumn = (height + McuHeight - 1) / McuHeight;

        foreach (var c in components)
        {
            c.Width = (int)Math.Ceiling((double)width * c.HorizontalFactor / MaxHorizontalFactor);
            c.Height = (int)Math.Ceiling((double)height * c.VerticalFactor / MaxVerticalFactor);
            c.ScaledWidth = (c.Width + denominator - 1) / denominator;
            c.ScaledHeight = (c.Height + denominator - 1) / denominator;
            c.BlocksPerLine = (c.Width + 7) / 8;
            c.BlocksPerColumn = (c.Height + 7) / 8;
            c.PaddedBlocksPerLine = McusPerLine * c.HorizontalFactor;
            c.PaddedBlocksPerColumn = McusPerColumn * c.VerticalFactor;
            c.BlockSize = 8 / denominator;
        }
    }

    /// <summary>The image's own size, as the file states it.</summary>
    public int Width { get; }

    public int Height { get; }

    public JpegScale Scale { get; }

    /// <summary>The size decoded: the image's own at <see cref="JpegScale.Full" />, a fraction of it otherwise.</summary>
    public int OutputWidth { get; }

    public int OutputHeight { get; }

    public bool Progressive { get; }

    public JpegComponent[] Components { get; }

    public int MaxHorizontalFactor { get; }

    public int MaxVerticalFactor { get; }

    public int McuWidth { get; }

    public int McuHeight { get; }

    public int McusPerLine { get; }

    public int McusPerColumn { get; }

    public static JpegFrame Parse(byte marker, ReadOnlySpan<byte> segment, JpegScale scale = JpegScale.Full)
    {
        if (segment.Length < 6)
            throw new ImageDecodeException("JPEG frame header is too short.");
        if (segment[0] != 8)
            throw new ImageDecodeException($"JPEG with {segment[0]}-bit samples is not supported; only 8-bit is.");

        var height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
        var width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
        var count = segment[5];
        if (width == 0 || height == 0)
            throw new ImageDecodeException("JPEG frame has a zero dimension (a DNL-defined height is not supported).");
        if (count is not (1 or 3))
            throw new ImageDecodeException($"JPEG with {count} components is not supported; greyscale and YCbCr are.");
        if (segment.Length < 6 + count * 3)
            throw new ImageDecodeException("JPEG frame header is shorter than its component count says.");

        var components = new JpegComponent[count];
        for (var i = 0; i < count; i++)
        {
            var at = 6 + i * 3;
            var h = segment[at + 1] >> 4;
            var v = segment[at + 1] & 15;
            if (h is < 1 or > 4 || v is < 1 or > 4)
                throw new ImageDecodeException($"JPEG component sampling factors {h}x{v} are out of range.");
            if (segment[at + 2] > 3)
                throw new ImageDecodeException("JPEG component refers to a quantisation table above 3.");
            components[i] = new JpegComponent(segment[at], h, v, segment[at + 2]);
        }

        return new JpegFrame(width, height, marker == JpegMarker.Sof2, components, scale);
    }
}

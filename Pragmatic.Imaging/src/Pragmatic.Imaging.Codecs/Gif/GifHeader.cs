using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Gif;

/// <summary>
///     A GIF up to its first frame's image data, read the way the gif crate reads it for the native library: the
///     signature, the logical screen and its colour table, the extensions before the frame, and the frame's
///     descriptor and colour table. An unknown block or extension is an error, and so is a graphic control
///     extension whose data is not four bytes, a frame with no colour table, a minimum code size outside 1 to 11,
///     and a file that ends before the frame's data starts.
/// </summary>
internal readonly record struct GifHeader
{
    private const int ScreenEnd = 13;

    public int Width { get; private init; }

    public int Height { get; private init; }

    public int FrameLeft { get; private init; }

    public int FrameTop { get; private init; }

    public int FrameWidth { get; private init; }

    public int FrameHeight { get; private init; }

    public bool Interlaced { get; private init; }

    /// <summary>The palette index that is transparent, or -1.</summary>
    public int Transparent { get; private init; }

    /// <summary>Where the frame's colour table is, local or global, and its length in bytes.</summary>
    public int PaletteStart { get; private init; }

    public int PaletteLength { get; private init; }

    public int MinimumCodeSize { get; private init; }

    /// <summary>Where the frame's first data sub-block starts.</summary>
    public int DataStart { get; private init; }

    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= 6 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8));

    /// <summary>The logical screen's size alone, which is the image's.</summary>
    public static (int Width, int Height) Identify(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data))
            throw new ImageDecodeException("GIF: the file has no GIF87a or GIF89a signature.");
        if (data.Length < ScreenEnd)
            throw Ended();
        return (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]));
    }

    /// <exception cref="ImageDecodeException">The bytes are not a GIF this decoder reads.</exception>
    public static GifHeader Read(ReadOnlySpan<byte> data)
    {
        var (width, height) = Identify(data);
        var flags = data[10];
        var position = ScreenEnd;
        int globalStart = 0, globalLength = 0;
        if ((flags & 0x80) != 0)
        {
            globalLength = 3 << ((flags & 7) + 1);
            if (data.Length < position + globalLength)
                throw Ended();
            globalStart = position;
            position += globalLength;
        }

        var transparent = -1;
        while (true)
        {
            if (position >= data.Length)
                throw Ended();
            var block = data[position++];
            switch (block)
            {
                case 0x21:
                    transparent = Extension(data, ref position, transparent);
                    continue;
                case 0x2C:
                    return Frame(data, position, width, height, transparent, globalStart, globalLength);
                case 0x3B:
                    throw new ImageDecodeException("GIF: the file has no image.");
                default:
                    throw new ImageDecodeException($"GIF: an unknown block 0x{block:X2}.");
            }
        }
    }

    // An extension's sub-blocks; a graphic control extension's last one sets the frame's transparency.
    private static int Extension(ReadOnlySpan<byte> data, ref int position, int transparent)
    {
        if (position >= data.Length)
            throw Ended();
        var label = data[position++];
        if (label is not (0x01 or 0xF9 or 0xFE or 0xFF))
            throw new ImageDecodeException($"GIF: an unknown extension 0x{label:X2}.");
        var last = ReadOnlySpan<byte>.Empty;
        while (true)
        {
            if (position >= data.Length)
                throw Ended();
            int length = data[position++];
            if (length == 0)
                break;
            if (data.Length < position + length)
                throw Ended();
            last = data.Slice(position, length);
            position += length;
        }

        if (label != 0xF9)
            return transparent;
        if (last.Length != 4)
            throw new ImageDecodeException("GIF: a graphic control extension is not four bytes long.");
        return (last[0] & 1) != 0 ? last[3] : -1;
    }

    private static GifHeader Frame(ReadOnlySpan<byte> data, int position, int width, int height, int transparent, int globalStart,
        int globalLength)
    {
        if (data.Length < position + 9)
            throw Ended();
        var descriptor = data.Slice(position, 9);
        position += 9;
        var flags = descriptor[8];
        int paletteStart = globalStart, paletteLength = globalLength;
        if ((flags & 0x80) != 0)
        {
            paletteLength = 3 << ((flags & 7) + 1);
            if (data.Length < position + paletteLength)
                throw Ended();
            paletteStart = position;
            position += paletteLength;
        }

        if (position >= data.Length)
            throw Ended();
        int minimumCodeSize = data[position++];
        if (minimumCodeSize is < 1 or > 11)
            throw new ImageDecodeException($"GIF: a minimum code size of {minimumCodeSize} is invalid.");
        if (paletteLength == 0)
            throw new ImageDecodeException("GIF: the frame has no colour table.");

        return new GifHeader
        {
            Width = width,
            Height = height,
            FrameLeft = BinaryPrimitives.ReadUInt16LittleEndian(descriptor),
            FrameTop = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[2..]),
            FrameWidth = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[4..]),
            FrameHeight = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[6..]),
            Interlaced = (flags & 0x40) != 0,
            Transparent = transparent,
            PaletteStart = paletteStart,
            PaletteLength = paletteLength,
            MinimumCodeSize = minimumCodeSize,
            DataStart = position,
        };
    }

    private static ImageDecodeException Ended() => new("GIF: the file ends before the image data.");
}

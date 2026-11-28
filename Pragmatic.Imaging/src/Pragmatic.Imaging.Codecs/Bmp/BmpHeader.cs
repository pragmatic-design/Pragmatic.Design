using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Bmp;

/// <summary>
///     The file header and the bitmap header of a BMP, read the way the image crate 0.25 reads them, which is
///     what the native library ran: the core (OS/2 1.x) header and the Windows info, V2, V3, V4 and V5 headers;
///     uncompressed, RLE8, RLE4 and bitfields. JPEG and PNG compression, CMYK, Huffman and the OS/2 2.x header
///     it refuses, and so does this.
/// </summary>
internal readonly record struct BmpHeader
{
    /// <summary>The bytes a palette takes at most here, as R, G, B: 256 entries, since an index is a byte.</summary>
    public const int PaletteBytes = 256 * 3;

    private const int FileHeaderSize = 14;
    private const int MaxSide = 0xFFFF;

    public int Width { get; private init; }

    public int Height { get; private init; }

    /// <summary>Whether the first stored row is the top one: a negative height says so.</summary>
    public bool TopDown { get; private init; }

    public int BitCount { get; private init; }

    public BmpLayout Layout { get; private init; }

    /// <summary>Where the pixels start, as the file header says.</summary>
    public int DataOffset { get; private init; }

    /// <summary>The channels of a bitfields image.</summary>
    public BmpBitfield Red { get; private init; }

    public BmpBitfield Green { get; private init; }

    public BmpBitfield Blue { get; private init; }

    public BmpBitfield Alpha { get; private init; }

    /// <summary>Where the palette starts, its size in entries and the bytes of one entry.</summary>
    private int PaletteStart { get; init; }

    private int PaletteEntries { get; init; }

    private int BytesPerColour { get; init; }

    /// <summary>Whether the bytes start with a BMP's <c>BM</c> signature.</summary>
    public static bool HasSignature(ReadOnlySpan<byte> data) => data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M';

    /// <exception cref="ImageDecodeException">The bytes are not a BMP this decoder reads.</exception>
    public static BmpHeader Read(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data))
            throw new ImageDecodeException("BMP: the file has no BM signature.");
        if (data.Length < FileHeaderSize + 4)
            throw new ImageDecodeException("BMP: the headers are truncated.");
        var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);
        if (headerSize < 12)
            throw new ImageDecodeException($"BMP: the bitmap header is too small ({headerSize} bytes).");
        if (headerSize is not (12 or 40 or 52 or 56 or 108 or 124))
            throw new ImageDecodeException($"BMP: a bitmap header of {headerSize} bytes is not supported.");
        if (data.Length < FileHeaderSize + headerSize)
            throw new ImageDecodeException("BMP: the bitmap header is truncated.");
        if (dataOffset > int.MaxValue)
            throw new ImageDecodeException("BMP: the pixel data offset is out of range.");

        var header = headerSize == 12 ? Core(data, (int)dataOffset) : Info(data, (int)headerSize, (int)dataOffset);
        if (header.Layout is not (BmpLayout.Palette or BmpLayout.Rle8 or BmpLayout.Rle4))
            return header;

        // The palette follows the bitmap header; the image crate checks it, and reads it, even for the size alone.
        var colorsUsed = headerSize == 12 ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(data[(FileHeaderSize + 32)..]);
        var size = colorsUsed == 0 ? 1L << header.BitCount : colorsUsed;
        if (size > 1L << header.BitCount)
            throw new ImageDecodeException($"BMP: a palette of {colorsUsed} colours is larger than {header.BitCount} bits can index.");
        var bytesPerColour = headerSize == 12 ? 3 : 4;
        var entries = (int)Math.Min(size, 256);
        var start = FileHeaderSize + (int)headerSize;
        if (data.Length < start + entries * bytesPerColour)
            throw new ImageDecodeException("BMP: the palette is truncated.");
        return header with { PaletteStart = start, PaletteEntries = entries, BytesPerColour = bytesPerColour };
    }

    /// <summary>Fills <paramref name="palette" /> (<see cref="PaletteBytes" />) as R, G, B; entries past the file's are black.</summary>
    public void ReadPalette(ReadOnlySpan<byte> data, Span<byte> palette)
    {
        palette[..PaletteBytes].Clear();
        for (var i = 0; i < PaletteEntries; i++)
        {
            var entry = data.Slice(PaletteStart + i * BytesPerColour, 3);
            palette[i * 3] = entry[2];
            palette[i * 3 + 1] = entry[1];
            palette[i * 3 + 2] = entry[0];
        }
    }

    private static BmpHeader Core(ReadOnlySpan<byte> data, int dataOffset)
    {
        var dib = data[FileHeaderSize..];
        int width = BinaryPrimitives.ReadUInt16LittleEndian(dib[4..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(dib[6..]);
        if (width == 0 || height == 0)
            throw new ImageDecodeException("BMP: the image has no pixels.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(dib[8..]) != 1)
            throw new ImageDecodeException("BMP: the image has more than one plane.");
        int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[10..]);
        var layout = bitCount switch
        {
            1 or 4 or 8 => BmpLayout.Palette,
            24 => BmpLayout.Rgb24,
            _ => throw new ImageDecodeException($"BMP: {bitCount} bits per pixel is not a core header's depth."),
        };
        return new BmpHeader { Width = width, Height = height, BitCount = bitCount, Layout = layout, DataOffset = dataOffset };
    }

    private static BmpHeader Info(ReadOnlySpan<byte> data, int headerSize, int dataOffset)
    {
        var dib = data[FileHeaderSize..];
        var width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
        if (width < 0)
            throw new ImageDecodeException($"BMP: the width is negative ({width}).");
        if (width > MaxSide || height > MaxSide)
            throw new ImageDecodeException($"BMP: {width} x {height} is larger than the decoder's limit of {MaxSide}.");
        if (height == int.MinValue)
            throw new ImageDecodeException("BMP: the height is out of range.");
        var topDown = height < 0;
        if (topDown)
            height = -height;
        if (width == 0 || height == 0)
            throw new ImageDecodeException("BMP: the image has no pixels.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(dib[12..]) != 1)
            throw new ImageDecodeException("BMP: the image has more than one plane.");

        int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        if (topDown && compression != 0 && compression != 3)
            throw new ImageDecodeException("BMP: a top-down image cannot be compressed.");
        var layout = (compression, bitCount) switch
        {
            (0, 1 or 2 or 4 or 8) => BmpLayout.Palette,
            (0, 16) => BmpLayout.Bitfields,
            (0, 24) => BmpLayout.Rgb24,
            (0, 32) => BmpLayout.Rgb32,
            (1, 8) => BmpLayout.Rle8,
            (2, 4) => BmpLayout.Rle4,
            (3, 16 or 32) => BmpLayout.Bitfields,
            (4, _) => throw new ImageDecodeException("BMP: JPEG compression is not supported."),
            (5, _) => throw new ImageDecodeException("BMP: PNG compression is not supported."),
            (>= 11 and <= 13, _) => throw new ImageDecodeException("BMP: CMYK is not supported."),
            (<= 3, _) => throw new ImageDecodeException($"BMP: {bitCount} bits per pixel does not go with compression {compression}."),
            _ => throw new ImageDecodeException($"BMP: compression {compression} is not supported."),
        };

        var header = new BmpHeader
        {
            Width = width,
            Height = height,
            TopDown = topDown,
            BitCount = bitCount,
            Layout = layout,
            DataOffset = dataOffset,
        };
        if (layout != BmpLayout.Bitfields)
            return header;
        if (compression == 0)
            return header.WithMasks(0x7C00, 0x03E0, 0x001F, 0);

        // The masks follow an info header and sit inside the later ones; only V3 and later carry alpha.
        var at = FileHeaderSize + 40;
        if (data.Length < at + 12)
            throw new ImageDecodeException("BMP: the bitfield masks are truncated.");
        var alpha = headerSize >= 56 ? BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 12)..]) : 0;
        return header.WithMasks(BinaryPrimitives.ReadUInt32LittleEndian(data[at..]), BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 8)..]), alpha);
    }

    private BmpHeader WithMasks(uint red, uint green, uint blue, uint alpha)
    {
        var header = this with
        {
            Red = BmpBitfield.FromMask(red, BitCount),
            Green = BmpBitfield.FromMask(green, BitCount),
            Blue = BmpBitfield.FromMask(blue, BitCount),
            Alpha = BmpBitfield.FromMask(alpha, BitCount),
        };
        if (!header.Red.IsPresent || !header.Green.IsPresent || !header.Blue.IsPresent)
            throw new ImageDecodeException($"BMP: a {BitCount}-bit bitfield mask is missing.");
        return header;
    }
}

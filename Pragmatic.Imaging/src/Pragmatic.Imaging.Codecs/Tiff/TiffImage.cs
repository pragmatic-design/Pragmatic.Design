namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     The first image of a TIFF file, read from its directory as the TIFF crate 0.11 reads it and checked as the
///     image crate 0.25 checks it before decoding: what the native library refused at the header is refused here.
///     Read again for each file, into the same instance and the same chunk lists.
/// </summary>
internal sealed class TiffImage
{
    public const int Uint = 1;
    public const int Int = 2;
    public const int Float = 3;
    public const int WhiteIsZero = 0;
    public const int NoCompression = 1;
    public const int Fax4 = 4;
    public const int Lzw = 5;
    public const int Jpeg = 7;
    public const int Deflate = 8;
    public const int OldDeflate = 0x80B2;
    public const int PackBits = 0x8005;
    public const int HorizontalPredictor = 2;
    public const int FloatPredictor = 3;

    private ulong[] _offsets = new ulong[16];
    private ulong[] _counts = new ulong[16];

    public bool BigEndian { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Bits { get; private set; }

    /// <summary>The samples of a pixel, extra samples included.</summary>
    public int Samples { get; private set; }

    public int SampleFormat { get; private set; }
    public int Photometric { get; private set; }
    public int Compression { get; private set; }
    public int Predictor { get; private set; }
    public bool Planar { get; private set; }
    public bool Tiled { get; private set; }

    /// <summary>A strip's width and rows, or a tile's size.</summary>
    public long ChunkWidth { get; private set; }

    public long ChunkHeight { get; private set; }

    /// <summary>The strips or tiles, the planes one after the other.</summary>
    public int Chunks { get; private set; }

    public ReadOnlySpan<ulong> Offsets => _offsets.AsSpan(0, Chunks);
    public ReadOnlySpan<ulong> ByteCounts => _counts.AsSpan(0, Chunks);

    /// <summary>Where the JPEG tables are in the file, and their length; a length of 0 for none.</summary>
    public (int Start, int Length) JpegTables { get; private set; }

    public TiffColor Color { get; private set; }

    /// <summary>The samples the colour model reads from a pixel: extra samples past it are dropped.</summary>
    public int ColorSamples { get; private set; }

    public TiffPixelFormat Format { get; private set; }

    /// <summary>Whether the bytes start with a classic TIFF header; the native library did not recognise BigTIFF.</summary>
    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && (data[..4].SequenceEqual("II*\0"u8) || data[..4].SequenceEqual("MM\0*"u8));

    /// <exception cref="ImageDecodeException">The header is not one the native library read.</exception>
    public void Read(ReadOnlySpan<byte> data, TiffWorkspace workspace)
    {
        if (!HasSignature(data))
            throw new ImageDecodeException("TIFF: the file does not start with a TIFF header.");
        var reader = new TiffReader(data, data[0] == (byte)'M');
        var first = reader.U32(4);
        var ifd = workspace.Directory;
        ifd.Read(reader, first);
        if (ifd.Next == first)
            throw new ImageDecodeException("TIFF: the first directory is its own successor.");

        var width = (uint)Require(reader, ifd, 256, uint.MaxValue);
        var height = (uint)Require(reader, ifd, 257, uint.MaxValue);
        if (width == 0 || height == 0)
            throw Unsupported("the image has no pixels");

        var photometric = Find(reader, ifd, 262, ushort.MaxValue, pointers: false)
            ?? throw Unsupported("the photometric interpretation is missing");
        if (photometric is not (<= 6 or 8 or 9 or 10))
            throw Unsupported("the photometric interpretation is unknown");
        var compression = (int)(Find(reader, ifd, 259, ushort.MaxValue, pointers: false) ?? NoCompression);

        var tables = (0, 0);
        if (compression == Jpeg && ifd.TryGet(347, out var tablesEntry))
        {
            var bytes = TiffValues.Bytes(reader, tablesEntry);
            if (bytes.Length < 2)
                throw Unsupported("the JPEG tables are too short");
            tables = ((int)tablesEntry.At(reader, 0), bytes.Length);
        }

        var samples = (int)(Find(reader, ifd, 277, ushort.MaxValue, pointers: false) ?? 1);
        if (samples == 0)
            throw Unsupported("a pixel has no samples");
        var extras = ifd.TryGet(338, out var extraEntry) ? TiffValues.Shorts(reader, extraEntry, ref workspace.Shorts) : 0;
        var photometricSamples = samples - extras;
        if (photometricSamples < 0)
            throw Unsupported("there are more extra samples than samples");
        var alpha = extras > 0 && workspace.Shorts[0] is 1 or 2;

        // A sample format, when the file states one: every value the same, and each one within 16 bits.
        int? stated = null;
        if (ifd.TryGet(339, out var formatEntry))
        {
            var count = TiffValues.Unsigned(reader, formatEntry, ref workspace.Values);
            if (count == 0)
                throw Unsupported("the sample format is empty");
            for (var i = 0; i < count; i++)
            {
                if (workspace.Values[i] > ushort.MaxValue)
                    throw Unsupported("the sample format is out of range");
                if (workspace.Values[i] != workspace.Values[0])
                    throw Unsupported("the samples have different formats");
            }

            stated = (int)workspace.Values[0];
        }

        var bitsCount = 1;
        ulong bits = 1;
        if (ifd.TryGet(258, out var bitsEntry))
        {
            bitsCount = TiffValues.Unsigned(reader, bitsEntry, ref workspace.Values);
            bits = bitsCount > 0 ? workspace.Values[0] : 0;
            for (var i = 0; i < bitsCount; i++)
            {
                if (workspace.Values[i] > byte.MaxValue)
                    throw Unsupported("the bits per sample are out of range");
                if (workspace.Values[i] != bits)
                    throw Unsupported("the samples have different depths");
            }
        }

        if (bitsCount != samples && bitsCount != 1)
            throw Unsupported("the bits per sample do not match the samples");
        if (bits == 0)
            throw Unsupported("the samples have no depth");

        var predictor = (int)(Find(reader, ifd, 317, ushort.MaxValue, pointers: false) ?? 1);
        if (predictor is < 1 or > 3)
            throw Unsupported("the predictor is unknown");
        var planar = (int)(Find(reader, ifd, 284, ushort.MaxValue, pointers: false) ?? 1);
        if (planar is not (1 or 2))
            throw Unsupported("the planar configuration is unknown");
        if (ifd.TryGet(530, out var subsamplingEntry))
        {
            var count = TiffValues.Unsigned(reader, subsamplingEntry, ref workspace.Values);
            for (var i = 0; i < count; i++)
            {
                if (workspace.Values[i] > ushort.MaxValue)
                    throw Unsupported("the chroma subsampling is out of range");
            }

            if (count != 2)
                throw Unsupported("the chroma subsampling does not have two values");
        }

        ReadChunks(reader, ifd, width, height, planar == 2 ? samples : 1, workspace);
        var (color, colorSamples) = ColorOf((int)photometric, photometricSamples, samples, alpha);
        Format = FormatOf(color, (int)bits, stated);
        BigEndian = reader.BigEndian;
        Width = (int)Math.Min(width, int.MaxValue);
        Height = (int)Math.Min(height, int.MaxValue);
        Bits = (int)bits;
        Samples = samples;
        SampleFormat = stated ?? Uint;
        Photometric = (int)photometric;
        Compression = compression;
        Predictor = predictor;
        Planar = planar == 2;
        JpegTables = tables;
        Color = color;
        ColorSamples = colorSamples;
    }

    private void ReadChunks(in TiffReader reader, TiffDirectory ifd, uint width, uint height, int planes, TiffWorkspace workspace)
    {
        var strips = ifd.Contains(279) && ifd.Contains(273);
        var tiles = ifd.Contains(325) && ifd.Contains(324);
        if (strips && !ifd.Contains(325) && !ifd.Contains(324))
        {
            var count = Lists(reader, ifd, 273, 279);
            var rows = (uint)(Find(reader, ifd, 278, uint.MaxValue) ?? height);
            if (rows == 0 || (ulong)count != (ulong)((height - 1) / rows + 1) * (ulong)planes)
                throw Unsupported("the strips do not cover the image");
            (Tiled, ChunkWidth, ChunkHeight, Chunks) = (false, width, rows, count);
            return;
        }

        if (tiles && !ifd.Contains(279) && !ifd.Contains(273))
        {
            var tileWidth = (uint)Require(reader, ifd, 322, uint.MaxValue);
            var tileHeight = (uint)Require(reader, ifd, 323, uint.MaxValue);
            if (tileWidth == 0 || tileHeight == 0)
                throw Unsupported("a tile has no pixels");
            var count = Lists(reader, ifd, 324, 325);
            var across = ((ulong)width + tileWidth - 1) / tileWidth;
            var down = ((ulong)height + tileHeight - 1) / tileHeight;
            if ((ulong)count != across * down * (ulong)planes)
                throw Unsupported("the tiles do not cover the image");
            (Tiled, ChunkWidth, ChunkHeight, Chunks) = (true, tileWidth, tileHeight, count);
            return;
        }

        throw Unsupported("the image has neither strips nor tiles, or both");
    }

    // The offsets and the byte counts, which must be as many.
    private int Lists(in TiffReader reader, TiffDirectory ifd, ushort offsetsTag, ushort countsTag)
    {
        ifd.TryGet(offsetsTag, out var offsetsEntry);
        ifd.TryGet(countsTag, out var countsEntry);
        var offsets = TiffValues.Unsigned(reader, offsetsEntry, ref _offsets);
        var counts = TiffValues.Unsigned(reader, countsEntry, ref _counts);
        if (offsets != counts)
            throw Unsupported("the chunk offsets and byte counts differ in number");
        return offsets;
    }

    // The TIFF crate's colour type: RGB and CMYK with an alpha first among the extra samples gain the alpha.
    private static (TiffColor Color, int Samples) ColorOf(int photometric, int photometricSamples, int samples, bool alpha) => photometric switch
    {
        2 when photometricSamples == 3 => alpha ? (TiffColor.Rgba, 4) : (TiffColor.Rgb, 3),
        2 when photometricSamples == 4 => (TiffColor.Rgba, 4),
        5 when photometricSamples == 4 => alpha ? (TiffColor.Cmyka, 5) : (TiffColor.Cmyk, 4),
        5 when photometricSamples == 5 => (TiffColor.Cmyka, 5),
        6 when photometricSamples == 3 => (TiffColor.YCbCr, 3),
        0 or 1 => samples == 1 ? (TiffColor.Gray, 1) : (TiffColor.Multiband, samples),
        9 when photometricSamples == 3 => (TiffColor.Lab, 3),
        _ => throw Unsupported("the photometric interpretation does not fit the samples"),
    };

    // What the image crate makes of it: a sample format, when the file states one, must be unsigned up to 16 bits
    // or float at 32; of the colour models only grey, RGB, RGBA and CMYK are read.
    private static TiffPixelFormat FormatOf(TiffColor color, int bits, int? format)
    {
        if (format is { } stated)
        {
            if (color is not (TiffColor.Gray or TiffColor.Rgb or TiffColor.Rgba or TiffColor.Cmyk))
                throw Unsupported("the colour model with a sample format is not read");
            if (!(stated == Uint && bits <= 16) && !(stated == Float && bits == 32))
                throw Unsupported("the sample format is not read at this depth");
        }

        return (color, bits) switch
        {
            (TiffColor.Gray, 1) => TiffPixelFormat.Gray1,
            (TiffColor.Gray, 8) => TiffPixelFormat.Gray8,
            (TiffColor.Gray, 16) => TiffPixelFormat.Gray16,
            (TiffColor.Rgb, 8) => TiffPixelFormat.Rgb8,
            (TiffColor.Rgb, 16) => TiffPixelFormat.Rgb16,
            (TiffColor.Rgba, 8) => TiffPixelFormat.Rgba8,
            (TiffColor.Rgba, 16) => TiffPixelFormat.Rgba16,
            (TiffColor.Cmyk, 8) => TiffPixelFormat.Cmyk8,
            (TiffColor.Cmyk, 16) => TiffPixelFormat.Cmyk16,
            (TiffColor.Rgb, 32) => TiffPixelFormat.Rgb32Float,
            (TiffColor.Rgba, 32) => TiffPixelFormat.Rgba32Float,
            _ => throw Unsupported("the colour model is not read at this depth"),
        };
    }

    private static ulong Require(in TiffReader reader, TiffDirectory ifd, ushort tag, ulong max) =>
        Find(reader, ifd, tag, max) ?? throw Unsupported("a required tag is missing");

    private static ulong? Find(in TiffReader reader, TiffDirectory ifd, ushort tag, ulong max, bool pointers = true) =>
        ifd.TryGet(tag, out var entry) ? TiffValues.Scalar(reader, entry, max, pointers) : null;

    private static ImageDecodeException Unsupported(string what) => new($"TIFF: {what}.");
}

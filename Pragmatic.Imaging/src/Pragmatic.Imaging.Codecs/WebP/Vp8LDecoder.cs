using System.Buffers;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The lossless format (RFC 9649): transforms, prefix-coded ARGB with backward references and a colour
///     cache, and the entropy image that picks a group of codes per block. It decodes a whole
///     <c>VP8L</c> chunk, or the header-less stream an <c>ALPH</c> chunk carries, straight into the
///     caller's pixels; every working buffer comes from the shared array pool and goes back to it.
/// </summary>
internal sealed class Vp8LDecoder
{
    private const byte Signature = 0x2F;
    private const int LiteralCodes = 256;
    private const int LengthCodes = 24;
    private const int DistanceCodes = 40;
    private const int MaxCacheBits = 11;

    private readonly Vp8LBitReader _reader;
    private readonly int[] _scratch = ArrayPool<int>.Shared.Rent(2 * Vp8LCodeReader.MaxAlphabet);
    private readonly List<uint[]> _rented = [];

    private Vp8LDecoder(Vp8LBitReader reader) => _reader = reader;

    /// <summary>The size and alpha hint of a <c>VP8L</c> chunk's header, from its first five bytes.</summary>
    public static (int Width, int Height, bool HasAlpha) ReadHeader(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length < 5 || chunk[0] != Signature)
            throw new ImageDecodeException("WebP: the lossless stream has no signature.");
        var header = (uint)(chunk[1] | (chunk[2] << 8) | (chunk[3] << 16) | (chunk[4] << 24));
        if (header >> 29 != 0)
            throw new ImageDecodeException("WebP: the lossless stream has a version other than 0.");
        return ((int)(header & 0x3FFF) + 1, (int)((header >> 14) & 0x3FFF) + 1, ((header >> 28) & 1) == 1);
    }

    /// <summary>Decodes a <c>VP8L</c> chunk to ARGB, one value per pixel, row by row, into <paramref name="argb" />.</summary>
    public static unsafe void Decode(ReadOnlySpan<byte> chunk, Span<uint> argb)
    {
        var (width, height, _) = ReadHeader(chunk);
        fixed (byte* data = chunk)
            Run(new Vp8LBitReader(data + 5, chunk.Length - 5), width, height, argb);
    }

    /// <summary>Decodes the header-less stream of an <c>ALPH</c> chunk, of a size the frame implies.</summary>
    public static unsafe void DecodeHeaderless(ReadOnlySpan<byte> stream, int width, int height, Span<uint> argb)
    {
        fixed (byte* data = stream)
            Run(new Vp8LBitReader(data, stream.Length), width, height, argb);
    }

    private static void Run(Vp8LBitReader reader, int width, int height, Span<uint> argb)
    {
        var decoder = new Vp8LDecoder(reader);
        try
        {
            decoder.DecodeImage(width, height, argb[..(width * height)]);
        }
        finally
        {
            decoder.Release();
        }
    }

    private void Release()
    {
        foreach (var array in _rented)
            ArrayPool<uint>.Shared.Return(array);
        _rented.Clear();
        ArrayPool<int>.Shared.Return(_scratch);
    }

    private Span<uint> Rent(int length)
    {
        var array = ArrayPool<uint>.Shared.Rent(length);
        _rented.Add(array);
        return array.AsSpan(0, length);
    }

    private void DecodeImage(int width, int height, Span<uint> output)
    {
        var transforms = new List<Vp8LTransform>();
        var seen = 0;
        var codedWidth = width;
        while (_reader.ReadBits(1) == 1)
        {
            var kind = (int)_reader.ReadBits(2);
            if ((seen & (1 << kind)) != 0)
                throw new ImageDecodeException("WebP: a lossless transform appears twice.");
            seen |= 1 << kind;
            transforms.Add(ReadTransform(kind, ref codedWidth, height));
        }

        // A colour table that packs pixels narrows the coded image: it is decoded aside and widened
        // into the output by the table's inverse; everything else is decoded and inverted in place.
        var pixels = codedWidth == width ? output : Rent(codedWidth * height);
        DecodeStream(codedWidth, height, isMainImage: true, pixels);
        for (var i = transforms.Count - 1; i >= 0; i--)
        {
            var transform = transforms[i];
            if (transform.Kind == Vp8LTransform.ColorIndexing)
            {
                Vp8LInverseTransforms.Index(pixels, output, transform.Width, height, transform.Bits, transform.Data);
                pixels = output;
            }
            else
            {
                Vp8LInverseTransforms.Apply(transform, pixels[..(transform.Width * height)], height);
            }
        }

        if (_reader.IsPastEnd)
            throw new ImageDecodeException("WebP: the lossless stream is truncated.");
    }

    private Vp8LTransform ReadTransform(int kind, ref int width, int height)
    {
        switch (kind)
        {
            case Vp8LTransform.Predictor or Vp8LTransform.CrossColor:
            {
                var bits = (int)_reader.ReadBits(3) + 2;
                var blocksWide = Vp8LInverseTransforms.DivideRoundingUp(width, bits);
                var blocksHigh = Vp8LInverseTransforms.DivideRoundingUp(height, bits);
                var data = Rent(blocksWide * blocksHigh);
                DecodeStream(blocksWide, blocksHigh, isMainImage: false, data);
                return new Vp8LTransform(kind, bits, _rented[^1], width);
            }
            case Vp8LTransform.SubtractGreen:
                return new Vp8LTransform(kind, 0, [], width);
            default:
            {
                var colours = (int)_reader.ReadBits(8) + 1;
                var bits = colours > 16 ? 0 : colours > 4 ? 1 : colours > 2 ? 2 : 3;
                var table = Rent(256);
                table.Clear();
                DecodeStream(colours, 1, isMainImage: false, table[..colours]);
                for (var i = 1; i < colours; i++)
                    table[i] = Vp8LInverseTransforms.Add(table[i], table[i - 1]);
                var transform = new Vp8LTransform(kind, bits, _rented[^1], width);
                width = Vp8LInverseTransforms.DivideRoundingUp(width, bits);
                return transform;
            }
        }
    }

    /// <summary>One entropy-coded image into <paramref name="output" />: the main one, or a sub-image.</summary>
    private void DecodeStream(int width, int height, bool isMainImage, Span<uint> output)
    {
        var cacheBits = 0;
        if (_reader.ReadBits(1) == 1)
        {
            cacheBits = (int)_reader.ReadBits(4);
            if (cacheBits is < 1 or > MaxCacheBits)
                throw new ImageDecodeException("WebP: the colour cache size is out of range.");
        }

        var groupBits = 0;
        Span<uint> groupImage = default;
        var groups = 1;
        if (isMainImage && _reader.ReadBits(1) == 1)
        {
            groupBits = (int)_reader.ReadBits(3) + 2;
            groupImage = Rent(Vp8LInverseTransforms.DivideRoundingUp(width, groupBits) * Vp8LInverseTransforms.DivideRoundingUp(height, groupBits));
            DecodeStream(Vp8LInverseTransforms.DivideRoundingUp(width, groupBits), Vp8LInverseTransforms.DivideRoundingUp(height, groupBits),
                isMainImage: false, groupImage);
            for (var i = 0; i < groupImage.Length; i++)
            {
                var group = (int)(groupImage[i] >> 8) & 0xFFFF;
                groupImage[i] = (uint)group;
                groups = Math.Max(groups, group + 1);
            }
        }

        var cacheSize = cacheBits > 0 ? 1 << cacheBits : 0;
        using var codes = new Vp8LHuffmanCodes(groups * 5);
        var roots = ArrayPool<int>.Shared.Rent(groups * 5);
        try
        {
            for (var g = 0; g < groups; g++)
            {
                roots[g * 5] = Vp8LCodeReader.Read(_reader, codes, LiteralCodes + LengthCodes + cacheSize, _scratch);
                roots[g * 5 + 1] = Vp8LCodeReader.Read(_reader, codes, LiteralCodes, _scratch);
                roots[g * 5 + 2] = Vp8LCodeReader.Read(_reader, codes, LiteralCodes, _scratch);
                roots[g * 5 + 3] = Vp8LCodeReader.Read(_reader, codes, LiteralCodes, _scratch);
                roots[g * 5 + 4] = Vp8LCodeReader.Read(_reader, codes, DistanceCodes, _scratch);
            }

            var cache = cacheBits > 0 ? Rent(cacheSize) : default;
            var stream = new Vp8LPixelStream(_reader, codes.Entries, roots, groupImage, groupBits, cache, cacheBits);
            stream.Decode(output, width, height);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(roots);
        }
    }
}

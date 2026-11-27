// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The pixels of one entropy-coded image (RFC 9649, section 5.2.2): literals, backward references and
///     colour cache hits, each read with the group of codes the entropy image gives its block. This is the
///     lossless decoder's hot loop; the stream's end is checked once a row, not once a symbol.
/// </summary>
internal readonly ref struct Vp8LPixelStream
{
    private const int LiteralCodes = 256;
    private const int LengthCodes = 24;

    private readonly Vp8LBitReader _reader;
    private readonly int[] _entries;
    private readonly int[] _roots;
    private readonly Span<uint> _groupImage;
    private readonly int _groupBits;
    private readonly Span<uint> _cache;
    private readonly int _cacheShift;

    /// <param name="reader">The stream, at the first pixel.</param>
    /// <param name="entries">The block of prefix codes.</param>
    /// <param name="roots">Five root offsets per group: green, red, blue, alpha, distance.</param>
    /// <param name="groupImage">The group of each block, or empty for one group.</param>
    /// <param name="groupBits">The block size bits of <paramref name="groupImage" />.</param>
    /// <param name="cache">The colour cache, or empty for none.</param>
    /// <param name="cacheBits">The colour cache's size bits.</param>
    public Vp8LPixelStream(Vp8LBitReader reader, int[] entries, int[] roots, Span<uint> groupImage, int groupBits, Span<uint> cache, int cacheBits)
    {
        _reader = reader;
        _entries = entries;
        _roots = roots;
        _groupImage = groupImage;
        _groupBits = groupBits;
        _cache = cache;
        _cacheShift = 32 - cacheBits;
        cache.Clear();
    }

    public void Decode(Span<uint> pixels, int width, int height)
    {
        var reader = _reader;
        var entries = _entries;
        var total = width * height;
        var hasGroups = !_groupImage.IsEmpty;
        var hasCache = !_cache.IsEmpty;
        var groupMask = hasGroups ? (1 << _groupBits) - 1 : 0;
        var groupsPerRow = hasGroups ? Vp8LInverseTransforms.DivideRoundingUp(width, _groupBits) : 0;
        var group = -1;
        int greenRoot = 0, redRoot = 0, blueRoot = 0, alphaRoot = 0, distanceRoot = 0;
        int redOnly = -1, blueOnly = -1, alphaOnly = -1;
        var position = 0;
        var x = 0;
        var y = 0;
        while (position < total)
        {
            if (group < 0 || (hasGroups && (x & groupMask) == 0))
            {
                var next = hasGroups ? Group(x, y, groupsPerRow) : 0;
                if (next != group)
                {
                    // A code with one symbol is a constant: libwebp's "trivial literal" shortcut, which
                    // saves the alpha read of every opaque pixel.
                    group = next;
                    greenRoot = _roots[group];
                    redRoot = _roots[group + 1];
                    blueRoot = _roots[group + 2];
                    alphaRoot = _roots[group + 3];
                    distanceRoot = _roots[group + 4];
                    redOnly = Vp8LHuffmanCodes.SingleSymbol(entries, redRoot);
                    blueOnly = Vp8LHuffmanCodes.SingleSymbol(entries, blueRoot);
                    alphaOnly = Vp8LHuffmanCodes.SingleSymbol(entries, alphaRoot);
                }
            }

            var green = Vp8LHuffmanCodes.ReadSymbol(entries, greenRoot, reader);
            if (green < LiteralCodes)
            {
                var red = redOnly >= 0 ? redOnly : Vp8LHuffmanCodes.ReadSymbol(entries, redRoot, reader);
                var blue = blueOnly >= 0 ? blueOnly : Vp8LHuffmanCodes.ReadSymbol(entries, blueRoot, reader);
                var alpha = alphaOnly >= 0 ? alphaOnly : Vp8LHuffmanCodes.ReadSymbol(entries, alphaRoot, reader);
                var argb = ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | (uint)blue;
                pixels[position++] = argb;
                if (hasCache)
                    _cache[(int)((0x1E35A7BDu * argb) >> _cacheShift)] = argb;
                if (++x == width)
                {
                    x = 0;
                    y++;
                    CheckEnd();
                }
            }
            else if (green < LiteralCodes + LengthCodes)
            {
                var length = PrefixValue(green - LiteralCodes);
                var distance = PlaneDistance(PrefixValue(Vp8LHuffmanCodes.ReadSymbol(entries, distanceRoot, reader)), width);
                if (distance > position || length > total - position)
                    throw new ImageDecodeException("WebP: a backward reference points outside the image.");
                Copy(pixels, position, distance, length);
                if (hasCache)
                {
                    for (var i = position; i < position + length; i++)
                        _cache[(int)((0x1E35A7BDu * pixels[i]) >> _cacheShift)] = pixels[i];
                }

                position += length;
                x += length;
                if (x >= width)
                {
                    y += x / width;
                    x %= width;
                    CheckEnd();
                }

                if (hasGroups && (x & groupMask) != 0 && position < total)
                {
                    var next = Group(x, y, groupsPerRow);
                    if (next != group)
                    {
                        group = next;
                        greenRoot = _roots[group];
                        redRoot = _roots[group + 1];
                        blueRoot = _roots[group + 2];
                        alphaRoot = _roots[group + 3];
                        distanceRoot = _roots[group + 4];
                        redOnly = Vp8LHuffmanCodes.SingleSymbol(entries, redRoot);
                        blueOnly = Vp8LHuffmanCodes.SingleSymbol(entries, blueRoot);
                        alphaOnly = Vp8LHuffmanCodes.SingleSymbol(entries, alphaRoot);
                    }
                }
            }
            else
            {
                if (!hasCache)
                    throw new ImageDecodeException("WebP: a colour cache symbol without a colour cache.");
                var index = green - LiteralCodes - LengthCodes;
                pixels[position++] = _cache[index];
                if (++x == width)
                {
                    x = 0;
                    y++;
                    CheckEnd();
                }
            }
        }

        CheckEnd();
    }

    private int Group(int x, int y, int groupsPerRow) => (int)_groupImage[(y >> _groupBits) * groupsPerRow + (x >> _groupBits)] * 5;

    private void CheckEnd()
    {
        if (_reader.IsPastEnd)
            throw new ImageDecodeException("WebP: the lossless stream is truncated.");
    }

    private static void Copy(Span<uint> pixels, int position, int distance, int length)
    {
        if (distance >= length)
        {
            pixels.Slice(position - distance, length).CopyTo(pixels.Slice(position, length));
            return;
        }

        for (var i = 0; i < length; i++)
            pixels[position + i] = pixels[position + i - distance];
    }

    /// <summary>A length or distance from its prefix symbol and the extra bits that follow (RFC 9649, section 5.2.2).</summary>
    private int PrefixValue(int prefix)
    {
        if (prefix < 4)
            return prefix + 1;
        var extraBits = (prefix - 2) >> 1;
        var offset = (2 + (prefix & 1)) << extraBits;
        return offset + (int)_reader.ReadBits(extraBits) + 1;
    }

    /// <summary>A distance code mapped to the pixels back it means: the first 120 are neighbours by (x, y).</summary>
    private static int PlaneDistance(int code, int width)
    {
        if (code > Vp8LDistanceMap.Count)
            return code - Vp8LDistanceMap.Count;
        var (dx, dy) = Vp8LDistanceMap.Offset(code - 1);
        var distance = dx + dy * width;
        return distance < 1 ? 1 : distance;
    }
}

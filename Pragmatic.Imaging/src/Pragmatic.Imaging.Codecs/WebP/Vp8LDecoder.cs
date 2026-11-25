namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The lossless format (RFC 9649): transforms, prefix-coded ARGB with backward references and a colour
///     cache, and the entropy image that picks a group of codes per block. It decodes a whole
///     <c>VP8L</c> chunk, or the header-less stream an <c>ALPH</c> chunk carries.
/// </summary>
internal sealed class Vp8LDecoder
{
    private const byte Signature = 0x2F;
    private const int LiteralCodes = 256;
    private const int LengthCodes = 24;
    private const int DistanceCodes = 40;
    private const int MaxCacheBits = 11;

    private readonly Vp8LBitReader _reader;

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

    /// <summary>Decodes a <c>VP8L</c> chunk to ARGB, one value per pixel, row by row.</summary>
    public static unsafe uint[] Decode(ReadOnlySpan<byte> chunk, out int width, out int height)
    {
        (width, height, _) = ReadHeader(chunk);
        fixed (byte* data = chunk)
        {
            var reader = new Vp8LBitReader(data + 5, chunk.Length - 5);
            return new Vp8LDecoder(reader).DecodeImage(width, height);
        }
    }

    /// <summary>Decodes the header-less stream of an <c>ALPH</c> chunk, of a size the frame implies.</summary>
    public static unsafe uint[] DecodeHeaderless(ReadOnlySpan<byte> stream, int width, int height)
    {
        fixed (byte* data = stream)
        {
            var reader = new Vp8LBitReader(data, stream.Length);
            return new Vp8LDecoder(reader).DecodeImage(width, height);
        }
    }

    private uint[] DecodeImage(int width, int height)
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

        var pixels = DecodeStream(codedWidth, height, isMainImage: true);
        for (var i = transforms.Count - 1; i >= 0; i--)
            pixels = Vp8LInverseTransforms.Apply(transforms[i], pixels, height);

        if (_reader.IsPastEnd)
            throw new ImageDecodeException("WebP: the lossless stream is truncated.");
        return pixels;
    }

    private Vp8LTransform ReadTransform(int kind, ref int width, int height)
    {
        switch (kind)
        {
            case Vp8LTransform.Predictor or Vp8LTransform.CrossColor:
            {
                var bits = (int)_reader.ReadBits(3) + 2;
                var data = DecodeStream(Vp8LInverseTransforms.DivideRoundingUp(width, bits),
                    Vp8LInverseTransforms.DivideRoundingUp(height, bits), isMainImage: false);
                return new Vp8LTransform(kind, bits, data, width);
            }
            case Vp8LTransform.SubtractGreen:
                return new Vp8LTransform(kind, 0, [], width);
            default:
            {
                var colours = (int)_reader.ReadBits(8) + 1;
                var bits = colours > 16 ? 0 : colours > 4 ? 1 : colours > 2 ? 2 : 3;
                var palette = DecodeStream(colours, 1, isMainImage: false);
                for (var i = 1; i < palette.Length; i++)
                    palette[i] = AddPixels(palette[i], palette[i - 1]);
                var transform = new Vp8LTransform(kind, bits, palette, width);
                width = Vp8LInverseTransforms.DivideRoundingUp(width, bits);
                return transform;
            }
        }
    }

    /// <summary>One entropy-coded image: the main one, or a sub-image of a transform or of the entropy image.</summary>
    private uint[] DecodeStream(int width, int height, bool isMainImage)
    {
        var cacheBits = 0;
        if (_reader.ReadBits(1) == 1)
        {
            cacheBits = (int)_reader.ReadBits(4);
            if (cacheBits is < 1 or > MaxCacheBits)
                throw new ImageDecodeException("WebP: the colour cache size is out of range.");
        }

        var groupBits = 0;
        uint[]? groupImage = null;
        var groups = 1;
        if (isMainImage && _reader.ReadBits(1) == 1)
        {
            groupBits = (int)_reader.ReadBits(3) + 2;
            groupImage = DecodeStream(Vp8LInverseTransforms.DivideRoundingUp(width, groupBits),
                Vp8LInverseTransforms.DivideRoundingUp(height, groupBits), isMainImage: false);
            for (var i = 0; i < groupImage.Length; i++)
            {
                var group = (int)(groupImage[i] >> 8) & 0xFFFF;
                groupImage[i] = (uint)group;
                groups = Math.Max(groups, group + 1);
            }
        }

        var cacheSize = cacheBits > 0 ? 1 << cacheBits : 0;
        var codes = new Vp8LHuffmanTable[groups * 5];
        for (var g = 0; g < groups; g++)
        {
            codes[g * 5] = Vp8LCodeReader.Read(_reader, LiteralCodes + LengthCodes + cacheSize);
            codes[g * 5 + 1] = Vp8LCodeReader.Read(_reader, LiteralCodes);
            codes[g * 5 + 2] = Vp8LCodeReader.Read(_reader, LiteralCodes);
            codes[g * 5 + 3] = Vp8LCodeReader.Read(_reader, LiteralCodes);
            codes[g * 5 + 4] = Vp8LCodeReader.Read(_reader, DistanceCodes);
        }

        return DecodePixels(width, height, codes, groupImage, groupBits, cacheBits);
    }

    private uint[] DecodePixels(int width, int height, Vp8LHuffmanTable[] codes, uint[]? groupImage, int groupBits, int cacheBits)
    {
        var total = width * height;
        var pixels = new uint[total];
        var cache = cacheBits > 0 ? new Vp8LColorCache(cacheBits) : null;
        var groupsPerRow = Vp8LInverseTransforms.DivideRoundingUp(width, groupBits);
        var groupMask = groupImage is null ? -1 : (1 << groupBits) - 1;
        var group = 0;
        var cached = 0;
        var position = 0;
        var x = 0;
        var y = 0;
        while (position < total)
        {
            if (groupImage is not null && (x & groupMask) == 0)
                group = (int)groupImage[(y >> groupBits) * groupsPerRow + (x >> groupBits)] * 5;

            var green = codes[group].ReadSymbol(_reader);
            if (green < LiteralCodes)
            {
                var red = codes[group + 1].ReadSymbol(_reader);
                var blue = codes[group + 2].ReadSymbol(_reader);
                var alpha = codes[group + 3].ReadSymbol(_reader);
                pixels[position++] = ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | (uint)blue;
                Advance(ref x, ref y, width, 1);
            }
            else if (green < LiteralCodes + LengthCodes)
            {
                var length = PrefixValue(green - LiteralCodes);
                var distanceCode = PrefixValue(codes[group + 4].ReadSymbol(_reader));
                var distance = PlaneDistance(distanceCode, width);
                if (distance > position || length > total - position)
                    throw new ImageDecodeException("WebP: a backward reference points outside the image.");
                for (var i = 0; i < length; i++)
                    pixels[position + i] = pixels[position + i - distance];
                position += length;
                Advance(ref x, ref y, width, length);
                if (groupImage is not null && length > 0)
                    group = (int)groupImage[(y >> groupBits) * groupsPerRow + (x >> groupBits)] * 5;
            }
            else
            {
                if (cache is null)
                    throw new ImageDecodeException("WebP: a colour cache symbol without a colour cache.");
                while (cached < position)
                    cache.Insert(pixels[cached++]);
                pixels[position++] = cache.Lookup(green - LiteralCodes - LengthCodes);
                Advance(ref x, ref y, width, 1);
            }

            if (cache is not null)
            {
                while (cached < position)
                    cache.Insert(pixels[cached++]);
            }

            if (_reader.IsPastEnd)
                throw new ImageDecodeException("WebP: the lossless stream is truncated.");
        }

        return pixels;
    }

    private static void Advance(ref int x, ref int y, int width, int count)
    {
        x += count;
        while (x >= width)
        {
            x -= width;
            y++;
        }
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

    private static uint AddPixels(uint a, uint b)
    {
        var alphaGreen = (a & 0xFF00FF00) + (b & 0xFF00FF00);
        var redBlue = (a & 0x00FF00FF) + (b & 0x00FF00FF);
        return (alphaGreen & 0xFF00FF00) | (redBlue & 0x00FF00FF);
    }
}

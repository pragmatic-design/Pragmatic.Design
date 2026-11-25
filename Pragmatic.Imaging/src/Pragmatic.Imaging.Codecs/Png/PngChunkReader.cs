using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>
///     Walks the chunks of a PNG in order, checking the signature first and every chunk's CRC as it goes.
///     It locates data; it copies nothing.
/// </summary>
internal ref struct PngChunkReader
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public PngChunkReader(ReadOnlySpan<byte> data)
    {
        if (data.Length < Signature.Length || !data[..Signature.Length].SequenceEqual(Signature))
            throw new ImageDecodeException("The bytes do not start with the PNG signature.");
        _data = data;
        _position = Signature.Length;
    }

    /// <summary>The next chunk, or false after IEND or at the end of the bytes.</summary>
    public bool TryNext(out PngChunk chunk)
    {
        chunk = default;
        if (_position >= _data.Length)
            return false;
        if (_data.Length - _position < 12)
            throw new ImageDecodeException("PNG ends in the middle of a chunk header.");

        var length = BinaryPrimitives.ReadUInt32BigEndian(_data[_position..]);
        if (length > int.MaxValue - 12 || _data.Length - _position - 12 < (int)length)
            throw new ImageDecodeException("PNG ends in the middle of a chunk.");

        var typeOffset = _position + 4;
        var type = BinaryPrimitives.ReadUInt32BigEndian(_data[typeOffset..]);
        var dataOffset = typeOffset + 4;
        var dataLength = (int)length;
        var crcOffset = dataOffset + dataLength;

        var expected = BinaryPrimitives.ReadUInt32BigEndian(_data[crcOffset..]);
        var actual = Crc32.Compute(_data.Slice(typeOffset, 4 + dataLength));
        if (expected != actual)
            throw new ImageDecodeException($"PNG chunk '{TypeName(type)}' fails its CRC check.");

        _position = crcOffset + 4;
        chunk = new PngChunk(type, dataOffset, dataLength);
        return type != PngChunk.Iend;
    }

    private static string TypeName(uint type) => string.Create(4, type, static (span, t) =>
    {
        span[0] = (char)(t >> 24);
        span[1] = (char)((t >> 16) & 0xFF);
        span[2] = (char)((t >> 8) & 0xFF);
        span[3] = (char)(t & 0xFF);
    });
}

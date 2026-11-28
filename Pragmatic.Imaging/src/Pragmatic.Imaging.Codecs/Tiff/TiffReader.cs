using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     A TIFF file's bytes, read in the file's byte order. A read past the end is the file ending where a value was
///     still wanted, which the native decoder reported as an error.
/// </summary>
internal readonly ref struct TiffReader(ReadOnlySpan<byte> data, bool bigEndian)
{
    public ReadOnlySpan<byte> Data { get; } = data;

    public bool BigEndian { get; } = bigEndian;

    public ushort U16(long at)
    {
        var bytes = Bytes(at, 2);
        return BigEndian ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    }

    public uint U32(long at)
    {
        var bytes = Bytes(at, 4);
        return BigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    public ulong U64(long at)
    {
        var bytes = Bytes(at, 8);
        return BigEndian ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    /// <exception cref="ImageDecodeException">The file ends before <paramref name="count" /> bytes from <paramref name="at" />.</exception>
    public ReadOnlySpan<byte> Bytes(long at, long count)
    {
        if (at < 0 || count < 0 || at > Data.Length || count > Data.Length - at)
            throw new ImageDecodeException("TIFF: the file ends before a value it points to.");
        return Data.Slice((int)at, (int)count);
    }
}

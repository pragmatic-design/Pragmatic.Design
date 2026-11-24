using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>One chunk out: length, type, data, and the CRC over type and data.</summary>
internal static class PngChunkWriter
{
    public static void Write(Stream output, uint type, ReadOnlySpan<byte> data)
    {
        Span<byte> head = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(head, data.Length);
        BinaryPrimitives.WriteUInt32BigEndian(head[4..], type);
        output.Write(head);
        output.Write(data);

        var crc = Crc32.Finish(Crc32.Append(Crc32.Append(Crc32.Initial, head[4..]), data));
        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, crc);
        output.Write(tail);
    }
}

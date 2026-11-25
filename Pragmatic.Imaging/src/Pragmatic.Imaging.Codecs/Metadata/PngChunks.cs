using System.Buffers.Binary;
using Pragmatic.Imaging.Codecs.Png;

namespace Pragmatic.Imaging.Codecs.Metadata;

/// <summary>
///     The chunks of a PNG as offsets into the file, without checking CRCs: what moving metadata needs,
///     which copies chunks it does not read. The pixel decoder checks every CRC it depends on.
/// </summary>
internal static class PngChunks
{
    /// <summary>"eXIf": EXIF data in TIFF form.</summary>
    public const uint Exif = 0x65584966;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static int SignatureLength => Signature.Length;

    /// <summary>
    ///     Every chunk up to and including IEND, or, with <paramref name="stopAtImageData" />, up to the
    ///     first IDAT (exclusive), which is as far as metadata that must precede the image data can be.
    ///     With <paramref name="prefixOnly" /> the bytes are the start of a file, and a cut chunk ends
    ///     the list instead of throwing <see cref="ImageDecodeException" />.
    /// </summary>
    public static List<PngChunk> Read(ReadOnlySpan<byte> data, bool stopAtImageData = false, bool prefixOnly = false)
    {
        if (!data.StartsWith(Signature))
            throw new ImageDecodeException("The bytes do not start with the PNG signature.");

        var chunks = new List<PngChunk>();
        var position = Signature.Length;
        while (position < data.Length)
        {
            var remaining = data.Length - position;
            var length = remaining < 12 ? uint.MaxValue : BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            if (length > (uint)(remaining - 12) || remaining < 12)
            {
                if (prefixOnly)
                    break;
                throw new ImageDecodeException("PNG ends in the middle of a chunk.");
            }

            var type = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 4)..]);
            if (stopAtImageData && type == PngChunk.Idat)
                break;

            chunks.Add(new PngChunk(type, position + 8, (int)length));
            position += 12 + (int)length;
            if (type == PngChunk.Iend)
                break;
        }

        return chunks;
    }
}

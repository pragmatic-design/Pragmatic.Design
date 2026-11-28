using System.Buffers;
using Pragmatic.Imaging.Codecs.Jpeg;

namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     Opens a strip or tile with its compression, over the bytes the TIFF crate's reader for it read: an
///     uncompressed or Deflate chunk from its offset to the end of the file, the others bounded by their byte count.
///     The sources are the workspace's, started on the chunk.
/// </summary>
internal static class TiffChunks
{
    public static ITiffChunkSource Open(TiffImage image, ReadOnlyMemory<byte> file, int chunk, TiffWorkspace workspace)
    {
        var offset = image.Offsets[chunk];
        var count = image.ByteCounts[chunk];
        var unbounded = offset >= (ulong)file.Length ? ReadOnlyMemory<byte>.Empty : file[(int)offset..];
        var bounded = unbounded[..(int)Math.Min((ulong)unbounded.Length, count)];
        return image.Compression switch
        {
            TiffImage.NoCompression => workspace.Memory.Start(unbounded),
            TiffImage.Lzw => workspace.Lzw.Start(bounded, count),
            TiffImage.PackBits => workspace.PackBits.Start(bounded),
            TiffImage.Deflate or TiffImage.OldDeflate => new TiffDeflateSource(unbounded),
            TiffImage.Jpeg => Jpeg(image, file.Span, bounded.Span, count, workspace),
            TiffImage.Fax4 => workspace.Fax.Start(bounded, image.ChunkWidth, image.ChunkHeight),
            _ => throw new ImageDecodeException($"TIFF: compression {image.Compression} is not supported."),
        };
    }

    // The chunk is a JPEG stream; with shared tables, the tables without their end marker and then the stream without
    // its start marker. The components come out as they are coded, unconverted.
    private static TiffMemorySource Jpeg(TiffImage image, ReadOnlySpan<byte> file, ReadOnlySpan<byte> chunk, ulong count, TiffWorkspace workspace)
    {
        var (start, length) = image.JpegTables;
        byte[]? joined = null;
        try
        {
            var stream = chunk;
            if (length > 0)
            {
                if (count < 2 || chunk.Length < 2)
                    throw new ImageDecodeException("TIFF: a JPEG strip is shorter than its start marker.");
                joined = ArrayPool<byte>.Shared.Rent(length - 2 + chunk.Length - 2);
                file.Slice(start, length - 2).CopyTo(joined);
                chunk[2..].CopyTo(joined.AsSpan(length - 2));
                stream = joined.AsSpan(0, length - 2 + chunk.Length - 2);
            }

            var pixels = JpegDecoder.DecodeComponents(stream, out var width, out var height, out var components);
            return workspace.Memory.Start(pixels.AsMemory(0, width * height * components), pixels);
        }
        finally
        {
            if (joined is not null)
                ArrayPool<byte>.Shared.Return(joined);
        }
    }
}

using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     A zlib stream from the strip's offset, read as flate2 read it for the TIFF crate: from the file as far as the
///     stream goes, not bounded by the byte count, so what follows a complete stream is never read.
/// </summary>
internal sealed class TiffDeflateSource : ITiffChunkSource
{
    private readonly ZLibStream _inflater;

    public TiffDeflateSource(ReadOnlyMemory<byte> input)
    {
        var stream = MemoryMarshal.TryGetArray(input, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, false)
            : new MemoryStream(input.ToArray(), false);
        _inflater = new ZLibStream(stream, CompressionMode.Decompress);
    }

    public int Read(Span<byte> buffer)
    {
        try
        {
            return _inflater.Read(buffer);
        }
        catch (InvalidDataException e)
        {
            throw new ImageDecodeException("TIFF: the Deflate data is corrupt.", e);
        }
    }

    public void Dispose() => _inflater.Dispose();
}

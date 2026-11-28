namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     The decoded bytes of one strip or tile, read the way the TIFF crate's readers are: a read returns what is
///     there, 0 at the end, and throws on data it cannot decode.
/// </summary>
internal interface ITiffChunkSource : IDisposable
{
    int Read(Span<byte> buffer);
}

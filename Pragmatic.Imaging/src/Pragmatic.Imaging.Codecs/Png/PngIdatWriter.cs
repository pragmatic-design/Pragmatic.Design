namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>
///     A sink for the deflater: compressed bytes arrive here and leave as IDAT chunks of a fixed size,
///     each with its length and CRC, so the whole compressed stream never has to be held to be chunked.
/// </summary>
internal sealed class PngIdatWriter : Stream
{
    private const int ChunkSize = 64 * 1024;

    private readonly Stream _output;
    private readonly byte[] _chunk = new byte[ChunkSize];
    private int _filled;

    public PngIdatWriter(Stream output) => _output = output;

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        while (buffer.Length > 0)
        {
            var count = Math.Min(ChunkSize - _filled, buffer.Length);
            buffer[..count].CopyTo(_chunk.AsSpan(_filled));
            _filled += count;
            buffer = buffer[count..];
            if (_filled == ChunkSize)
                FlushChunk();
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    /// <summary>Writes what is pending as a last, shorter IDAT. Call once, after the deflater is done.</summary>
    public void Finish()
    {
        if (_filled > 0)
            FlushChunk();
    }

    private void FlushChunk()
    {
        PngChunkWriter.Write(_output, PngChunk.Idat, _chunk.AsSpan(0, _filled));
        _filled = 0;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>
///     The IDAT chunks of a PNG read as one stream, in place: the inflater reads the compressed bytes
///     where they are, and no copy of them is made.
/// </summary>
internal sealed class PngIdatStream : Stream
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly List<PngChunk> _segments;
    private int _segment;
    private int _offsetInSegment;

    public PngIdatStream(ReadOnlyMemory<byte> data, List<PngChunk> idatChunks)
    {
        _data = data;
        _segments = idatChunks;
    }

    public override int Read(Span<byte> buffer)
    {
        var total = 0;
        while (buffer.Length > 0 && _segment < _segments.Count)
        {
            var current = _segments[_segment];
            var remaining = current.DataLength - _offsetInSegment;
            if (remaining == 0)
            {
                _segment++;
                _offsetInSegment = 0;
                continue;
            }

            var count = Math.Min(remaining, buffer.Length);
            _data.Span.Slice(current.DataOffset + _offsetInSegment, count).CopyTo(buffer);
            buffer = buffer[count..];
            _offsetInSegment += count;
            total += count;
        }

        return total;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

using System.Text;

namespace Pragmatic.Logging.Benchmarks.Json;

/// <summary>
///     A stream that keeps only the line being written, in a reused buffer: what a sink costs without the
///     cost of somewhere to put it, and readable once by the equivalence check.
/// </summary>
internal sealed class LastLineStream : Stream
{
    private byte[] _buffer = new byte[1024];
    private int _length;
    private bool _complete;

    /// <summary>The last complete line, decoded; for the equivalence check only.</summary>
    public string LastLine => Encoding.UTF8.GetString(_buffer, 0, _length).TrimEnd('\r', '\n');

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_complete)
        {
            _length = 0;
            _complete = false;
        }

        if (_length + buffer.Length > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + buffer.Length));

        buffer.CopyTo(_buffer.AsSpan(_length));
        _length += buffer.Length;
        _complete = buffer.IndexOf((byte)'\n') >= 0;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Flush()
    {
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _length;

    public override long Position
    {
        get => _length;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

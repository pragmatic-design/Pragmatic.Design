using System.Buffers.Text;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     A run of JSON formatted into a buffer as <see cref="System.Text.Json.Utf8JsonWriter" /> would write it, to be
///     written with one <c>WriteRawValue</c>. A run that does not fit says so, and the caller writes it the plain way.
/// </summary>
internal ref struct RawBlock(Span<byte> buffer)
{
    private readonly Span<byte> _buffer = buffer;
    private int _length;
    private bool _overflow;

    public readonly bool Fits => !_overflow;

    public readonly ReadOnlySpan<byte> Written => _buffer[.._length];

    public void Byte(byte value)
    {
        if (_length < _buffer.Length)
            _buffer[_length++] = value;
        else
            _overflow = true;
    }

    public void Bytes(ReadOnlySpan<byte> value)
    {
        if (value.TryCopyTo(_buffer[_length..]))
            _length += value.Length;
        else
            _overflow = true;
    }

    public void Number(int value)
    {
        if (Utf8Formatter.TryFormat(value, _buffer[_length..], out var count))
            _length += count;
        else
            _overflow = true;
    }

    public void Number(long value)
    {
        if (Utf8Formatter.TryFormat(value, _buffer[_length..], out var count))
            _length += count;
        else
            _overflow = true;
    }

    public void Number(decimal value)
    {
        if (Utf8Formatter.TryFormat(value, _buffer[_length..], out var count))
            _length += count;
        else
            _overflow = true;
    }

    public void String(Guid value)
    {
        Byte((byte)'"');
        if (Utf8Formatter.TryFormat(value, _buffer[_length..], out var count))
            _length += count;
        else
            _overflow = true;
        Byte((byte)'"');
    }

    /// <summary>
    ///     A date as the writer writes one: the round-trip format, with the trailing zeros of the fraction trimmed,
    ///     and the fraction gone when it is all zeros.
    /// </summary>
    public void String(DateTimeOffset value)
    {
        Byte((byte)'"');
        var target = _buffer[_length..];
        if (!Utf8Formatter.TryFormat(value, target, out var count, 'O'))
        {
            _overflow = true;
            return;
        }

        // yyyy-MM-ddTHH:mm:ss.fffffff then the offset: the fraction's dot is at 19, its digits run to 26.
        const int dot = 19;
        const int fractionEnd = 27;
        var last = fractionEnd - 1;
        while (last > dot && target[last] == (byte)'0')
            last--;

        var keep = last == dot ? dot : last + 1;
        target[fractionEnd..count].CopyTo(target[keep..]);
        _length += keep + (count - fractionEnd);
        Byte((byte)'"');
    }
}

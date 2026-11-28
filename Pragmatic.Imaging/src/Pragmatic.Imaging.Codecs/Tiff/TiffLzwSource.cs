
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     TIFF's LZW, decoded as weezl 0.1 decodes it for the TIFF crate's reader: most significant bit first, codes of 9
///     to 12 bits that widen one code early, an implicit table before a first clear code, a code past the next one an
///     error, and decoding that stops when the caller has its bytes.
/// </summary>
/// <remarks>
///     weezl fills a 64-bit buffer from the input once per burst of up to six codes, and reports that a call made no
///     progress when it begins on a word's boundary and takes no new input byte, even if it decoded some: once the input
///     is all in that buffer, such a call is an error unless it reaches the end code. The native library refused those
///     strips, so the bursts, the refills and the word held between calls are kept here as weezl keeps them; the bytes
///     decoded are the same either way.
/// </remarks>
internal sealed class TiffLzwSource : ITiffChunkSource
{
    private const int MaxEntries = 4096;
    private const int Clear = 256;
    private const int End = 257;
    private const int Burst = 6;

    private readonly TiffLzwEntry[] _table = new TiffLzwEntry[MaxEntries];
    private readonly byte[] _word = new byte[MaxEntries + 1];
    private ReadOnlyMemory<byte> _input;
    private int _capacity;
    private int _position;
    private int _pieceEnd;
    private ulong _bits;
    private int _held;
    private int _codeSize;
    private int _next;
    private int _wordAt;
    private int _wordEnd;
    private int _link;
    private bool _ended;

    // The literals, which no reset changes; the entries past them are written before they are read.
    public TiffLzwSource()
    {
        for (var code = 0; code < Clear; code++)
        {
            _table[code] = new TiffLzwEntry { Block = (byte)code, Prefix = -1, First = (byte)code, Depth = 1 };
        }
    }

    /// <param name="input">The strip's bytes: the file from the strip's offset, at most its byte count long.</param>
    /// <param name="byteCount">The strip's byte count, which sized the reader's buffer.</param>
    public TiffLzwSource Start(ReadOnlyMemory<byte> input, ulong byteCount)
    {
        _input = input;
        _capacity = (int)Math.Min(32 * 1024, byteCount);
        _position = _pieceEnd = 0;
        _bits = 0;
        _held = 0;
        _codeSize = 9;
        _next = End + 1;
        _wordAt = _wordEnd = 0;
        _link = -1;
        _ended = false;
        return this;
    }

    // LZWReader::read: an advance that made no output and no error is tried again.
    public int Read(Span<byte> buffer)
    {
        while (true)
        {
            if (_position == _pieceEnd && _position < _input.Length)
                _pieceEnd = Math.Min(_input.Length, _position + _capacity);
            var (written, status) = Advance(buffer);
            switch (status)
            {
                case Status.Ok when written == 0:
                    continue;
                case Status.Ok or Status.Done:
                    return written;
                case Status.NoProgress:
                    throw new ImageDecodeException("TIFF: the LZW data ends without an end code.");
                default:
                    throw new ImageDecodeException("TIFF: the LZW data has an invalid code.");
            }
        }
    }

    private (int Written, Status Status) Advance(Span<byte> output)
    {
        if (_ended)
            return (0, Status.Done);
        var start = _position;
        var out0 = output.Length;
        var status = Status.Ok;
        var link = _link;
        _link = -1;

        if (link < 0)
        {
            var code = NextSymbol();
            if (code is null)
                status = Status.NoProgress;
            else if (code > _next || code == _next)
                status = Status.Error;
            else if (code == Clear)
                Reset();
            else if (code == End)
            {
                _ended = true;
                status = Status.Done;
            }
            else
            {
                Hold(code.Value, -1, false);
                link = code.Value;
            }
        }

        var yetToDecode = false;
        if (link >= 0)
        {
            var remain = _wordEnd - _wordAt;
            if (remain > output.Length)
            {
                if (output.IsEmpty)
                    status = Status.NoProgress;
                else
                {
                    _word.AsSpan(_wordAt, output.Length).CopyTo(output);
                    _wordAt += output.Length;
                    output = [];
                }
            }
            else if (remain == 0)
            {
                status = Status.NoProgress;
                yetToDecode = true;
            }
            else
            {
                _word.AsSpan(_wordAt, remain).CopyTo(output);
                _wordAt = _wordEnd;
                output = output[remain..];
            }
        }

        if (_wordEnd == _wordAt)
            Decode(ref output, ref link, ref status, yetToDecode);

        if (_position > start && status == Status.NoProgress)
            status = Status.Ok;
        _link = link;
        return (out0 - output.Length, status);
    }

    // The table's size, the code width and the bit buffer live in locals for the loop and go back to the fields when it
    // ends: through the fields, every write would be read back after each call the loop makes.
    private void Decode(ref Span<byte> output, ref int link, ref Status status, bool yetToDecode)
    {
        ref var table = ref MemoryMarshal.GetArrayDataReference(_table);
        var input = _input.Span;
        var next = _next;
        var codeSize = _codeSize;
        var bits = _bits;
        var held = _held;
        var position = _position;
        while (!output.IsEmpty && link >= 0)
        {
            var deriv = link;
            link = -1;

            // Refill: as many whole bytes as the buffer takes, from what the reader's buffer holds.
            var fill = Math.Min((64 - held) >> 3, _pieceEnd - position);
            if (fill > 0)
            {
                if (position + 8 <= input.Length)
                {
                    // Eight bytes at once, those past the ones taken masked off.
                    var word = BinaryPrimitives.ReadUInt64BigEndian(input[position..]) & (ulong.MaxValue << (64 - fill * 8));
                    bits |= word >> held;
                }
                else
                {
                    for (var i = 0; i < fill; i++)
                        bits |= (ulong)input[position + i] << (56 - held - i * 8);
                }

                position += fill;
                held += fill * 8;
            }

            var count = Math.Min(Burst, held / codeSize);
            if (count == 0)
            {
                if (yetToDecode)
                    status = Status.NoProgress;
                link = deriv;
                break;
            }

            // A burst: codes already in the table before it, independent of each other, that fit where they go, each
            // written as it is read. The first that is not ends it and is handled on its own; a burst that ends with
            // its last code has nothing more to handle, as weezl's handling of that code is what writing it did. What
            // the burst decides with is the table as it stood when the burst began, so the refills come where weezl's
            // do.
            var tableSize = next;
            var left = Math.Max(0, (1 << codeSize) - 1 - 1 - tableSize);
            ref var target = ref MemoryMarshal.GetReference(output);
            var room = output.Length;
            var written = 0;
            var size = count;
            var newCode = -1;
            for (var i = 0; i < count; i++)
            {
                var code = (int)((bits << (i * codeSize)) >> (64 - codeSize));
                if (i + 1 > left || code is Clear or End || code >= tableSize || room - written <= Unsafe.Add(ref table, code).Depth)
                {
                    newCode = code;
                    size = i + 1;
                    break;
                }

                Spell(ref table, code, ref Unsafe.Add(ref target, written));
                Derive(ref table, ref next, deriv, Unsafe.Add(ref table, code).First);
                deriv = code;
                written += Unsafe.Add(ref table, code).Depth;
            }

            var consumed = size * codeSize;
            bits = consumed >= held ? 0 : bits << consumed;
            held -= consumed;
            yetToDecode = false;
            output = output[written..];
            if (newCode < 0)
            {
                link = deriv;
                continue;
            }
            if (newCode == Clear)
            {
                next = End + 1;
                codeSize = 9;
                _wordAt = _wordEnd = 0;
                break;
            }

            if (newCode == End)
            {
                _ended = true;
                status = Status.Done;
                break;
            }

            if (newCode > next)
            {
                status = Status.Error;
                break;
            }

            var defining = newCode == next;
            var required = defining ? Unsafe.Add(ref table, deriv).Depth + 1 : Unsafe.Add(ref table, newCode).Depth;
            var heldWord = required > output.Length;
            byte first;
            if (heldWord)
            {
                first = Hold(newCode, deriv, defining);
            }
            else
            {
                first = defining ? Unsafe.Add(ref table, deriv).First : Unsafe.Add(ref table, newCode).First;
                if (defining)
                {
                    Spell(ref table, deriv, ref MemoryMarshal.GetReference(output));
                    output[required - 1] = first;
                }
                else
                {
                    Spell(ref table, newCode, ref MemoryMarshal.GetReference(output));
                }

                output = output[required..];
            }

            if (next < MaxEntries)
            {
                Derive(ref table, ref next, deriv, first);
                if (next - 1 >= (1 << codeSize) - 1 - 1 && codeSize < 12)
                    codeSize++;
            }

            link = newCode;
            if (heldWord || output.IsEmpty)
                break;
        }

        _next = next;
        _codeSize = codeSize;
        _bits = bits;
        _held = held;
        _position = position;
    }

    // A word that does not fit where it goes waits in the word buffer for the next call; for the code being defined it
    // is the previous word and that word's first byte.
    private byte Hold(int code, int previous, bool defining)
    {
        ref var table = ref MemoryMarshal.GetArrayDataReference(_table);
        int length;
        if (defining && previous >= 0)
        {
            length = Entry(previous).Depth + 1;
            Spell(ref table, previous, ref MemoryMarshal.GetArrayDataReference(_word));
            _word[length - 1] = Entry(previous).First;
        }
        else
        {
            length = Entry(code).Depth;
            Spell(ref table, code, ref MemoryMarshal.GetArrayDataReference(_word));
        }

        _wordAt = 0;
        _wordEnd = length;
        return _word[0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Derive(ref TiffLzwEntry table, ref int next, int previous, byte first)
    {
        if (next >= MaxEntries)
            return;
        ref readonly var from = ref Unsafe.Add(ref table, previous);
        ref var entry = ref Unsafe.Add(ref table, next);
        var filled = from.Depth & 7;
        if (filled != 0)
        {
            // The previous word's last block has room: the byte goes on its end.
            entry.Block = from.Block | ((ulong)first << (filled * 8));
            entry.Prefix = from.Prefix;
        }
        else
        {
            entry.Block = first;
            entry.Prefix = (short)previous;
        }

        entry.First = from.First;
        entry.Depth = (short)(from.Depth + 1);
        next++;
    }

    // The word of a code: its last block byte by byte, then the whole blocks before it eight bytes at a time, back to
    // the front. The callers have made room for its whole length, so it writes without checks; a reference, not a
    // span, so nothing goes through memory per call.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Spell(ref TiffLzwEntry table, int code, ref byte target)
    {
        ref readonly var entry = ref Unsafe.Add(ref table, code);
        int depth = entry.Depth;
        var tail = ((depth - 1) & 7) + 1;
        var at = depth - tail;
        var block = entry.Block;
        for (var i = 0; i < tail; i++, block >>= 8)
            Unsafe.Add(ref target, at + i) = (byte)block;
        for (var c = entry.Prefix; c >= 0;)
        {
            ref readonly var full = ref Unsafe.Add(ref table, c);
            at -= 8;
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref target, at), BitConverter.IsLittleEndian ? full.Block : BinaryPrimitives.ReverseEndianness(full.Block));
            c = full.Prefix;
        }
    }

    // A code read from the data is under 4,096, as is every entry it names: the table is read without checks.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref TiffLzwEntry Entry(int code) => ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_table), code);

    private void Reset()
    {
        _codeSize = 9;
        _next = End + 1;
        _wordAt = _wordEnd = 0;
    }

    private int? NextSymbol()
    {
        if (_held < _codeSize)
            Refill();
        if (_held < _codeSize)
            return null;
        var code = (int)(_bits >> (64 - _codeSize));
        Consume(1);
        return code;
    }

    // As many whole bytes as the 64-bit buffer takes, from what the reader's buffer holds.
    private void Refill()
    {
        var input = _input.Span;
        var count = Math.Min((64 - _held) / 8, _pieceEnd - _position);
        for (var i = 0; i < count; i++)
            _bits |= (ulong)input[_position + i] << (56 - _held - i * 8);
        _position += count;
        _held += count * 8;
    }

    private void Consume(int codes)
    {
        var bits = codes * _codeSize;
        _bits = bits >= _held ? 0 : _bits << bits;
        _held -= bits;
    }

    // The tables stay with the workspace for the next strip.
    public void Dispose() => _input = default;

    private enum Status
    {
        Ok,
        NoProgress,
        Done,
        Error,
    }
}

namespace Pragmatic.Imaging.Codecs.Gif;

/// <summary>
///     LZW for a GIF frame, writing the codes weezl writes for the gif crate, which the native library ran: a
///     clear code first; a code size that grows once the newest entry's code reaches its width, up to 12 bits; a
///     clear code, and a fresh table, once the table passes 4,096 entries; the last code, an end code and the
///     final byte padded with zeros. Codes are packed least significant bit first, into 255-byte sub-blocks.
/// </summary>
internal sealed class GifLzwEncoder
{
    private const int MaxEntries = 4096;
    private const int MaxCodeSize = 12;
    private const int Slots = 1 << 13;

    // An open-addressed map from (prefix code, byte) to the entry's code; a slot is free when its stamp is not
    // the current one, so a clear empties it without touching it.
    private readonly int[] _keys = new int[Slots];
    private readonly short[] _codes = new short[Slots];
    private readonly int[] _stamps = new int[Slots];
    private int _stamp;

    // How many successors each code has had. weezl keeps the first 16 in a short list; the 17th moves the code to a
    // full table without entering itself, so its entry is numbered but never found, and the 18th on are entered.
    // The file is byte for byte the native one only if this encoder loses that one string too.
    private readonly byte[] _successors = new byte[MaxEntries + 1];
    private const int ShortList = 16;

    /// <summary>Writes the minimum code size, then the frame's sub-blocks and their terminator.</summary>
    public void Encode(ReadOnlySpan<byte> indices, int minimumSize, GifOutput output)
    {
        output.Byte((byte)minimumSize);
        var writer = new BlockWriter(output);
        var clear = 1 << minimumSize;
        var end = clear + 1;
        var codeSize = minimumSize + 1;
        var entries = clear + 2;
        NewTable();
        writer.Code(clear, codeSize);

        var current = clear;
        foreach (var value in indices)
        {
            if (current == clear)
            {
                current = value;
                continue;
            }

            var key = (current << 8) | value;
            var slot = Find(key);
            if (_stamps[slot] == _stamp)
            {
                current = _codes[slot];
                continue;
            }

            // A new string: its prefix is written, and it becomes the next entry.
            var successors = _successors[current];
            if (successors != ShortList)
            {
                _keys[slot] = key;
                _codes[slot] = (short)entries;
                _stamps[slot] = _stamp;
            }

            if (successors <= ShortList)
                _successors[current] = (byte)(successors + 1);
            entries++;
            writer.Code(current, codeSize);
            if (entries > 1 << codeSize && codeSize < MaxCodeSize)
                codeSize++;
            if (entries > MaxEntries)
            {
                writer.Code(clear, codeSize);
                NewTable();
                entries = clear + 2;
                codeSize = minimumSize + 1;
            }

            current = value;
        }

        if (current != clear)
        {
            writer.Code(current, codeSize);

            // Reading this code, the decoder adds one more entry, which may widen its next code.
            if (entries > (1 << codeSize) - 1 && codeSize < MaxCodeSize)
                codeSize++;
        }

        writer.Code(end, codeSize);
        writer.Finish();
    }

    private void NewTable()
    {
        Array.Clear(_successors);
        _stamp++;
        if (_stamp != 0)
            return;
        Array.Clear(_stamps);
        _stamp = 1;
    }

    private int Find(int key)
    {
        var slot = (int)(((uint)key * 2654435761u) >> 19);
        while (_stamps[slot] == _stamp && _keys[slot] != key)
            slot = (slot + 1) & (Slots - 1);
        return slot;
    }

    /// <summary>Codes packed least significant bit first, into sub-blocks of 255 bytes and a shorter last one.</summary>
    private ref struct BlockWriter(GifOutput output)
    {
        private readonly GifOutput _output = output;
        private uint _bits;
        private int _held;

        public void Code(int code, int size)
        {
            _bits |= (uint)code << _held;
            _held += size;
            while (_held >= 8)
            {
                _output.Data((byte)_bits);
                _bits >>= 8;
                _held -= 8;
            }
        }

        public void Finish()
        {
            if (_held > 0)
                _output.Data((byte)_bits);
            _output.EndData();
        }
    }
}

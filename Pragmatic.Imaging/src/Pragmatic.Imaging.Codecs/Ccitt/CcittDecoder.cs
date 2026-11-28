namespace Pragmatic.Imaging.Codecs.Ccitt;

/// <summary>
///     Decodes CCITT fax data a line at a time: Group 4 (T.6), Group 3 one-dimensional (T.4 modified Huffman) and
///     Group 3 two-dimensional (T.4 modified READ), the codings of TIFF's compressions 2 to 4 and of PDF's
///     CCITTFaxDecode. A line comes out one bit per pixel, most significant first, 1 for black.
/// </summary>
/// <remarks>
///     Two-dimensional lines are decoded as the fax crate 0.2 decodes them for the TIFF crate, which the native
///     library ran: the same reading of the reference line, the same leniency at its end.
/// </remarks>
public sealed class CcittDecoder
{
    private ReadOnlyMemory<byte> _data;
    private int _columns;
    private int _k;
    private bool _byteAligned;
    private List<int> _reference = [];
    private List<int> _current = [];
    private long _bit;
    private bool _ended;

    /// <param name="data">The coded data.</param>
    /// <param name="columns">The pixels of a line.</param>
    /// <param name="k">Below 0 Group 4; 0 Group 3 one-dimensional; above 0 Group 3 with a tag bit choosing each line's coding.</param>
    /// <param name="byteAligned">Whether each line starts on a byte boundary.</param>
    public CcittDecoder(ReadOnlyMemory<byte> data, int columns, int k = -1, bool byteAligned = false) => Start(data, columns, k, byteAligned);

    // A decoder kept for reuse, which Start points at its data.
    internal CcittDecoder()
    {
    }

    /// <summary>Starts over on other data, keeping the lists of changes.</summary>
    internal void Start(ReadOnlyMemory<byte> data, int columns, int k, bool byteAligned)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        _data = data;
        _columns = columns;
        _k = k;
        _byteAligned = byteAligned;
        _reference.Clear();
        _current.Clear();
        _bit = 0;
        _ended = false;
    }

    /// <summary>Decodes the next line into <paramref name="row" />, which holds at least (columns + 7) / 8 bytes.</summary>
    /// <returns>False at the end of the data: an end of block, a return to control, or no bits left at a line's start.</returns>
    /// <exception cref="ImageDecodeException">The data is not a valid line.</exception>
    public bool ReadLine(Span<byte> row)
    {
        if (_ended)
            return false;
        if (_byteAligned)
            _bit = (_bit + 7) & ~7L;

        var twoDimensional = _k < 0;
        if (_k >= 0)
        {
            if (!SkipEndsOfLine())
                return false;
            if (_k > 0)
            {
                if (Remaining < 1)
                    return false;
                twoDimensional = Peek(1) == 0;
                _bit++;
            }
        }
        else if (Remaining == 0)
        {
            return false;
        }

        _current.Clear();
        if (twoDimensional)
        {
            if (!DecodeTwoDimensional())
            {
                _ended = true;
                return false;
            }
        }
        else
        {
            DecodeOneDimensional();
        }

        (_reference, _current) = (_current, _reference);
        Render(_reference, row);
        return true;
    }

    private long Remaining => (long)_data.Length * 8 - _bit;

    // A Group 3 line may follow fill and an end-of-line code; six in a row are the end of the page.
    private bool SkipEndsOfLine()
    {
        var ends = 0;
        while (true)
        {
            if (Remaining < 12)
                return Remaining > 0 && Peek((int)Remaining) != 0;
            if (Peek(12) == 1)
            {
                _bit += 12;
                if (++ends == 6)
                {
                    _ended = true;
                    return false;
                }

                continue;
            }

            if (Peek(12) == 0)
            {
                // Fill before an end-of-line code.
                _bit++;
                continue;
            }

            return true;
        }
    }

    private void DecodeOneDimensional()
    {
        var a0 = 0;
        var white = true;
        while (a0 < _columns)
        {
            a0 += Run(white) ?? throw Invalid();
            _current.Add(a0);
            white = !white;
        }
    }

    // The fax crate's Group 4 line: the changes on the reference line are walked with a cursor, a missing b1 is the
    // line's end, and a vertical or horizontal change at or past the end closes the line.
    private bool DecodeTwoDimensional()
    {
        var position = 0;
        var a0 = 0;
        var white = true;
        var startOfRow = true;
        while (true)
        {
            var mode = Code(CcittCodes.Modes) ?? throw Invalid();
            switch (mode)
            {
                case CcittCodes.Pass:
                    if (startOfRow && white)
                        position++;
                    else if (NextChange(ref position, a0, black: white, startOfRow: false) is null)
                        throw Invalid();
                    if (position < _reference.Count)
                        a0 = _reference[position++];
                    break;
                case >= CcittCodes.VerticalZero - 3 and <= CcittCodes.VerticalZero + 3:
                {
                    var b1 = NextChange(ref position, a0, black: white, startOfRow) ?? _columns;
                    var a1 = (ushort)(b1 + mode - CcittCodes.VerticalZero);
                    if (a1 >= _columns)
                        return true;
                    _current.Add(a1);
                    white = !white;
                    a0 = a1;
                    if (mode < CcittCodes.VerticalZero)
                        SeekBack(ref position, a0);
                    break;
                }
                case CcittCodes.Horizontal:
                {
                    var first = Run(white) ?? throw Invalid();
                    var second = Run(!white) ?? throw Invalid();
                    var a1 = (ushort)(a0 + first);
                    var a2 = (ushort)(a1 + second);
                    _current.Add(a1);
                    if (a2 >= _columns)
                        return true;
                    _current.Add(a2);
                    a0 = a2;
                    break;
                }
                case CcittCodes.Extension:
                    throw new ImageDecodeException("CCITT: the data uses an extension mode, which is not supported.");
                default:
                    return false;
            }

            startOfRow = false;
            if (a0 >= _columns)
                return true;
        }
    }

    // b1: the next change on the reference line past a0 to the colour opposite a0's; at a line's start, the first
    // change to black or the first back to white.
    private int? NextChange(ref int position, int start, bool black, bool startOfRow)
    {
        if (startOfRow)
        {
            position = black ? 1 : 2;
            var index = black ? 0 : 1;
            return index < _reference.Count ? _reference[index] : null;
        }

        while (position < _reference.Count)
        {
            if (_reference[position] <= start)
            {
                position++;
                continue;
            }

            if ((position % 2 == 0) != black)
                position++;
            break;
        }

        return position < _reference.Count ? _reference[position++] : null;
    }

    private void SeekBack(ref int position, int start)
    {
        position = Math.Min(position, Math.Max(_reference.Count - 1, 0));
        while (position > 0 && start < _reference[position - 1])
            position--;
    }

    private int? Run(bool white)
    {
        var table = white ? CcittCodes.White : CcittCodes.Black;
        ushort sum = 0;
        while (Code(table) is { } length)
        {
            sum += (ushort)length;
            if (length < 64)
                return sum;
        }

        return null;
    }

    private int? Code((short Value, byte Length)[] table)
    {
        var available = (int)Math.Min(Remaining, CcittCodes.PeekBits);
        if (available <= 0)
            return null;
        var bits = Peek(available) << (CcittCodes.PeekBits - available);
        var (value, length) = table[bits];
        if (length == 0 || length > available)
            return null;
        _bit += length;
        return value;
    }

    private int Peek(int count)
    {
        var data = _data.Span;
        var value = 0;
        for (var i = 0; i < count; i++)
        {
            var at = _bit + i;
            value = (value << 1) | ((data[(int)(at >> 3)] >> (7 - (int)(at & 7))) & 1);
        }

        return value;
    }

    // As the fax crate's pels: from white, each change ends a run as long as it is past the previous change (none if it
    // is not), the runs laid end to end; the rest of the line takes the colour after the last change.
    private void Render(List<int> changes, Span<byte> row)
    {
        row[..((_columns + 7) / 8)].Clear();
        var x = 0;
        var last = 0;
        var black = false;
        foreach (var change in changes)
        {
            var run = Math.Max(0, change - last);
            last = change;
            var end = (int)Math.Min((long)x + run, _columns);
            if (black)
                Fill(row, x, end);
            x = end;
            black = !black;
        }

        if (black)
            Fill(row, x, _columns);
    }

    private static void Fill(Span<byte> row, int from, int to)
    {
        for (var x = from; x < to; x++)
            row[x >> 3] |= (byte)(0x80 >> (x & 7));
    }

    private static ImageDecodeException Invalid() => new("CCITT: the data is not a valid fax line.");
}

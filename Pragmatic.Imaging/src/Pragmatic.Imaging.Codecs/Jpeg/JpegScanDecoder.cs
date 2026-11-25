using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     Decodes one scan's entropy-coded segment. A sequential scan dequantises and transforms each
///     block as it is decoded, straight into the component's plane; a progressive scan adds what it
///     carries to the component's coefficients, for the transform to happen once, after the last scan.
/// </summary>
internal ref partial struct JpegScanDecoder
{
    /// <summary>What a block of one component needs, resolved once per scan instead of once per block.</summary>
    private readonly struct ComponentTables
    {
        public ComponentTables(JpegComponent component, JpegHuffmanTable dc, JpegHuffmanTable ac, ushort[] quantization)
        {
            Component = component;
            Dc = dc;
            Ac = ac;
            Quantization = quantization;
        }

        public JpegComponent Component { get; }
        public JpegHuffmanTable Dc { get; }
        public JpegHuffmanTable Ac { get; }
        public ushort[] Quantization { get; }
    }

    private readonly JpegFrame _frame;
    private readonly JpegScan _scan;
    private readonly JpegHuffmanTable?[] _dcTables;
    private readonly JpegHuffmanTable?[] _acTables;
    private readonly ushort[][] _quantization;
    private readonly int _restartInterval;
    private JpegBitReader _reader;
    private int _eobRun;
    private unsafe byte* _dataStart;
    private unsafe byte* _dataEnd;

    public JpegScanDecoder(ReadOnlySpan<byte> data, int position, JpegFrame frame, JpegScan scan,
        JpegHuffmanTable?[] dcTables, JpegHuffmanTable?[] acTables, ushort[][] quantization, int restartInterval)
    {
        _frame = frame;
        _scan = scan;
        _dcTables = dcTables;
        _acTables = acTables;
        _quantization = quantization;
        _restartInterval = restartInterval;
        _reader = new JpegBitReader(data, position);
    }

    /// <summary>Decodes the scan and returns the position of the byte after its entropy-coded data.</summary>
    public unsafe int Decode()
    {
        foreach (var c in _scan.Components)
            c.DcPrediction = 0;
        _eobRun = 0;

        var tables = new ComponentTables[_scan.Components.Length];
        for (var i = 0; i < tables.Length; i++)
            tables[i] = Resolve(_scan.Components[i]);

        // Pinned once for the scan: the block decoder fills its bit buffer straight from the data.
        fixed (byte* start = _reader.Data)
        {
            _dataStart = start;
            _dataEnd = start + _reader.Data.Length;
            if (tables.Length == 1)
                DecodeNonInterleaved(in tables[0]);
            else
                DecodeInterleaved(tables);
        }

        return _reader.Position;
    }

    private ComponentTables Resolve(JpegComponent c)
    {
        // A progressive AC scan has no DC table and a DC scan no AC table; a missing one is only an
        // error when a block asks for it, which the sequential path always does.
        var needsBoth = !_frame.Progressive;
        var dc = _dcTables[c.DcTableId];
        var ac = _acTables[c.AcTableId];
        if (dc is null && (needsBoth || _scan.SpectralStart == 0) && _scan.ApproximationHigh == 0)
            throw new ImageDecodeException($"JPEG scan uses DC Huffman table {c.DcTableId}, which was not defined.");
        if (ac is null && (needsBoth || _scan.SpectralStart > 0))
            throw new ImageDecodeException($"JPEG scan uses AC Huffman table {c.AcTableId}, which was not defined.");
        var q = _quantization[c.QuantizationTableId];
        if (q is null && needsBoth)
            throw new ImageDecodeException($"JPEG component uses quantisation table {c.QuantizationTableId}, which was not defined.");
        return new ComponentTables(c, dc!, ac!, q!);
    }

    private void DecodeNonInterleaved(in ComponentTables tables)
    {
        var c = tables.Component;
        var sinceRestart = 0;
        for (var row = 0; row < c.BlocksPerColumn; row++)
        {
            for (var col = 0; col < c.BlocksPerLine; col++)
            {
                if (_restartInterval > 0 && sinceRestart == _restartInterval)
                {
                    Restart();
                    sinceRestart = 0;
                }

                DecodeBlock(in tables, row, col);
                sinceRestart++;
            }
        }
    }

    private void DecodeInterleaved(ComponentTables[] tables)
    {
        var sinceRestart = 0;
        for (var mcuRow = 0; mcuRow < _frame.McusPerColumn; mcuRow++)
        {
            for (var mcuCol = 0; mcuCol < _frame.McusPerLine; mcuCol++)
            {
                if (_restartInterval > 0 && sinceRestart == _restartInterval)
                {
                    Restart();
                    sinceRestart = 0;
                }

                for (var i = 0; i < tables.Length; i++)
                {
                    ref readonly var t = ref tables[i];
                    var c = t.Component;
                    for (var v = 0; v < c.VerticalFactor; v++)
                    {
                        for (var h = 0; h < c.HorizontalFactor; h++)
                            DecodeBlock(in t, mcuRow * c.VerticalFactor + v, mcuCol * c.HorizontalFactor + h);
                    }
                }

                sinceRestart++;
            }
        }
    }

    private void Restart()
    {
        if (!_reader.Restart())
            throw new ImageDecodeException("JPEG restart marker is missing where the restart interval says one should be.");
        foreach (var c in _scan.Components)
            c.DcPrediction = 0;
        _eobRun = 0;
    }

    [SkipLocalsInit]
    private void DecodeBlock(in ComponentTables tables, int blockRow, int blockCol)
    {
        var c = tables.Component;
        if (_frame.Progressive)
        {
            var offset = (blockRow * c.PaddedBlocksPerLine + blockCol) * 64;
            var coefficients = c.Coefficients.AsSpan(offset, 64);
            if (_scan.SpectralStart == 0)
            {
                if (_scan.ApproximationHigh == 0)
                    DecodeDcFirst(in tables, coefficients);
                else
                    DecodeDcRefine(coefficients);
            }
            else
            {
                if (_scan.ApproximationHigh == 0)
                    DecodeAcFirst(in tables, coefficients);
                else
                    DecodeAcRefine(in tables, coefficients);
            }

            return;
        }

        // Cleared by the block decoder itself: no need for the runtime to zero it as well.
        scoped Span<short> block = stackalloc short[64];
        var n = c.BlockSize;
        var plane = c.Plane.AsSpan((blockRow * n) * c.PlaneStride + blockCol * n);
        if (n == 8)
        {
            // Decoded transposed: the full-size transform saves a transpose that way.
            DecodeSequentialBlock(in tables, block, JpegCoefficientCoding.ZigzagTransposed);
            JpegIdct.TransformTransposed(block, plane, c.PlaneStride);
        }
        else
        {
            DecodeSequentialBlock(in tables, block, JpegMarker.Zigzag);
            JpegIdctScaled.Transform(block, n, plane, c.PlaneStride);
        }
    }

    /// <summary>
    ///     The hot loop of a baseline decode: once per non-zero coefficient of the image. The bit buffer
    ///     is left-aligned and lives in locals with its count and the read position; it fills itself from
    ///     the data eight bytes at a time and goes back to the reader only for a slow fill or a long code.
    ///     The tables are reached through refs. The one range check that matters, k ≤ 63, stays explicit.
    /// </summary>
    /// <param name="tables">The component and its tables.</param>
    /// <param name="block">Receives the 64 dequantised coefficients.</param>
    /// <param name="zigzagToIndex">Where zigzag position k lands in the block: natural order, or the transposed one.</param>
    private unsafe void DecodeSequentialBlock(in ComponentTables tables, scoped Span<short> block, ReadOnlySpan<byte> zigzagToIndex)
    {
        block.Clear();
        var c = tables.Component;
        ref var dcLookup = ref MemoryMarshal.GetArrayDataReference(tables.Dc.Lookup);
        ref var acEntries = ref MemoryMarshal.GetArrayDataReference(tables.Ac.AcEntries);
        ref var quant = ref MemoryMarshal.GetArrayDataReference(tables.Quantization);
        ref var zigzag = ref MemoryMarshal.GetReference(zigzagToIndex);
        ref var coefficients = ref MemoryMarshal.GetReference(block);
        var dataStart = _dataStart;
        var dataEnd = _dataEnd;
        const int lookupBits = JpegHuffmanTable.LookupBits;

        _reader.EnsureBits(32);
        var (buffer, bitCount, position) = _reader.Export();
        var cursor = dataStart + position;

        // DC: category, then that many magnitude bits.
        var peek = (int)(buffer >> (64 - lookupBits));
        int entry = Unsafe.Add(ref dcLookup, (nuint)peek);
        int t;
        if (entry != 0)
        {
            var length = entry >> 8;
            buffer <<= length;
            bitCount -= length;
            t = entry & 0xFF;
        }
        else
        {
            buffer <<= lookupBits;
            bitCount -= lookupBits;
            _reader.Import(buffer, bitCount, (int)(cursor - dataStart));
            t = tables.Dc.DecodeSlow(ref _reader, peek);
            (buffer, bitCount, position) = _reader.Export();
            cursor = dataStart + position;
        }

        if (t > 15)
            throw new ImageDecodeException("JPEG DC coefficient category is above 15.");
        if (t > 0)
        {
            var bits = (int)(buffer >> (64 - t));
            buffer <<= t;
            bitCount -= t;
            c.DcPrediction += Extend(bits, t);
        }

        coefficients = (short)(c.DcPrediction * Unsafe.Add(ref quant, 0));

        var k = 1;
        while (k < 64)
        {
            // A code is at most 16 bits and its magnitude at most 11: 32 in hand cover one coefficient.
            if (bitCount < 32 && !JpegBitReader.TryFillFast(ref cursor, dataEnd, ref buffer, ref bitCount))
            {
                _reader.Import(buffer, bitCount, (int)(cursor - dataStart));
                _reader.EnsureBits(32);
                (buffer, bitCount, position) = _reader.Export();
                cursor = dataStart + position;
            }

            // The path every symbol waits on is peek → load → shift: the index is taken unsigned so no
            // sign extension sits on it, and the shift takes the raw entry, its count in the low byte.
            entry = Unsafe.Add(ref acEntries, (nuint)(buffer >> (64 - lookupBits)));

            // The common case: code and magnitude both inside the lookahead, the value in the entry.
            if (entry < 0)
            {
                buffer <<= entry;
                bitCount -= (byte)entry;
                k += (entry >> 8) & 15;
                if (k > 63)
                    throw new ImageDecodeException("JPEG block holds more than 64 coefficients.");
                Unsafe.Add(ref coefficients, Unsafe.Add(ref zigzag, k)) = (short)(((entry << 4) >> 20) * Unsafe.Add(ref quant, k));
                k++;
                continue;
            }

            int rs;
            if (entry != 0)
            {
                buffer <<= entry;
                bitCount -= (byte)entry;
                rs = entry >> 8;
            }
            else
            {
                peek = (int)(buffer >> (64 - lookupBits));
                buffer <<= lookupBits;
                bitCount -= lookupBits;
                _reader.Import(buffer, bitCount, (int)(cursor - dataStart));
                rs = tables.Ac.DecodeSlow(ref _reader, peek);
                (buffer, bitCount, position) = _reader.Export();
                cursor = dataStart + position;
            }

            var r = rs >> 4;
            var s = rs & 15;
            if (s == 0)
            {
                if (r != 15)
                    break;
                k += 16;
                continue;
            }

            k += r;
            if (k > 63)
                throw new ImageDecodeException("JPEG block holds more than 64 coefficients.");

            var magnitude = (int)(buffer >> (64 - s));
            buffer <<= s;
            bitCount -= s;
            var value = Extend(magnitude, s);
            Unsafe.Add(ref coefficients, Unsafe.Add(ref zigzag, k)) = (short)(value * Unsafe.Add(ref quant, k));
            k++;
        }

        _reader.Import(buffer, bitCount, (int)(cursor - dataStart));
    }

    /// <summary>
    ///     The JPEG sign extension of <paramref name="size" /> magnitude bits, without a branch: the sign
    ///     of a coefficient is a coin toss, and a mispredicted branch per coefficient was a third of the loop.
    ///     When the top bit is clear the value is negative: add (−1 &lt;&lt; size) + 1.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Extend(int bits, int size)
    {
        var negative = (bits - (1 << (size - 1))) >> 31; // all ones when bits < 2^(size−1)
        return bits + (negative & ((-1 << size) + 1));
    }
}

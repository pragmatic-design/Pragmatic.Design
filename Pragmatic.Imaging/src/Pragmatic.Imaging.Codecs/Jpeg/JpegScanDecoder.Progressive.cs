namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>The progressive passes (ITU-T T.81, Annex G): DC and AC first scans, and their refinements.</summary>
internal ref partial struct JpegScanDecoder
{
    private void DecodeDcFirst(in ComponentTables tables, Span<short> coefficients)
    {
        var c = tables.Component;
        _reader.EnsureBits(32);
        var t = _reader.DecodeHuffman(tables.Dc);
        if (t > 15)
            throw new ImageDecodeException("JPEG DC coefficient category is above 15.");
        c.DcPrediction += _reader.ReceiveUnchecked(t);
        coefficients[0] = (short)(c.DcPrediction << _scan.ApproximationLow);
    }

    private void DecodeDcRefine(Span<short> coefficients)
    {
        if (_reader.ReadBit() != 0)
            coefficients[0] |= (short)(1 << _scan.ApproximationLow);
    }

    private void DecodeAcFirst(in ComponentTables tables, Span<short> coefficients)
    {
        if (_eobRun > 0)
        {
            _eobRun--;
            return;
        }

        var acTable = tables.Ac;
        var zigzag = JpegMarker.Zigzag;
        var shift = _scan.ApproximationLow;
        var k = _scan.SpectralStart;
        while (k <= _scan.SpectralEnd)
        {
            _reader.EnsureBits(32);
            var rs = _reader.DecodeHuffman(acTable);
            var r = rs >> 4;
            var s = rs & 15;
            if (s == 0)
            {
                if (r < 15)
                {
                    _eobRun = (1 << r) - 1;
                    if (r > 0)
                        _eobRun += _reader.ReadBitsUnchecked(r);
                    break;
                }

                k += 16;
                continue;
            }

            k += r;
            if (k > 63)
                throw new ImageDecodeException("JPEG block holds more than 64 coefficients.");
            coefficients[zigzag[k]] = (short)(_reader.ReceiveUnchecked(s) * (1 << shift));
            k++;
        }
    }

    private void DecodeAcRefine(in ComponentTables tables, Span<short> coefficients)
    {
        var zigzag = JpegMarker.Zigzag;
        var bit = (short)(1 << _scan.ApproximationLow);
        var k = _scan.SpectralStart;

        if (_eobRun > 0)
        {
            _eobRun--;
            for (; k <= _scan.SpectralEnd; k++)
                RefineNonZero(ref coefficients[zigzag[k]], bit);
            return;
        }

        var acTable = tables.Ac;
        do
        {
            _reader.EnsureBits(32);
            var rs = _reader.DecodeHuffman(acTable);
            var r = rs >> 4;
            var s = rs & 15;
            short newValue = 0;
            if (s == 0)
            {
                if (r < 15)
                {
                    _eobRun = (1 << r) - 1;
                    if (r > 0)
                        _eobRun += _reader.ReadBits(r);
                    r = 64; // The rest of this block is refinement of what is already there.
                }
            }
            else
            {
                if (s != 1)
                    throw new ImageDecodeException("JPEG AC refinement scan holds a coefficient wider than one bit.");
                newValue = _reader.ReadBit() != 0 ? bit : (short)-bit;
            }

            while (k <= _scan.SpectralEnd)
            {
                ref var coefficient = ref coefficients[zigzag[k++]];
                if (coefficient != 0)
                {
                    RefineNonZero(ref coefficient, bit);
                }
                else
                {
                    if (r == 0)
                    {
                        if (newValue != 0)
                            coefficient = newValue;
                        break;
                    }

                    r--;
                }
            }
        }
        while (k <= _scan.SpectralEnd);
    }

    private void RefineNonZero(ref short coefficient, short bit)
    {
        if (coefficient == 0)
            return;
        if (_reader.ReadBit() != 0 && (coefficient & bit) == 0)
            coefficient += coefficient > 0 ? bit : (short)-bit;
    }
}

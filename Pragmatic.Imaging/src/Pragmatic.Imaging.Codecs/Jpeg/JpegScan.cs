namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>What SOS says: which components, which tables, and for a progressive file which part of the spectrum.</summary>
internal sealed class JpegScan
{
    public JpegScan(JpegComponent[] components, int spectralStart, int spectralEnd, int approximationHigh, int approximationLow)
    {
        Components = components;
        SpectralStart = spectralStart;
        SpectralEnd = spectralEnd;
        ApproximationHigh = approximationHigh;
        ApproximationLow = approximationLow;
    }

    public JpegComponent[] Components { get; }

    public int SpectralStart { get; }

    public int SpectralEnd { get; }

    public int ApproximationHigh { get; }

    public int ApproximationLow { get; }

    public static JpegScan Parse(ReadOnlySpan<byte> segment, JpegFrame frame)
    {
        if (segment.Length < 1)
            throw new ImageDecodeException("JPEG scan header is empty.");
        var count = segment[0];
        if (count is < 1 or > 4 || segment.Length < 1 + count * 2 + 3)
            throw new ImageDecodeException("JPEG scan header is malformed.");

        var components = new JpegComponent[count];
        for (var i = 0; i < count; i++)
        {
            var id = segment[1 + i * 2];
            var tables = segment[2 + i * 2];
            var component = Array.Find(frame.Components, c => c.Id == id)
                ?? throw new ImageDecodeException($"JPEG scan refers to component {id}, which the frame does not have.");
            component.DcTableId = tables >> 4;
            component.AcTableId = tables & 15;
            if (component.DcTableId > 3 || component.AcTableId > 3)
                throw new ImageDecodeException("JPEG scan refers to a Huffman table above 3.");
            components[i] = component;
        }

        var at = 1 + count * 2;
        var ss = segment[at];
        var se = segment[at + 1];
        var ah = segment[at + 2] >> 4;
        var al = segment[at + 2] & 15;

        if (frame.Progressive)
        {
            if (ss > 63 || se > 63 || ss > se || (ss == 0 && se != 0) || (ss > 0 && count != 1) || al > 13 || ah > 13)
                throw new ImageDecodeException("JPEG progressive scan parameters are out of range.");
        }
        else
        {
            // A sequential scan covers the whole spectrum; what the header says is ignored, as decoders do.
            ss = 0;
            se = 63;
            ah = 0;
            al = 0;
        }

        return new JpegScan(components, ss, se, ah, al);
    }
}

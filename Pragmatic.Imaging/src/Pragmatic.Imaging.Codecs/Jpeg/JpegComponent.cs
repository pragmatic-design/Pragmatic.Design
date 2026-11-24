namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     One colour component of the frame: its sampling factors, its quantisation table, the plane its
///     samples land in, and, for a progressive file, the coefficients gathered scan by scan.
/// </summary>
internal sealed class JpegComponent
{
    public JpegComponent(byte id, int horizontalFactor, int verticalFactor, byte quantizationTableId)
    {
        Id = id;
        HorizontalFactor = horizontalFactor;
        VerticalFactor = verticalFactor;
        QuantizationTableId = quantizationTableId;
    }

    public byte Id { get; }

    public int HorizontalFactor { get; }

    public int VerticalFactor { get; }

    public byte QuantizationTableId { get; }

    /// <summary>Set by the current scan.</summary>
    public int DcTableId { get; set; }

    public int AcTableId { get; set; }

    /// <summary>The DC predictor of the current scan.</summary>
    public int DcPrediction { get; set; }

    /// <summary>Samples per row and rows of this component in the file, before padding: ceil(frame × factor / max factor).</summary>
    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>The same at the decode scale: what the plane holds of real samples.</summary>
    public int ScaledWidth { get; set; }

    public int ScaledHeight { get; set; }

    /// <summary>Blocks the component actually has: what a non-interleaved scan walks.</summary>
    public int BlocksPerLine { get; set; }

    public int BlocksPerColumn { get; set; }

    /// <summary>Blocks per line and column once padded to whole MCUs: the stride of the plane and of the coefficients.</summary>
    public int PaddedBlocksPerLine { get; set; }

    public int PaddedBlocksPerColumn { get; set; }

    /// <summary>Samples a block becomes on a side at the decode scale: 8, 4, 2 or 1.</summary>
    public int BlockSize { get; set; } = 8;

    /// <summary>Row stride of <see cref="Plane" />, in samples.</summary>
    public int PlaneStride => PaddedBlocksPerLine * BlockSize;

    public int PlaneHeight => PaddedBlocksPerColumn * BlockSize;

    /// <summary>Decoded samples, padded to whole blocks, rented from the pool by the decoder.</summary>
    public byte[] Plane { get; set; } = [];

    /// <summary>Progressive only: 64 coefficients per block, in natural order, not yet dequantised.</summary>
    public short[] Coefficients { get; set; } = [];
}

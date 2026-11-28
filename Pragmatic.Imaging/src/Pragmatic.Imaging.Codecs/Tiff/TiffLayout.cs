namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     Where the TIFF crate put a chunk's samples in its buffer: one plane per sample when the image is planar, rows
///     of a plane's samples, chunks side by side and then down.
/// </summary>
internal readonly record struct TiffLayout(
    long RowStride,
    long TiffRowBytes,
    int DataSamples,
    int TiffSamples,
    long ChunkRowStride,
    long ChunkColumnStride,
    long PlaneStride,
    long ChunksAcross,
    long ChunksPerPlane,
    int Planes)
{
    /// <exception cref="ImageDecodeException">The chunks of a row do not start on byte boundaries.</exception>
    public static TiffLayout Of(TiffImage image)
    {
        var dataSamples = image.Planar ? 1 : image.ColorSamples;
        var tiffSamples = image.Planar ? 1 : image.Samples;
        long bits = image.Bits;
        var tiffRowBytes = (image.ChunkWidth * bits * tiffSamples + 7) / 8;
        var chunkRowBits = image.ChunkWidth * bits * dataSamples;
        var rowStride = ((long)image.Width * bits * dataSamples + 7) / 8;
        var across = ((long)image.Width + image.ChunkWidth - 1) / image.ChunkWidth;
        if (across > 1 && chunkRowBits % 8 != 0)
            throw new ImageDecodeException("TIFF: the tiles of a row do not start on byte boundaries.");
        return new TiffLayout(rowStride, tiffRowBytes, dataSamples, tiffSamples, (chunkRowBits + 7) / 8, rowStride * image.ChunkHeight,
            rowStride * image.Height, across, image.Chunks / (image.Planar ? image.Samples : 1), image.ColorSamples / dataSamples);
    }

    /// <summary>The bytes of a row of <paramref name="width" /> pixels of a chunk, in the buffer.</summary>
    public long DataRowBytes(TiffImage image, long width) => (width * image.Bits * DataSamples + 7) / 8;
}

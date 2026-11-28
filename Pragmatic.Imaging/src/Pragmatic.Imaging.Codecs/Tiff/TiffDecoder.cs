using System.Buffers;

namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     Decodes the first image of a TIFF to RGBA, as the native library did (the image crate 0.25 over the TIFF crate
///     0.11): strips or tiles, chunky or planar, either byte order; uncompressed, PackBits, LZW, Deflate, JPEG and
///     Group 4; one-bit, 8 and 16-bit grey, 8 and 16-bit RGB and RGBA, 32-bit float RGB and RGBA, 8 and 16-bit CMYK;
///     the horizontal and floating-point predictors. What the native library refused is refused: palettes, YCbCr,
///     grey with alpha, other depths and sample formats, Group 3, BigTIFF. Later images in the file are not read.
/// </summary>
public static class TiffDecoder
{
    // The image crate's default allocation limit, from which the TIFF crate's limits were set.
    private const long AllocationLimit = 512L * 1024 * 1024;

    /// <summary>Whether the bytes start like a TIFF file the native library recognised.</summary>
    public static bool IsTiff(ReadOnlySpan<byte> encoded) => TiffImage.HasSignature(encoded);

    /// <summary>The size of the first image, after checking its header as a decode would.</summary>
    /// <exception cref="ImageDecodeException">The header is not one the native library read.</exception>
    public static (int Width, int Height) Identify(ReadOnlySpan<byte> encoded)
    {
        var workspace = TiffWorkspace.Take();
        try
        {
            workspace.Image.Read(encoded, workspace);
            return (workspace.Image.Width, workspace.Image.Height);
        }
        finally
        {
            TiffWorkspace.Give(workspace);
        }
    }

    /// <exception cref="ImageDecodeException">The file is truncated, corrupt, or not a TIFF this decoder reads.</exception>
    public static PixelBuffer Decode(ReadOnlyMemory<byte> encoded)
    {
        // Every part of the workspace starts over on each use, so a decode that failed leaves nothing behind.
        var workspace = TiffWorkspace.Take();
        try
        {
            return Decode(encoded, workspace);
        }
        finally
        {
            TiffWorkspace.Give(workspace);
        }
    }

    private static PixelBuffer Decode(ReadOnlyMemory<byte> encoded, TiffWorkspace workspace)
    {
        var image = workspace.Image;
        image.Read(encoded.Span, workspace);
        var outputBytes = (long)image.Width * image.Height * BytesPerPixel(image.Format);
        if (outputBytes > AllocationLimit)
            throw new ImageDecodeException("TIFF: the image is larger than the native library allocated.");
        var intermediate = (ulong)(AllocationLimit - outputBytes);
        Validate(image);

        var layout = TiffLayout.Of(image);
        var length = layout.PlaneStride * layout.Planes;

        // Eight-bit RGBA in one plane is already the output's layout: the chunks go straight into the pixels.
        var direct = image.Format == TiffPixelFormat.Rgba8 && layout.Planes == 1 && layout.RowStride == (long)image.Width * 4;
        var samples = direct ? null : ArrayPool<byte>.Shared.Rent((int)length);
        PixelBuffer? output = null;
        try
        {
            if (direct)
                output = new PixelBuffer(image.Width, image.Height);
            var buffer = direct ? output!.Pixels : samples.AsSpan(0, (int)length);
            buffer.Clear();
            for (var chunk = 0L; chunk < layout.ChunksPerPlane; chunk++)
            {
                var x = chunk % layout.ChunksAcross;
                var y = chunk / layout.ChunksAcross;
                var offset = y * layout.ChunkColumnStride + x * layout.ChunkRowStride;
                for (var plane = 0; plane < layout.Planes; plane++)
                {
                    var index = (int)(chunk + plane * layout.ChunksPerPlane);
                    var start = plane * layout.PlaneStride + offset;
                    if (start > buffer.Length)
                        throw new ImageDecodeException("TIFF: a chunk starts past the image.");
                    if (image.ByteCounts[index] > intermediate)
                        throw new ImageDecodeException("TIFF: a strip is larger than the native library read.");
                    var (width, rows) = DataSize(image, index);
                    using var source = TiffChunks.Open(image, encoded, index, workspace);
                    TiffChunkExpander.Expand(image, layout, source, buffer[(int)start..], width, rows);
                }
            }

            if (direct)
                return output!;
            output = new PixelBuffer(image.Width, image.Height);
            TiffPixels.ToRgba(image, layout, buffer, output);
            return output;
        }
        catch
        {
            output?.Dispose();
            throw;
        }
        finally
        {
            if (samples is not null)
                ArrayPool<byte>.Shared.Return(samples);
        }
    }

    // What expand_chunk checks before it reads anything.
    private static void Validate(TiffImage image)
    {
        if (image.Bits < 8 && image.Predictor != 1)
            throw new ImageDecodeException("TIFF: a predictor on samples under 8 bits is not supported.");
        if (image.Predictor == TiffImage.HorizontalPredictor && image.SampleFormat is not (TiffImage.Uint or TiffImage.Int or TiffImage.Float))
            throw new ImageDecodeException("TIFF: the horizontal predictor does not apply to this sample format.");
        if (image.Predictor == TiffImage.FloatPredictor && image.SampleFormat != TiffImage.Float)
            throw new ImageDecodeException("TIFF: the floating-point predictor applies to floats only.");
    }

    // The chunk's pixels in the image: a strip's rows to the image's bottom, a tile less the padding of the last
    // column and row, its row counted from the chunk's index as the TIFF crate counts it, the plane included.
    private static (long Width, long Rows) DataSize(TiffImage image, int index)
    {
        if (!image.Tiled)
        {
            var perBand = (image.Height + image.ChunkHeight - 1) / image.ChunkHeight;
            var before = index % perBand * image.ChunkHeight;
            return (image.ChunkWidth, Math.Min(image.ChunkHeight, image.Height - before));
        }

        var across = (image.Width + image.ChunkWidth - 1) / image.ChunkWidth;
        var down = (image.Height + image.ChunkHeight - 1) / image.ChunkHeight;
        var row = index / across;
        var column = index % across;
        var right = column == across - 1 ? (image.ChunkWidth - image.Width % image.ChunkWidth) % image.ChunkWidth : 0;
        var bottom = row == down - 1 ? (image.ChunkHeight - image.Height % image.ChunkHeight) % image.ChunkHeight : 0;
        return (image.ChunkWidth - right, image.ChunkHeight - bottom);
    }

    private static int BytesPerPixel(TiffPixelFormat format) => format switch
    {
        TiffPixelFormat.Gray1 or TiffPixelFormat.Gray8 => 1,
        TiffPixelFormat.Gray16 => 2,
        TiffPixelFormat.Rgb8 => 3,
        TiffPixelFormat.Rgba8 or TiffPixelFormat.Cmyk8 => 4,
        TiffPixelFormat.Rgb16 => 6,
        TiffPixelFormat.Rgba16 or TiffPixelFormat.Cmyk16 => 8,
        TiffPixelFormat.Rgb32Float => 12,
        _ => 16,
    };
}

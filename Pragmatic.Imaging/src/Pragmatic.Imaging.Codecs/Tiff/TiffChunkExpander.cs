using System.Buffers;
using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     Reads one chunk into the sample buffer as the TIFF crate's expand_chunk does: the rows straight in when they
///     are the buffer's rows, row by row otherwise, extra samples dropped; then the byte order made the machine's, the
///     predictor undone and white-is-zero turned around, on the same bytes the crate touches.
/// </summary>
internal static class TiffChunkExpander
{
    /// <param name="image">The image the chunk belongs to.</param>
    /// <param name="layout">Where the chunk's samples go in the buffer.</param>
    /// <param name="source">The chunk's decoded bytes.</param>
    /// <param name="buffer">The sample buffer from where the chunk starts to its end.</param>
    /// <param name="width">The chunk's pixels across that lie in the image.</param>
    /// <param name="rows">The chunk's rows that lie in the image.</param>
    public static void Expand(TiffImage image, in TiffLayout layout, ITiffChunkSource source, Span<byte> buffer, long width, long rows)
    {
        var dataRowBytes = layout.DataRowBytes(image, width);
        var stride = layout.RowStride;
        var tiffRow = layout.TiffRowBytes;
        if (rows < 1 || buffer.Length < stride * (rows - 1) + dataRowBytes)
            throw new ImageDecodeException("TIFF: a chunk runs past the image.");

        var allBits = layout.TiffSamples == layout.DataSamples;
        if (allBits && stride == tiffRow)
        {
            var chunk = buffer[..(int)(tiffRow * rows)];
            ReadExact(source, chunk);
            for (var r = 0; r < rows; r++)
                Restore(image, layout, chunk.Slice((int)(r * tiffRow), (int)tiffRow));
            Invert(image, chunk);
            return;
        }

        var encoded = ArrayPool<byte>.Shared.Rent((int)tiffRow);
        try
        {
            if (tiffRow > dataRowBytes && image.Predictor == TiffImage.FloatPredictor)
            {
                for (var r = 0; r < rows; r++)
                {
                    var input = encoded.AsSpan(0, (int)tiffRow);
                    ReadExact(source, input);
                    var row = buffer.Slice((int)(r * stride), (int)dataRowBytes);
                    UndoFloatPredictor(input, row, image.Bits, layout.TiffSamples);
                    Invert(image, row);
                }
            }
            else if (allBits)
            {
                for (var r = 0; r < rows; r++)
                {
                    var row = buffer.Slice((int)(r * stride), (int)dataRowBytes);
                    ReadExact(source, row[..(int)Math.Min(dataRowBytes, tiffRow)]);
                    if (tiffRow > dataRowBytes)
                        Skip(source, encoded.AsSpan(0, (int)(tiffRow - dataRowBytes)));
                    Restore(image, layout, row);
                    Invert(image, row);
                }
            }
            else
            {
                // Extra samples past the colour model: a whole coded row, then the first samples of each pixel. The
                // row the crate writes into is a whole buffer row from the chunk's start, past the chunk when a tile
                // is not the leftmost, and so is the row it restores.
                var pixelBits = image.Bits * image.Samples;
                var keptBits = image.Bits * layout.DataSamples;
                if (pixelBits % 8 != 0 || keptBits % 8 != 0)
                    throw new ImageDecodeException("TIFF: the extra samples do not fill whole bytes.");
                int kept = keptBits / 8, all = pixelBits / 8;
                for (var r = 0; r < rows; r++)
                {
                    var input = encoded.AsSpan(0, (int)tiffRow);
                    ReadExact(source, input);
                    var start = r * stride;
                    var row = buffer.Slice((int)start, (int)Math.Min(stride, buffer.Length - start));
                    var pixels = Math.Min(input.Length / all, row.Length / kept);
                    for (var p = 0; p < pixels; p++)
                        input.Slice(p * all, kept).CopyTo(row[(p * kept)..]);
                    Restore(image, layout, row);
                    Invert(image, row);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(encoded);
        }
    }

    private static void ReadExact(ITiffChunkSource source, Span<byte> target)
    {
        while (target.Length > 0)
        {
            var count = source.Read(target);
            if (count == 0)
                throw new ImageDecodeException("TIFF: a strip holds fewer bytes than its rows.");
            target = target[count..];
        }
    }

    // io::copy into a sink, 8 KiB at a time: the end of the data ends it quietly, corrupt data still throws.
    private static void Skip(ITiffChunkSource source, Span<byte> scratch)
    {
        while (scratch.Length > 0)
        {
            var count = source.Read(scratch[..Math.Min(scratch.Length, 8192)]);
            if (count == 0)
                return;
            scratch = scratch[count..];
        }
    }

    // The samples to the machine's byte order and the predictor undone: fix_endianness_and_predict.
    private static void Restore(TiffImage image, in TiffLayout layout, Span<byte> row)
    {
        if (image.Predictor == TiffImage.FloatPredictor)
        {
            var copy = ArrayPool<byte>.Shared.Rent(row.Length);
            try
            {
                row.CopyTo(copy);
                UndoFloatPredictor(copy.AsSpan(0, row.Length), row, image.Bits, layout.TiffSamples);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(copy);
            }

            return;
        }

        if (image.BigEndian)
            SwapBytes(row, image.Bits);
        if (image.Predictor == TiffImage.HorizontalPredictor)
            UndoHorizontalPredictor(row, image.Bits, layout.TiffSamples);
    }

    private static void SwapBytes(Span<byte> row, int bits)
    {
        var width = bits switch { <= 8 => 1, <= 16 => 2, <= 32 => 4, _ => 8 };
        if (width == 1)
            return;
        for (var i = 0; i + width <= row.Length; i += width)
            row.Slice(i, width).Reverse();
    }

    private static void UndoHorizontalPredictor(Span<byte> row, int bits, int samples)
    {
        var width = bits switch { <= 8 => 1, <= 16 => 2, <= 32 => 4, _ => 8 };
        var back = samples * width;
        for (var i = back; i + width <= row.Length; i += width)
        {
            switch (width)
            {
                case 1:
                    row[i] += row[i - back];
                    break;
                case 2:
                    BinaryPrimitives.WriteUInt16LittleEndian(row[i..],
                        (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(row[i..]) + BinaryPrimitives.ReadUInt16LittleEndian(row[(i - back)..])));
                    break;
                case 4:
                    BinaryPrimitives.WriteUInt32LittleEndian(row[i..],
                        BinaryPrimitives.ReadUInt32LittleEndian(row[i..]) + BinaryPrimitives.ReadUInt32LittleEndian(row[(i - back)..]));
                    break;
                default:
                    BinaryPrimitives.WriteUInt64LittleEndian(row[i..],
                        BinaryPrimitives.ReadUInt64LittleEndian(row[i..]) + BinaryPrimitives.ReadUInt64LittleEndian(row[(i - back)..]));
                    break;
            }
        }
    }

    // The floating-point predictor: bytes differenced a pixel apart, then the most significant byte of every sample
    // first, the next ones after; each value is put back together from its column, the coded row's length apart.
    private static void UndoFloatPredictor(Span<byte> input, Span<byte> output, int bits, int samples)
    {
        for (var i = samples; i < input.Length; i++)
            input[i] += input[i - samples];
        var size = bits / 8;
        if (size is not (2 or 4 or 8))
            throw new ImageDecodeException("TIFF: the floating-point predictor needs 16, 32 or 64-bit samples.");
        var plane = input.Length / size;
        for (var v = 0; v + size <= output.Length; v += size)
        {
            var index = v / size;
            for (var b = 0; b < size; b++)
                output[v + size - 1 - b] = input[b * plane + index];
        }
    }

    private static void Invert(TiffImage image, Span<byte> row)
    {
        if (image.Photometric != TiffImage.WhiteIsZero)
            return;
        if (image.Color != TiffColor.Gray || image.SampleFormat != TiffImage.Uint || image.Bits is not (1 or 2 or 4 or 8 or 16))
            throw new ImageDecodeException("TIFF: white is zero is only read for unsigned grey.");
        if (image.Bits == 16)
        {
            for (var i = 0; i + 2 <= row.Length; i += 2)
                BinaryPrimitives.WriteUInt16LittleEndian(row[i..], (ushort)(0xFFFF - BinaryPrimitives.ReadUInt16LittleEndian(row[i..])));
            return;
        }

        for (var i = 0; i < row.Length; i++)
            row[i] = (byte)~row[i];
    }
}

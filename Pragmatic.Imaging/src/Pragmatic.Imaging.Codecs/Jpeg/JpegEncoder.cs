using System.Buffers;
using System.Buffers.Binary;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     8-bit RGBA to a baseline JPEG: YCbCr 4:2:0 and the quantisation tables of Annex K scaled for the
///     quality. With optimised coding (the default) the Huffman tables are fitted to the file: the
///     coefficients are walked once into a compact stream of symbols, counted as they go, the tables are
///     built from the counts, and the stream is written through them. Without, the standard tables of
///     Annex K.3 are used and the symbols go straight to the output in one pass: faster, a somewhat
///     larger file.
/// </summary>
public static class JpegEncoder
{
    /// <summary>Luma Y: id 1, 2×2 sampling, table 0; Cb and Cr: ids 2 and 3, 1×1, table 1.</summary>
    private const int McuBlocks = 6;

    public static void Write(PixelBuffer image, Stream output, int quality = 85, bool optimizeCoding = true)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(output);
        using var lease = new Rgba8Lease(image);
        image = lease.Image;

        var (lumaQuant, chromaQuant) = JpegQuantization.ForQuality(quality);
        using var planes = JpegPlanes.From(image);

        if (optimizeCoding)
            WriteOptimized(planes, image, output, lumaQuant, chromaQuant);
        else
            WriteSinglePass(planes, image, output, lumaQuant, chromaQuant);

        output.Write([0xFF, JpegMarker.Eoi]);
    }

    private static void WriteOptimized(JpegPlanes planes, PixelBuffer image, Stream output, byte[] lumaQuant, byte[] chromaQuant)
    {
        var blockCount = planes.McusPerLine * planes.McusPerColumn * McuBlocks;
        // At most 65 symbols a block: a DC and one per AC position, counting the ZRLs as a position's worth.
        var stream = ArrayPool<JpegCountingEmitter.Symbol>.Shared.Rent(blockCount * 66);
        try
        {
            var dcLuma = new JpegHuffmanBuilder();
            var acLuma = new JpegHuffmanBuilder();
            var dcChroma = new JpegHuffmanBuilder();
            var acChroma = new JpegHuffmanBuilder();
            var emitter = new JpegCountingEmitter(stream, dcLuma, acLuma, dcChroma, acChroma);
            Walk(planes, lumaQuant, chromaQuant, ref emitter);

            var tables = (dcLuma.Build(), acLuma.Build(), dcChroma.Build(), acChroma.Build());
            Span<uint> codes = stackalloc uint[4 * 256];
            JpegEncodingTable.Flatten(tables.Item1, tables.Item2, tables.Item3, tables.Item4, codes);

            WriteHeaders(output, image.Width, image.Height, lumaQuant, chromaQuant, tables.Item1, tables.Item2, tables.Item3, tables.Item4);
            var writer = new JpegBitWriter(output);
            emitter.WriteStream(writer, codes);
            writer.Finish();
        }
        finally
        {
            ArrayPool<JpegCountingEmitter.Symbol>.Shared.Return(stream);
        }
    }

    private static void WriteSinglePass(JpegPlanes planes, PixelBuffer image, Stream output, byte[] lumaQuant, byte[] chromaQuant)
    {
        WriteHeaders(output, image.Width, image.Height, lumaQuant, chromaQuant,
            JpegStandardHuffmanTables.DcLuma, JpegStandardHuffmanTables.AcLuma, JpegStandardHuffmanTables.DcChroma, JpegStandardHuffmanTables.AcChroma);
        var writer = new JpegBitWriter(output);
        var emitter = new JpegDirectEmitter(writer, JpegStandardHuffmanTables.Flattened);
        Walk(planes, lumaQuant, chromaQuant, ref emitter);
        writer.Finish();
    }

    /// <summary>Every block in scan order: transform, quantise, and hand to the emitter with its non-zero mask.</summary>
    private static void Walk<TEmitter>(JpegPlanes planes, byte[] lumaQuant, byte[] chromaQuant, ref TEmitter emitter)
        where TEmitter : struct, IJpegBlockEmitter
    {
        Span<float> lumaReciprocals = stackalloc float[64];
        Span<float> chromaReciprocals = stackalloc float[64];
        if (JpegForwardDctAvx2.IsSupported)
        {
            JpegForwardDctAvx2.BuildReciprocals(lumaQuant, lumaReciprocals);
            JpegForwardDctAvx2.BuildReciprocals(chromaQuant, chromaReciprocals);
        }

        Span<float> dct = stackalloc float[64];
        Span<short> block = stackalloc short[64];
        Span<int> predictors = stackalloc int[3];
        for (var mcuY = 0; mcuY < planes.McusPerColumn; mcuY++)
        {
            for (var mcuX = 0; mcuX < planes.McusPerLine; mcuX++)
            {
                for (var v = 0; v < 2; v++)
                {
                    for (var h = 0; h < 2; h++)
                    {
                        var offset = (mcuY * 16 + v * 8) * planes.LumaStride + mcuX * 16 + h * 8;
                        var mask = Quantize(planes.Luma.AsSpan(offset), planes.LumaStride, lumaQuant, lumaReciprocals, dct, block);
                        emitter.Emit(block, mask, ref predictors[0], 0);
                    }
                }

                var chromaOffset = mcuY * 8 * planes.ChromaStride + mcuX * 8;
                var maskCb = Quantize(planes.Cb.AsSpan(chromaOffset), planes.ChromaStride, chromaQuant, chromaReciprocals, dct, block);
                emitter.Emit(block, maskCb, ref predictors[1], 1);
                var maskCr = Quantize(planes.Cr.AsSpan(chromaOffset), planes.ChromaStride, chromaQuant, chromaReciprocals, dct, block);
                emitter.Emit(block, maskCr, ref predictors[2], 1);
            }
        }
    }

    /// <summary>
    ///     Transforms and quantises one block into the transposed layout of <see cref="JpegForwardDctAvx2" />
    ///     (coefficient (v, u) at u·8 + v); returns the mask of its non-zero coefficients with bit k
    ///     standing for zigzag position k, so that the symbols can be read off the set bits without a
    ///     walk over the zeros.
    /// </summary>
    private static ulong Quantize(ReadOnlySpan<byte> samples, int stride, byte[] quant, ReadOnlySpan<float> reciprocals, Span<float> dct, Span<short> block)
    {
        if (JpegForwardDctAvx2.IsSupported)
        {
            JpegForwardDctAvx2.TransformAndQuantize(samples, stride, reciprocals, block);
        }
        else
        {
            JpegForwardDct.Transform(samples, stride, dct);
            for (var v = 0; v < 8; v++)
            {
                for (var u = 0; u < 8; u++)
                    block[u * 8 + v] = (short)MathF.Round(dct[v * 8 + u] / quant[v * 8 + u], MidpointRounding.ToEven);
            }
        }

        var blockOrder = JpegForwardDctAvx2.IsSupported ? JpegForwardDctAvx2.NonZeroMask(block) : ScalarNonZeroMask(block);
        return JpegCoefficientCoding.ToZigzagOrder(blockOrder);
    }

    private static ulong ScalarNonZeroMask(ReadOnlySpan<short> block)
    {
        ulong mask = 0;
        for (var i = 0; i < 64; i++)
        {
            if (block[i] != 0)
                mask |= 1UL << i;
        }

        return mask;
    }

    private static void WriteHeaders(Stream output, int width, int height, byte[] lumaQuant, byte[] chromaQuant,
        JpegEncodingTable dcLuma, JpegEncodingTable acLuma, JpegEncodingTable dcChroma, JpegEncodingTable acChroma)
    {
        output.Write([0xFF, JpegMarker.Soi]);

        // APP0: JFIF 1.01, no density, no thumbnail.
        WriteSegment(output, JpegMarker.App0, [(byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0]);

        var zigzag = JpegMarker.Zigzag;
        Span<byte> dqt = stackalloc byte[2 * 65];
        dqt[0] = 0x00;
        dqt[65] = 0x01;
        for (var k = 0; k < 64; k++)
        {
            dqt[1 + k] = lumaQuant[zigzag[k]];
            dqt[66 + k] = chromaQuant[zigzag[k]];
        }

        WriteSegment(output, JpegMarker.Dqt, dqt);

        Span<byte> sof = stackalloc byte[15];
        sof[0] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(sof[1..], (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(sof[3..], (ushort)width);
        sof[5] = 3;
        sof[6] = 1; sof[7] = 0x22; sof[8] = 0;
        sof[9] = 2; sof[10] = 0x11; sof[11] = 1;
        sof[12] = 3; sof[13] = 0x11; sof[14] = 1;
        WriteSegment(output, JpegMarker.Sof0, sof);

        // One DHT segment with the four tables: a class/id byte, 16 counts and the symbols for each.
        var dhtLength = 2 + 4 * 17 + dcLuma.Symbols.Length + acLuma.Symbols.Length + dcChroma.Symbols.Length + acChroma.Symbols.Length;
        Span<byte> dhtHead = stackalloc byte[4];
        dhtHead[0] = 0xFF;
        dhtHead[1] = JpegMarker.Dht;
        BinaryPrimitives.WriteUInt16BigEndian(dhtHead[2..], (ushort)dhtLength);
        output.Write(dhtHead);
        WriteTable(output, 0x00, dcLuma);
        WriteTable(output, 0x10, acLuma);
        WriteTable(output, 0x01, dcChroma);
        WriteTable(output, 0x11, acChroma);

        WriteSegment(output, JpegMarker.Sos, [3, 1, 0x00, 2, 0x11, 3, 0x11, 0, 63, 0]);
    }

    private static void WriteTable(Stream output, byte classAndId, JpegEncodingTable table)
    {
        output.WriteByte(classAndId);
        output.Write(table.Counts);
        output.Write(table.Symbols);
    }

    private static void WriteSegment(Stream output, byte marker, ReadOnlySpan<byte> payload)
    {
        Span<byte> head = stackalloc byte[4];
        head[0] = 0xFF;
        head[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(head[2..], (ushort)(payload.Length + 2));
        output.Write(head);
        output.Write(payload);
    }
}

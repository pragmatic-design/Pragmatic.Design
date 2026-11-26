// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     What the first partition of a VP8 key frame says before its macroblocks (RFC 6386, sections 9.3 to
///     9.11): segmentation, the loop filter, the quantizers per segment and the coefficient probabilities.
///     Read the way libwebp's <c>VP8GetHeaders</c> reads it.
/// </summary>
internal sealed class Vp8FrameHeader
{
    public const int Segments = 4;

    public bool UseSegments { get; private set; }

    public bool UpdateSegmentMap { get; private set; }

    public bool AbsoluteSegmentValues { get; private set; }

    public int[] SegmentQuantizer { get; } = new int[Segments];

    public int[] SegmentFilterStrength { get; } = new int[Segments];

    public byte[] SegmentProbabilities { get; } = [255, 255, 255];

    public bool SimpleFilter { get; private set; }

    public int FilterLevel { get; private set; }

    public int Sharpness { get; private set; }

    public bool UseFilterDeltas { get; private set; }

    public int[] ReferenceFilterDelta { get; } = new int[4];

    public int[] ModeFilterDelta { get; } = new int[4];

    public int Partitions { get; private set; }

    /// <summary>The dequantization factors per segment: luma DC and AC, Y2 DC and AC, chroma DC and AC.</summary>
    public Vp8Quantizer[] Quantizers { get; } = new Vp8Quantizer[Segments];

    /// <summary>The coefficient probabilities: [type, band, context, node], flattened.</summary>
    public byte[] CoefficientProbabilities { get; } = Vp8Tables.DefaultCoefficientProbabilities.ToArray();

    public bool UseSkipProbability { get; private set; }

    public int SkipProbability { get; private set; }

    public static Vp8FrameHeader Read(Vp8BoolDecoder reader)
    {
        var header = new Vp8FrameHeader();
        reader.ReadBit(128); // colour space: only 0 is defined, and decoders ignore it
        reader.ReadBit(128); // clamping type: the reconstruction clamps either way
        header.ReadSegmentation(reader);
        header.ReadFilter(reader);
        header.Partitions = 1 << reader.ReadLiteral(2);
        header.ReadQuantizers(reader);
        reader.ReadBit(128); // refresh_entropy_probs: meaningless for a still image
        header.ReadCoefficientProbabilities(reader);
        header.UseSkipProbability = reader.ReadBit(128) == 1;
        if (header.UseSkipProbability)
            header.SkipProbability = reader.ReadLiteral(8);
        return header;
    }

    private void ReadSegmentation(Vp8BoolDecoder reader)
    {
        UseSegments = reader.ReadBit(128) == 1;
        if (!UseSegments)
            return;

        UpdateSegmentMap = reader.ReadBit(128) == 1;
        if (reader.ReadBit(128) == 1)
        {
            AbsoluteSegmentValues = reader.ReadBit(128) == 1;
            for (var s = 0; s < Segments; s++)
                SegmentQuantizer[s] = reader.ReadOptionalSigned(7);
            for (var s = 0; s < Segments; s++)
                SegmentFilterStrength[s] = reader.ReadOptionalSigned(6);
        }

        if (UpdateSegmentMap)
        {
            for (var i = 0; i < SegmentProbabilities.Length; i++)
                SegmentProbabilities[i] = reader.ReadBit(128) == 1 ? (byte)reader.ReadLiteral(8) : (byte)255;
        }
    }

    private void ReadFilter(Vp8BoolDecoder reader)
    {
        SimpleFilter = reader.ReadBit(128) == 1;
        FilterLevel = reader.ReadLiteral(6);
        Sharpness = reader.ReadLiteral(3);
        UseFilterDeltas = reader.ReadBit(128) == 1;
        if (UseFilterDeltas && reader.ReadBit(128) == 1)
        {
            for (var i = 0; i < ReferenceFilterDelta.Length; i++)
            {
                if (reader.ReadBit(128) == 1)
                    ReferenceFilterDelta[i] = reader.ReadSigned(6);
            }

            for (var i = 0; i < ModeFilterDelta.Length; i++)
            {
                if (reader.ReadBit(128) == 1)
                    ModeFilterDelta[i] = reader.ReadSigned(6);
            }
        }
    }

    private void ReadQuantizers(Vp8BoolDecoder reader)
    {
        var baseIndex = reader.ReadLiteral(7);
        var yDc = reader.ReadOptionalSigned(4);
        var y2Dc = reader.ReadOptionalSigned(4);
        var y2Ac = reader.ReadOptionalSigned(4);
        var uvDc = reader.ReadOptionalSigned(4);
        var uvAc = reader.ReadOptionalSigned(4);
        for (var s = 0; s < Segments; s++)
        {
            int q;
            if (UseSegments)
                q = SegmentQuantizer[s] + (AbsoluteSegmentValues ? 0 : baseIndex);
            else if (s > 0)
            {
                Quantizers[s] = Quantizers[0];
                continue;
            }
            else
                q = baseIndex;

            // Y2 AC is scaled by 155/100, which for every step the table holds equals (x * 101581) >> 16.
            var y2AcStep = (Vp8Tables.AcQuantizer[Clip(q + y2Ac, 127)] * 101581) >> 16;
            Quantizers[s] = new Vp8Quantizer(
                Vp8Tables.DcQuantizer[Clip(q + yDc, 127)],
                Vp8Tables.AcQuantizer[Clip(q, 127)],
                Vp8Tables.DcQuantizer[Clip(q + y2Dc, 127)] * 2,
                Math.Max(y2AcStep, 8),
                Vp8Tables.DcQuantizer[Clip(q + uvDc, 117)],
                Vp8Tables.AcQuantizer[Clip(q + uvAc, 127)]);
        }
    }

    private void ReadCoefficientProbabilities(Vp8BoolDecoder reader)
    {
        var update = Vp8Tables.CoefficientUpdateProbabilities;
        for (var i = 0; i < CoefficientProbabilities.Length; i++)
        {
            if (reader.ReadBit(update[i]) == 1)
                CoefficientProbabilities[i] = (byte)reader.ReadLiteral(8);
        }
    }

    private static int Clip(int value, int max) => value < 0 ? 0 : value > max ? max : value;
}

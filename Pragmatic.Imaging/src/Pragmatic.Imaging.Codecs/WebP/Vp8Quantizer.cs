namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>The dequantization steps of one VP8 segment: DC and AC for luma, for the Y2 block, and for chroma.</summary>
internal readonly record struct Vp8Quantizer(int YDc, int YAc, int Y2Dc, int Y2Ac, int UvDc, int UvAc);

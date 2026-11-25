namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     The size a JPEG is decoded at, as a fraction of its own: a block of 8×8 coefficients becomes
///     8×8, 4×4, 2×2 or 1×1 samples, from its low frequencies alone. Decoding at a quarter does a
///     sixteenth of the transform, colour conversion and pixel memory, and what comes out is already a
///     low-pass-filtered reduction: the thumbnail path's first step.
/// </summary>
public enum JpegScale
{
    Full = 1,
    Half = 2,
    Quarter = 4,
    Eighth = 8,
}

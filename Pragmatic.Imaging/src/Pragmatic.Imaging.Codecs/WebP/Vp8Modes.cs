namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The intra prediction modes of a VP8 key frame, numbered as RFC 6386 numbers them. A 16 x 16 mode
///     doubles as the context of the 4 x 4 modes around it, as the 4 x 4 mode it corresponds to.
/// </summary>
internal static class Vp8Modes
{
    public const int Dc = 0;
    public const int V = 1;
    public const int H = 2;
    public const int Tm = 3;

    public const int BDc = 0;
    public const int BTm = 1;
    public const int BVe = 2;
    public const int BHe = 3;
    public const int BLd = 4;
    public const int BRd = 5;
    public const int BVr = 6;
    public const int BVl = 7;
    public const int BHd = 8;
    public const int BHu = 9;

    /// <summary>The 4 x 4 mode a 16 x 16 (or chroma) mode stands for as a neighbour's context.</summary>
    public static int AsSubblockMode(int mode) => mode switch
    {
        V => BVe,
        H => BHe,
        Tm => BTm,
        _ => BDc,
    };
}

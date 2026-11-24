namespace Pragmatic.Imaging.Codecs.Transforms;

/// <summary>The interpolation filters of a resize, the same five Pragmatic.Imaging offers today.</summary>
public enum ResizeFilter
{
    /// <summary>Nearest neighbour: fastest, blocky.</summary>
    Nearest = 0,

    /// <summary>Linear interpolation, a tent of radius 1.</summary>
    Triangle = 1,

    /// <summary>Catmull-Rom cubic, radius 2: sharp, with little ringing.</summary>
    CatmullRom = 2,

    /// <summary>Gaussian, radius 3: soft.</summary>
    Gaussian = 3,

    /// <summary>Lanczos with window 3: the sharpest, and the one the thumbnail path uses by default.</summary>
    Lanczos3 = 4,
}

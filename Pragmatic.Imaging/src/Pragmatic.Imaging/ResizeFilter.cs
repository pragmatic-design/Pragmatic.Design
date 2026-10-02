namespace Pragmatic.Imaging;

/// <summary>
/// Resize interpolation filters. Values match the Rust ResizeFilterCode enum.
/// </summary>
public enum ResizeFilter
{
    /// <summary>Nearest neighbor — fastest, lowest quality.</summary>
    Nearest = 0,

    /// <summary>Linear interpolation.</summary>
    Triangle = 1,

    /// <summary>Cubic interpolation — good balance of speed and quality.</summary>
    CatmullRom = 2,

    /// <summary>Gaussian interpolation.</summary>
    Gaussian = 3,

    /// <summary>Lanczos with window 3 — highest quality, slowest.</summary>
    Lanczos3 = 4
}

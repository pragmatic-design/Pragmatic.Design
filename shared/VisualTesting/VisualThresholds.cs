// ReSharper disable once CheckNamespace
namespace Pragmatic.Testing.Visual;

/// <summary>
///     What a comparison holds the actual image to. A bound left null is not checked; at least one must be
///     set, so a comparison always says what it measures.
/// </summary>
public sealed record VisualThresholds
{
    /// <summary>The fewest SSIM of the luma, 1 for identical.</summary>
    public double? MinSsimLuma { get; init; }

    /// <summary>The fewest decibels of PSNR over R, G and B.</summary>
    public double? MinPsnr { get; init; }

    /// <summary>The largest absolute difference of any colour sample, 0 for identical.</summary>
    public int? MaxAbsolute { get; init; }

    /// <summary>The largest mean CIEDE2000 over the pixels.</summary>
    public double? MaxMeanDeltaE { get; init; }

    /// <summary>Byte for byte the same colour.</summary>
    public static VisualThresholds Identical { get; } = new() { MaxAbsolute = 0 };

    internal bool IsEmpty => MinSsimLuma is null && MinPsnr is null && MaxAbsolute is null && MaxMeanDeltaE is null;
}

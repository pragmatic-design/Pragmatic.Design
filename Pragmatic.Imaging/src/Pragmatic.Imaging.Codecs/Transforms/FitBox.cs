namespace Pragmatic.Imaging.Codecs.Transforms;

/// <summary>The size an image takes to fit inside a box with its aspect ratio kept: the thumbnail rule.</summary>
public static class FitBox
{
    /// <summary>
    ///     Never larger than the source unless <paramref name="allowUpscale" />; never smaller than one
    ///     pixel on a side. Rounded the way Pragmatic.Imaging rounds today, so the two agree on the size.
    /// </summary>
    public static (int Width, int Height) Fit(int sourceWidth, int sourceHeight, int maxWidth, int maxHeight, bool allowUpscale = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHeight, 1);

        var ratio = Math.Min((double)maxWidth / sourceWidth, (double)maxHeight / sourceHeight);
        if (!allowUpscale)
            ratio = Math.Min(ratio, 1.0);

        var width = Math.Max(1, (int)Math.Round(sourceWidth * ratio));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * ratio));
        return (width, height);
    }
}

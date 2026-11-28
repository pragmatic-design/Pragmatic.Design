namespace Pragmatic.Imaging.Tests;

/// <summary>
///     A transparent PNG resized: on straight alpha the edge pixels average the red with the black of pixels nobody
///     sees, the halo around a transparent thumbnail. Resampled premultiplied, the edge keeps its colour
///     (<see cref="TransparentEdges" />).
/// </summary>
public class AResizedTransparentImageHasNoDarkFringeTests
{
    [Theory]
    [InlineData(16, 16)] // two exact halvings, no kernel
    [InlineData(23, 23)] // a halving, then Lanczos over the rest
    [InlineData(40, 40)] // Lanczos alone
    public void The_edge_pixels_of_a_resized_transparent_png_keep_their_colour(uint width, uint height)
    {
        using var pipeline = ImagePipeline.Load(TransparentEdges.HalfRedHalfTransparent());
        pipeline.Resize(width, height);

        TransparentEdges.AssertNoDarkFringe(pipeline.Encode(ImageFormat.Png));
    }
}

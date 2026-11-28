namespace Pragmatic.Imaging.Tests;

/// <summary>
///     A blur is a weighted mean of neighbours, as a resize is, and darkens a transparent edge the same way on straight
///     alpha; a sharpen adds back the difference from that blur, so it inherits it. Both work premultiplied on an image
///     with translucent pixels (<see cref="TransparentEdges" />).
/// </summary>
public class ABlurredTransparentImageHasNoDarkFringeTests
{
    [Theory]
    [InlineData(0.8f)]
    [InlineData(2f)]
    [InlineData(5f)]
    public void The_edge_pixels_of_a_blurred_transparent_png_keep_their_colour(float sigma)
    {
        using var pipeline = ImagePipeline.Load(TransparentEdges.HalfRedHalfTransparent());
        pipeline.Blur(sigma);

        TransparentEdges.AssertNoDarkFringe(pipeline.Encode(ImageFormat.Png));
    }

    [Theory]
    [InlineData(1f, 0)]
    [InlineData(2f, 1)]
    public void The_edge_pixels_of_a_sharpened_transparent_png_keep_their_colour(float sigma, int threshold)
    {
        using var pipeline = ImagePipeline.Load(TransparentEdges.GreyBesideTransparentWhite());
        pipeline.Sharpen(sigma, threshold);

        TransparentEdges.AssertVisiblePixelsStayGrey(pipeline.Encode(ImageFormat.Png));
    }

    [Fact]
    public void A_blur_does_not_mix_a_hidden_colour_into_the_visible_one()
    {
        using var pipeline = ImagePipeline.Load(TransparentEdges.GreyBesideTransparentWhite());
        pipeline.Blur(2f);

        TransparentEdges.AssertVisiblePixelsStayGrey(pipeline.Encode(ImageFormat.Png));
    }
}
